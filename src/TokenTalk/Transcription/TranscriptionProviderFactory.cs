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
