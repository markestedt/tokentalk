package main

import (
	"context"
	"fmt"
	"log/slog"
	"strings"
	"time"

	"markestedt/tokentalk/audio"
	"markestedt/tokentalk/config"
	"markestedt/tokentalk/platform"
	"markestedt/tokentalk/storage"
	"markestedt/tokentalk/transcribe"
	"markestedt/tokentalk/web"
)

// Agent coordinates hotkey detection, recording, and transcription
type Agent struct {
	cfg       *config.Config
	hotkey    platform.Hotkey
	clipboard platform.Clipboard
	paster    platform.Paster
	recorder  *audio.Recorder
	provider  transcribe.Provider
	db        *storage.DB
	webServer *web.Server
}

// NewAgent creates a new agent instance
func NewAgent(cfg *config.Config) (*Agent, error) {
	// Convert device ID to string
	deviceID := ""
	if cfg.Audio.Device > 0 {
		deviceID = fmt.Sprintf("%d", cfg.Audio.Device)
	}

	// Create recorder
	recorder, err := audio.NewRecorder(deviceID, cfg.Audio.MaxSeconds)
	if err != nil {
		return nil, fmt.Errorf("failed to create recorder: %w", err)
	}

	// Create transcription provider
	provider, err := transcribe.NewProvider(cfg.Transcription, cfg.DeveloperMode)
	if err != nil {
		return nil, fmt.Errorf("failed to create transcription provider: %w", err)
	}

	return &Agent{
		cfg:       cfg,
		hotkey:    platform.NewHotkey(),
		clipboard: platform.NewClipboard(),
		paster:    platform.NewPaster(),
		recorder:  recorder,
		provider:  provider,
	}, nil
}

// setStatus updates the agent status and broadcasts to web clients
func (a *Agent) setStatus(status string) {
	if a.webServer != nil {
		a.webServer.BroadcastStatus(status)
	}
}

