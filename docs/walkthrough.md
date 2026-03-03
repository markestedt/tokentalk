# TokenTalk: A Code Walkthrough

*2026-03-02T15:41:23Z by Showboat 0.6.1*
<!-- showboat-id: 57b0161b-ff62-4d3f-86b9-d9489b0b19ea -->

## Overview

TokenTalk is a Windows voice-dictation agent. Press the hotkey, speak, release — your words appear in whatever window had focus. This walkthrough traces every step from keypress to injected text.

The core pipeline:

```
Hotkey pressed   → AudioRecorder.Start()
Hotkey released  → AudioRecorder.Stop() → AudioSegment (WAV bytes in memory)
AudioSegment     → ITranscriptionProvider.TranscribeAsync() → raw text
raw text         → PostProcessingPipeline.ProcessAsync() → processed text
processed text   → PasteService.PasteTextAsync() → clipboard + Ctrl+V → active window
result           → DictationRepository.SaveAsync() → SQLite row with full telemetry
```

Three concurrent execution contexts run the whole time:

- **WPF message loop** (main `[STAThread]`) — the settings/history window.
- **WinForms message loop** (dedicated STA background thread) — tray icon and the recording overlay.
- **Agent task** (thread-pool background task) — the hotkey/record/transcribe/paste loop.

## Program.cs — The Composition Root

`Program.cs` manually wires every dependency — no DI framework. The `[STAThread]` attribute is required: WPF and WinForms both mandate an STA main thread. Startup proceeds top-to-bottom: logging → config → database → dictionary → HTTP client → model manager → transcription → post-processing → platform services → overlay → agent → WPF app → tray thread.

```powershell
Get-Content src/TokenTalk/Program.cs | Select-Object -Skip 19 -First 35

```

```output
    [STAThread]
    public static void Main()
    {
        var cts = new CancellationTokenSource();

        // Logging
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(LogLevel.Information)
                .AddSimpleConsole(opts =>
                {
                    opts.TimestampFormat = "HH:mm:ss ";
                    opts.SingleLine = true;
                });
        });

        var logger = loggerFactory.CreateLogger<Program>();

        // Configuration
        var configPath = ConfigManager.GetConfigPath();
        var configDir = ConfigManager.GetConfigDirectory();
        Directory.CreateDirectory(configDir);

        var configManager = new ConfigManager(configPath, loggerFactory.CreateLogger<ConfigManager>());
        var cfg = configManager.Current;

        logger.LogInformation("TokenTalk starting. Config: {Path}", configPath);

        // Database
        var dbPath = Path.Combine(configDir, "tokentalk.db");
        var db = new TokenTalkDbContext(dbPath);
        db.InitializeAsync().GetAwaiter().GetResult();
        var repository = new DictationRepository(db);

```

All components receive live config via `Func<X>` lambdas (`() => configManager.Current.Something`). This means any setting change takes effect on the **next** dictation without restarting the app.

The transcription factory and post-processing pipeline are wired here:

```powershell
Get-Content src/TokenTalk/Program.cs | Select-Object -Skip 96 -First 28

```

```output
        ITranscriptionProvider transcriptionProvider = new TranscriptionProviderFactory(
            () => configManager.Current.Transcription.Provider,
            new OpenAiWhisperProvider(
                httpClientFactory,
                () => configManager.Current.Transcription.ApiKey,
                () => configManager.Current.Transcription.Model,
                () => configManager.Current.Transcription.Language,
                () => BuildWhisperPrompt(configManager.Current),
                dictionary.GetSimpleTerms()),
            new WhisperCppProvider(
                () => configManager.Current.Transcription.ModelPath,
                () => configManager.Current.Transcription.Language));

        // Post-Processing Pipeline
        var pipeline = new PostProcessingPipeline(loggerFactory.CreateLogger<PostProcessingPipeline>());

        // Dictionary mapping replacement always runs when entries exist (independent of PostProcessing toggle)
        if (dictionary.Entries.Any(e => e.IsMapping))
            pipeline.AddProcessor(new DictionaryProcessor(dictionary));

        pipeline.AddProcessor(new VoiceCommandProcessor(() => configManager.Current.PostProcessing.Commands));

        // Platform Services
        var clipboard = new ClipboardService();
        var paste = new PasteService(clipboard);
        var recorder = new AudioRecorder(cfg.Audio.DeviceIndex, cfg.Audio.MaxSeconds);

        // Overlay
```

Then the three execution contexts are launched: the tray/overlay STA thread, the agent background task, and finally `wpfApp.Run(mainWindow)` which blocks until shutdown.

```powershell
Get-Content src/TokenTalk/Program.cs | Select-Object -Skip 151 -First 30

```

```output
        // Tray Icon (STA thread)
        Action showWindow = () => wpfApp.Dispatcher.Invoke(() =>
        {
            if (!mainWindow.IsVisible)
                mainWindow.Show();
            mainWindow.Activate();
            if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;
        });

        var trayManager = new TrayIconManager(cts, showWindow, loggerFactory.CreateLogger<TrayIconManager>());

        var trayThread = new Thread(() =>
        {
            try { trayManager.Run(overlay); }
            catch (Exception ex) { logger.LogError(ex, "Tray icon error"); }
        });
        trayThread.SetApartmentState(ApartmentState.STA);
        trayThread.IsBackground = true;
        trayThread.Name = "TrayIconThread";
        trayThread.Start();

        // Agent task (background thread)
        var agentTask = Task.Run(() => agent.RunAsync(cts.Token));

        logger.LogInformation("All services started. Use tray menu to quit.");

        // WPF message loop (blocks until Shutdown() called)
        wpfApp.Run(mainWindow);

```

## Configuration/TokenTalkOptions + ConfigManager

