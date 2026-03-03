using Microsoft.Extensions.Logging;

namespace TokenTalk.PostProcessing;

public class DictionaryService
{
    private readonly ILogger<DictionaryService> _logger;

    public DictionaryService(ILogger<DictionaryService> logger)
    {
        _logger = logger;
    }

    public CustomDictionary Load(string path)
    {
        var resolvedPath = ResolvePath(path);

        if (!File.Exists(resolvedPath))
            return new CustomDictionary();

        try
        {
            var entries = new List<DictionaryEntry>();
            foreach (var line in File.ReadAllLines(resolvedPath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;

                if (trimmed.Contains("->"))
                {
                    var parts = trimmed.Split("->", 2);
                    if (parts.Length == 2)
                    {
                        entries.Add(new DictionaryEntry
                        {
                            Original = parts[0].Trim(),
                            Replacement = parts[1].Trim(),
                            IsMapping = true
                        });
                    }
                }
                else
                {
                    entries.Add(new DictionaryEntry
                    {
                        Replacement = trimmed,
                        IsMapping = false
                    });
                }
            }
            return new CustomDictionary { Entries = entries };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load dictionary from {Path}", resolvedPath);
            return new CustomDictionary();
        }
    }

    public void Save(string path, CustomDictionary dictionary)
    {
        var resolvedPath = ResolvePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);

        using var writer = new StreamWriter(resolvedPath);
        writer.WriteLine("# TokenTalk Custom Dictionary");
        writer.WriteLine("# Simple terms (bias Whisper):");

        foreach (var entry in dictionary.Entries.Where(e => !e.IsMapping))
            writer.WriteLine(entry.Replacement);

        writer.WriteLine();
        writer.WriteLine("# Correction mappings (misheard -> correct):");
        foreach (var entry in dictionary.Entries.Where(e => e.IsMapping))
            writer.WriteLine($"{entry.Original} -> {entry.Replacement}");
    }

    public static string ResolvePath(string path)
    {
        if (!string.IsNullOrEmpty(path))
            return path;

        var appData = Environment.GetEnvironmentVariable("APPDATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming");
        return Path.Combine(appData, "TokenTalk", "dictionary.txt");
    }
}
