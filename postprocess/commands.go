package postprocess

import (
	"context"
	"strings"
	"unicode"
)

// VoiceCommand represents a voice command mapping
type VoiceCommand struct {
	Phrase      string
	Replacement string
}

// DefaultVoiceCommands returns the standard set of voice commands
func DefaultVoiceCommands() []VoiceCommand {
	return []VoiceCommand{
		{"new line", "\n"},
		{"newline", "\n"},
		{"new paragraph", "\n\n"},
		{"full stop", "."},
		{"dot", "."},
		{"comma", ","},
		{"question mark", "?"},
		{"exclamation mark", "!"},
		{"exclamation point", "!"},
		{"colon", ":"},
		{"semicolon", ";"},
		{"open quote", "\""},
		{"close quote", "\""},
		{"open parenthesis", "("},
		{"close parenthesis", ")"},
		{"open bracket", "["},
		{"close bracket", "]"},
		{"open brace", "{"},
		{"close brace", "}"},
		{"dash", "-"},
		{"underscore", "_"},
		{"slash", "/"},
		{"backslash", "\\"},
		{"at sign space", "@ "}, // @ with space (check this first before "at sign")
		{"at sign", "@"},
		{"at-sign", "@"},
		{"atsign", "@"},
		{"hash", "#"},
		{"dollar sign", "$"},
		{"percent sign", "%"},
		{"ampersand", "&"},
		{"asterisk", "*"},
		{"plus", "+"},
		{"equals", "="},
	}
}

// CommandProcessor creates a processor that replaces voice commands
// Only replaces commands that are complete words (respects word boundaries)
func CommandProcessor(commands []VoiceCommand) Processor {
	return func(ctx context.Context, text string) (string, error) {
		result := text

		// Apply each command replacement with word boundary checking
		for _, cmd := range commands {
			result = replaceWithWordBoundaries(result, cmd.Phrase, cmd.Replacement)

			// Also try with capital first letter
			if len(cmd.Phrase) > 0 {
				capitalized := strings.ToUpper(cmd.Phrase[:1]) + cmd.Phrase[1:]
				result = replaceWithWordBoundaries(result, capitalized, cmd.Replacement)
			}
		}

		return result, nil
	}
}

// replaceWithWordBoundaries replaces phrase with replacement only when phrase is surrounded by word boundaries
func replaceWithWordBoundaries(text, phrase, replacement string) string {
	if phrase == "" {
		return text
	}

	lowerText := strings.ToLower(text)
	lowerPhrase := strings.ToLower(phrase)

	var result strings.Builder
	lastIndex := 0

	for {
		// Find next occurrence (case-insensitive)
		index := strings.Index(lowerText[lastIndex:], lowerPhrase)
		if index == -1 {
			// No more matches, append the rest
			result.WriteString(text[lastIndex:])
			break
		}

		actualIndex := lastIndex + index

		// Check if this is a word boundary match
		// Check character before
		beforeIsBoundary := actualIndex == 0 || isWordBoundary(rune(text[actualIndex-1]))

		// Check character after
		afterIndex := actualIndex + len(phrase)
		afterIsBoundary := afterIndex >= len(text) || isWordBoundary(rune(text[afterIndex]))

		// Only replace if both before and after are boundaries
		if beforeIsBoundary && afterIsBoundary {
			// Append text before match
			result.WriteString(text[lastIndex:actualIndex])
			// Append replacement
			result.WriteString(replacement)
			lastIndex = afterIndex
		} else {
			// Not a word boundary match, skip this occurrence
			result.WriteString(text[lastIndex : actualIndex+1])
			lastIndex = actualIndex + 1
		}
	}

	return result.String()
}

// isWordBoundary returns true if the character is a word boundary
func isWordBoundary(r rune) bool {
	// Space, punctuation, or other non-letter/non-digit characters
	return unicode.IsSpace(r) || unicode.IsPunct(r) || !unicode.IsLetter(r) && !unicode.IsDigit(r)
}
