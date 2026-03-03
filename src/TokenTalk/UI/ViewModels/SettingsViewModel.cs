using System.Collections.ObjectModel;
using NAudio.Wave;
using TokenTalk.Configuration;
using TokenTalk.Transcription;

namespace TokenTalk.UI.ViewModels;

public class AudioDeviceItem
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public override string ToString() => Name;
}

public class ModelCatalogItem : ViewModelBase
{
    private bool _isDownloaded;
    private bool _isSelected;
    private bool _isDownloading;
    private double _downloadProgress;
    private string _downloadError = "";

    public ModelInfo Info { get; }
    public CancellationTokenSource? DownloadCts { get; set; }

    public bool IsDownloaded
    {
        get => _isDownloaded;
        set { if (SetProperty(ref _isDownloaded, value)) NotifyVisibility(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) NotifyVisibility(); }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set { if (SetProperty(ref _isDownloading, value)) NotifyVisibility(); }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set
        {
            if (SetProperty(ref _downloadProgress, value))
                OnPropertyChanged(nameof(DownloadProgressText));
        }
    }

    public string DownloadError
    {
        get => _downloadError;
        set { if (SetProperty(ref _downloadError, value)) OnPropertyChanged(nameof(HasDownloadError)); }
    }

    public string DownloadProgressText => $"{DownloadProgress:P0}";
    public bool HasDownloadError => !string.IsNullOrEmpty(_downloadError);

    // Visibility helpers for XAML bindings
    public bool ShowDownloadButton => !IsDownloaded && !IsDownloading;
    public bool ShowProgress => IsDownloading;
    public bool ShowUseButton => IsDownloaded && !IsSelected && !IsDownloading;
    public bool ShowActiveLabel => IsSelected;
    public bool ShowDeleteButton => IsDownloaded && !IsDownloading;

    private void NotifyVisibility()
    {
        OnPropertyChanged(nameof(ShowDownloadButton));
        OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(ShowUseButton));
        OnPropertyChanged(nameof(ShowActiveLabel));
        OnPropertyChanged(nameof(ShowDeleteButton));
    }

    public ModelCatalogItem(ModelInfo info) => Info = info;
}