`TokenTalkOptions` is the strongly-typed settings model. Settings live at `%APPDATA%\TokenTalk\appsettings.json`.

```powershell
Get-Content src/TokenTalk/Configuration/TokenTalkOptions.cs

```

```output
namespace TokenTalk.Configuration;

public class TokenTalkOptions
{
    public string Hotkey { get; set; } = "Ctrl+Shift+V";
    public bool DeveloperMode { get; set; } = false;
    public AudioOptions Audio { get; set; } = new();
    public TranscriptionOptions Transcription { get; set; } = new();
    public PostProcessingOptions PostProcessing { get; set; } = new();
}

public class AudioOptions
{
    public int DeviceIndex { get; set; } = 0;
    public int MaxSeconds { get; set; } = 120;
    public double SilenceThreshold { get; set; } = 200;
}

public class TranscriptionOptions
{
    public string Provider { get; set; } = "openai";
    public string Model { get; set; } = "whisper-1";
    public string Language { get; set; } = "auto";
    public string Prompt { get; set; } = "";
    public string ApiKey { get; set; } = "";
    // Path to local GGML model file, used when Provider = "whisper.cpp"
    public string ModelPath { get; set; } = "";
}

public class PostProcessingOptions
{
    public bool Commands { get; set; } = true;
    public string DictionaryFile { get; set; } = "";
}

```

`ConfigManager` loads from disk at startup. If the file is absent it writes defaults. `Save` updates the in-memory snapshot atomically under a lock. The `Current` property is also locked so any thread can read it safely.

```powershell
Get-Content src/TokenTalk/Configuration/ConfigManager.cs | Select-Object -Skip 30 -First 32

```

```output
    private TokenTalkOptions Load()
    {
        if (!File.Exists(_configPath))
        {
            _logger.LogInformation("Config file not found at {Path}, creating defaults", _configPath);
            var defaults = new TokenTalkOptions();
            SaveInternal(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var options = JsonSerializer.Deserialize<TokenTalkOptions>(json, JsonOptions);
            return options ?? new TokenTalkOptions();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config from {Path}, using defaults", _configPath);
            return new TokenTalkOptions();
        }
    }

    public void Save(TokenTalkOptions options)
    {
        lock (_lock)
        {
            SaveInternal(options);
            _current = options;
        }
    }

```

## Platform/HotkeyListener — Win32 Low-Level Keyboard Hook

A **low-level keyboard hook** (`WH_KEYBOARD_LL`) intercepts all keystrokes system-wide, regardless of which app has focus — even above the Start menu. The hook requires a dedicated background thread running a Win32 message pump (`GetMessage` / `DispatchMessage`):

```powershell
Get-Content src/TokenTalk/Platform/HotkeyListener.cs | Select-Object -Skip 126 -First 32

```

```output
    private void RunMessageLoop()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        _proc = HookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to install keyboard hook. Error: {Marshal.GetLastWin32Error()}");
        }

        // Message pump required for WH_KEYBOARD_LL to fire
        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

```

The hook callback tracks modifier state (`Ctrl`, `Shift`, `Alt`, `Win`) by observing the event stream itself rather than calling `GetAsyncKeyState`. That API is unreliable from background threads because it reads from the *calling thread's* key-state table, which the OS never updates for a hook thread:

```powershell
Get-Content src/TokenTalk/Platform/HotkeyListener.cs | Select-Object -Skip 158 -First 58

```

```output
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kbInfo = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)kbInfo.vkCode;
            bool isKeyDown = wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN;
            bool isKeyUp   = wParam == NativeMethods.WM_KEYUP   || wParam == NativeMethods.WM_SYSKEYUP;

            // Update tracked modifier state from the event stream itself.
            // This is reliable across all applications, unlike GetAsyncKeyState
            // which reads from the hook thread's own (unupdated) key state table.
            UpdateModifierState(vk, isKeyDown, isKeyUp);

            bool modifiersMatch = ModifiersMatch();

            if (_triggerVk != 0)
            {
                // Standard combo: modifiers + a non-modifier trigger key (e.g. Ctrl+Shift+V)
                if (vk == _triggerVk)
                {
                    if (isKeyDown && modifiersMatch && !_isPressed)
                    {
                        _isPressed = true;
                        _channel.Writer.TryWrite(new HotkeyEvent(HotkeyEventType.Pressed));
                    }
                    else if (isKeyUp && _isPressed)
                    {
                        _isPressed = false;
                        _channel.Writer.TryWrite(new HotkeyEvent(HotkeyEventType.Released));
                    }
                }
                // Release if a required modifier is lifted while trigger was held
                else if (isKeyUp && _isPressed && IsRequiredModifier(vk))
                {
                    _isPressed = false;
                    _channel.Writer.TryWrite(new HotkeyEvent(HotkeyEventType.Released));
                }
            }
            else
            {
                // Modifier-only combo (e.g. Ctrl+Win): fire when the combo
                // becomes fully satisfied, release when it breaks.
                if (isKeyDown && IsRequiredModifier(vk) && modifiersMatch && !_isPressed)
                {
                    _isPressed = true;
                    _channel.Writer.TryWrite(new HotkeyEvent(HotkeyEventType.Pressed));
                }
                else if (isKeyUp && _isPressed && IsRequiredModifier(vk) && !modifiersMatch)
                {
                    _isPressed = false;
                    _channel.Writer.TryWrite(new HotkeyEvent(HotkeyEventType.Released));
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
```

`ModifiersMatch` does a precise double-check: every *required* modifier must be down AND no *unrequired* modifier may be down. This prevents false-fires when the user holds extra keys:

```powershell
Get-Content src/TokenTalk/Platform/HotkeyListener.cs | Select-Object -Skip 257 -First 5

```

