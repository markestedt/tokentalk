# Publishing & Installation

## Build & Publish

```bash
dotnet publish src/TokenTalk/TokenTalk.csproj -c Release -o publish
```

The output lands in the `publish\` folder.

> **Note:** Native libraries (whisper.cpp, SQLite, WPF graphics) are intentionally kept as
> separate files alongside the exe (`IncludeNativeLibrariesForSelfExtract=false`). This is
> required for whisper.cpp to work correctly, particularly when the app is run as administrator
> (which changes `%TEMP%` to `C:\Windows\Temp` and breaks temp-dir extraction of native DLLs).

---

## Installation (win-x64)

Copy the following files to your installation folder, preserving the subfolder structure:

```
TokenTalk.exe
D3DCompiler_47_cor3.dll
e_sqlite3.dll
PenImc_cor3.dll
PresentationNative_cor3.dll
vcruntime140_cor3.dll
wpfgfx_cor3.dll
TokenTalk.staticwebassets.endpoints.json
Configuration\appsettings.json
runtimes\win-x64\whisper.dll
runtimes\win-x64\ggml-whisper.dll
runtimes\win-x64\ggml-base-whisper.dll
runtimes\win-x64\ggml-cpu-whisper.dll
```

### What to skip

| File / Folder | Reason |
|---|---|
| `TokenTalk.pdb` | Debug symbols — not needed at runtime |
| `runtimes\win-arm64\` | Wrong architecture |
| `runtimes\win-x86\` | Wrong architecture |

---

## Runtime data (auto-created on first run)

TokenTalk stores user data in `%APPDATA%\TokenTalk\`:

| Path | Contents |
|---|---|
| `appsettings.json` | User configuration (created from bundled default on first run) |
| `tokentalk.db` | SQLite dictation history |
| `models\` | Downloaded whisper.cpp model files (e.g. `ggml-base.bin`) |
