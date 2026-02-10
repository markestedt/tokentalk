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
func NewProvider(cfg config.TranscriptionConfig, developerMode bool) (Provider, error) {
	switch cfg.Provider {
	case "openai":
		if cfg.APIKey == "" {
			return nil, fmt.Errorf("api_key is required for OpenAI provider")
		}
		return NewOpenAIProvider(cfg.APIKey, cfg.Model, cfg.Language, cfg.Prompt, developerMode), nil
	case "whisper":
		return NewWhisperProvider(cfg.WhisperModelDir, cfg.Model, cfg.Language)
	default:
		return nil, fmt.Errorf("unknown provider: %s", cfg.Provider)
	}
}