```output
    private bool ModifiersMatch() =>
        (!_requireCtrl  || _ctrlDown)  && (_ctrlDown  == _requireCtrl)  &&
        (!_requireShift || _shiftDown) && (_shiftDown == _requireShift) &&
        (!_requireAlt   || _altDown)   && (_altDown   == _requireAlt)   &&
        (!_requireWin   || _winDown)   && (_winDown   == _requireWin);
```

Hotkey events are published to a `Channel<HotkeyEvent>` — a lock-free, async-friendly queue. Agent reads from it with `await foreach`.

## Agent.cs — The Orchestrator / State Machine

`Agent` is the heart of the application. Its `RunAsync` loop continuously reads hotkey events off the channel and drives the whole pipeline:

```powershell
Get-Content src/TokenTalk/Agent.cs | Select-Object -Skip 53 -First 30

```

```output
    public async Task RunAsync(CancellationToken ct)
    {
        var cfg = _configManager.Current;

        _hotkeyListener.Start(cfg.Hotkey);
        _logger.LogInformation("TokenTalk started. Hotkey: {Hotkey}, Provider: {Provider}",
            cfg.Hotkey, _transcriptionProvider.Name);

        SetStatus("idle");

        try
        {
            await foreach (var evt in _hotkeyListener.Events.ReadAllAsync(ct))
            {
                switch (evt.Type)
                {
                    case HotkeyEventType.Pressed:
                        HandleHotkeyPressed();
                        break;
                    case HotkeyEventType.Released:
                        _ = HandleHotkeyReleasedAsync(ct);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Agent stopped");
        }
    }
```

When the hotkey is **pressed**, recording starts synchronously — no `await`, so there is no perceptible gap between the keypress and the mic opening:

```powershell
Get-Content src/TokenTalk/Agent.cs | Select-Object -Skip 84 -First 15

```

```output
    private void HandleHotkeyPressed()
    {
        try
        {
            _recorder.Start();
            _overlay?.StartRecording();
            _logger.LogInformation("Recording started");
            SetStatus("recording");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recording");
            SetStatus("idle");
        }
    }
```

When the hotkey is **released**, `HandleHotkeyReleasedAsync` is called fire-and-forget (`_ = Handle...`). This is deliberate: the event-reading loop must stay responsive so a second press during transcription is not missed. The method stops the recorder, validates the audio, then runs the full async pipeline:

```powershell
Get-Content src/TokenTalk/Agent.cs | Select-Object -Skip 100 -First 42

```

```output
    private async Task HandleHotkeyReleasedAsync(CancellationToken ct)
    {
        _logger.LogInformation("Recording stopped, transcribing...");
        var recordingStart = DateTimeOffset.UtcNow;

        AudioSegment audio;
        try
        {
            audio = _recorder.Stop();
            _overlay?.StartProcessing();
        }
        catch (Exception ex)
        {
            _overlay?.StopProcessing();
            _logger.LogError(ex, "Failed to stop recording");
            SetStatus("idle");
            return;
        }

        var cfg = _configManager.Current;

        // Validate duration
        if (AudioHelpers.IsTooShort(audio, TimeSpan.FromMilliseconds(100)))
        {
            _logger.LogWarning("Recording too short ({Duration}ms), ignoring", audio.Duration.TotalMilliseconds);
            _overlay?.StopProcessing();
            SetStatus("idle");
            return;
        }

        // Validate silence
        if (cfg.Audio.SilenceThreshold > 0 && AudioHelpers.IsSilent(audio, cfg.Audio.SilenceThreshold))
        {
            _logger.LogWarning("Recording too quiet, ignoring");
            _overlay?.StopProcessing();
            SetStatus("idle");
            return;
        }

        SetStatus("processing");

        var dictation = new Dictation
```

After validation passes, a `Dictation` record is created to capture telemetry. Then transcription, post-processing, and injection run in sequence, with latency timestamps captured at each stage:

```powershell
Get-Content src/TokenTalk/Agent.cs | Select-Object -Skip 141 -First 82

```

```output
        var dictation = new Dictation
        {
            RecordingStartMs = recordingStart.ToUnixTimeMilliseconds(),
            RecordingDurationMs = (long)audio.Duration.TotalMilliseconds,
            AudioSizeBytes = audio.WavData.Length,
            AudioSampleRate = audio.SampleRate,
            Provider = _transcriptionProvider.Name,
            Model = cfg.Transcription.Model,
            Language = cfg.Transcription.Language,
            Success = false,
        };

        try
        {
            // Transcribe
            var transcribeStart = DateTimeOffset.UtcNow;
            string text;
            try
            {
                text = await _transcriptionProvider.TranscribeAsync(audio, ct);
                dictation.TranscriptionLatencyMs = (long)(DateTimeOffset.UtcNow - transcribeStart).TotalMilliseconds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transcription failed");
                dictation.TranscriptionLatencyMs = (long)(DateTimeOffset.UtcNow - transcribeStart).TotalMilliseconds;
                dictation.ErrorMessage = ex.Message;
                await SaveDictationAsync(dictation, ct);
                SetStatus("idle");
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Empty transcription");
                dictation.ErrorMessage = "Empty transcription";
                await SaveDictationAsync(dictation, ct);
                SetStatus("idle");
                return;
            }

            dictation.TranscribedText = text;
            dictation.WordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            dictation.CharacterCount = text.Length;

            _logger.LogInformation("Transcribed: {Text} ({Duration})", text, audio.Duration);

            // Post-process
            var processed = text;
            try
            {
                processed = await _pipeline.ProcessAsync(text, ct);
                if (processed != text)
                    _logger.LogInformation("Post-processed: {Original} ->' {Processed}", text, processed);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Post-processing failed, using original text");
            }

            // Inject text
            var injectStart = DateTimeOffset.UtcNow;
            try
            {
                await _paste.PasteTextAsync(processed, ct);
                dictation.InjectionLatencyMs = (long)(DateTimeOffset.UtcNow - injectStart).TotalMilliseconds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to inject text");
                dictation.InjectionLatencyMs = (long)(DateTimeOffset.UtcNow - injectStart).TotalMilliseconds;
                dictation.TotalLatencyMs = (long)(DateTimeOffset.UtcNow - recordingStart).TotalMilliseconds;
                dictation.ErrorMessage = ex.Message;
                await SaveDictationAsync(dictation, ct);
                SetStatus("idle");
                return;
            }

            dictation.TotalLatencyMs = (long)(DateTimeOffset.UtcNow - recordingStart).TotalMilliseconds;
            dictation.Success = true;

            await SaveDictationAsync(dictation, ct);
```

