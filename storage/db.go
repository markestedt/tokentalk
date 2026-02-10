package storage

import (
	"database/sql"
	"fmt"
	"path/filepath"

	_ "modernc.org/sqlite"
)

type DB struct {
	conn *sql.DB
}

// Open opens the database and initializes the schema
func Open(configDir string) (*DB, error) {
	dbPath := filepath.Join(configDir, "tokentalk.db")

	conn, err := sql.Open("sqlite", dbPath)
	if err != nil {
		return nil, fmt.Errorf("failed to open database: %w", err)
	}

	// Enable WAL mode for better concurrency
	if _, err := conn.Exec("PRAGMA journal_mode=WAL"); err != nil {
		conn.Close()
		return nil, fmt.Errorf("failed to enable WAL mode: %w", err)
	}

	// Enable foreign keys
	if _, err := conn.Exec("PRAGMA foreign_keys=ON"); err != nil {
		conn.Close()
		return nil, fmt.Errorf("failed to enable foreign keys: %w", err)
	}

	db := &DB{conn: conn}

	if err := db.initSchema(); err != nil {
		conn.Close()
		return nil, fmt.Errorf("failed to initialize schema: %w", err)
	}

	return db, nil
}

// Close closes the database connection
func (db *DB) Close() error {
	return db.conn.Close()
}

// initSchema creates the database schema
func (db *DB) initSchema() error {
	schema := `
	CREATE TABLE IF NOT EXISTS dictations (
		id INTEGER PRIMARY KEY AUTOINCREMENT,
		timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,

		-- Timing metrics
		recording_start_ms INTEGER NOT NULL,
		recording_duration_ms INTEGER NOT NULL,
		transcription_latency_ms INTEGER NOT NULL,
		injection_latency_ms INTEGER NOT NULL,
		total_latency_ms INTEGER NOT NULL,

		-- Audio metadata
		audio_size_bytes INTEGER NOT NULL,
		audio_sample_rate INTEGER NOT NULL,

		-- Provider info (provider-agnostic)
		provider TEXT NOT NULL,
		model TEXT NOT NULL,
		language TEXT NOT NULL,

		-- Output
		transcribed_text TEXT NOT NULL,
		word_count INTEGER NOT NULL,
		character_count INTEGER NOT NULL,

		-- Status
		success BOOLEAN NOT NULL,
		error_message TEXT
	);

	CREATE INDEX IF NOT EXISTS idx_dictations_timestamp ON dictations(timestamp);
	CREATE INDEX IF NOT EXISTS idx_dictations_provider ON dictations(provider);
	CREATE INDEX IF NOT EXISTS idx_dictations_success ON dictations(success);
	`

	_, err := db.conn.Exec(schema)
	return err
}
