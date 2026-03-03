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