## Audio/AudioRecorder — Capturing the Microphone

`AudioRecorder` wraps NAudio's `WaveInEvent`. Everything is recorded at **16 kHz / 16-bit mono** — the exact format both OpenAI Whisper and whisper.cpp expect. A `MemoryStream` accumulates the PCM data wrapped in a `WaveFileWriter`, which automatically writes the WAV file header.

`AudioSegment` — the value handed to the transcription layer — is a simple immutable record:

```powershell
Get-Content src/TokenTalk/Audio/AudioRecorder.cs | Select-Object -Skip 4 -First 1

```

```output
public record AudioSegment(byte[] WavData, int SampleRate, TimeSpan Duration);
```

`Start()` creates a fresh buffer and `WaveInEvent`, then begins streaming 50 ms chunks:

```powershell
Get-Content src/TokenTalk/Audio/AudioRecorder.cs | Select-Object -Skip 29 -First 25

```

```output
    public void Start()
    {
        lock (_lock)
        {
            if (_recording)
                return;

            _buffer = new MemoryStream();
            var format = new WaveFormat(SampleRate, BitsPerSample, Channels);
            _writer = new WaveFileWriter(_buffer, format);

            _waveIn = new WaveInEvent
            {
                DeviceNumber = _deviceIndex,
                WaveFormat = format,
                BufferMilliseconds = 50
            };

            _waveIn.DataAvailable += OnDataAvailable;

            _startTime = DateTime.UtcNow;
            _recording = true;
            _waveIn.StartRecording();
        }
    }
```

Each 50 ms chunk fires `DataAvailable`. The chunk is written to the WAV buffer and an **RMS amplitude** value is computed inline and broadcast via the `AmplitudeAvailable` event. The overlay subscribes to this to animate the waveform:

```powershell
Get-Content src/TokenTalk/Audio/AudioRecorder.cs | Select-Object -Skip 71 -First 22

```

```output
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        float amplitude;
        Action<float>? handler;
        lock (_lock)
        {
            if (!_recording || _writer == null)
                return;

            _writer.Write(e.Buffer, 0, e.BytesRecorded);

            amplitude = ComputeNormalizedRms(e.Buffer, e.BytesRecorded);
            handler = AmplitudeAvailable;

            // Enforce max recording time
            if ((DateTime.UtcNow - _startTime).TotalSeconds >= _maxSeconds)
            {
                // Signal that max time was reached recording will be stopped externally
            }
        }
        handler?.Invoke(amplitude);
    }
```

`Stop()` finalises the WAV stream, captures the elapsed wall-clock duration, and returns the immutable `AudioSegment`. All NAudio resources are released immediately:

```powershell
Get-Content src/TokenTalk/Audio/AudioRecorder.cs | Select-Object -Skip 94 -First 25

```

```output
    public AudioSegment Stop()
    {
        lock (_lock)
        {
            if (!_recording || _waveIn == null || _writer == null || _buffer == null)
                return new AudioSegment([], SampleRate, TimeSpan.Zero);

            _recording = false;
            _waveIn.StopRecording();
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.Dispose();
            _waveIn = null;

            _writer.Flush();
            _writer.Dispose();
            _writer = null;

            var duration = DateTime.UtcNow - _startTime;
            var wavData = _buffer.ToArray();
            _buffer.Dispose();
            _buffer = null;

            return new AudioSegment(wavData, SampleRate, duration);
        }
    }
```

## Audio/AudioHelpers — Silence and Duration Validation

Before spending a network round-trip (or CPU time) on transcription, Agent validates the recording. `AudioHelpers` provides two guards — too short (< 100 ms) and too quiet. The silence check reads raw PCM samples directly from the WAV byte array, skipping the 44-byte WAV header, and computes root-mean-square amplitude:

```powershell
Get-Content src/TokenTalk/Audio/AudioHelpers.cs

```

```output
namespace TokenTalk.Audio;

public static class AudioHelpers
{
    /// <summary>
    /// Calculates the RMS (Root Mean Square) amplitude of the audio samples in a WAV byte array.
    /// Assumes 16-bit signed PCM audio.
    /// </summary>
    public static double CalculateRms(byte[] wavData)
    {
        if (wavData.Length < 44)
            return 0; // Too short to contain valid WAV data

        // WAV data starts at byte 44 (after the 44-byte header)
        const int headerSize = 44;
        int sampleCount = (wavData.Length - headerSize) / 2; // 16-bit = 2 bytes per sample

        if (sampleCount <= 0)
            return 0;

        double sumSquares = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(wavData, headerSize + i * 2);
            sumSquares += (double)sample * sample;
        }

        return Math.Sqrt(sumSquares / sampleCount);
    }

    /// <summary>
    /// Determines if an audio segment should be considered silent based on RMS threshold.
    /// </summary>
    public static bool IsSilent(AudioSegment segment, double threshold)
    {
        if (threshold <= 0)
            return false;

        double rms = CalculateRms(segment.WavData);
        return rms < threshold;
    }

    /// <summary>
    /// Determines if an audio segment is too short to be valid.
    /// </summary>
    public static bool IsTooShort(AudioSegment segment, TimeSpan minDuration)
    {
        return segment.Duration < minDuration;
    }
}
```

