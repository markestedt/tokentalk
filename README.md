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
- **Post-Processing**: Voice commands, code generation, custom dictionary, and optional grammar correction
  - **Voice Commands**: Say "comma", "period", "new line", etc. to insert punctuation
  - **Code Generation**: Describe code and get formatted code blocks (e.g., "code block: Python function to parse JSON")
  - **Custom Dictionary**: Define corrections for technical terms and jargon
  - **Grammar Correction**: Optional LLM-based grammar and punctuation fixes
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

[postprocessing]
# Enable post-processing (voice commands, dictionary, grammar)
enabled = true

# Voice commands: Convert "comma" to ,, "period" to ., etc.
commands = true

# Grammar correction: Fix grammar and punctuation using LLM (adds latency)
grammar = false

# Grammar provider: "match" (same as transcription), "openai", or "ollama" (future)
grammar_provider = "match"

# Model for OpenAI grammar correction (cheap, fast, competent)
grammar_model = "gpt-4o-mini"

# Dictionary file location for custom terms and corrections
dictionary_file = ""  # defaults to %APPDATA%\tokentalk\dictionary.txt

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

#### `[postprocessing]`
- `enabled`: Master toggle for all post-processing features
- `commands`: Enable voice commands (e.g., "comma" → `,`)
- `grammar`: Enable LLM-based grammar correction (adds latency)
- `grammar_provider`: Provider for grammar correction
  - `"match"` - Use same provider as transcription
  - `"openai"` - Always use OpenAI
  - `"ollama"` - Local LLM (future)
- `grammar_model`: Model to use for grammar correction
  - Recommended: `"gpt-4o-mini"` (cheap, fast, capable)
- `dictionary_file`: Path to custom dictionary file
  - Defaults to `%APPDATA%\tokentalk\dictionary.txt`

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

## Post-Processing

TokenTalk includes powerful post-processing features to improve transcription accuracy and add punctuation through voice commands.

### Voice Commands

Say specific phrases to insert punctuation and symbols. Commands only trigger when spoken as complete words (not as part of other words).

**Common Commands:**
- "comma" → `,`
- "period" or "full stop" or "dot" → `.`
- "question mark" → `?`
- "exclamation mark" → `!`
- "colon" → `:`
- "semicolon" → `;`
- "new line" → `\n`
- "new paragraph" → `\n\n`

**Quotes and Brackets:**
- "open quote" / "close quote" → `"`
- "open parenthesis" / "close parenthesis" → `(` / `)`
- "open bracket" / "close bracket" → `[` / `]`
- "open brace" / "close brace" → `{` / `}`

**Symbols:**
- "at sign" → `@`
- "at sign space" → `@ ` (@ followed by space)
- "hash" → `#`
- "dollar sign" → `$`
- "percent sign" → `%`
- "ampersand" → `&`
- "asterisk" → `*`
- "plus" → `+`
- "equals" → `=`
- "dash" → `-`
- "underscore" → `_`
- "slash" → `/`
- "backslash" → `\`

**Word Boundary Detection:**
Voice commands only trigger when they're complete words. For example:
- ✅ "I want a comma here" → "I want a , here"
- ❌ "Use the command prompt" → "Use the command prompt" (comma not replaced because it's part of "command")

### Code Generation

Describe code you want to write using natural language, and TokenTalk will generate properly formatted code blocks for you.

**How to Use:**

Start your dictation with one of these trigger phrases:
- "code block: [description]"
- "codeblock: [description]"
- "code: [description]"

**Examples:**

- "code block: Python function to calculate fibonacci numbers"
- "code: JavaScript async function to fetch user data from an API"
- "codeblock: Go HTTP handler that returns JSON"

**What It Does:**

1. Detects the code generation trigger phrase
2. Sends your description to the AI model
3. Generates clean, runnable code
4. Automatically detects the programming language
5. Returns the code wrapped in a markdown code block

**Output Format:**

The generated code is automatically formatted as a markdown code block with syntax highlighting:

````markdown
```python
def fibonacci(n):
    if n <= 1:
        return n
    return fibonacci(n-1) + fibonacci(n-2)
