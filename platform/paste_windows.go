//go:build windows

package platform

import (
	"fmt"
	"time"
	"unsafe"
)

var (
	sendInput      = user32.NewProc("SendInput")
	getMessageW    = user32.NewProc("GetMessageW")
	mapVirtualKeyW = user32.NewProc("MapVirtualKeyW")
)

const (
	inputKeyboard     = 1
	keyeventfKeyup    = 0x0002
	keyeventfScancode = 0x0008
	mapvkVkToVsc      = 0
	vkControl         = 0x11
	vkV               = 0x56
)

type keyboardInput struct {
	wVk         uint16
	wScan       uint16
	dwFlags     uint32
	time        uint32
	dwExtraInfo uintptr
}

type input struct {
	inputType uint32
	ki        keyboardInput
	padding   [8]byte // Padding to match C struct size
}

// WindowsPaster implements the Paster interface for Windows
type WindowsPaster struct{}

// NewPaster creates a new Windows paster instance
func NewPaster() Paster {
	return &WindowsPaster{}
}

// Paste simulates Ctrl+V keypress with scan codes for better compatibility
func (p *WindowsPaster) Paste() error {
	// Get scan codes for better compatibility with elevated applications
	ctrlScan, _, _ := mapVirtualKeyW.Call(vkControl, mapvkVkToVsc)
	vScan, _, _ := mapVirtualKeyW.Call(vkV, mapvkVkToVsc)

	inputs := []input{
		// Ctrl down
		{
			inputType: inputKeyboard,
			ki: keyboardInput{
				wVk:     vkControl,
				wScan:   uint16(ctrlScan),
				dwFlags: 0,
			},
		},
		// V down
		{
			inputType: inputKeyboard,
			ki: keyboardInput{
				wVk:     vkV,
				wScan:   uint16(vScan),
				dwFlags: 0,
			},
		},
		// V up
		{
			inputType: inputKeyboard,
			ki: keyboardInput{
				wVk:     vkV,
				wScan:   uint16(vScan),
				dwFlags: keyeventfKeyup,
			},
		},
		// Ctrl up
		{
			inputType: inputKeyboard,
			ki: keyboardInput{
				wVk:     vkControl,
				wScan:   uint16(ctrlScan),
				dwFlags: keyeventfKeyup,
			},
		},
	}

	// Send all inputs at once for better atomicity
	ret, _, err := sendInput.Call(
		uintptr(len(inputs)),
		uintptr(unsafe.Pointer(&inputs[0])),
		unsafe.Sizeof(inputs[0]),
	)

	if ret == 0 {
		return fmt.Errorf("SendInput failed: %w", err)
	}

	// Small delay to ensure input is processed
	time.Sleep(20 * time.Millisecond)

	return nil
}
