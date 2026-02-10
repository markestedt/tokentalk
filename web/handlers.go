package web

import (
	"encoding/json"
	"log/slog"
	"net/http"
	"strconv"
	"strings"
)

// handleConfig handles GET and PUT requests for configuration
func (s *Server) handleConfig(w http.ResponseWriter, r *http.Request) {
	switch r.Method {
	case http.MethodGet:
		s.handleGetConfig(w, r)
	case http.MethodPut:
		s.handlePutConfig(w, r)
	default:
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
	}
}

// handleGetConfig returns the current configuration
func (s *Server) handleGetConfig(w http.ResponseWriter, r *http.Request) {
	cfg := s.GetConfig()

	// Create a sanitized version of the config (hide API keys)
	sanitized := struct {
		Hotkey           string  `json:"hotkey"`
		Provider         string  `json:"provider"`
		Model            string  `json:"model"`
		Language         string  `json:"language"`
		Prompt           string  `json:"prompt"`
		AudioDevice      int     `json:"audioDevice"`
		SilenceThreshold float64 `json:"silenceThreshold"`
		HasAPIKey        bool    `json:"hasApiKey"`
		WebEnabled       bool    `json:"webEnabled"`
		WebPort          int     `json:"webPort"`
		DeveloperMode    bool    `json:"developerMode"`
	}{
		Hotkey:           cfg.Hotkey,
		Provider:         cfg.Transcription.Provider,
		Model:            cfg.Transcription.Model,
		Language:         cfg.Transcription.Language,
		Prompt:           cfg.Transcription.Prompt,
		AudioDevice:      cfg.Audio.Device,
		SilenceThreshold: cfg.Audio.SilenceThreshold,
		HasAPIKey:        cfg.Transcription.APIKey != "",
		WebEnabled:       cfg.Web.Enabled,
		WebPort:          cfg.Web.Port,
		DeveloperMode:    cfg.DeveloperMode,
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(sanitized)
}

// handlePutConfig updates the configuration
func (s *Server) handlePutConfig(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Hotkey           *string  `json:"hotkey"`
		Provider         *string  `json:"provider"`
		Model            *string  `json:"model"`
		Language         *string  `json:"language"`
		Prompt           *string  `json:"prompt"`
		AudioDevice      *int     `json:"audioDevice"`
		SilenceThreshold *float64 `json:"silenceThreshold"`
		APIKey           *string  `json:"apiKey"`
		WebEnabled       *bool    `json:"webEnabled"`
		WebPort          *int     `json:"webPort"`
		DeveloperMode    *bool    `json:"developerMode"`
	}

	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		http.Error(w, "Invalid request body", http.StatusBadRequest)
		return
	}

	cfg := s.GetConfig()

	// Update fields if provided
	if req.Hotkey != nil {
		cfg.Hotkey = *req.Hotkey
	}
	if req.Provider != nil {
		cfg.Transcription.Provider = *req.Provider
	}
	if req.Model != nil {
		cfg.Transcription.Model = *req.Model
	}
	if req.Language != nil {
		cfg.Transcription.Language = *req.Language
	}
	if req.Prompt != nil {
		cfg.Transcription.Prompt = *req.Prompt
	}
	if req.AudioDevice != nil {
		cfg.Audio.Device = *req.AudioDevice
	}
	if req.SilenceThreshold != nil {
		cfg.Audio.SilenceThreshold = *req.SilenceThreshold
	}
	if req.APIKey != nil && *req.APIKey != "" {
		cfg.Transcription.APIKey = *req.APIKey
	}
	if req.WebEnabled != nil {
		cfg.Web.Enabled = *req.WebEnabled
	}
	if req.WebPort != nil {
		cfg.Web.Port = *req.WebPort
	}
	if req.DeveloperMode != nil {
		cfg.DeveloperMode = *req.DeveloperMode
	}

	// Save to file
	if err := cfg.Save(); err != nil {
		slog.Error("Failed to save config", "error", err)
		http.Error(w, "Failed to save configuration", http.StatusInternalServerError)
		return
	}

	// Update in-memory config
	s.UpdateConfig(cfg)

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(map[string]string{"status": "success"})
}

// handleStats returns statistics for the specified time range
func (s *Server) handleStats(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	daysStr := r.URL.Query().Get("days")
	days := 7 // default to 7 days
	if daysStr != "" {
		if d, err := strconv.Atoi(daysStr); err == nil && d > 0 {
			days = d
		}
	}

	overall, err := s.db.GetOverallStats(days)
	if err != nil {
		slog.Error("Failed to get overall stats", "error", err)
		http.Error(w, "Failed to get statistics", http.StatusInternalServerError)
		return
	}

	daily, err := s.db.GetDailyStats(days)
	if err != nil {
		slog.Error("Failed to get daily stats", "error", err)
		http.Error(w, "Failed to get statistics", http.StatusInternalServerError)
		return
	}

	provider, err := s.db.GetProviderStats(days)
	if err != nil {
		slog.Error("Failed to get provider stats", "error", err)
		http.Error(w, "Failed to get statistics", http.StatusInternalServerError)
		return
	}

	response := map[string]interface{}{
		"overall":  overall,
		"daily":    daily,
		"provider": provider,
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(response)
}

// handleHistory handles GET and DELETE requests for dictation history
func (s *Server) handleHistory(w http.ResponseWriter, r *http.Request) {
	switch r.Method {
	case http.MethodGet:
		s.handleGetHistory(w, r)
	case http.MethodDelete:
		s.handleDeleteHistory(w, r)
	default:
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
	}
}

// handleGetHistory returns paginated dictation history
func (s *Server) handleGetHistory(w http.ResponseWriter, r *http.Request) {
	limitStr := r.URL.Query().Get("limit")
	offsetStr := r.URL.Query().Get("offset")

	limit := 50 // default
	offset := 0

	if limitStr != "" {
		if l, err := strconv.Atoi(limitStr); err == nil && l > 0 {
			limit = l
		}
	}

	if offsetStr != "" {
		if o, err := strconv.Atoi(offsetStr); err == nil && o >= 0 {
			offset = o
		}
	}

	dictations, err := s.db.GetDictations(limit, offset)
	if err != nil {
		slog.Error("Failed to get dictations", "error", err)
		http.Error(w, "Failed to get history", http.StatusInternalServerError)
		return
	}

	total, err := s.db.GetDictationCount()
	if err != nil {
		slog.Error("Failed to get dictation count", "error", err)
		http.Error(w, "Failed to get history", http.StatusInternalServerError)
		return
	}

	response := map[string]interface{}{
		"dictations": dictations,
		"total":      total,
		"limit":      limit,
		"offset":     offset,
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(response)
}

// handleDeleteHistory deletes a dictation by ID
func (s *Server) handleDeleteHistory(w http.ResponseWriter, r *http.Request) {
	// Extract ID from path (e.g., /api/history/123)
	path := r.URL.Path
	parts := strings.Split(path, "/")
	if len(parts) < 4 {
		http.Error(w, "Invalid path", http.StatusBadRequest)
		return
	}

	idStr := parts[len(parts)-1]
	id, err := strconv.ParseInt(idStr, 10, 64)
	if err != nil {
		http.Error(w, "Invalid ID", http.StatusBadRequest)
		return
	}

	if err := s.db.DeleteDictation(id); err != nil {
		slog.Error("Failed to delete dictation", "error", err, "id", id)
		http.Error(w, "Failed to delete dictation", http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(map[string]string{"status": "success"})
}

// handleStatus returns the current agent status
func (s *Server) handleStatus(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	// This will be updated by the agent
	response := map[string]string{
		"status": "idle",
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(response)
}
