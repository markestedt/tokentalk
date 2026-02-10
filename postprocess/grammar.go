package postprocess

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"strings"
	"time"
)

// GrammarProvider is an interface for grammar correction providers
type GrammarProvider interface {
	Correct(ctx context.Context, text string, dictionary *Dictionary) (string, error)
	Name() string
}

// OpenAIGrammarProvider implements grammar correction using OpenAI
type OpenAIGrammarProvider struct {
	apiKey string
	model  string
	client *http.Client
}

// NewOpenAIGrammarProvider creates a new OpenAI grammar provider
func NewOpenAIGrammarProvider(apiKey, model string) *OpenAIGrammarProvider {
	return &OpenAIGrammarProvider{
		apiKey: apiKey,
		model:  model,
		client: &http.Client{
			Timeout: 30 * time.Second,
		},
	}
}

// Name returns the provider name
func (p *OpenAIGrammarProvider) Name() string {
	return "openai"
}

// Correct performs grammar correction using OpenAI's chat API
func (p *OpenAIGrammarProvider) Correct(ctx context.Context, text string, dictionary *Dictionary) (string, error) {
	systemPrompt := buildSystemPrompt(dictionary)

	// Build request
	reqBody := map[string]interface{}{
		"model": p.model,
		"messages": []map[string]string{
			{
				"role":    "system",
				"content": systemPrompt,
			},
			{
				"role":    "user",
				"content": text,
			},
		},
		"temperature": 0.3, // Low temperature for consistent corrections
		"max_tokens":  1000,
	}

	jsonData, err := json.Marshal(reqBody)
	if err != nil {
		return text, fmt.Errorf("failed to marshal request: %w", err)
	}

	req, err := http.NewRequestWithContext(ctx, "POST", "https://api.openai.com/v1/chat/completions", bytes.NewReader(jsonData))
	if err != nil {
		return text, fmt.Errorf("failed to create request: %w", err)
	}

	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+p.apiKey)

	resp, err := p.client.Do(req)
	if err != nil {
		return text, fmt.Errorf("failed to call OpenAI API: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(resp.Body)
		return text, fmt.Errorf("OpenAI API error (status %d): %s", resp.StatusCode, string(body))
	}

	var result struct {
		Choices []struct {
			Message struct {
				Content string `json:"content"`
			} `json:"message"`
		} `json:"choices"`
	}

	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return text, fmt.Errorf("failed to decode response: %w", err)
	}

	if len(result.Choices) == 0 {
		return text, fmt.Errorf("no response from OpenAI")
	}

	corrected := strings.TrimSpace(result.Choices[0].Message.Content)
	return corrected, nil
}

// buildSystemPrompt creates the system prompt for grammar correction
func buildSystemPrompt(dictionary *Dictionary) string {
	prompt := `You are a post-processor for a developer voice dictation tool. The user is a software developer dictating text. Fix grammar and punctuation while preserving technical content.

Pay special attention to:
- File paths (convert spoken "slash" to /)
- Version numbers ("three dot two dot one" → 3.2.1)
- Technical terms, API names, CLI flags (--flag)
- Code identifiers (camelCase, snake_case, PascalCase)
- Package/module names, URLs, environment variables
- Programming language keywords and syntax

Rules:
- Fix obvious grammar and punctuation errors
- DO NOT change technical terms or code-related content
- DO NOT add explanations or commentary
- Return ONLY the corrected text, nothing else`

	// Add dictionary context if available
	if dictionary != nil && len(dictionary.Entries) > 0 {
		prompt += "\n\nCustom dictionary (use these correct spellings):\n"
		for _, entry := range dictionary.Entries {
			if entry.IsMapping {
				prompt += fmt.Sprintf("- \"%s\" should be \"%s\"\n", entry.Original, entry.Replacement)
			} else {
				prompt += fmt.Sprintf("- %s\n", entry.Replacement)
			}
		}
	}

	return prompt
}

// GrammarProcessor creates a processor that performs grammar correction
func GrammarProcessor(provider GrammarProvider, dictionary *Dictionary) Processor {
	return func(ctx context.Context, text string) (string, error) {
		if provider == nil {
			return text, nil
		}

		return provider.Correct(ctx, text, dictionary)
	}
}