// Run starts the agent's main event loop
func (a *Agent) Run(ctx context.Context) error {
	// Parse hotkey combo
	combo, err := config.ParseHotkey(a.cfg.Hotkey)
	if err != nil {
		return fmt.Errorf("failed to parse hotkey: %w", err)
	}

	// Convert key to VK code (0 means modifier-only combo)
	vkCode, err := platform.VKCode(combo.Key)
	if err != nil {
		return fmt.Errorf("failed to get VK code: %w", err)
	}

	pkCombo := platform.KeyCombo{
		Ctrl:  combo.Ctrl,
		Shift: combo.Shift,
		Alt:   combo.Alt,
		Win:   combo.Win,
		Key:   vkCode,
	}

	// Start listening for hotkey
	events, err := a.hotkey.Listen(ctx, pkCombo)
	if err != nil {
		return fmt.Errorf("failed to start hotkey listener: %w", err)
	}

	slog.Info("TokenTalk started", "hotkey", a.cfg.Hotkey, "provider", a.provider.Name())
	a.setStatus("idle")

	// Main event loop
	for {
		select {
		case <-ctx.Done():
			a.recorder.Close()
			return nil

		case evt := <-events:
			switch evt.Type {
			case platform.Pressed:
				// Start recording immediately to minimize latency
				if err := a.recorder.Start(ctx); err != nil {
					slog.Error("Failed to start recording", "error", err)
					a.setStatus("idle")
					continue
				}
				// Log and update status asynchronously to avoid blocking
				go func() {
					slog.Info("Recording started")
					a.setStatus("recording")
				}()

			case platform.Released:
				slog.Info("Recording stopped, transcribing...")
				recordingStart := time.Now()
				audioSeg, err := a.recorder.Stop()
				if err != nil {
					slog.Error("Failed to stop recording", "error", err)
					a.setStatus("idle")
					continue
				}

				// Check if audio is too short
				if audioSeg.Duration < 100*time.Millisecond {
					slog.Warn("Recording too short, ignoring", "duration", audioSeg.Duration)
					a.setStatus("idle")
					continue
				}

				// Check if audio is silent or too quiet (if threshold is set)
				if a.cfg.Audio.SilenceThreshold > 0 {
					rms := audioSeg.CalculateRMS()
					if rms < a.cfg.Audio.SilenceThreshold {
						slog.Warn("Recording too quiet or silent, ignoring", "rms", rms, "threshold", a.cfg.Audio.SilenceThreshold)
						a.setStatus("idle")
						continue
					}
				}

				// Transcribe in background to avoid blocking
				go func(seg audio.AudioSegment) {
					a.setStatus("processing")

					dictation := &storage.Dictation{
						RecordingStartMs:    recordingStart.UnixMilli(),
						RecordingDurationMs: seg.Duration.Milliseconds(),
						AudioSizeBytes:      int64(len(seg.Data)),
						AudioSampleRate:     seg.SampleRate,
						Provider:            a.provider.Name(),
						Model:               a.cfg.Transcription.Model,
						Language:            a.cfg.Transcription.Language,
						Success:             false,
					}

					transcribeStart := time.Now()
					text, err := a.provider.Transcribe(ctx, seg)
					dictation.TranscriptionLatencyMs = time.Since(transcribeStart).Milliseconds()

					if err != nil {
						slog.Error("Transcription failed", "error", err)
						dictation.ErrorMessage = err.Error()
						if a.db != nil {
							a.db.SaveDictation(dictation)
						}
						a.setStatus("idle")
						return
					}

					if text == "" {
						slog.Warn("Empty transcription")
						dictation.ErrorMessage = "Empty transcription"
						if a.db != nil {
							a.db.SaveDictation(dictation)
						}
						a.setStatus("idle")
						return
					}

					dictation.TranscribedText = text
					dictation.WordCount = len(strings.Fields(text))
					dictation.CharacterCount = len(text)

					slog.Info("Transcribed", "text", text, "duration", seg.Duration)

					// Inject text
					injectStart := time.Now()
					if err := a.injectText(text); err != nil {
						slog.Error("Failed to inject text", "error", err)
						dictation.ErrorMessage = err.Error()
						dictation.InjectionLatencyMs = time.Since(injectStart).Milliseconds()
						dictation.TotalLatencyMs = time.Since(recordingStart).Milliseconds()
						if a.db != nil {
							a.db.SaveDictation(dictation)
						}
						a.setStatus("idle")
						return
					}

					dictation.InjectionLatencyMs = time.Since(injectStart).Milliseconds()
					dictation.TotalLatencyMs = time.Since(recordingStart).Milliseconds()
					dictation.Success = true

					if a.db != nil {
						if err := a.db.SaveDictation(dictation); err != nil {
							slog.Error("Failed to save dictation", "error", err)
						} else if a.webServer != nil {
							a.webServer.BroadcastDictation(dictation)
						}
					}

					a.setStatus("idle")
				}(audioSeg)
			}
		}
	}
}

// injectText injects transcribed text via clipboard paste
func (a *Agent) injectText(text string) error {
	// Save current clipboard content
	originalClip, err := a.clipboard.Get()
	if err != nil {
		slog.Warn("Failed to get clipboard content, continuing anyway", "error", err)
		originalClip = ""
	}

	// Set clipboard to transcribed text
	if err := a.clipboard.Set(text); err != nil {
		return fmt.Errorf("failed to set clipboard: %w", err)
	}

	// Wait for clipboard to update
	time.Sleep(50 * time.Millisecond)

	// Simulate paste
	if err := a.paster.Paste(); err != nil {
		return fmt.Errorf("failed to paste: %w", err)
	}

	// Wait for paste to complete
	time.Sleep(100 * time.Millisecond)

	// Restore original clipboard
	if originalClip != "" {
		if err := a.clipboard.Set(originalClip); err != nil {
			slog.Warn("Failed to restore clipboard", "error", err)
		}
	}

	return nil
}
