# Copilot Instructions — TokenTalk

## Project Overview

TokenTalk is a Windows voice dictation agent built on .NET 10.0 (WPF + WinForms). It captures audio via a global hotkey, transcribes it using either OpenAI Whisper (cloud) or whisper.cpp (local), processes the text through a configurable pipeline, and injects it into the active window via clipboard-based paste simulation.

## Build & Run

```bash
dotnet build -c Debug
dotnet build -c Release          # self-contained single-file exe for win-x64
dotnet publish -c Release -o publish
dotnet run --project src/TokenTalk/TokenTalk.csproj
```

No tests or linting are configured.

## Architecture

### Core Flow

```
Hotkey press/release → AudioRecorder → ITranscriptionProvider → PostProcessingPipeline → PasteService → SQLite + UI events
```

`Agent.cs` is the central orchestrator. It listens for hotkey events via `Channel<HotkeyEvent>`, coordinates the full dictation lifecycle, and raises events (`StatusChanged`, `DictationCompleted`) consumed by the UI.

`Program.cs` wires everything manually — no DI container. Dependencies use `Func<>` delegates for lazy config access so components always read live configuration.

### Key Abstractions

- **`ITranscriptionProvider`** — `Name` + `TranscribeAsync(AudioSegment, CancellationToken)`. Two implementations: `OpenAiWhisperProvider` (cloud, HTTP) and `WhisperCppProvider` (local, via `Whisper.net`). `TranscriptionProviderFactory` selects between them based on the `Transcription.Provider` config value (`"openai"` or `"whisper.cpp"`). The active provider is resolved at call time via a `Func<string>` delegate so config changes take effect without restart.
- **`ModelManager`** — Manages local GGML model files (`%APPDATA%\TokenTalk\models\`). Provides a catalog of known models with download URLs (HuggingFace), progress-reporting async download, and delete.
- **`IPostProcessor`** — `ProcessAsync(string, CancellationToken)`. Chain-of-responsibility pipeline where each processor transforms text sequentially. Failures are caught and logged — processing continues with the last successful result.
- **`ConfigManager`** — Thread-safe (`lock`) JSON config reader/writer. Runtime config at `%APPDATA%\TokenTalk\appsettings.json`, bundled defaults in `src/TokenTalk/Configuration/appsettings.json`. Key `Transcription` fields: `Provider` (`"openai"` | `"whisper.cpp"`), `ModelPath` (absolute path to a local GGML `.bin` file for whisper.cpp), `Language` (`"auto"` or BCP-47 code).

### Threading Model

Three threads cooperate:
1. **WPF main thread (STA)** — UI message loop via `wpfApp.Run()`
2. **Tray/Overlay thread (STA)** — WinForms `NotifyIcon` + `DictationOverlayForm` on a dedicated STA thread with `Application.Run()`
3. **Agent background thread** — `Task.Run(() => agent.RunAsync(ct))`

The `HotkeyListener` runs a low-level keyboard hook on its own thread with a manual Win32 message pump, communicating via `Channel<HotkeyEvent>`.

### Platform Layer

Win32 P/Invoke in `Platform/`:
- `NativeMethods` — `internal static` class with all P/Invoke signatures (keyboard hooks, `SendInput`, clipboard API)
- `HotkeyListener` — Low-level keyboard hook tracking modifier state in the hook callback; uses `Channel` for async event delivery
- `ClipboardService` — Clipboard operations run on STA threads via `RunOnStaThread<T>` helper
- `PasteService` — Saves clipboard → sets text → `SendInput` Ctrl+V → restores clipboard

### Storage

EF Core + SQLite with `EnsureCreatedAsync()` (no migrations). `DictationRepository` uses a classic repository pattern. The `Dictation` entity has both `[Column]` (EF) and `[JsonPropertyName]` (API serialization) attributes. Database columns use snake_case.

### UI

WPF with MVVM pattern. `ViewModelBase` provides `INotifyPropertyChanged` with `SetProperty<T>`. `MainViewModel` composes page-specific ViewModels (`HomeViewModel`, `HistoryViewModel`, `DictionaryViewModel`, `SettingsViewModel`) with navigation via `AppPage` enum.

The `DictationOverlayForm` (WinForms) is a transparent, topmost, non-activatable floating widget using GDI+ custom painting and `volatile float` for lock-free amplitude data flow from NAudio.

## Publishing

See `PUBLISHING.md` for full instructions. Key point: `IncludeNativeLibrariesForSelfExtract` is set to `false` so that whisper.cpp native DLLs (`whisper.dll`, `ggml-*.dll`) are deployed as separate files alongside the exe rather than embedded. Embedding them causes silent transcription failures when the app runs as administrator because `%TEMP%` resolves to `C:\Windows\Temp`, breaking the .NET single-file extraction path that `Whisper.net` relies on.

## Conventions

- **C# 12+ features**: File-scoped namespaces, collection expressions, record types
- **Nullable**: Enabled project-wide, used consistently
- **Async**: Full async/await pipeline with `CancellationToken` threaded throughout; fire-and-forget pattern `_ = HandleHotkeyReleasedAsync(ct)` for hotkey release
- **Error handling**: Per-step try/catch in `Agent` with structured logging; pipeline processors are fault-isolated
- **DI**: Manual composition in `Program.Main()` — no IoC container. Use `Func<>` for live config access
- **Naming**: PascalCase types/properties, `_camelCase` private fields, snake_case DB columns
- **Logging**: `Microsoft.Extensions.Logging` with structured log message templates (`{Hotkey}`, `{Provider}`)
- **Configuration**: Nested POCO model in `TokenTalkOptions` — sections for `Hotkey`, `Audio`, `Transcription`, `PostProcessing`
