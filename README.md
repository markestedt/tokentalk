# TokenTalk

TokenTalk is a push-to-talk voice dictation tool for Windows. Press a global hotkey, speak, and have your words transcribed and pasted into any application.

## Features

- **Global Hotkey**: Press Ctrl+Shift+V (configurable) to start/stop recording
- **Push-to-Talk**: Hold the hotkey while speaking, release to transcribe
- **Universal**: Works in any Windows application
- **Multiple Providers**: OpenAI Whisper API or local whisper.cpp (coming soon)
- **Simple Configuration**: TOML-based configuration file

## Quick Start

1. **Build the application** (if not already built):
   ```bash
   go build -o tokentalk.exe .
   ```

2. **Run TokenTalk for the first time**:
   ```bash
   tokentalk.exe
   ```

   This will create a default configuration file at:
   `%APPDATA%\tokentalk\config.toml`

3. **Configure your OpenAI API key**:
   - Open the config file in a text editor
   - Set your `openai_api_key` in the `[transcription]` section
   - Save the file

4. **Run TokenTalk again**:
   ```bash
   tokentalk.exe
   ```

5. **Use it**:
   - Click in any text field (Notepad, browser, VS Code, etc.)
   - Press and hold the configured hotkey
   - Speak (like you would speak normally)
   - Release the keys
   - Your transcribed text will be pasted automatically!

## Configuration

The configuration file is located at `%APPDATA%\tokentalk\config.toml` (typically `C:\Users\YourName\AppData\Roaming\tokentalk\config.toml`).

### Default Configuration

```toml
[hotkey]
combo = "ctrl+shift+v"

[audio]
device = ""  # Empty = system default microphone
max_seconds = 120

[transcription]
provider = "openai"  # Options: "openai", "whisper" (whisper not yet implemented)
model = "whisper-1"  # OpenAI model
language = "en"      # Language code (e.g., "en", "es", "fr")
openai_api_key = ""  # REQUIRED: Your OpenAI API key
whisper_model_dir = "C:\\Users\\YourName\\AppData\\Roaming\\tokentalk\\models"
```

### Configuration Options

#### `[hotkey]`
- `combo`: Keyboard combination to activate recording
  - Format: `"modifier+modifier+key"`
  - Modifiers: `ctrl`, `shift`, `alt`, `win`
  - Examples: `"ctrl+shift+v"`, `"alt+space"`, `"ctrl+alt+r"`

#### `[audio]`
- `device`: Microphone device ID (empty string uses system default)
- `max_seconds`: Maximum recording duration in seconds (default: 120)

#### `[transcription]`
- `provider`: Transcription provider (`"openai"` or `"whisper"`)
- `model`: Model to use:
  - For OpenAI: `"whisper-1"` (only option currently)
  - For whisper.cpp: `"tiny"`, `"base"`, `"small"`, `"medium"`, `"large"` (not yet implemented)
- `language`: ISO language code (`"en"`, `"es"`, `"fr"`, etc.)
- `openai_api_key`: Your OpenAI API key (required for OpenAI provider)
- `whisper_model_dir`: Directory for local whisper models (for future whisper.cpp support)

## Getting an OpenAI API Key

1. Go to https://platform.openai.com/api-keys
2. Sign in or create an account
3. Click "Create new secret key"
4. Copy the key and paste it into your config file
5. Note: OpenAI charges for API usage. Check their pricing at https://openai.com/pricing

## How It Works

1. **Hotkey Detection**: TokenTalk listens for your configured hotkey combination globally
2. **Recording**: When you press the hotkey, it starts recording from your microphone
3. **Transcription**: When you release the hotkey, it sends the audio to the transcription provider
4. **Injection**: The transcribed text is copied to your clipboard and pasted automatically

### Text Injection Process

To paste text into any application, TokenTalk:
1. Saves your current clipboard content
2. Sets the clipboard to the transcribed text
3. Simulates Ctrl+V keypress
4. Restores your original clipboard content

This happens in ~150ms, so you rarely lose your clipboard data.

## Troubleshooting

### "OpenAI API key is required"
- Make sure you've set `openai_api_key` in your config file
- The config file is at `%APPDATA%\tokentalk\config.toml`

### "Failed to start recording"
- Check that your microphone is connected and working
- Try setting a specific `device` in the `[audio]` section
- Make sure no other application has exclusive access to your microphone

### Hotkey not working
- Make sure the key combination isn't already used by another application
- Try a different combination in the config file
- Check that TokenTalk is running (you should see log messages when you start it)

### Text not pasting
- Make sure you click in a text field before pressing the hotkey
- Some applications (like some games) may block simulated input
- Try in a simple application like Notepad first to verify it works

### Recording too short / "Recording too short, ignoring"
- Hold the hotkey for at least 100ms before releasing
- If you're trying to record a very short word, try holding the key a bit longer

## Building from Source

### Requirements
- Go 1.21 or later
- Windows (Linux support coming in M2)

### Dependencies
- `github.com/BurntSushi/toml` - TOML configuration parsing
- `github.com/gen2brain/malgo` - Cross-platform audio I/O
- `golang.org/x/sys/windows` - Windows API bindings

### Build Commands

**Standard build (OpenAI only, no CGo)**:
```bash
go build -o tokentalk.exe .
```

**With whisper.cpp support** (when implemented):
```bash
set CGO_ENABLED=1
go build -o tokentalk.exe .
```

## Roadmap

### M1 (Current)
- ✅ Global hotkey detection (Ctrl+Shift+V)
- ✅ Audio recording
- ✅ OpenAI Whisper API transcription
- ✅ Text injection via clipboard
- ✅ TOML configuration
- ⏳ Local whisper.cpp support (placeholder implemented)

### M2 (Planned)
- Linux support
- Web GUI for configuration and history
- SQLite database for history and statistics
- Post-processing (punctuation, formatting)
- Voice Activity Detection (VAD)
- Automatic model download
- System tray icon

## License

MIT License - see LICENSE file for details

## Contributing

Contributions welcome! Please open an issue or pull request on GitHub.
