package web

import (
	"embed"
	"fmt"
	"io/fs"
	"log/slog"
	"net/http"
	"sync"

	"github.com/gorilla/websocket"
	"markestedt/tokentalk/config"
	"markestedt/tokentalk/storage"
)

//go:embed static/*
var staticFiles embed.FS

var upgrader = websocket.Upgrader{
	ReadBufferSize:  1024,
	WriteBufferSize: 1024,
	CheckOrigin: func(r *http.Request) bool {
		return true // Allow all origins for local development
	},
}

// Server represents the web server
type Server struct {
	db     *storage.DB
	config *config.Config
	port   int
	hub    *Hub
	mu     sync.RWMutex
}

// NewServer creates a new web server
func NewServer(db *storage.DB, cfg *config.Config, port int) *Server {
	hub := NewHub()
	go hub.Run()

	return &Server{
		db:     db,
		config: cfg,
		port:   port,
		hub:    hub,
	}
}

// Start starts the web server
func (s *Server) Start() error {
	mux := http.NewServeMux()

	// API endpoints
	mux.HandleFunc("/api/config", s.handleConfig)
	mux.HandleFunc("/api/stats", s.handleStats)
	mux.HandleFunc("/api/history", s.handleHistory)
	mux.HandleFunc("/api/status", s.handleStatus)
	mux.HandleFunc("/ws", s.handleWebSocket)

	// Static files
	staticFS, err := fs.Sub(staticFiles, "static")
	if err != nil {
		return fmt.Errorf("failed to load static files: %w", err)
	}
	mux.Handle("/", http.FileServer(http.FS(staticFS)))

	addr := fmt.Sprintf(":%d", s.port)
	slog.Info("Starting web server", "port", s.port, "url", fmt.Sprintf("http://localhost:%d", s.port))

	return http.ListenAndServe(addr, mux)
}

// GetConfig returns the current configuration (thread-safe)
func (s *Server) GetConfig() *config.Config {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.config
}

// UpdateConfig updates the configuration (thread-safe)
func (s *Server) UpdateConfig(cfg *config.Config) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.config = cfg
}

// BroadcastStatus broadcasts a status update to all connected clients
func (s *Server) BroadcastStatus(status string) {
	s.hub.BroadcastMessage(Message{
		Type: MessageTypeStatus,
		Data: StatusMessage{Status: status},
	})
}

// BroadcastDictation broadcasts a new dictation to all connected clients
func (s *Server) BroadcastDictation(d *storage.Dictation) {
	s.hub.BroadcastMessage(Message{
		Type: MessageTypeDictation,
		Data: DictationMessage{
			ID:        d.ID,
			WordCount: d.WordCount,
			Success:   d.Success,
			Timestamp: d.Timestamp.Format("2006-01-02T15:04:05Z"),
		},
	})
}

// handleWebSocket handles WebSocket connections
func (s *Server) handleWebSocket(w http.ResponseWriter, r *http.Request) {
	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		slog.Error("Failed to upgrade WebSocket connection", "error", err)
		return
	}

	client := &Client{
		hub:  s.hub,
		conn: conn,
		send: make(chan []byte, 256),
	}

	client.hub.register <- client

	// Start client goroutines
	go client.writePump()
	go client.readPump()
}
