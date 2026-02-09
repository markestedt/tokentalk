package transcribe

import (
	"context"
	"fmt"

	"markestedt/tokentalk/audio"
	"markestedt/tokentalk/config"
)

// Provider defines the interface for speech-to-text transcription
type Provider interface {
	Name() string
	Transcribe(ctx context.Context, audio audio.AudioSegment) (string, error)
}

// NewProvider creates a transcription provider based on configuration
func NewProvider(cfg config.TranscriptionConfig) (Provider, error) {
	switch cfg.Provider {
	case "openai":
		if cfg.OpenAIAPIKey == "" {
			return nil, fmt.Errorf("openai_api_key is required for OpenAI provider")
		}
		return NewOpenAIProvider(cfg.OpenAIAPIKey, cfg.Model, cfg.Language), nil
	case "whisper":
		return NewWhisperProvider(cfg.WhisperModelDir, cfg.Model, cfg.Language)
	default:
		return nil, fmt.Errorf("unknown provider: %s", cfg.Provider)
	}
}
