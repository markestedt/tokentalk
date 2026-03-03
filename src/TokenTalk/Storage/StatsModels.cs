namespace TokenTalk.Storage;

public class OverallStats
{
    public int TotalDictations { get; set; }
    public int TotalWords { get; set; }
    public int TotalCharacters { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double AvgRecordingMs { get; set; }
    public double AvgTranscriptionMs { get; set; }
    public double AvgInjectionMs { get; set; }
    public double AvgTotalLatencyMs { get; set; }
    public long TotalRecordingTimeMs { get; set; }
    public long TotalAudioSizeBytes { get; set; }
}

public class DailyStats
{
    public string Date { get; set; } = string.Empty;
    public int TotalDictations { get; set; }
    public int TotalWords { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
}

public class ProviderStats
{
    public string Provider { get; set; } = string.Empty;
    public int TotalDictations { get; set; }
    public int TotalWords { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double AvgLatencyMs { get; set; }
}

public class HeatmapStats
{
    public string Date { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class WordFrequencyEntry
{
    public string Word { get; set; } = string.Empty;
    public int Count { get; set; }
}
