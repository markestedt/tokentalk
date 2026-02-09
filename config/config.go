package config

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"

	"github.com/BurntSushi/toml"
)

type Config struct {
	Hotkey        HotkeyConfig        `toml:"hotkey"`
	Audio         AudioConfig         `toml:"audio"`
	Transcription TranscriptionConfig `toml:"transcription"`
}

type HotkeyConfig struct {
	Combo string `toml:"combo"`
}

type AudioConfig struct {
	Device     string `toml:"device"`
	MaxSeconds int    `toml:"max_seconds"`
}

type TranscriptionConfig struct {
	Provider        string `toml:"provider"`
	Model           string `toml:"model"`
	Language        string `toml:"language"`
	DeveloperMode   bool   `toml:"developer_mode"`
	OpenAIAPIKey    string `toml:"openai_api_key"`
	WhisperModelDir string `toml:"whisper_model_dir"`
}

// Default configuration
func defaultConfig() *Config {
	appData := os.Getenv("APPDATA")
	if appData == "" {
		appData = filepath.Join(os.Getenv("USERPROFILE"), "AppData", "Roaming")
	}

	return &Config{
		Hotkey: HotkeyConfig{
			Combo: "ctrl+shift+v",
		},
		Audio: AudioConfig{
			Device:     "",
			MaxSeconds: 120,
		},
		Transcription: TranscriptionConfig{
			Provider:        "openai",
			Model:           "whisper-1",
			Language:        "en",
			DeveloperMode:   false,
			OpenAIAPIKey:    "",
			WhisperModelDir: filepath.Join(appData, "tokentalk", "models"),
		},
	}
}

// ConfigPath returns the path to the configuration file
func ConfigPath() (string, error) {
	appData := os.Getenv("APPDATA")
	if appData == "" {
		appData = filepath.Join(os.Getenv("USERPROFILE"), "AppData", "Roaming")
	}

	configDir := filepath.Join(appData, "tokentalk")
	if err := os.MkdirAll(configDir, 0755); err != nil {
		return "", fmt.Errorf("failed to create config directory: %w", err)
	}

	return filepath.Join(configDir, "config.toml"), nil
}

// Load loads the configuration from the TOML file
// If the file doesn't exist, it creates it with default values
func Load() (*Config, error) {
	configPath, err := ConfigPath()
	if err != nil {
		return nil, err
	}

	// If config doesn't exist, create it with defaults
	if _, err := os.Stat(configPath); os.IsNotExist(err) {
		cfg := defaultConfig()
		if err := save(configPath, cfg); err != nil {
			return nil, fmt.Errorf("failed to create default config: %w", err)
		}
		return cfg, nil
	}

	// Load existing config
	cfg := defaultConfig()
	if _, err := toml.DecodeFile(configPath, cfg); err != nil {
		return nil, fmt.Errorf("failed to decode config: %w", err)
	}

	return cfg, nil
}

// save writes the configuration to the TOML file
func save(path string, cfg *Config) error {
	f, err := os.Create(path)
	if err != nil {
		return err
	}
	defer f.Close()

	enc := toml.NewEncoder(f)
	return enc.Encode(cfg)
}

// KeyCombo represents a parsed keyboard combination
type KeyCombo struct {
	Ctrl  bool
	Shift bool
	Alt   bool
	Win   bool
	Key   string
}

// ParseHotkey parses a hotkey combo string like "ctrl+shift+v" or "ctrl+win"
func ParseHotkey(combo string) (KeyCombo, error) {
	var kc KeyCombo
	parts := strings.Split(strings.ToLower(combo), "+")

	if len(parts) == 0 {
		return kc, fmt.Errorf("empty hotkey combo")
	}

	for i, part := range parts {
		part = strings.TrimSpace(part)

		// Check if this part is a modifier
		isModifier := false
		switch part {
		case "ctrl", "control":
			kc.Ctrl = true
			isModifier = true
		case "shift":
			kc.Shift = true
			isModifier = true
		case "alt":
			kc.Alt = true
			isModifier = true
		case "win", "windows":
			kc.Win = true
			isModifier = true
		}

		// If it's not a modifier and it's the last part, it's the key
		if !isModifier {
			if i == len(parts)-1 {
				kc.Key = part
			} else {
				return kc, fmt.Errorf("unknown modifier: %s", part)
			}
		}
	}

	// Key is optional - if empty, it's a modifier-only combo
	// But we need at least one modifier
	if !kc.Ctrl && !kc.Shift && !kc.Alt && !kc.Win {
		return kc, fmt.Errorf("no modifiers or key specified in combo")
	}

	return kc, nil
}