public class SettingsViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly ModelManager _modelManager;

    // Hotkey
    private string _hotkey = "";
    public string Hotkey { get => _hotkey; set => SetProperty(ref _hotkey, value); }

    // Transcription — provider
    private string _provider = "openai";
    public string Provider
    {
        get => _provider;
        set
        {
            if (SetProperty(ref _provider, value))
            {
                OnPropertyChanged(nameof(IsOpenAiProvider));
                OnPropertyChanged(nameof(IsLocalProvider));
            }
        }
    }

    public bool IsOpenAiProvider => Provider == "openai";
    public bool IsLocalProvider => Provider == "whisper.cpp";

    // Transcription — OpenAI
    private string _apiKey = "";
    private string _model = "";
    private string _prompt = "";
    public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }
    public string Model { get => _model; set => SetProperty(ref _model, value); }
    public string Prompt { get => _prompt; set => SetProperty(ref _prompt, value); }

    // Transcription — shared language
    private string _language = "";
    public string Language { get => _language; set => SetProperty(ref _language, value); }

    // Audio
    private int _deviceIndex;
    private int _maxSeconds;
    private double _silenceThreshold;
    public int DeviceIndex { get => _deviceIndex; set => SetProperty(ref _deviceIndex, value); }
    public int MaxSeconds { get => _maxSeconds; set => SetProperty(ref _maxSeconds, value); }
    public double SilenceThreshold { get => _silenceThreshold; set => SetProperty(ref _silenceThreshold, value); }

    // Post-processing
    private bool _ppCommands;
    public bool Commands { get => _ppCommands; set => SetProperty(ref _ppCommands, value); }

    // UI state
    private bool _saveSuccess;
    public bool SaveSuccess { get => _saveSuccess; set => SetProperty(ref _saveSuccess, value); }

    public List<AudioDeviceItem> AudioDevices { get; } = [];
    public ObservableCollection<ModelCatalogItem> ModelCatalog { get; } = [];

    public static readonly List<string> ProviderOptions = ["openai", "whisper.cpp"];

    public static readonly List<string> LanguageOptions =
    [
        "auto", "en", "sv", "de", "fr", "es", "it", "pt", "nl", "pl",
        "da", "nb", "fi", "zh", "ja", "ko", "ar", "ru",
    ];

    public SettingsViewModel(ConfigManager configManager, ModelManager modelManager)
    {
        _configManager = configManager;
        _modelManager = modelManager;

        foreach (var info in ModelManager.Catalog)
            ModelCatalog.Add(new ModelCatalogItem(info));

        LoadAudioDevices();
        Load();
    }

    private void LoadAudioDevices()
    {
        AudioDevices.Add(new AudioDeviceItem { Index = -1, Name = "Default" });
        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            AudioDevices.Add(new AudioDeviceItem { Index = i, Name = caps.ProductName });
        }
    }

    public void Load()
    {
        var cfg = _configManager.Current;
        Hotkey = cfg.Hotkey;
        Provider = cfg.Transcription.Provider;
        ApiKey = cfg.Transcription.ApiKey;
        Model = cfg.Transcription.Model;
        Language = cfg.Transcription.Language;
        Prompt = cfg.Transcription.Prompt;
        DeviceIndex = cfg.Audio.DeviceIndex;
        MaxSeconds = cfg.Audio.MaxSeconds;
        SilenceThreshold = cfg.Audio.SilenceThreshold;
        Commands = cfg.PostProcessing.Commands;
        RefreshModelStates(cfg.Transcription.ModelPath);
    }

    private void RefreshModelStates(string? currentModelPath)
    {
        foreach (var item in ModelCatalog)
        {
            item.IsDownloaded = _modelManager.IsDownloaded(item.Info);
            item.IsSelected = string.Equals(
                _modelManager.GetModelPath(item.Info),
                currentModelPath,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Save()
    {
        var cfg = _configManager.Current;
        cfg.Hotkey = Hotkey;
        cfg.Transcription.Provider = Provider;
        cfg.Transcription.ApiKey = ApiKey;
        cfg.Transcription.Model = Model;
        cfg.Transcription.Language = Language;
        cfg.Transcription.Prompt = Prompt;
        cfg.Audio.DeviceIndex = DeviceIndex;
        cfg.Audio.MaxSeconds = MaxSeconds;
        cfg.Audio.SilenceThreshold = SilenceThreshold;
        cfg.PostProcessing.Commands = Commands;
        _configManager.Save(cfg);

        SaveSuccess = true;
        Task.Delay(2000).ContinueWith(_ =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => SaveSuccess = false);
        });
    }

    public async Task DownloadModelAsync(ModelCatalogItem item)
    {
        if (item.IsDownloading) return;

        item.DownloadError = "";
        item.IsDownloading = true;
        item.DownloadProgress = 0;
        item.DownloadCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(p =>
                System.Windows.Application.Current?.Dispatcher.Invoke(
                    () => item.DownloadProgress = p));

            await _modelManager.DownloadAsync(item.Info, progress, item.DownloadCts.Token);
            item.IsDownloaded = true;
        }
        catch (OperationCanceledException)
        {
            // Download cancelled — no error message needed
        }
        catch (Exception ex)
        {
            item.DownloadError = $"Download failed: {ex.Message}";
        }
        finally
        {
            item.IsDownloading = false;
            item.DownloadCts?.Dispose();
            item.DownloadCts = null;
        }
    }

    public void CancelDownload(ModelCatalogItem item)
    {
        item.DownloadCts?.Cancel();
    }

    public void SelectModel(ModelCatalogItem item)
    {
        var cfg = _configManager.Current;
        cfg.Transcription.ModelPath = _modelManager.GetModelPath(item.Info);
        _configManager.Save(cfg);
        RefreshModelStates(cfg.Transcription.ModelPath);
    }

    public void DeleteModel(ModelCatalogItem item)
    {
        // Deselect if this was the active model
        var cfg = _configManager.Current;
        var modelPath = _modelManager.GetModelPath(item.Info);
        if (string.Equals(cfg.Transcription.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
        {
            cfg.Transcription.ModelPath = "";
            _configManager.Save(cfg);
        }

        _modelManager.DeleteModel(item.Info);
        item.IsDownloaded = false;
        item.IsSelected = false;
    }
}
