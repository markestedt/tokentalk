package storage

import (
	"database/sql"
	"fmt"
	"time"
)

// Dictation represents a single dictation event with all metrics
type Dictation struct {
	ID                     int64
	Timestamp              time.Time
	RecordingStartMs       int64
	RecordingDurationMs    int64
	TranscriptionLatencyMs int64
	InjectionLatencyMs     int64
	TotalLatencyMs         int64
	AudioSizeBytes         int64
	AudioSampleRate        uint32
	Provider               string
	Model                  string
	Language               string
	TranscribedText        string
	WordCount              int
	CharacterCount         int
	Success                bool
	ErrorMessage           string
}

// SaveDictation saves a dictation to the database
func (db *DB) SaveDictation(d *Dictation) error {
	query := `
		INSERT INTO dictations (
			recording_start_ms, recording_duration_ms, transcription_latency_ms,
			injection_latency_ms, total_latency_ms, audio_size_bytes, audio_sample_rate,
			provider, model, language, transcribed_text, word_count, character_count,
			success, error_message
		) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
	`

	result, err := db.conn.Exec(query,
		d.RecordingStartMs, d.RecordingDurationMs, d.TranscriptionLatencyMs,
		d.InjectionLatencyMs, d.TotalLatencyMs, d.AudioSizeBytes, d.AudioSampleRate,
		d.Provider, d.Model, d.Language, d.TranscribedText, d.WordCount, d.CharacterCount,
		d.Success, d.ErrorMessage,
	)
	if err != nil {
		return fmt.Errorf("failed to save dictation: %w", err)
	}

	id, err := result.LastInsertId()
	if err != nil {
		return fmt.Errorf("failed to get last insert ID: %w", err)
	}

	d.ID = id
	return nil
}

// GetDictations retrieves dictations with pagination
func (db *DB) GetDictations(limit, offset int) ([]Dictation, error) {
	query := `
		SELECT
			id, timestamp, recording_start_ms, recording_duration_ms, transcription_latency_ms,
			injection_latency_ms, total_latency_ms, audio_size_bytes, audio_sample_rate,
			provider, model, language, transcribed_text, word_count, character_count,
			success, error_message
		FROM dictations
		ORDER BY timestamp DESC
		LIMIT ? OFFSET ?
	`

	rows, err := db.conn.Query(query, limit, offset)
	if err != nil {
		return nil, fmt.Errorf("failed to query dictations: %w", err)
	}
	defer rows.Close()

	var dictations []Dictation
	for rows.Next() {
		var d Dictation
		var errorMessage sql.NullString

		err := rows.Scan(
			&d.ID, &d.Timestamp, &d.RecordingStartMs, &d.RecordingDurationMs, &d.TranscriptionLatencyMs,
			&d.InjectionLatencyMs, &d.TotalLatencyMs, &d.AudioSizeBytes, &d.AudioSampleRate,
			&d.Provider, &d.Model, &d.Language, &d.TranscribedText, &d.WordCount, &d.CharacterCount,
			&d.Success, &errorMessage,
		)
		if err != nil {
			return nil, fmt.Errorf("failed to scan dictation: %w", err)
		}

		if errorMessage.Valid {
			d.ErrorMessage = errorMessage.String
		}

		dictations = append(dictations, d)
	}

	return dictations, rows.Err()
}

// DeleteDictation deletes a dictation by ID
func (db *DB) DeleteDictation(id int64) error {
	query := `DELETE FROM dictations WHERE id = ?`

	result, err := db.conn.Exec(query, id)
	if err != nil {
		return fmt.Errorf("failed to delete dictation: %w", err)
	}

	rowsAffected, err := result.RowsAffected()
	if err != nil {
		return fmt.Errorf("failed to get rows affected: %w", err)
	}

	if rowsAffected == 0 {
		return fmt.Errorf("dictation not found")
	}

	return nil
}

// GetDictationCount returns the total number of dictations
func (db *DB) GetDictationCount() (int, error) {
	var count int
	err := db.conn.QueryRow("SELECT COUNT(*) FROM dictations").Scan(&count)
	return count, err
}
