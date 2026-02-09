//go:build windows

package platform

import (
	"context"
	"fmt"
	"runtime"
	"sync"
	"unsafe"

	"golang.org/x/sys/windows"
)

var (
	setWindowsHookEx    = user32.NewProc("SetWindowsHookExW")
	callNextHookEx      = user32.NewProc("CallNextHookEx")
	unhookWindowsHookEx = user32.NewProc("UnhookWindowsHookEx")
	getMessage          = user32.NewProc("GetMessageW")
	peekMessage         = user32.NewProc("PeekMessageW")
	getAsyncKeyState    = user32.NewProc("GetAsyncKeyState")
)

const (
	whKeyboardLL = 13
	wmKeydown    = 0x0100
	wmKeyup      = 0x0101
	wmSyskeydown = 0x0104
	wmSyskeyup   = 0x0105
	pmRemove     = 0x0001
)

const (
	vkShift = 0x10
	vkCtrl  = 0x11
	vkAlt   = 0x12
	vkLwin  = 0x5B // Left Windows key
	vkRwin  = 0x5C // Right Windows key
)

type kbdllhookstruct struct {
	vkCode      uint32
	scanCode    uint32
	flags       uint32
	time        uint32
	dwExtraInfo uintptr
}

type msg struct {
	hwnd    uintptr
	message uint32
	wParam  uintptr
	lParam  uintptr
	time    uint32
	pt      struct{ x, y int32 }
}

// WindowsHotkey implements the Hotkey interface for Windows
type WindowsHotkey struct {
	mu      sync.Mutex
	combo   KeyCombo
	pressed bool
	events  chan Event
	hook    uintptr
	done    chan struct{}
}

// NewHotkey creates a new Windows hotkey listener
func NewHotkey() Hotkey {
	return &WindowsHotkey{}
}

// Listen starts listening for the specified key combination
func (h *WindowsHotkey) Listen(ctx context.Context, combo KeyCombo) (<-chan Event, error) {
	h.mu.Lock()
	h.combo = combo
	h.pressed = false
	h.events = make(chan Event, 10)
	h.done = make(chan struct{})
	h.mu.Unlock()

	// Start hook in a goroutine
	errCh := make(chan error, 1)
	go h.runHook(errCh)

	// Wait for hook to be installed or error
	select {
	case err := <-errCh:
		if err != nil {
			return nil, err
		}
	case <-ctx.Done():
		return nil, ctx.Err()
	}

	// Monitor context cancellation
	go func() {
		<-ctx.Done()
		close(h.done)
		if h.hook != 0 {
			unhookWindowsHookEx.Call(h.hook)
		}
	}()

	return h.events, nil
}

func (h *WindowsHotkey) runHook(errCh chan<- error) {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	// Install keyboard hook
	hookProc := func(nCode int32, wParam uintptr, lParam uintptr) uintptr {
		if nCode >= 0 {
			kbInfo := (*kbdllhookstruct)(unsafe.Pointer(lParam))
			h.handleKeyEvent(wParam, kbInfo)
		}
		r, _, _ := callNextHookEx.Call(0, uintptr(nCode), wParam, lParam)
		return r
	}

	hook, _, err := setWindowsHookEx.Call(
		whKeyboardLL,
		windows.NewCallback(hookProc),
		0,
		0,
	)

	if hook == 0 {
		errCh <- fmt.Errorf("SetWindowsHookEx failed: %w", err)
		return
	}

	h.mu.Lock()
	h.hook = hook
	h.mu.Unlock()

	errCh <- nil

	// Message loop
	var m msg
	for {
		select {
		case <-h.done:
			return
		default:
			// Non-blocking peek
			r, _, _ := peekMessage.Call(
				uintptr(unsafe.Pointer(&m)),
				0,
				0,
				0,
				pmRemove,
			)
			if r != 0 {
				// Process message if available
				continue
			}
			// Small sleep to prevent busy loop
			runtime.Gosched()
		}
	}
}

