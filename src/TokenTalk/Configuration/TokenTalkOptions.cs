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

