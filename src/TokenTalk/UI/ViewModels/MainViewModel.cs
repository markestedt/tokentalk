using TokenTalk.Configuration;
using TokenTalk.Transcription;
using WpfApplication = System.Windows.Application;
using TokenTalk.PostProcessing;
using TokenTalk.Storage;

namespace TokenTalk.UI.ViewModels;

public enum AppPage { Home, History, Dictionary, Settings, Statistics }

public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly Agent _agent;

    private string _statusText = "Idle";
    private string _statusColor = "#8E8E93";
    private AppPage _currentPage = AppPage.Home;

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string StatusColor { get => _statusColor; private set => SetProperty(ref _statusColor, value); }
    public AppPage CurrentPage { get => _currentPage; set => SetProperty(ref _currentPage, value); }

    public HomeViewModel HomeVm { get; }
    public HistoryViewModel HistoryVm { get; }
    public DictionaryViewModel DictionaryVm { get; }
    public SettingsViewModel SettingsVm { get; }
    public StatisticsViewModel StatisticsVm { get; }

    public MainViewModel(
        Agent agent,
        DictationRepository repository,
        ConfigManager configManager,
        DictionaryService dictionaryService,
        CustomDictionary dictionary,
        ModelManager modelManager)
    {
        _agent = agent;
        HomeVm = new HomeViewModel(repository);
        HistoryVm = new HistoryViewModel(repository);
        DictionaryVm = new DictionaryViewModel(dictionaryService, dictionary);
        SettingsVm = new SettingsViewModel(configManager, modelManager);
        StatisticsVm = new StatisticsViewModel(repository);

        _agent.StatusChanged += OnStatusChanged;
        _agent.DictationCompleted += OnDictationCompleted;
    }

    private void OnStatusChanged(object? sender, string status)
    {
        WpfApplication.Current?.Dispatcher.Invoke(() =>
        {
            StatusText = status switch
            {
                "recording" => "Recording",
                "processing" => "Processing",
                _ => "Idle",
            };
            StatusColor = status switch
            {
                "recording" => "#FF3B30",
                "processing" => "#FF9500",
                _ => "#8E8E93",
            };
        });
    }

    private void OnDictationCompleted(object? sender, DictationCompletedEventArgs e)
    {
        WpfApplication.Current?.Dispatcher.Invoke(() =>
        {
            HomeVm.OnNewDictation(e.Dictation);
        });
    }

    public void Dispose()
    {
        _agent.StatusChanged -= OnStatusChanged;
        _agent.DictationCompleted -= OnDictationCompleted;
    }
}
