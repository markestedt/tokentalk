//go:build windows

package platform

import (
	"fmt"
	"syscall"
	"time"
	"unsafe"

	"golang.org/x/sys/windows"
)

var (
	user32           = windows.NewLazySystemDLL("user32.dll")
	kernel32         = windows.NewLazySystemDLL("kernel32.dll")
	openClipboard    = user32.NewProc("OpenClipboard")
	closeClipboard   = user32.NewProc("CloseClipboard")
	emptyClipboard   = user32.NewProc("EmptyClipboard")
	getClipboardData = user32.NewProc("GetClipboardData")
	setClipboardData = user32.NewProc("SetClipboardData")
	globalAlloc      = kernel32.NewProc("GlobalAlloc")
	globalLock       = kernel32.NewProc("GlobalLock")
	globalUnlock     = kernel32.NewProc("GlobalUnlock")
)

const (
	cfUnicodeText = 13
	gmemMoveable  = 0x0002
)

// WindowsClipboard implements the Clipboard interface for Windows
type WindowsClipboard struct{}

// NewClipboard creates a new Windows clipboard instance
func NewClipboard() Clipboard {
	return &WindowsClipboard{}
}

// Get retrieves text from the clipboard
func (c *WindowsClipboard) Get() (string, error) {
	if err := c.open(); err != nil {
		return "", err
	}
	defer c.close()

	h, _, err := getClipboardData.Call(cfUnicodeText)
	if h == 0 {
		if err != nil && err != syscall.Errno(0) {
			return "", fmt.Errorf("GetClipboardData failed: %w", err)
		}
		return "", nil // No text data
	}

	l, _, err := globalLock.Call(h)
	if l == 0 {
		return "", fmt.Errorf("GlobalLock failed: %w", err)
	}
	defer globalUnlock.Call(h)

	text := windows.UTF16PtrToString((*uint16)(unsafe.Pointer(l)))
	return text, nil
}

// Set sets text to the clipboard
func (c *WindowsClipboard) Set(text string) error {
	if err := c.open(); err != nil {
		return err
	}
	defer c.close()

	emptyClipboard.Call()

	utf16, err := windows.UTF16FromString(text)
	if err != nil {
		return fmt.Errorf("UTF16 conversion failed: %w", err)
	}

	n := len(utf16) * 2 // UTF-16 uses 2 bytes per character
	h, _, err := globalAlloc.Call(gmemMoveable, uintptr(n))
	if h == 0 {
		return fmt.Errorf("GlobalAlloc failed: %w", err)
	}

	l, _, err := globalLock.Call(h)
	if l == 0 {
		return fmt.Errorf("GlobalLock failed: %w", err)
	}

	// Copy data
	dest := unsafe.Slice((*uint16)(unsafe.Pointer(l)), len(utf16))
	copy(dest, utf16)

	globalUnlock.Call(h)

	r, _, err := setClipboardData.Call(cfUnicodeText, h)
	if r == 0 {
		return fmt.Errorf("SetClipboardData failed: %w", err)
	}

	return nil
}

func (c *WindowsClipboard) open() error {
	// Try to open clipboard with retries
	for i := 0; i < 10; i++ {
		r, _, _ := openClipboard.Call(0)
		if r != 0 {
			return nil
		}
		time.Sleep(10 * time.Millisecond)
	}
	return fmt.Errorf("failed to open clipboard after retries")
}

func (c *WindowsClipboard) close() {
	closeClipboard.Call()
}
