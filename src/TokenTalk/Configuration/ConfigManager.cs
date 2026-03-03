using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TokenTalk.Configuration;

public class ConfigManager
{
    private TokenTalkOptions _current;
    private readonly string _configPath;
    private readonly ILogger<ConfigManager> _logger;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public ConfigManager(string configPath, ILogger<ConfigManager> logger)
    {
        _configPath = configPath;
        _logger = logger;
        _current = Load();
    }

    public TokenTalkOptions Current
    {
        get { lock (_lock) return _current; }
    }

    private TokenTalkOptions Load()
    {
        if (!File.Exists(_configPath))
        {
            _logger.LogInformation("Config file not found at {Path}, creating defaults", _configPath);
            var defaults = new TokenTalkOptions();
            SaveInternal(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var options = JsonSerializer.Deserialize<TokenTalkOptions>(json, JsonOptions);
            return options ?? new TokenTalkOptions();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config from {Path}, using defaults", _configPath);
            return new TokenTalkOptions();
        }
    }

    public void Save(TokenTalkOptions options)
    {
        lock (_lock)
        {
            SaveInternal(options);
            _current = options;
        }
    }

    private void SaveInternal(TokenTalkOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        var json = JsonSerializer.Serialize(options, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    public static string GetConfigDirectory()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming");
        return Path.Combine(appData, "TokenTalk");
    }

    public static string GetConfigPath()
    {
        return Path.Combine(GetConfigDirectory(), "appsettings.json");
    }
}
