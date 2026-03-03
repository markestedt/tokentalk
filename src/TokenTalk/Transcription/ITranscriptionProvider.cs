using TokenTalk.Audio;

namespace TokenTalk.Transcription;

public interface ITranscriptionProvider
{
    string Name { get; }
    Task<string> TranscribeAsync(AudioSegment audio, CancellationToken ct = default);
}
