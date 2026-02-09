package platform

import (
	"context"
)

// KeyCombo represents a keyboard key combination
type KeyCombo struct {
	Ctrl  bool
	Shift bool
	Alt   bool
	Win   bool
	Key   int // Virtual key code
}

// EventType represents the type of hotkey event
type EventType int

const (
	Pressed EventType = iota
	Released
)

// Event represents a hotkey event
type Event struct {
	Type EventType
}

// Hotkey provides global hotkey detection
type Hotkey interface {
	Listen(ctx context.Context, combo KeyCombo) (<-chan Event, error)
}

// Clipboard provides clipboard access
type Clipboard interface {
	Get() (string, error)
	Set(text string) error
}

// Paster simulates paste operation
type Paster interface {
	Paste() error
}
