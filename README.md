# TokenTalk

TokenTalk is a push-to-talk voice dictation tool for Windows. Press a global hotkey, speak, and have your words transcribed and pasted into any application.

## Features

- **Global Hotkey**: Press Ctrl+Shift+V (configurable) to start/stop recording
- **Push-to-Talk**: Hold the hotkey while speaking, release to transcribe
- **Universal**: Works in any Windows application
- **Multiple Providers**: OpenAI Whisper API or local whisper.cpp (coming soon)
- **Web Dashboard**: Modern web UI for configuration, statistics, and history
- **Smart Silence Detection**: Automatically filters out silent or empty recordings
- **Instant Recording Start**: Pre-initialized audio device for minimal latency
- **Developer Mode**: Enhanced recognition of technical terms and programming keywords
- **Simple Configuration**: TOML-based configuration file or web UI

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

   **Option A - Web Dashboard (Recommended)**:
   - Open http://localhost:9876 in your browser
   - Go to the Settings tab
   - Enter your OpenAI API key
   - Click "Save Configuration"

   **Option B - Config File**:
   - Open `%APPDATA%\tokentalk\config.toml` in a text editor
   - Set your `api_key` in the `[transcription]` section
   - Save the file and restart TokenTalk

4. **Use it**:
   - Click in any text field (Notepad, browser, VS Code, etc.)
   - Press and hold the configured hotkey (default: Ctrl+Shift+V)
   - Speak naturally
   - Release the keys
   - Your transcribed text will be pasted automatically!

## Configuration

The configuration file is located at `%APPDATA%\tokentalk\config.toml` (typically `C:\Users\YourName\AppData\Roaming\tokentalk\config.toml`).

You can edit the config file directly or use the **Web Dashboard** at http://localhost:9876 for a graphical interface.

### Default Configuration

```toml
# Keyboard combination to activate recording
hotkey = "ctrl+shift+v"

# Developer mode: Enhanced recognition of technical terms
developer_mode = false

[audio]
# Microphone device ID (0 = system default, or specific device number)
device = 0

# Maximum recording duration in seconds
max_seconds = 120

# Silence threshold: Recordings below this level are rejected as silent
# Lower values = more sensitive, higher values = less sensitive
# Set to 0 to disable silence detection
silence_threshold = 200

[transcription]
# Provider: "openai" or "whisper" (whisper not yet implemented)
provider = "openai"

# Model to use
model = "whisper-1"

# Language code (ISO 639-1): "en", "es", "fr", "de", etc.
language = "en"

# Optional prompt to guide transcription
prompt = ""

# Your OpenAI API key (required for OpenAI provider)
api_key = ""

# Directory for local whisper models (for future whisper.cpp support)
whisper_model_dir = ""  # defaults to %APPDATA%\tokentalk\models

[web]
# Enable the web-based configuration UI and statistics dashboard
enabled = true

# Port for the web server (default: 9876)
port = 9876
```

### Configuration Options

#### Root Level
- `hotkey`: Keyboard combination to activate recording
  - Format: `"modifier+modifier+key"` or `"modifier+modifier"` (modifier-only)
  - Modifiers: `ctrl`, `shift`, `alt`, `win` (or `windows`)
  - Examples: `"ctrl+shift+v"`, `"alt+space"`, `"ctrl+win"`
- `developer_mode`: Enable enhanced recognition of technical terms and programming keywords

#### `[audio]`
- `device`: Microphone device ID (`0` = system default)
- `max_seconds`: Maximum recording duration in seconds (default: 120)
- `silence_threshold`: RMS audio level threshold for silence detection
  - Values: `100-300` typical for normal speech
  - Lower = more sensitive (picks up quieter speech)
  - Higher = less sensitive (only loud/clear speech)
  - `0` = disable silence detection

#### `[transcription]`
- `provider`: Transcription provider (`"openai"` or `"whisper"`)
- `model`: Model to use:
  - For OpenAI: `"whisper-1"` (only option currently)
  - For whisper.cpp: `"tiny"`, `"base"`, `"small"`, `"medium"`, `"large"` (not yet implemented)
- `language`: ISO 639-1 language code (`"en"`, `"es"`, `"fr"`, etc.)
- `prompt`: Optional prompt to guide transcription (helps with domain-specific terminology)
- `api_key`: Your OpenAI API key (required for OpenAI provider)
- `whisper_model_dir`: Directory for local whisper models (for future whisper.cpp support)

#### `[web]`
- `enabled`: Enable/disable the web dashboard
- `port`: Port for the web server (default: 9876)