## Transcription/ITranscriptionProvider — The Interface

The transcription layer hides behind a minimal interface — just a display name and one async method:

```powershell
Get-Content src/TokenTalk/Transcription/ITranscriptionProvider.cs

```

```output
using TokenTalk.Audio;

namespace TokenTalk.Transcription;

public interface ITranscriptionProvider
{
    string Name { get; }
    Task<string> TranscribeAsync(AudioSegment audio, CancellationToken ct = default);
}
```

`TranscriptionProviderFactory` implements the same interface and routes at call-time to either `OpenAiWhisperProvider` or `WhisperCppProvider` based on the live config value. This enables hot-switching between cloud and local inference without restarting:

```powershell
Get-Content src/TokenTalk/Transcription/TranscriptionProviderFactory.cs

```

```output
using TokenTalk.Audio;

namespace TokenTalk.Transcription;

/// <summary>
/// Delegates to OpenAI or whisper.cpp provider based on current config,
/// enabling hot-switching at runtime without restart.
/// </summary>
public sealed class TranscriptionProviderFactory : ITranscriptionProvider, IDisposable
{
    private readonly Func<string> _getProvider;
    private readonly ITranscriptionProvider _openAiProvider;
    private readonly ITranscriptionProvider _whisperCppProvider;

    public string Name => Current.Name;

    public TranscriptionProviderFactory(
        Func<string> getProvider,
        ITranscriptionProvider openAiProvider,
        ITranscriptionProvider whisperCppProvider)
    {
        _getProvider = getProvider;
        _openAiProvider = openAiProvider;
        _whisperCppProvider = whisperCppProvider;
    }

    private ITranscriptionProvider Current =>
        _getProvider().Equals("whisper.cpp", StringComparison.OrdinalIgnoreCase)
            ? _whisperCppProvider
            : _openAiProvider;

    public Task<string> TranscribeAsync(AudioSegment audio, CancellationToken ct = default)
        => Current.TranscribeAsync(audio, ct);

    public void Dispose()
    {
        (_openAiProvider as IDisposable)?.Dispose();
        (_whisperCppProvider as IDisposable)?.Dispose();
    }
}
```

## Transcription/OpenAiWhisperProvider — Cloud Transcription

`OpenAiWhisperProvider` POSTs the WAV bytes to `https://api.openai.com/v1/audio/transcriptions` as a `multipart/form-data` request. The audio, model, optional language, and a carefully engineered prompt are all included. Dictionary "simple terms" are appended to the prompt as a hint list — Whisper uses the prompt as a prior to bias recognition toward those words:

```powershell
Get-Content src/TokenTalk/Transcription/OpenAiWhisperProvider.cs | Select-Object -Skip 33 -First 51

```

```output
    public async Task<string> TranscribeAsync(AudioSegment audio, CancellationToken ct = default)
    {
        var apiKey = _getApiKey();
        var model = _getModel();
        var language = _getLanguage();
        var prompt = _getPrompt();

        var httpClient = _httpClientFactory.CreateClient("OpenAI");
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        using var content = new MultipartFormDataContent();

        // Add audio file
        var audioContent = new ByteArrayContent(audio.WavData);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "audio.wav");

        // Add model
        content.Add(new StringContent(model), "model");

        // Add language ("auto" or empty = omit parameter, Whisper auto-detects)
        if (!string.IsNullOrEmpty(language) && language != "auto")
            content.Add(new StringContent(language), "language");

        // Build prompt: user prompt + dictionary simple terms
        var promptParts = new List<string>();
        if (!string.IsNullOrEmpty(prompt))
            promptParts.Add(prompt);
        promptParts.AddRange(_dictionaryTerms);

        if (promptParts.Count > 0)
            content.Add(new StringContent(string.Join(", ", promptParts)), "prompt");

        var response = await httpClient.PostAsync(
            "https://api.openai.com/v1/audio/transcriptions",
            content,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Whisper API error ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
    }
}
```

The prompt itself is assembled in `Program.cs` (`BuildWhisperPrompt`). In developer mode it includes a curated vocabulary of ~60 programming languages, frameworks, cloud terms, tools, and acronyms — dramatically improving accuracy for technical dictation. The prompt is re-read via a `Func<string>` lambda on every call, so edits in the settings UI apply immediately.

## Transcription/WhisperCppProvider — Local Inference

`WhisperCppProvider` runs inference locally via `Whisper.net`, a .NET binding to the C++ GGML implementation of Whisper. Models are quantized GGML files (75 MB – 1.5 GB) downloaded from Hugging Face. The `WhisperFactory` is reused across calls and reloaded only when the model path changes:

```powershell
Get-Content src/TokenTalk/Transcription/WhisperCppProvider.cs | Select-Object -Skip 21 -First 43

```

