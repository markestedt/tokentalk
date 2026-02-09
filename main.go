package main

import (
	"context"
	"log/slog"
	"os"
	"os/signal"
	"syscall"

	"markestedt/tokentalk/config"
)

func main() {
	// Setup logging
	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{
		Level: slog.LevelInfo,
	}))
	slog.SetDefault(logger)

	// Load configuration
	cfg, err := config.Load()
	if err != nil {
		slog.Error("Failed to load config", "error", err)
		os.Exit(1)
	}

	configPath, _ := config.ConfigPath()
	slog.Info("Configuration loaded", "path", configPath)

	// Validate configuration
	if cfg.Transcription.Provider == "openai" && cfg.Transcription.OpenAIAPIKey == "" {
		slog.Error("OpenAI API key is required. Please set 'openai_api_key' in config file", "path", configPath)
		os.Exit(1)
	}

	// Create agent
	agent, err := NewAgent(cfg)
	if err != nil {
		slog.Error("Failed to create agent", "error", err)
		os.Exit(1)
	}

	// Setup signal handling for graceful shutdown
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer cancel()

	// Run agent
	if err := agent.Run(ctx); err != nil {
		slog.Error("Agent error", "error", err)
		os.Exit(1)
	}

	slog.Info("TokenTalk stopped")
}
