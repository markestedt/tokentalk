using Microsoft.Extensions.Logging;
using TokenTalk.Audio;
using TokenTalk.Configuration;
using TokenTalk.Overlay;
using TokenTalk.Platform;
using TokenTalk.PostProcessing;
using TokenTalk.Storage;
using TokenTalk.Transcription;

namespace TokenTalk;

public class Agent : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly AudioRecorder _recorder;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly PostProcessingPipeline _pipeline;
    private readonly ClipboardService _clipboard;
    private readonly PasteService _paste;
    private readonly DictationRepository _repository;
    private readonly DictationOverlay? _overlay;
    private readonly HotkeyListener _hotkeyListener;
    private readonly ILogger<Agent> _logger;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<DictationCompletedEventArgs>? DictationCompleted;

    public Agent(
        ConfigManager configManager,
        AudioRecorder recorder,
        ITranscriptionProvider transcriptionProvider,
        PostProcessingPipeline pipeline,
        ClipboardService clipboard,
        PasteService paste,
        DictationRepository repository,
        DictationOverlay? overlay,
        ILogger<Agent> logger)
    {
        _configManager = configManager;
        _recorder = recorder;
        _transcriptionProvider = transcriptionProvider;
        _pipeline = pipeline;
        _clipboard = clipboard;
        _paste = paste;
        _repository = repository;
        _overlay = overlay;
        _hotkeyListener = new HotkeyListener();
        _logger = logger;

        if (_overlay != null)
            _recorder.AmplitudeAvailable += _overlay.PushAmplitude;
    }

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
                    _logger.LogInformation("Post-processed: {Original} → {Processed}", text, processed);
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
            DictationCompleted?.Invoke(this, new DictationCompletedEventArgs(dictation));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in dictation flow");
            dictation.ErrorMessage = ex.Message;
            dictation.TotalLatencyMs = (long)(DateTimeOffset.UtcNow - recordingStart).TotalMilliseconds;
            await SaveDictationAsync(dictation, ct);
        }
        finally
        {
            _overlay?.StopProcessing();
            SetStatus("idle");
        }
    }

    private async Task SaveDictationAsync(Dictation dictation, CancellationToken ct)
    {
        try
        {
            await _repository.SaveAsync(dictation, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save dictation");
        }
    }

    private void SetStatus(string status)
    {
        StatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        if (_overlay != null)
            _recorder.AmplitudeAvailable -= _overlay.PushAmplitude;
        _hotkeyListener.Dispose();
        _recorder.Dispose();
    }
}

public sealed class DictationCompletedEventArgs : EventArgs
{
    public Dictation Dictation { get; }
    public DictationCompletedEventArgs(Dictation d) => Dictation = d;
}