## Web Dashboard

TokenTalk includes a modern web dashboard for easy configuration and monitoring.

**Access**: Open http://localhost:9876 in your browser (default port, configurable)

### Features

**Statistics Tab** (Default):
- Total dictations, words, and success rate
- Average latency metrics
- Interactive charts showing dictations and words over time
- Provider statistics breakdown
- Filterable by time range (7, 30, or 90 days)

**History Tab**:
- Complete dictation history with timestamps
- View transcribed text, word count, and latency for each dictation
- Click any entry to copy text to clipboard
- Delete individual dictations
- Paginated view (50 per page)

**Settings Tab**:
- Configure all TokenTalk settings via web UI
- Live validation and helpful descriptions
- No need to manually edit config files
- Changes saved immediately

### Real-time Status

The dashboard shows live status updates:
- **Idle**: Ready for dictation
- **Recording...**: Currently capturing audio
- **Processing...**: Transcribing audio

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

## Performance & Optimizations

### Instant Recording Start

TokenTalk pre-initializes the audio device at startup and keeps it in a ready state. This eliminates the 100-500ms initialization delay that would otherwise occur when pressing the hotkey.

**Privacy**: The audio device is open but NOT recording. Audio frames are immediately discarded unless you're actively holding the hotkey. No audio is buffered or stored until you explicitly press the record button.

### Smart Silence Detection

TokenTalk calculates the RMS (Root Mean Square) audio level of each recording and rejects recordings below the configured threshold. This prevents:
- Accidental silent recordings from being sent to the API
- API hallucinations on empty audio
- System prompt leakage in transcriptions
- Wasted API credits

The threshold is fully configurable via `silence_threshold` in the config file or web dashboard.

### Optimized I/O

Recording starts immediately when the hotkey is pressed, with logging and status updates happening asynchronously to avoid blocking the recording thread.

## Troubleshooting

### "OpenAI API key is required"
- Make sure you've set `api_key` in the `[transcription]` section of your config file
- Or configure it via the web dashboard at http://localhost:9876
- The config file is at `%APPDATA%\tokentalk\config.toml`

### "Failed to start recording"
- Check that your microphone is connected and working
- Try setting a specific `device` in the `[audio]` section (0 = default)
- Make sure no other application has exclusive access to your microphone
- Restart TokenTalk to reinitialize the audio device

### Hotkey not working
- Make sure the key combination isn't already used by another application
- Try a different combination in the config file or web dashboard
- Check that TokenTalk is running (you should see log messages when you start it)
- Either left or right Windows key will work for `win` modifier

### Text not pasting
- Make sure you click in a text field before pressing the hotkey
- Some applications (like some games) may block simulated input
- Try in a simple application like Notepad first to verify it works

### "Recording too short, ignoring"
- Hold the hotkey for at least 100ms before releasing
- If you're trying to record a very short word, try holding the key a bit longer

### "Recording too quiet or silent, ignoring"
- The silence detection filtered out your recording
- Try speaking louder or closer to your microphone
- Lower the `silence_threshold` in config or web dashboard (default: 200)
- Set `silence_threshold = 0` to disable silence detection entirely
- Check your microphone volume in Windows sound settings

### Missing first few words
- This should be resolved with the pre-initialized audio device
- If still occurring, ensure TokenTalk is running before you start speaking
- Check system resource usage - heavy CPU load can cause delays

### Web dashboard not accessible
- Check that `[web] enabled = true` in your config
- Verify the port isn't in use by another application
- Try a different port in the `[web]` section
- Check firewall settings if accessing from another device

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

### M1 (Completed)
- ✅ Global hotkey detection with configurable combinations
- ✅ Push-to-talk audio recording with pre-initialized device
- ✅ OpenAI Whisper API transcription
- ✅ Text injection via clipboard
- ✅ TOML configuration
- ✅ Web GUI for configuration, statistics, and history
- ✅ SQLite database for history and statistics
- ✅ Smart silence detection (configurable threshold)
- ✅ Developer mode for technical terms
- ✅ Real-time status updates via WebSocket
- ✅ Interactive charts and analytics

### M2 (Planned)
- ⏳ Local whisper.cpp support (in progress)
- Linux support
- Post-processing (punctuation, formatting improvements)
- Automatic model download for local whisper
- System tray icon
- Audio device hot-swapping
- Custom vocabulary/terminology lists
- Keyboard shortcuts for common actions
- Export history to CSV/JSON

## License

MIT License - see LICENSE file for details

## Contributing

Contributions welcome! Please open an issue or pull request on GitHub.
