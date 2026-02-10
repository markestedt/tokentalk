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
	"markestedt/tokentalk/systray"
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

	// Load icon for system tray
	iconData, err := os.ReadFile("tokentalk.ico")
	if err != nil {
		slog.Warn("Failed to load icon for system tray, continuing without icon", "error", err)
		iconData = nil
	}

	// Create system tray manager
	trayMgr := systray.NewSystrayManager(cfg.Web.Port, iconData)

	// Run agent in background
	agentDone := make(chan error, 1)
	go func() {
		agentDone <- agent.Run(ctx)
	}()

	// Run system tray in main goroutine (blocking)
	// This needs to be in the main goroutine on Windows
	go trayMgr.Run()

	// Wait for shutdown signal
	select {
	case <-ctx.Done():
		slog.Info("Shutdown signal received")
		trayMgr.Stop()
	case <-trayMgr.WaitForQuit():
		slog.Info("Quit requested from system tray")
		cancel()
	case err := <-agentDone:
		if err != nil {
			slog.Error("Agent error", "error", err)
		}
		trayMgr.Stop()
	}

	slog.Info("TokenTalk stopped")
}