func (h *WindowsHotkey) handleKeyEvent(wParam uintptr, kbInfo *kbdllhookstruct) {
	isKeyDown := wParam == wmKeydown || wParam == wmSyskeydown

	// Check if this is a modifier-only combo (Key == 0)
	if h.combo.Key == 0 {
		// For modifier-only combos, check if this key is one of our modifiers
		isOurModifier := false
		if h.combo.Ctrl && kbInfo.vkCode == vkCtrl {
			isOurModifier = true
		}
		if h.combo.Shift && kbInfo.vkCode == vkShift {
			isOurModifier = true
		}
		if h.combo.Alt && kbInfo.vkCode == vkAlt {
			isOurModifier = true
		}
		if h.combo.Win && (kbInfo.vkCode == vkLwin || kbInfo.vkCode == vkRwin) {
			isOurModifier = true
		}

		if !isOurModifier {
			return
		}

		if isKeyDown {
			// Check if all required modifiers are now pressed
			if h.checkModifiers() {
				h.mu.Lock()
				if !h.pressed {
					h.pressed = true
					h.mu.Unlock()
					select {
					case h.events <- Event{Type: Pressed}:
					default:
					}
				} else {
					h.mu.Unlock()
				}
			}
		} else {
			// Key up - if we were pressed, release
			h.mu.Lock()
			if h.pressed {
				h.pressed = false
				h.mu.Unlock()
				select {
				case h.events <- Event{Type: Released}:
				default:
				}
			} else {
				h.mu.Unlock()
			}
		}
		return
	}

	// Standard key combo (with a specific key)
	if kbInfo.vkCode == uint32(h.combo.Key) {
		if isKeyDown {
			// Check if all modifiers are pressed
			if h.checkModifiers() {
				h.mu.Lock()
				if !h.pressed {
					h.pressed = true
					h.mu.Unlock()
					select {
					case h.events <- Event{Type: Pressed}:
					default:
					}
				} else {
					h.mu.Unlock()
				}
			}
		} else {
			// Key up
			h.mu.Lock()
			if h.pressed {
				h.pressed = false
				h.mu.Unlock()
				select {
				case h.events <- Event{Type: Released}:
				default:
				}
			} else {
				h.mu.Unlock()
			}
		}
	}
}

func (h *WindowsHotkey) checkModifiers() bool {
	ctrl := h.isKeyPressed(vkCtrl)
	shift := h.isKeyPressed(vkShift)
	alt := h.isKeyPressed(vkAlt)
	win := h.isKeyPressed(vkLwin) || h.isKeyPressed(vkRwin)

	return ctrl == h.combo.Ctrl &&
		shift == h.combo.Shift &&
		alt == h.combo.Alt &&
		win == h.combo.Win
}

func (h *WindowsHotkey) isKeyPressed(vk int) bool {
	r, _, _ := getAsyncKeyState.Call(uintptr(vk))
	return r&0x8000 != 0
}

// VKCode returns the Windows virtual key code for a key name
// Returns 0 for empty string (modifier-only hotkey)
func VKCode(key string) (int, error) {
	// Empty key means modifier-only combo
	if key == "" {
		return 0, nil
	}

	// Map common keys to VK codes
	codes := map[string]int{
		"v": 0x56,
		"a": 0x41, "b": 0x42, "c": 0x43, "d": 0x44, "e": 0x45,
		"f": 0x46, "g": 0x47, "h": 0x48, "i": 0x49, "j": 0x4A,
		"k": 0x4B, "l": 0x4C, "m": 0x4D, "n": 0x4E, "o": 0x4F,
		"p": 0x50, "q": 0x51, "r": 0x52, "s": 0x53, "t": 0x54,
		"u": 0x55, "w": 0x57, "x": 0x58, "y": 0x59, "z": 0x5A,
		"0": 0x30, "1": 0x31, "2": 0x32, "3": 0x33, "4": 0x34,
		"5": 0x35, "6": 0x36, "7": 0x37, "8": 0x38, "9": 0x39,
		"f1": 0x70, "f2": 0x71, "f3": 0x72, "f4": 0x73,
		"f5": 0x74, "f6": 0x75, "f7": 0x76, "f8": 0x77,
		"f9": 0x78, "f10": 0x79, "f11": 0x7A, "f12": 0x7B,
		"space": 0x20, "enter": 0x0D, "esc": 0x1B,
		"tab": 0x09, "backspace": 0x08,
	}

	if code, ok := codes[key]; ok {
		return code, nil
	}

	return 0, fmt.Errorf("unknown key: %s", key)
}
