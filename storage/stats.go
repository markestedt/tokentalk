package storage

import (
	"fmt"
	"time"
)

// DailyStats represents statistics for a single day
type DailyStats struct {
	Date            string
	TotalDictations int
	TotalWords      int
	SuccessCount    int
	FailureCount    int
}

// ProviderStats represents statistics grouped by provider
type ProviderStats struct {
	Provider        string
	TotalDictations int
	TotalWords      int
	SuccessCount    int
	FailureCount    int
	AvgLatencyMs    float64
}

// OverallStats represents overall statistics
type OverallStats struct {
	TotalDictations       int
	TotalWords            int
	TotalCharacters       int
	SuccessCount          int
	FailureCount          int
	AvgRecordingMs        float64
	AvgTranscriptionMs    float64
	AvgInjectionMs        float64
	AvgTotalLatencyMs     float64
	TotalRecordingTimeMs  int64
	TotalAudioSizeBytes   int64
}

// GetDailyStats retrieves statistics grouped by date for the last N days
func (db *DB) GetDailyStats(days int) ([]DailyStats, error) {
	query := `
		SELECT
			DATE(timestamp) as date,
			COUNT(*) as total_dictations,
			SUM(word_count) as total_words,
			SUM(CASE WHEN success = 1 THEN 1 ELSE 0 END) as success_count,
			SUM(CASE WHEN success = 0 THEN 1 ELSE 0 END) as failure_count
		FROM dictations
		WHERE timestamp >= datetime('now', '-' || ? || ' days')
		GROUP BY DATE(timestamp)
		ORDER BY date DESC
	`

	rows, err := db.conn.Query(query, days)
	if err != nil {
		return nil, fmt.Errorf("failed to query daily stats: %w", err)
	}
	defer rows.Close()

	var stats []DailyStats
	for rows.Next() {
		var s DailyStats
		err := rows.Scan(&s.Date, &s.TotalDictations, &s.TotalWords, &s.SuccessCount, &s.FailureCount)
		if err != nil {
			return nil, fmt.Errorf("failed to scan daily stats: %w", err)
		}
		stats = append(stats, s)
	}

	return stats, rows.Err()
}

// GetProviderStats retrieves statistics grouped by provider for the last N days
func (db *DB) GetProviderStats(days int) ([]ProviderStats, error) {
	query := `
		SELECT
			provider,
			COUNT(*) as total_dictations,
			SUM(word_count) as total_words,
			SUM(CASE WHEN success = 1 THEN 1 ELSE 0 END) as success_count,
			SUM(CASE WHEN success = 0 THEN 1 ELSE 0 END) as failure_count,
			AVG(total_latency_ms) as avg_latency_ms
		FROM dictations
		WHERE timestamp >= datetime('now', '-' || ? || ' days')
		GROUP BY provider
		ORDER BY total_dictations DESC
	`

	rows, err := db.conn.Query(query, days)
	if err != nil {
		return nil, fmt.Errorf("failed to query provider stats: %w", err)
	}
	defer rows.Close()

	var stats []ProviderStats
	for rows.Next() {
		var s ProviderStats
		err := rows.Scan(&s.Provider, &s.TotalDictations, &s.TotalWords, &s.SuccessCount, &s.FailureCount, &s.AvgLatencyMs)
		if err != nil {
			return nil, fmt.Errorf("failed to scan provider stats: %w", err)
		}
		stats = append(stats, s)
	}

	return stats, rows.Err()
}

// GetOverallStats retrieves overall statistics for the last N days
func (db *DB) GetOverallStats(days int) (*OverallStats, error) {
	query := `
		SELECT
			COUNT(*) as total_dictations,
			COALESCE(SUM(word_count), 0) as total_words,
			COALESCE(SUM(character_count), 0) as total_characters,
			SUM(CASE WHEN success = 1 THEN 1 ELSE 0 END) as success_count,
			SUM(CASE WHEN success = 0 THEN 1 ELSE 0 END) as failure_count,
			COALESCE(AVG(recording_duration_ms), 0) as avg_recording_ms,
			COALESCE(AVG(transcription_latency_ms), 0) as avg_transcription_ms,
			COALESCE(AVG(injection_latency_ms), 0) as avg_injection_ms,
			COALESCE(AVG(total_latency_ms), 0) as avg_total_latency_ms,
			COALESCE(SUM(recording_duration_ms), 0) as total_recording_time_ms,
			COALESCE(SUM(audio_size_bytes), 0) as total_audio_size_bytes
		FROM dictations
		WHERE timestamp >= datetime('now', '-' || ? || ' days')
	`

	var stats OverallStats
	err := db.conn.QueryRow(query, days).Scan(
		&stats.TotalDictations,
		&stats.TotalWords,
		&stats.TotalCharacters,
		&stats.SuccessCount,
		&stats.FailureCount,
		&stats.AvgRecordingMs,
		&stats.AvgTranscriptionMs,
		&stats.AvgInjectionMs,
		&stats.AvgTotalLatencyMs,
		&stats.TotalRecordingTimeMs,
		&stats.TotalAudioSizeBytes,
	)
	if err != nil {
		return nil, fmt.Errorf("failed to query overall stats: %w", err)
	}

	return &stats, nil
}

// GetStatsForDateRange retrieves overall stats for a custom date range
func (db *DB) GetStatsForDateRange(startTime, endTime time.Time) (*OverallStats, error) {
	query := `
		SELECT
			COUNT(*) as total_dictations,
			COALESCE(SUM(word_count), 0) as total_words,
			COALESCE(SUM(character_count), 0) as total_characters,
			SUM(CASE WHEN success = 1 THEN 1 ELSE 0 END) as success_count,
			SUM(CASE WHEN success = 0 THEN 1 ELSE 0 END) as failure_count,
			COALESCE(AVG(recording_duration_ms), 0) as avg_recording_ms,
			COALESCE(AVG(transcription_latency_ms), 0) as avg_transcription_ms,
			COALESCE(AVG(injection_latency_ms), 0) as avg_injection_ms,
			COALESCE(AVG(total_latency_ms), 0) as avg_total_latency_ms,
			COALESCE(SUM(recording_duration_ms), 0) as total_recording_time_ms,
			COALESCE(SUM(audio_size_bytes), 0) as total_audio_size_bytes
		FROM dictations
		WHERE timestamp >= ? AND timestamp <= ?
	`

	var stats OverallStats
	err := db.conn.QueryRow(query, startTime.Format(time.RFC3339), endTime.Format(time.RFC3339)).Scan(
		&stats.TotalDictations,
		&stats.TotalWords,
		&stats.TotalCharacters,
		&stats.SuccessCount,
		&stats.FailureCount,
		&stats.AvgRecordingMs,
		&stats.AvgTranscriptionMs,
		&stats.AvgInjectionMs,
		&stats.AvgTotalLatencyMs,
		&stats.TotalRecordingTimeMs,
		&stats.TotalAudioSizeBytes,
	)
	if err != nil {
		return nil, fmt.Errorf("failed to query date range stats: %w", err)
	}

	return &stats, nil
}
