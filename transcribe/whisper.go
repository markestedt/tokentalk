package transcribe

import (
	"context"
	"fmt"

	"markestedt/tokentalk/audio"
)

// WhisperProvider implements local transcription using whisper.cpp
type WhisperProvider struct {
	modelDir string
	model    string
	language string
}

// NewWhisperProvider creates a new whisper.cpp transcription provider
func NewWhisperProvider(modelDir, model, language string) (*WhisperProvider, error) {
	if model == "" {
		model = "base"
	}

	// TODO: For M1 MVP, we'll defer whisper.cpp implementation
	// This is a placeholder that returns an error
	return nil, fmt.Errorf("whisper.cpp provider not yet implemented - use 'openai' provider instead")
}

// Name returns the provider name
func (p *WhisperProvider) Name() string {
	return "whisper"
}

// Transcribe transcribes audio using local whisper.cpp
func (p *WhisperProvider) Transcribe(ctx context.Context, audioSeg audio.AudioSegment) (string, error) {
	// TODO: Implement whisper.cpp bindings
	// For now, return not implemented error
	return "", fmt.Errorf("whisper.cpp transcription not yet implemented")
}
