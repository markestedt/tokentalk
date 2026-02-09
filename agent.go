package main

import (
	"context"
	"fmt"
	"log/slog"
	"time"

	"markestedt/tokentalk/audio"
	"markestedt/tokentalk/config"
	"markestedt/tokentalk/platform"
	"markestedt/tokentalk/transcribe"
)

// Agent coordinates hotkey detection, recording, and transcription
type Agent struct {
	cfg       *config.Config
	hotkey    platform.Hotkey
	clipboard platform.Clipboard
	paster    platform.Paster
	recorder  *audio.Recorder
	provider  transcribe.Provider
}

// NewAgent creates a new agent instance
func NewAgent(cfg *config.Config) (*Agent, error) {
	// Create recorder
	recorder, err := audio.NewRecorder(cfg.Audio.Device, cfg.Audio.MaxSeconds)
	if err != nil {
		return nil, fmt.Errorf("failed to create recorder: %w", err)
	}

	// Create transcription provider
	provider, err := transcribe.NewProvider(cfg.Transcription)
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

// Run starts the agent's main event loop
func (a *Agent) Run(ctx context.Context) error {
	// Parse hotkey combo
	combo, err := config.ParseHotkey(a.cfg.Hotkey.Combo)
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

	slog.Info("TokenTalk started", "hotkey", a.cfg.Hotkey.Combo, "provider", a.provider.Name())

	// Main event loop
	for {
		select {
		case <-ctx.Done():
			a.recorder.Close()
			return nil

		case evt := <-events:
			switch evt.Type {
			case platform.Pressed:
				slog.Info("Recording started")
				if err := a.recorder.Start(ctx); err != nil {
					slog.Error("Failed to start recording", "error", err)
					continue
				}

			case platform.Released:
				slog.Info("Recording stopped, transcribing...")
				audioSeg, err := a.recorder.Stop()
				if err != nil {
					slog.Error("Failed to stop recording", "error", err)
					continue
				}

				// Check if audio is too short
				if audioSeg.Duration < 100*time.Millisecond {
					slog.Warn("Recording too short, ignoring", "duration", audioSeg.Duration)
					continue
				}

				// Transcribe in background to avoid blocking
				go func(seg audio.AudioSegment) {
					text, err := a.provider.Transcribe(ctx, seg)
					if err != nil {
						slog.Error("Transcription failed", "error", err)
						return
					}

					if text == "" {
						slog.Warn("Empty transcription")
						return
					}

					slog.Info("Transcribed", "text", text, "duration", seg.Duration)

					// Inject text
					if err := a.injectText(text); err != nil {
						slog.Error("Failed to inject text", "error", err)
					}
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
