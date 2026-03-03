namespace TokenTalk.Transcription;

public record ModelInfo(
    string Name,
    string FileName,
    string SizeDescription,
    string SpeedDescription,
    string QualityDescription)
{
    public string Url =>
        $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}";
}

public class ModelManager
{
    private readonly string _modelsDirectory;
    // Dedicated client with a long timeout for large model downloads
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromHours(2) };

    public static readonly IReadOnlyList<ModelInfo> Catalog =
    [
        new("tiny",           "ggml-tiny.bin",           "~75 MB",  "Fastest", "Basic"),
        new("tiny.en",        "ggml-tiny.en.bin",        "~75 MB",  "Fastest", "Basic (EN only)"),
        new("base",           "ggml-base.bin",           "~142 MB", "Fast",    "Good"),
        new("base.en",        "ggml-base.en.bin",        "~142 MB", "Fast",    "Good (EN only)"),
        new("small",          "ggml-small.bin",          "~466 MB", "Medium",  "Better"),
        new("small.en",       "ggml-small.en.bin",       "~466 MB", "Medium",  "Better (EN only)"),
        new("medium",         "ggml-medium.bin",         "~1.5 GB", "Slow",    "Great"),
        new("large-v3-turbo", "ggml-large-v3-turbo.bin", "~1.5 GB", "Slow",    "Best"),
    ];

    public ModelManager(string modelsDirectory)
    {
        _modelsDirectory = modelsDirectory;
        Directory.CreateDirectory(_modelsDirectory);
    }

    public string ModelsDirectory => _modelsDirectory;

    public string GetModelPath(ModelInfo model) =>
        Path.Combine(_modelsDirectory, model.FileName);

    public bool IsDownloaded(ModelInfo model) =>
        File.Exists(GetModelPath(model));

    public IEnumerable<ModelInfo> GetDownloadedModels() =>
        Catalog.Where(IsDownloaded);

    public async Task DownloadAsync(
        ModelInfo model,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var destPath = GetModelPath(model);
        var tempPath = destPath + ".tmp";

        try
        {
            using var response = await _httpClient.GetAsync(
                model.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
            await using var destStream = File.Create(tempPath);

            var buffer = new byte[81920];
            long bytesRead = 0;
            int read;
            while ((read = await sourceStream.ReadAsync(buffer, ct)) > 0)
            {
                await destStream.WriteAsync(buffer.AsMemory(0, read), ct);
                bytesRead += read;
                if (totalBytes > 0)
                    progress?.Report((double)bytesRead / totalBytes);
            }

            await destStream.FlushAsync(ct);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }

        File.Move(tempPath, destPath, overwrite: true);
        progress?.Report(1.0);
    }

    public void DeleteModel(ModelInfo model)
    {
        var path = GetModelPath(model);
        if (File.Exists(path)) File.Delete(path);
    }
}