```output
    public async Task<string> TranscribeAsync(AudioSegment audio, CancellationToken ct = default)
    {
        var modelPath = _getModelPath();
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            throw new InvalidOperationException(
                $"whisper.cpp model not found at '{modelPath}'. Download a model in Settings.");

        await _semaphore.WaitAsync(ct);
        // SemaphoreSlim.WaitAsync() completes synchronously when the semaphore is available,
        // meaning the code below would run without ever yielding. This prevents the overlay
        // from rendering before transcription finishes. Force a genuine async yield here so
        // the WinForms STA thread can process ShowProcessing before we start CPU-bound work.
        await Task.Yield();
        try
        {
            // Reload factory only when model path changes
            if (_factory == null || _loadedModelPath != modelPath)
            {
                _factory?.Dispose();
                _factory = WhisperFactory.FromPath(modelPath);
                _loadedModelPath = modelPath;
            }

            var language = _getLanguage();
            var builder = _factory.CreateBuilder();
            if (!string.IsNullOrEmpty(language) && language != "auto")
                builder = builder.WithLanguage(language);

            using var processor = builder.Build();
            using var stream = new MemoryStream(audio.WavData);

            var sb = new System.Text.StringBuilder();
            await foreach (var segment in processor.ProcessAsync(stream, ct))
                sb.Append(segment.Text);

            return sb.ToString().Trim();
        }
        finally
        {
            _semaphore.Release();
        }
    }

```

Two threading subtleties are worth calling out:

1. **`SemaphoreSlim(1,1)`** serialises transcription calls — whisper.cpp is not thread-safe.
2. **`await Task.Yield()`** forces a genuine async yield before CPU-bound work. Without it, `SemaphoreSlim.WaitAsync()` completes synchronously when the semaphore is free, keeping execution on the caller's thread and blocking the WinForms STA from rendering the "processing" spinner before inference begins.

## PostProcessing/PostProcessingPipeline — The Transform Chain

After transcription, the text passes through a configurable chain of `IPostProcessor` implementations. The interface is deliberately minimal:

```powershell
Get-Content src/TokenTalk/PostProcessing/IPostProcessor.cs

```

```output
namespace TokenTalk.PostProcessing;

public interface IPostProcessor
{
    Task<string> ProcessAsync(string text, CancellationToken ct = default);
}
```

The pipeline threads each processor's output into the next. Failures in individual processors are swallowed (with a warning log) so a single broken processor never kills the whole dictation:

```powershell
Get-Content src/TokenTalk/PostProcessing/PostProcessingPipeline.cs | Select-Object -Skip 19 -First 18

```

```output
    public async Task<string> ProcessAsync(string text, CancellationToken ct = default)
    {
        var result = text;
        foreach (var processor in _processors)
        {
            try
            {
                result = await processor.ProcessAsync(result, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Post-processor {Processor} failed, continuing with previous text",
                    processor.GetType().Name);
            }
        }
        return result;
    }
}
```

## PostProcessing/VoiceCommandProcessor — Spoken Punctuation

When enabled, `VoiceCommandProcessor` translates spoken phrases into their punctuation equivalents. A static table maps phrases to replacements. Replacements use word-boundary detection so "new line" only fires as a standalone phrase, not inside words like "airline":

```powershell
Get-Content src/TokenTalk/PostProcessing/VoiceCommandProcessor.cs | Select-Object -Skip 11 -First 50

```

```output
    private static readonly (string Phrase, string Replacement)[] Commands =
    [
        ("new line", "\n"),
        ("newline", "\n"),
        ("new paragraph", "\n\n"),
        ("full stop", "."),
        ("dot", "."),
        ("comma", ","),
        ("question mark", "?"),
        ("exclamation mark", "!"),
        ("exclamation point", "!"),
        ("colon", ":"),
        ("semicolon", ";"),
        ("open quote", "\""),
        ("close quote", "\""),
        ("open parenthesis", "("),
        ("close parenthesis", ")"),
        ("open bracket", "["),
        ("close bracket", "]"),
        ("open brace", "{"),
        ("close brace", "}"),
        ("dash", "-"),
        ("underscore", "_"),
        ("slash", "/"),
        ("backslash", "\\"),
        ("at sign space", "@ "), // check before "at sign"
        ("at sign", "@"),
        ("at-sign", "@"),
        ("atsign", "@"),
        ("hash", "#"),
        ("dollar sign", "$"),
        ("percent sign", "%"),
        ("ampersand", "&"),
        ("asterisk", "*"),
        ("plus", "+"),
        ("equals", "="),
    ];

    public Task<string> ProcessAsync(string text, CancellationToken ct = default)
    {
        if (!_isEnabled())
            return Task.FromResult(text);

        var result = text;
        foreach (var (phrase, replacement) in Commands)
        {
            result = ReplaceWithWordBoundaries(result, phrase, replacement);
        }
        return Task.FromResult(result);
    }
```

## PostProcessing/DictionaryService — Custom Dictionary

The dictionary file (`%APPDATA%\TokenTalk\dictionary.txt`) has two kinds of entries:

- **Simple terms** (no `->`) — appended to the Whisper prompt as hint words to bias recognition.
- **Correction mappings** (`misheard -> correct`) — applied as case-insensitive text replacements after transcription by `DictionaryProcessor`.

`DictionaryService.Load` parses the file:

```powershell
Get-Content src/TokenTalk/PostProcessing/DictionaryService.cs | Select-Object -Skip 13 -First 46

```

```output
    public CustomDictionary Load(string path)
    {
        var resolvedPath = ResolvePath(path);

        if (!File.Exists(resolvedPath))
            return new CustomDictionary();

        try
        {
            var entries = new List<DictionaryEntry>();
            foreach (var line in File.ReadAllLines(resolvedPath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;

                if (trimmed.Contains("->"))
                {
                    var parts = trimmed.Split("->", 2);
                    if (parts.Length == 2)
                    {
                        entries.Add(new DictionaryEntry
                        {
                            Original = parts[0].Trim(),
                            Replacement = parts[1].Trim(),
                            IsMapping = true
                        });
                    }
                }
                else
                {
                    entries.Add(new DictionaryEntry
                    {
                        Replacement = trimmed,
                        IsMapping = false
                    });
                }
            }
            return new CustomDictionary { Entries = entries };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load dictionary from {Path}", resolvedPath);
            return new CustomDictionary();
        }
    }
```

