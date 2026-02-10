package main

import (
	"context"
	"log/slog"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"

	"markestedt/tokentalk/config"
	"markestedt/tokentalk/storage"
	"markestedt/tokentalk/web"
)

func main() {
	// Setup logging
	logLevel := slog.LevelInfo
	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{
		Level: logLevel,
	}))
	slog.SetDefault(logger)

	// Load configuration
	cfg, err := config.Load()
	if err != nil {
		slog.Error("Failed to load config", "error", err)
		os.Exit(1)
	}

	configPath, _ := config.ConfigPath()
	configDir := filepath.Dir(configPath)
	slog.Info("Configuration loaded", "path", configPath)

	// Validate configuration
	if cfg.Transcription.Provider == "openai" && cfg.Transcription.APIKey == "" {
		slog.Error("OpenAI API key is required. Please set 'api_key' in config file", "path", configPath)
		os.Exit(1)
	}

	// Open database
	db, err := storage.Open(configDir)
	if err != nil {
		slog.Error("Failed to open database", "error", err)
		os.Exit(1)
	}
	defer db.Close()
	slog.Info("Database opened", "path", filepath.Join(configDir, "tokentalk.db"))

	// Create web server
	var webServer *web.Server
	if cfg.Web.Enabled {
		webServer = web.NewServer(db, cfg, cfg.Web.Port)
		go func() {
			if err := webServer.Start(); err != nil {
				slog.Error("Web server error", "error", err)
			}
		}()
	}

	// Create agent
	agent, err := NewAgent(cfg)
	if err != nil {
		slog.Error("Failed to create agent", "error", err)
		os.Exit(1)
	}

	// Attach database and web server to agent
	agent.db = db
	agent.webServer = webServer

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
