package systray

import (
	"fmt"
	"log/slog"
	"os/exec"
	"runtime"

	"github.com/getlantern/systray"
)

// SystrayManager manages the system tray icon and menu
type SystrayManager struct {
	webPort int
	iconData []byte
	quit chan struct{}
}

// NewSystrayManager creates a new systray manager
func NewSystrayManager(webPort int, iconData []byte) *SystrayManager {
	return &SystrayManager{
		webPort: webPort,
		iconData: iconData,
		quit: make(chan struct{}),
	}
}

// Run starts the system tray (blocking call)
func (m *SystrayManager) Run() {
	systray.Run(m.onReady, m.onExit)
}

// Stop stops the system tray
func (m *SystrayManager) Stop() {
	systray.Quit()
}

// WaitForQuit returns a channel that will be closed when user clicks Quit
func (m *SystrayManager) WaitForQuit() <-chan struct{} {
	return m.quit
}

// onReady is called when the systray is ready
func (m *SystrayManager) onReady() {
	// Set icon
	if len(m.iconData) > 0 {
		systray.SetIcon(m.iconData)
	}

	// Set tooltip
	systray.SetTitle("TokenTalk")
	systray.SetTooltip("TokenTalk - Voice Dictation")

	// Add menu items
	mOpenWebUI := systray.AddMenuItem("Open Web UI", "Open the TokenTalk web dashboard")
	systray.AddSeparator()
	mQuit := systray.AddMenuItem("Quit", "Exit TokenTalk")

	// Handle menu clicks
	go func() {
		for {
			select {
			case <-mOpenWebUI.ClickedCh:
				m.openWebUI()
			case <-mQuit.ClickedCh:
				slog.Info("User requested quit from system tray")
				close(m.quit)
				systray.Quit()
				return
			}
		}
	}()
}

// onExit is called when the systray is exiting
func (m *SystrayManager) onExit() {
	slog.Info("System tray exited")
}

// openWebUI opens the web UI in the default browser
func (m *SystrayManager) openWebUI() {
	url := fmt.Sprintf("http://localhost:%d", m.webPort)
	slog.Info("Opening web UI", "url", url)

	var cmd *exec.Cmd
	switch runtime.GOOS {
	case "windows":
		cmd = exec.Command("cmd", "/c", "start", url)
	case "darwin":
		cmd = exec.Command("open", url)
	case "linux":
		cmd = exec.Command("xdg-open", url)
	default:
		slog.Error("Unsupported platform for opening browser", "platform", runtime.GOOS)
		return
	}

	if err := cmd.Start(); err != nil {
		slog.Error("Failed to open web UI", "error", err)
	}
}
