package postprocess

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"strings"
	"time"
)

// CodePrefixes are the voice triggers for code generation mode
var CodePrefixes = []string{
	"code block:",
	"code block",
	"codeblock:",
	"codeblock",
	"code:",
}

// CodeGenResult holds the generated code and detected language
type CodeGenResult struct {
	Code     string `json:"code"`
	Language string `json:"language"`
}

// CodeGenProvider is an interface for code generation providers
type CodeGenProvider interface {
	Generate(ctx context.Context, description string) (CodeGenResult, error)
	Name() string
}

// OpenAICodeGenProvider implements code generation using OpenAI
type OpenAICodeGenProvider struct {
	apiKey string
	model  string
	client *http.Client
}

// NewOpenAICodeGenProvider creates a new OpenAI code generation provider
func NewOpenAICodeGenProvider(apiKey, model string) *OpenAICodeGenProvider {
	return &OpenAICodeGenProvider{
		apiKey: apiKey,
		model:  model,
		client: &http.Client{
			Timeout: 30 * time.Second,
		},
	}
}

// Name returns the provider name
func (p *OpenAICodeGenProvider) Name() string {
	return "openai"
}

// Generate creates code from a natural language description
func (p *OpenAICodeGenProvider) Generate(ctx context.Context, description string) (CodeGenResult, error) {
	systemPrompt := `You are a code generator for a developer voice dictation tool. The user will describe code they want written.

Instructions:
1. Generate clean, well-formatted code based on the description
2. Detect the programming language from context clues in the description
3. If no language is specified, infer the most appropriate language
4. Return ONLY valid JSON in this exact format: {"code": "...", "language": "..."}
5. Use standard language identifiers (python, javascript, typescript, go, rust, java, cpp, c, csharp, ruby, php, sql, bash, html, css, yaml, json, etc.)
6. The code should be complete and runnable when possible
7. Do not include markdown formatting or backticks in the code field
8. Use \n for newlines within the code string`

	reqBody := map[string]interface{}{
		"model": p.model,
		"messages": []map[string]string{
			{
				"role":    "system",
				"content": systemPrompt,
			},
			{
				"role":    "user",
				"content": description,
			},
		},
		"temperature": 0.3,
		"max_tokens":  2000,
	}

	jsonData, err := json.Marshal(reqBody)
	if err != nil {
		return CodeGenResult{}, fmt.Errorf("failed to marshal request: %w", err)
	}

	req, err := http.NewRequestWithContext(ctx, "POST", "https://api.openai.com/v1/chat/completions", bytes.NewReader(jsonData))
	if err != nil {
		return CodeGenResult{}, fmt.Errorf("failed to create request: %w", err)
	}

	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+p.apiKey)

	resp, err := p.client.Do(req)
	if err != nil {
		return CodeGenResult{}, fmt.Errorf("failed to call OpenAI API: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(resp.Body)
		return CodeGenResult{}, fmt.Errorf("OpenAI API error (status %d): %s", resp.StatusCode, string(body))
	}

	var apiResult struct {
		Choices []struct {
			Message struct {
				Content string `json:"content"`
			} `json:"message"`
		} `json:"choices"`
	}

	if err := json.NewDecoder(resp.Body).Decode(&apiResult); err != nil {
		return CodeGenResult{}, fmt.Errorf("failed to decode response: %w", err)
	}

	if len(apiResult.Choices) == 0 {
		return CodeGenResult{}, fmt.Errorf("no response from OpenAI")
	}

	content := strings.TrimSpace(apiResult.Choices[0].Message.Content)

	// Try to parse JSON response
	var result CodeGenResult
	if err := json.Unmarshal([]byte(content), &result); err != nil {
		// Fallback: treat entire response as code with unknown language
		slog.Warn("Failed to parse code gen response as JSON, using raw content", "error", err)
		return CodeGenResult{
			Code:     content,
			Language: "",
		}, nil
	}

	return result, nil
}

// DetectCodePrefix checks if text starts with a code generation prefix
// Returns (remainingText, isCodeMode)
func DetectCodePrefix(text string) (string, bool) {
	lowerText := strings.ToLower(strings.TrimSpace(text))
	for _, prefix := range CodePrefixes {
		if strings.HasPrefix(lowerText, prefix) {
			// Extract the description after the prefix, preserving original case
			remaining := strings.TrimSpace(text[len(prefix):])
			return remaining, true
		}
	}
	return text, false
}

// CodeGenProcessor creates a processor that detects code prefixes and generates code
func CodeGenProcessor(provider CodeGenProvider) Processor {
	return func(ctx context.Context, text string) (string, error) {
		description, isCodeMode := DetectCodePrefix(text)
		if !isCodeMode {
			return text, nil // Pass through unchanged
		}

		if provider == nil {
			return text, nil
		}

		if description == "" {
			slog.Warn("Code prefix detected but no description provided")
			return text, nil
		}

		slog.Info("Code generation triggered", "description", description)

		result, err := provider.Generate(ctx, description)
		if err != nil {
			slog.Error("Code generation failed", "error", err)
			return text, err // Return original on error
		}

		if result.Code == "" {
			slog.Warn("Code generation returned empty code")
			return text, nil
		}

		// Wrap in markdown code block
		codeBlock := fmt.Sprintf("```%s\n%s\n```", result.Language, result.Code)
		slog.Info("Code generated", "language", result.Language, "length", len(result.Code))

		return codeBlock, nil
	}
}
