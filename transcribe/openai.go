package transcribe

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"mime/multipart"
	"net/http"

	"markestedt/tokentalk/audio"
)

// OpenAIProvider implements transcription using OpenAI's Whisper API
type OpenAIProvider struct {
	apiKey        string
	model         string
	language      string
	prompt        string
	developerMode bool
	client        *http.Client
}

// NewOpenAIProvider creates a new OpenAI transcription provider
func NewOpenAIProvider(apiKey, model, language, prompt string, developerMode bool) *OpenAIProvider {
	if model == "" {
		model = "whisper-1"
	}
	return &OpenAIProvider{
		apiKey:        apiKey,
		model:         model,
		language:      language,
		prompt:        prompt,
		developerMode: developerMode,
		client:        &http.Client{},
	}
}

// Name returns the provider name
func (p *OpenAIProvider) Name() string {
	return "openai"
}

// Transcribe sends audio to OpenAI's Whisper API for transcription
func (p *OpenAIProvider) Transcribe(ctx context.Context, audioSeg audio.AudioSegment) (string, error) {
	// Convert to WAV format
	wavData, err := audioSeg.ToWAV()
	if err != nil {
		return "", fmt.Errorf("failed to convert to WAV: %w", err)
	}

	// Create multipart form data
	body := &bytes.Buffer{}
	writer := multipart.NewWriter(body)

	// Add audio file
	part, err := writer.CreateFormFile("file", "audio.wav")
	if err != nil {
		return "", fmt.Errorf("failed to create form file: %w", err)
	}
	if _, err := part.Write(wavData); err != nil {
		return "", fmt.Errorf("failed to write audio data: %w", err)
	}

	// Add model
	if err := writer.WriteField("model", p.model); err != nil {
		return "", fmt.Errorf("failed to write model field: %w", err)
	}

	// Add language if specified
	if p.language != "" {
		if err := writer.WriteField("language", p.language); err != nil {
			return "", fmt.Errorf("failed to write language field: %w", err)
		}
	}

	// Build prompt: always start with default, then append user's custom prompt if configured
	prompt := "Transcribe the following audio with proper grammar, punctuation, and capitalization. " +
		"Ensure sentences start with capital letters and end with appropriate punctuation marks (periods, question marks, or exclamation marks). " +
		"Correct minor grammatical errors while preserving the speaker's intended meaning and tone. "
	if p.developerMode {
		prompt += "Recognize and accurately transcribe technical terminology, programming language keywords, API names, framework names, software tools, and common development acronyms (e.g., API, REST, SQL, JSON, HTML, CSS, Git, CI/CD, etc.). "
	}
	prompt += "Format the output as natural, well-structured text in the configured language."

	// Append user's custom prompt if configured
	if p.prompt != "" {
		prompt += " " + p.prompt
	}

	if err := writer.WriteField("prompt", prompt); err != nil {
		return "", fmt.Errorf("failed to write prompt field: %w", err)
	}

	fmt.Printf("[OPENAI DEBUG] Sending %d bytes of audio (%.2fs), language=%s, model=%s\n",
		len(wavData), audioSeg.Duration.Seconds(), p.language, p.model)

	if err := writer.Close(); err != nil {
		return "", fmt.Errorf("failed to close writer: %w", err)
	}

	// Create request
	req, err := http.NewRequestWithContext(ctx, "POST", "https://api.openai.com/v1/audio/transcriptions", body)
	if err != nil {
		return "", fmt.Errorf("failed to create request: %w", err)
	}

	req.Header.Set("Authorization", "Bearer "+p.apiKey)
	req.Header.Set("Content-Type", writer.FormDataContentType())

	// Send request
	resp, err := p.client.Do(req)
	if err != nil {
		return "", fmt.Errorf("failed to send request: %w", err)
	}
	defer resp.Body.Close()

	// Read response
	respBody, err := io.ReadAll(resp.Body)
	if err != nil {
		return "", fmt.Errorf("failed to read response: %w", err)
	}

	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("API request failed with status %d: %s", resp.StatusCode, string(respBody))
	}

	// Parse JSON response
	var result struct {
		Text string `json:"text"`
	}
	if err := json.Unmarshal(respBody, &result); err != nil {
		return "", fmt.Errorf("failed to parse response: %w", err)
	}

	return result.Text, nil
}