```
````

**Requirements:**

- Requires an OpenAI API key configured in the `[transcription]` section
- Uses the same API key as transcription
- Recommended model: GPT-4o-mini (fast, affordable, capable)

**Configuration:**

The code generation feature is automatically enabled when post-processing is enabled:

```toml
[postprocessing]
enabled = true  # Enables code generation along with other features
```

**Supported Languages:**

The AI automatically detects the language from your description. Supported languages include:
- Python, JavaScript, TypeScript, Go, Rust, Java, C++, C, C#
- Ruby, PHP, Swift, Kotlin, Scala
- SQL, Bash, PowerShell
- HTML, CSS, YAML, JSON, and more

### Custom Dictionary

Define custom terms and correction mappings to improve transcription of technical jargon, product names, and domain-specific terminology.

**Two Types of Entries:**

1. **Simple Terms** (bias Whisper toward recognizing these):
   - `Kubernetes`
   - `Anthropic`
   - `OAuth2`

2. **Correction Mappings** (fix common misrecognitions):
   - `cube control -> kubectl`
   - `enn eight -> n8n`
   - `post grass -> Postgres`

**Dictionary File Location:**
`%APPDATA%\tokentalk\dictionary.txt`

**Managing Dictionary:**
Use the web dashboard (Settings → "Manage Dictionary") to:
- Add simple terms
- Add correction mappings
- Edit and delete entries
- See real-time preview of changes

### Grammar Correction

Optional LLM-based grammar and punctuation correction. **Disabled by default** as it adds latency (~500ms).

**Configuration:**
```toml
[postprocessing]
enabled = true
grammar = true                    # Enable grammar correction
grammar_provider = "match"        # Use same provider as transcription
grammar_model = "gpt-4o-mini"     # Fast, cheap, capable model
```

**Provider Options:**
- `"match"` - Use the same provider as transcription (OpenAI or local)
- `"openai"` - Always use OpenAI
- `"ollama"` - Local LLM (future)

**What It Does:**
- Fixes grammar and punctuation errors
- Preserves technical terms and code identifiers
- Formats file paths and version numbers correctly
- Uses your custom dictionary for context

**Recommended Model:**
- **gpt-4o-mini**: $0.15/1M input tokens, ~200-500ms latency
- Very affordable for grammar correction
- Excellent at following instructions

### Processing Pipeline Order

Post-processing runs in this order:
1. **Code Generation** → Detect "code block:" triggers and generate code
2. **Voice Commands** → Replace "comma" with `,`, etc.
3. **Dictionary** → Apply custom corrections
4. **Grammar** (if enabled) → LLM fixes remaining issues

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

### Voice commands not working
- Ensure `[postprocessing] enabled = true` and `commands = true` in your config
- Commands only trigger when spoken as complete words (not part of other words)
- Example: "comma" triggers in "I want a comma here" but not in "command prompt"
- Check the web dashboard Settings → Post-Processing section

### Grammar correction not working
- Ensure `[postprocessing] grammar = true` in your config
- Check that you have an OpenAI API key configured (if using OpenAI provider)
- Grammar correction adds ~500ms latency - this is normal
- Check logs for any API errors

### Dictionary corrections not applying
- Verify your dictionary file exists at the configured location
- Use the web dashboard (Settings → Manage Dictionary) to verify entries
- Dictionary corrections are case-insensitive but preserve replacement case
- Restart TokenTalk after manually editing the dictionary file

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

### M4 - Post-Processing (Completed)
- ✅ Voice commands (30+ commands with word boundary detection)
- ✅ Code generation (AI-powered code block generation from natural language)
- ✅ Custom dictionary (simple terms and correction mappings)
- ✅ Grammar correction via OpenAI (optional, configurable)
- ✅ Web UI dictionary editor
- ✅ Provider abstraction for future Ollama support
- ✅ Processing pipeline (code gen → commands → dictionary → grammar)

### M2 (Planned)
- ⏳ Local whisper.cpp support (in progress)
- Linux support
- Automatic model download for local whisper
- System tray icon
- Audio device hot-swapping
- Keyboard shortcuts for common actions
- Export history to CSV/JSON
- Custom voice command editor in web UI

## License

MIT License - see LICENSE file for details

## Contributing

Contributions welcome! Please open an issue or pull request on GitHub.