## Platform/PasteService — Injecting Text into the Active Window

`PasteService` uses the clipboard-paste trick: write the processed text to the clipboard, simulate Ctrl+V into the currently focused window, then restore the original clipboard contents. This works in virtually every Windows application, including those that do not support `WM_SETTEXT`:

```powershell
Get-Content src/TokenTalk/Platform/PasteService.cs | Select-Object -Skip 11 -First 27

```

```output
    public async Task PasteTextAsync(string text, CancellationToken ct = default)
    {
        // Save current clipboard
        string original = string.Empty;
        try { original = _clipboard.GetText(); }
        catch { /* ignore */ }

        // Set clipboard to new text
        _clipboard.SetText(text);

        // Wait for clipboard to be ready
        await Task.Delay(50, ct);

        // Simulate Ctrl+V
        SendCtrlV();

        // Wait for target app to process paste
        await Task.Delay(100, ct);

        // Restore clipboard
        if (!string.IsNullOrEmpty(original))
        {
            try { _clipboard.SetText(original); }
            catch { /* ignore */ }
        }
    }

```

`ClipboardService` uses raw Win32 clipboard APIs via P/Invoke. Windows requires clipboard calls to be made from an STA thread, so every operation is dispatched to a fresh dedicated STA thread and joined synchronously. `SetText` allocates moveable global memory, writes UTF-16 text, and hands the handle to the clipboard:

```powershell
Get-Content src/TokenTalk/Platform/ClipboardService.cs | Select-Object -Skip 42 -First 51

```

```output
    public bool SetText(string text)
    {
        return RunOnStaThread(() =>
        {
            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
                return false;

            try
            {
                NativeMethods.EmptyClipboard();

                // Allocate global memory for the text (UTF-16 + null terminator)
                int byteCount = (text.Length + 1) * 2;
                var hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)byteCount);
                if (hGlobal == IntPtr.Zero)
                    return false;

                var ptr = NativeMethods.GlobalLock(hGlobal);
                if (ptr == IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(hGlobal);
                    return false;
                }

                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                    // Write null terminator
                    Marshal.WriteInt16(ptr, text.Length * 2, 0);
                }
                finally
                {
                    NativeMethods.GlobalUnlock(hGlobal);
                }

                var result = NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hGlobal);
                if (result == IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(hGlobal);
                    return false;
                }

                return true;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        });
    }

```

## Storage/Dictation + DictationRepository — EF Core SQLite

Every dictation — successful or not — is persisted to a local SQLite database via EF Core. The `Dictation` entity captures rich telemetry including latency breakdowns at each pipeline stage:

```powershell
Get-Content src/TokenTalk/Storage/Dictation.cs

```

```output
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TokenTalk.Storage;

[Table("dictations")]
public class Dictation
{
    [Column("id")]
    [JsonPropertyName("ID")]
    public long Id { get; set; }

    [Column("timestamp")]
    [JsonPropertyName("Timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Column("recording_start_ms")]
    [JsonPropertyName("RecordingStartMs")]
    public long RecordingStartMs { get; set; }

    [Column("recording_duration_ms")]
    [JsonPropertyName("RecordingDurationMs")]
    public long RecordingDurationMs { get; set; }

    [Column("transcription_latency_ms")]
    [JsonPropertyName("TranscriptionLatencyMs")]
    public long TranscriptionLatencyMs { get; set; }

    [Column("injection_latency_ms")]
    [JsonPropertyName("InjectionLatencyMs")]
    public long InjectionLatencyMs { get; set; }

    [Column("total_latency_ms")]
    [JsonPropertyName("TotalLatencyMs")]
    public long TotalLatencyMs { get; set; }

    [Column("audio_size_bytes")]
    [JsonPropertyName("AudioSizeBytes")]
    public long AudioSizeBytes { get; set; }

    [Column("audio_sample_rate")]
    [JsonPropertyName("AudioSampleRate")]
    public int AudioSampleRate { get; set; }

    [Column("provider")]
    [JsonPropertyName("Provider")]
    public string Provider { get; set; } = string.Empty;

    [Column("model")]
    [JsonPropertyName("Model")]
    public string Model { get; set; } = string.Empty;

    [Column("language")]
    [JsonPropertyName("Language")]
    public string Language { get; set; } = string.Empty;

    [Column("transcribed_text")]
    [JsonPropertyName("TranscribedText")]
    public string TranscribedText { get; set; } = string.Empty;

    [Column("word_count")]
    [JsonPropertyName("WordCount")]
    public int WordCount { get; set; }

    [Column("character_count")]
    [JsonPropertyName("CharacterCount")]
    public int CharacterCount { get; set; }

    [Column("success")]
    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [Column("error_message")]
    [JsonPropertyName("ErrorMessage")]
    public string? ErrorMessage { get; set; }
}
```

`DictationRepository` wraps the EF Core `DbContext`. Beyond simple save/retrieve it provides aggregated stats queries — total words, average latency per stage, daily breakdowns, provider stats, and word-frequency counts — all used by the analytics dashboard:

```powershell
Get-Content src/TokenTalk/Storage/DictationRepository.cs | Select-Object -Skip 13 -First 19

```

```output
    public async Task SaveAsync(Dictation dictation, CancellationToken ct = default)
    {
        _db.Dictations.Add(dictation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<(List<Dictation> Items, int Total)> GetHistoryAsync(
        int limit, int offset, CancellationToken ct = default)
    {
        var total = await _db.Dictations.CountAsync(ct);
        var items = await _db.Dictations
            .OrderByDescending(d => d.Timestamp)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        return (items, total);
    }

```

## Overlay/DictationOverlay — Visual Feedback

