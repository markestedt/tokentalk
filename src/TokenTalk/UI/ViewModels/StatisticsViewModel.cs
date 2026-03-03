using System.Collections.ObjectModel;
using TokenTalk.Storage;

namespace TokenTalk.UI.ViewModels;

public enum StatsTimeRange { Week, Month, Year, AllTime }

public class WordCloudItem
{
    public string Word { get; init; } = "";
    public double FontSize { get; init; }
    public string Color { get; init; } = "#5E5CE6";
}

public class StatisticsViewModel : ViewModelBase
{
    private static readonly string[] Palette =
    [
        "#5E5CE6", "#FF9500", "#34C759", "#FF3B30", "#007AFF", "#AF52DE"
    ];

    private readonly DictationRepository _repository;
    private StatsTimeRange _selectedRange = StatsTimeRange.Week;
    private bool _isLoading;
    private bool _isEmpty;

    public StatsTimeRange SelectedRange
    {
        get => _selectedRange;
        set
        {
            if (SetProperty(ref _selectedRange, value))
            {
                OnPropertyChanged(nameof(IsWeekActive));
                OnPropertyChanged(nameof(IsMonthActive));
                OnPropertyChanged(nameof(IsYearActive));
                OnPropertyChanged(nameof(IsAllTimeActive));
            }
        }
    }

    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool IsEmpty { get => _isEmpty; private set => SetProperty(ref _isEmpty, value); }

    public bool IsWeekActive => SelectedRange == StatsTimeRange.Week;
    public bool IsMonthActive => SelectedRange == StatsTimeRange.Month;
    public bool IsYearActive => SelectedRange == StatsTimeRange.Year;
    public bool IsAllTimeActive => SelectedRange == StatsTimeRange.AllTime;

    public ObservableCollection<WordCloudItem> Words { get; } = [];

    public StatisticsViewModel(DictationRepository repository)
    {
        _repository = repository;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        Words.Clear();
        try
        {
            int? days = SelectedRange switch
            {
                StatsTimeRange.Week => 7,
                StatsTimeRange.Month => 30,
                StatsTimeRange.Year => 365,
                _ => null,
            };

            var entries = await _repository.GetWordFrequenciesAsync(days);

            if (entries.Count == 0)
            {
                IsEmpty = true;
                return;
            }

            IsEmpty = false;
            double maxCount = entries[0].Count;
            double minCount = entries[^1].Count;
            const double minSize = 14;
            const double maxSize = 40;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                double t = maxCount == minCount
                    ? 1.0
                    : (entry.Count - minCount) / (maxCount - minCount);
                double fontSize = minSize + t * (maxSize - minSize);

                Words.Add(new WordCloudItem
                {
                    Word = entry.Word,
                    FontSize = Math.Round(fontSize, 1),
                    Color = Palette[i % Palette.Length],
                });
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
