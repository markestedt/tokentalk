using Whisper.net;
using TokenTalk.Audio;

namespace TokenTalk.Transcription;

public sealed class WhisperCppProvider : ITranscriptionProvider, IDisposable
{
    private readonly Func<string> _getModelPath;
    private readonly Func<string> _getLanguage;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private WhisperFactory? _factory;
    private string _loadedModelPath = "";

    public string Name => "whisper.cpp";

    public WhisperCppProvider(Func<string> getModelPath, Func<string> getLanguage)
    {
        _getModelPath = getModelPath;
        _getLanguage = getLanguage;
    }

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

    public void Dispose()
    {
        _factory?.Dispose();
        _semaphore.Dispose();
    }
}
