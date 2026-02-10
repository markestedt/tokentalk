package postprocess

import (
	"bufio"
	"context"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

// DictionaryEntry represents either a simple term or a correction mapping
type DictionaryEntry struct {
	Original    string // Empty for simple terms, populated for mappings
	Replacement string // The correct term
	IsMapping   bool   // True if this is a correction mapping
}

// Dictionary holds all dictionary entries
type Dictionary struct {
	Entries []DictionaryEntry
}

// LoadDictionary loads a dictionary from a file
func LoadDictionary(path string) (*Dictionary, error) {
	// If path is empty, use default location
	if path == "" {
		configDir := os.Getenv("APPDATA")
		if configDir == "" {
			configDir = filepath.Join(os.Getenv("USERPROFILE"), "AppData", "Roaming")
		}
		path = filepath.Join(configDir, "tokentalk", "dictionary.txt")
	}

	// If file doesn't exist, return empty dictionary
	if _, err := os.Stat(path); os.IsNotExist(err) {
		return &Dictionary{Entries: []DictionaryEntry{}}, nil
	}

	file, err := os.Open(path)
	if err != nil {
		return nil, fmt.Errorf("failed to open dictionary: %w", err)
	}
	defer file.Close()

	var entries []DictionaryEntry
	scanner := bufio.NewScanner(file)

	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())

		// Skip empty lines and comments
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}

		// Check if this is a mapping (contains "->")
		if strings.Contains(line, "->") {
			parts := strings.Split(line, "->")
			if len(parts) == 2 {
				entries = append(entries, DictionaryEntry{
					Original:    strings.TrimSpace(parts[0]),
					Replacement: strings.TrimSpace(parts[1]),
					IsMapping:   true,
				})
			}
		} else {
			// Simple term
			entries = append(entries, DictionaryEntry{
				Replacement: line,
				IsMapping:   false,
			})
		}
	}

	if err := scanner.Err(); err != nil {
		return nil, fmt.Errorf("failed to read dictionary: %w", err)
	}

	return &Dictionary{Entries: entries}, nil
}

// SaveDictionary saves the dictionary to a file
func SaveDictionary(path string, dict *Dictionary) error {
	// If path is empty, use default location
	if path == "" {
		configDir := os.Getenv("APPDATA")
		if configDir == "" {
			configDir = filepath.Join(os.Getenv("USERPROFILE"), "AppData", "Roaming")
		}
		path = filepath.Join(configDir, "tokentalk", "dictionary.txt")
	}

	// Ensure directory exists
	dir := filepath.Dir(path)
	if err := os.MkdirAll(dir, 0755); err != nil {
		return fmt.Errorf("failed to create directory: %w", err)
	}

	file, err := os.Create(path)
	if err != nil {
		return fmt.Errorf("failed to create dictionary file: %w", err)
	}
	defer file.Close()

	writer := bufio.NewWriter(file)

	// Write header comment
	writer.WriteString("# TokenTalk Custom Dictionary\n")
	writer.WriteString("# Simple terms (bias Whisper):\n")

	// Write simple terms first
	for _, entry := range dict.Entries {
		if !entry.IsMapping {
			writer.WriteString(entry.Replacement + "\n")
		}
	}

	// Write mappings
	writer.WriteString("\n# Correction mappings (misheard -> correct):\n")
	for _, entry := range dict.Entries {
		if entry.IsMapping {
			writer.WriteString(entry.Original + " -> " + entry.Replacement + "\n")
		}
	}

	return writer.Flush()
}

// GetSimpleTerms returns all simple (non-mapping) terms for use in Whisper prompt
func (d *Dictionary) GetSimpleTerms() []string {
	var terms []string
	for _, entry := range d.Entries {
		if !entry.IsMapping {
			terms = append(terms, entry.Replacement)
		}
	}
	return terms
}

// GetMappings returns all correction mappings
func (d *Dictionary) GetMappings() map[string]string {
	mappings := make(map[string]string)
	for _, entry := range d.Entries {
		if entry.IsMapping {
			mappings[entry.Original] = entry.Replacement
		}
	}
	return mappings
}

// DictionaryProcessor creates a processor that applies dictionary corrections
func DictionaryProcessor(dict *Dictionary) Processor {
	return func(ctx context.Context, text string) (string, error) {
		if dict == nil {
			return text, nil
		}

		result := text

		// Apply all mappings (case-insensitive search, preserve replacement case)
		for _, entry := range dict.Entries {
			if entry.IsMapping {
				// Find and replace case-insensitively
				lowerResult := strings.ToLower(result)
				lowerOriginal := strings.ToLower(entry.Original)

				// Find all occurrences
				startPos := 0
				for {
					index := strings.Index(lowerResult[startPos:], lowerOriginal)
					if index == -1 {
						break
					}
					actualIndex := startPos + index

					// Replace in the original string
					result = result[:actualIndex] + entry.Replacement + result[actualIndex+len(entry.Original):]

					// Update lowercase version and continue searching
					lowerResult = strings.ToLower(result)
					startPos = actualIndex + len(entry.Replacement)
				}
			}
		}

		return result, nil
	}
}