`DictationOverlay` is a transparent borderless WinForms window that hovers on screen during recording (pulsing mic waveform) and transcription (spinner). It lives on the WinForms STA thread; `DictationOverlay` is a thin thread-safe wrapper that marshals state changes back via `BeginInvoke`.

Amplitude data is the exception — it is written to a `volatile float` directly from the NAudio callback thread and read by the paint timer at ~30 fps with no marshalling needed. A gauge reading does not need a happens-before guarantee:

```powershell
Get-Content src/TokenTalk/Overlay/DictationOverlay.cs

```

```output
using System.Windows.Forms;

namespace TokenTalk.Overlay;

public sealed class DictationOverlay : IDisposable
{
    private DictationOverlayForm? _form;

    /// <summary>Must be called on the STA thread (inside TrayIconManager.Run).</summary>
    public void Initialize()
    {
        _form = new DictationOverlayForm();
    }

    public void StartRecording()
    {
        var form = _form;
        if (form?.IsHandleCreated == true)
            form.BeginInvoke(form.ShowRecording);
    }

    public void StopRecording()
    {
        var form = _form;
        if (form?.IsHandleCreated == true)
            form.BeginInvoke(form.HideRecording);
    }

    public void StartProcessing()
    {
        var form = _form;
        if (form?.IsHandleCreated == true)
            form.BeginInvoke(form.ShowProcessing);
    }

    public void StopProcessing()
    {
        var form = _form;
        if (form?.IsHandleCreated == true)
            form.BeginInvoke(form.HideRecording);
    }

    /// <summary>
    // / Called from the NAudio thread. Writes directly to a volatile field
    /// no BeginInvoke needed; the UI paint timer reads it at ~30fps.
    /// </summary>
    public void PushAmplitude(float amp)
    {
        _form?.PushAmplitude(amp);
    }

    public void Dispose()
    {
        var form = _form;
        _form = null;
        if (form != null && form.IsHandleCreated)
        {
            form.BeginInvoke(() =>
            {
                form.Close();
                form.Dispose();
            });
        }
        else
        {
            form?.Dispose();
        }
    }
}
```

## Tray/TrayIconManager — System Tray Icon

`NotifyIcon` requires a WinForms message loop (`Application.Run()`), which requires an STA thread. Rather than repurposing the main thread (which runs the WPF loop), a dedicated STA background thread is spun up in `Program.cs` just for this. That same thread also calls `overlay.Initialize()` — the overlay form must be created on the same STA thread as the WinForms loop:

```powershell
Get-Content src/TokenTalk/Tray/TrayIconManager.cs | Select-Object -Skip 21 -First 41

```

```output
    public void Run(DictationOverlay? overlay = null)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        overlay?.Initialize();

        _notifyIcon = new NotifyIcon
        {
            Text = "TokenTalk - Voice Dictation",
            Visible = true,
            Icon = LoadIcon(),
        };

        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open TokenTalk");
        openItem.Click += (_, _) =>
        {
            try { _openWindowCallback(); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to open window"); }
        };

        var separator = new ToolStripSeparator();

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) =>
        {
            _cts.Cancel();
            Application.ExitThread();
        };

        menu.Items.Add(openItem);
        menu.Items.Add(separator);
        menu.Items.Add(quitItem);
        _notifyIcon.ContextMenuStrip = menu;

        // Run the Windows Forms message loop on this STA thread
        Application.Run();
    }
```

## The Complete Flow — End to End



Here is one full dictation cycle, traced through the code:



**1. Hotkey pressed** (`Ctrl+Shift+V`)

`HotkeyListener.HookCallback` detects the combo, writes `HotkeyEvent(Pressed)` to the `Channel`.



**2. Agent reads the event** → `HandleHotkeyPressed`

`AudioRecorder.Start()` opens `WaveInEvent` at 16 kHz mono, starts buffering. `DictationOverlay.StartRecording()` shows the mic animation.



**3. User speaks**

NAudio fires `DataAvailable` every 50 ms. Samples accumulate in `MemoryStream` via `WaveFileWriter`. RMS amplitude is pushed to the overlay for the waveform animation.



**4. Hotkey released**

`HotkeyEvent(Released)` written to channel. `HandleHotkeyReleasedAsync` fires (fire-and-forget). `AudioRecorder.Stop()` finalises the WAV, returns `AudioSegment`. Overlay switches to the processing spinner.



**5. Validation**

Duration < 100 ms → discard and reset. RMS < silence threshold → discard and reset.



**6. Transcription**

`TranscriptionProviderFactory` routes to the configured provider.

- OpenAI path: multipart POST to `/v1/audio/transcriptions` with WAV bytes, model, language, prompt (including developer vocabulary and dictionary terms as hints).

- whisper.cpp path: local GGML inference via `Whisper.net`; `SemaphoreSlim` serialises calls; `Task.Yield()` ensures a genuine async suspend before CPU-bound work.



**7. Post-processing**

`DictionaryProcessor`: case-insensitive replacements from `dictionary.txt`.

`VoiceCommandProcessor` (if enabled): "new line" → `\n`, "comma" → `,`, "open quote" → `"`, etc.



**8. Text injection**

`ClipboardService.SetText(processed)` writes to the Win32 clipboard on a fresh STA thread.

`PasteService` synthesises Ctrl+V via `SendInput` (4 key events: Ctrl↓ V↓ V↑ Ctrl↑).

100 ms wait for the target app to process the paste.

Clipboard restored to its original contents.



**9. Persistence**

`DictationRepository.SaveAsync` writes a row to SQLite with: transcribed text, word/character counts, recording duration, transcription latency, injection latency, total latency, provider, model, language, success flag.



**10. Reset**

`DictationOverlay.StopProcessing()` hides the overlay. `StatusChanged` fires `"idle"`. Agent loop returns to `await foreach` for the next hotkey event.


