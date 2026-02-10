package web

import (
	"encoding/json"
	"log/slog"
	"net/http"
	"strconv"
	"strings"

	"markestedt/tokentalk/postprocess"
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
		Hotkey                 string  `json:"hotkey"`
		Provider               string  `json:"provider"`
		Model                  string  `json:"model"`
		Language               string  `json:"language"`
		Prompt                 string  `json:"prompt"`
		AudioDevice            int     `json:"audioDevice"`
		SilenceThreshold       float64 `json:"silenceThreshold"`
		HasAPIKey              bool    `json:"hasApiKey"`
		WebEnabled             bool    `json:"webEnabled"`
		WebPort                int     `json:"webPort"`
		DeveloperMode          bool    `json:"developerMode"`
		PostprocessingEnabled  bool    `json:"postprocessingEnabled"`
		PostprocessingCommands bool    `json:"postprocessingCommands"`
		PostprocessingGrammar  bool    `json:"postprocessingGrammar"`
		GrammarProvider        string  `json:"grammarProvider"`
		GrammarModel           string  `json:"grammarModel"`
	}{
		Hotkey:                 cfg.Hotkey,
		Provider:               cfg.Transcription.Provider,
		Model:                  cfg.Transcription.Model,
		Language:               cfg.Transcription.Language,
		Prompt:                 cfg.Transcription.Prompt,
		AudioDevice:            cfg.Audio.Device,
		SilenceThreshold:       cfg.Audio.SilenceThreshold,
		HasAPIKey:              cfg.Transcription.APIKey != "",
		WebEnabled:             cfg.Web.Enabled,
		WebPort:                cfg.Web.Port,
		DeveloperMode:          cfg.DeveloperMode,
		PostprocessingEnabled:  cfg.Postprocessing.Enabled,
		PostprocessingCommands: cfg.Postprocessing.Commands,
		PostprocessingGrammar:  cfg.Postprocessing.Grammar,
		GrammarProvider:        cfg.Postprocessing.GrammarProvider,
		GrammarModel:           cfg.Postprocessing.GrammarModel,
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(sanitized)
}

// handlePutConfig updates the configuration
func (s *Server) handlePutConfig(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Hotkey                 *string  `json:"hotkey"`
		Provider               *string  `json:"provider"`
		Model                  *string  `json:"model"`
		Language               *string  `json:"language"`
		Prompt                 *string  `json:"prompt"`
		AudioDevice            *int     `json:"audioDevice"`
		SilenceThreshold       *float64 `json:"silenceThreshold"`
		APIKey                 *string  `json:"apiKey"`
		WebEnabled             *bool    `json:"webEnabled"`
		WebPort                *int     `json:"webPort"`
		DeveloperMode          *bool    `json:"developerMode"`
		PostprocessingEnabled  *bool    `json:"postprocessingEnabled"`
		PostprocessingCommands *bool    `json:"postprocessingCommands"`
		PostprocessingGrammar  *bool    `json:"postprocessingGrammar"`
		GrammarProvider        *string  `json:"grammarProvider"`
		GrammarModel           *string  `json:"grammarModel"`
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
	if req.PostprocessingEnabled != nil {
		cfg.Postprocessing.Enabled = *req.PostprocessingEnabled
	}
	if req.PostprocessingCommands != nil {
		cfg.Postprocessing.Commands = *req.PostprocessingCommands
	}
	if req.PostprocessingGrammar != nil {
		cfg.Postprocessing.Grammar = *req.PostprocessingGrammar
	}
	if req.GrammarProvider != nil {
		cfg.Postprocessing.GrammarProvider = *req.GrammarProvider
	}
	if req.GrammarModel != nil {
		cfg.Postprocessing.GrammarModel = *req.GrammarModel
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

	slog.Info("Delete history request", "path", path, "parts", parts, "len", len(parts))

	if len(parts) < 4 {
		slog.Error("Invalid path for delete", "path", path, "parts", parts)
		http.Error(w, "Invalid path", http.StatusBadRequest)
		return
	}

	idStr := parts[len(parts)-1]
	id, err := strconv.ParseInt(idStr, 10, 64)
	if err != nil {
		slog.Error("Invalid ID format", "idStr", idStr, "error", err)
		http.Error(w, "Invalid ID", http.StatusBadRequest)
		return
	}

	slog.Info("Attempting to delete dictation", "id", id)

	if err := s.db.DeleteDictation(id); err != nil {
		slog.Error("Failed to delete dictation", "error", err, "id", id)
		http.Error(w, "Failed to delete dictation", http.StatusInternalServerError)
		return
	}

	slog.Info("Successfully deleted dictation", "id", id)

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

// handleDictionary handles GET and PUT requests for the dictionary
func (s *Server) handleDictionary(w http.ResponseWriter, r *http.Request) {
	switch r.Method {
	case http.MethodGet:
		s.handleGetDictionary(w, r)
	case http.MethodPut:
		s.handlePutDictionary(w, r)
	default:
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
	}
}

// handleGetDictionary returns the current dictionary
func (s *Server) handleGetDictionary(w http.ResponseWriter, r *http.Request) {
	cfg := s.GetConfig()

	// Load dictionary
	dict, err := postprocess.LoadDictionary(cfg.Postprocessing.DictionaryFile)
	if err != nil {
		slog.Error("Failed to load dictionary", "error", err)
		http.Error(w, "Failed to load dictionary", http.StatusInternalServerError)
		return
	}

	// Convert to API format
	type DictionaryEntry struct {
		Original    string `json:"original"`
		Replacement string `json:"replacement"`
		IsMapping   bool   `json:"isMapping"`
	}

	var entries []DictionaryEntry
	for _, entry := range dict.Entries {
		entries = append(entries, DictionaryEntry{
			Original:    entry.Original,
			Replacement: entry.Replacement,
			IsMapping:   entry.IsMapping,
		})
	}

	response := map[string]interface{}{
		"entries": entries,
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(response)
}

// handlePutDictionary updates the dictionary
func (s *Server) handlePutDictionary(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Entries []struct {
			Original    string `json:"original"`
			Replacement string `json:"replacement"`
			IsMapping   bool   `json:"isMapping"`
		} `json:"entries"`
	}

	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		http.Error(w, "Invalid request body", http.StatusBadRequest)
		return
	}

	// Convert to dictionary format
	var entries []postprocess.DictionaryEntry
	for _, entry := range req.Entries {
		entries = append(entries, postprocess.DictionaryEntry{
			Original:    entry.Original,
			Replacement: entry.Replacement,
			IsMapping:   entry.IsMapping,
		})
	}

	dict := &postprocess.Dictionary{
		Entries: entries,
	}

	// Save dictionary
	cfg := s.GetConfig()
	if err := postprocess.SaveDictionary(cfg.Postprocessing.DictionaryFile, dict); err != nil {
		slog.Error("Failed to save dictionary", "error", err)
		http.Error(w, "Failed to save dictionary", http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(map[string]string{"status": "success"})
}
