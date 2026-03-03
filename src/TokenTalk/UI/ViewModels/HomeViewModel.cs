using System.Collections.ObjectModel;
using TokenTalk.Storage;

namespace TokenTalk.UI.ViewModels;

public class DictationRowViewModel
{
    public long Id { get; init; }
    public string TimeDisplay { get; init; } = "";
    public string Text { get; init; } = "";
    public bool Success { get; init; }
}

public class DictationGroupViewModel : ViewModelBase
{
    public string Label { get; init; } = "";
    public ObservableCollection<DictationRowViewModel> Items { get; } = [];
}

public class HomeViewModel : ViewModelBase
{
    private readonly DictationRepository _repository;

    private string _weeksStreakDisplay = "—";
    private string _totalWordsDisplay = "—";
    private string _avgWpmDisplay = "—";
    private int _totalWordsRaw;

    public string WeeksStreakDisplay { get => _weeksStreakDisplay; private set => SetProperty(ref _weeksStreakDisplay, value); }
    public string TotalWordsDisplay { get => _totalWordsDisplay; private set => SetProperty(ref _totalWordsDisplay, value); }
    public string AvgWpmDisplay { get => _avgWpmDisplay; private set => SetProperty(ref _avgWpmDisplay, value); }

    public ObservableCollection<DictationGroupViewModel> Groups { get; } = [];

    public HomeViewModel(DictationRepository repository)
    {
        _repository = repository;
    }

    public async Task LoadAsync()
    {
        var (items, _) = await _repository.GetHistoryAsync(50, 0);
        var stats = await _repository.GetOverallStatsAsync(365);
        var heatmap = await _repository.GetHeatmapStatsAsync();

        _totalWordsRaw = stats.TotalWords;
        TotalWordsDisplay = FormatWordCount(stats.TotalWords);

        int avgWpm = stats.TotalRecordingTimeMs > 0
            ? (int)(stats.TotalWords / (stats.TotalRecordingTimeMs / 60000.0))
            : 0;
        AvgWpmDisplay = avgWpm > 0 ? $"{avgWpm} WPM" : "— WPM";

        int streak = ComputeWeeksStreak(heatmap);
        WeeksStreakDisplay = streak == 1 ? "1 week" : $"{streak} weeks";

        Groups.Clear();

        var today = DateTime.UtcNow.Date;
        var yesterday = today.AddDays(-1);

        var grouped = items
            .GroupBy(d => d.Timestamp.ToLocalTime().Date)
            .OrderByDescending(g => g.Key);

        foreach (var g in grouped)
        {
            string label = g.Key == today ? "TODAY"
                : g.Key == yesterday ? "YESTERDAY"
                : g.Key.ToString("MMM d");

            var group = new DictationGroupViewModel { Label = label };
            foreach (var d in g)
            {
                group.Items.Add(new DictationRowViewModel
                {
                    Id = d.Id,
                    TimeDisplay = d.Timestamp.ToLocalTime().ToString("HH:mm"),
                    Text = d.TranscribedText ?? d.ErrorMessage ?? "(empty)",
                    Success = d.Success,
                });
            }
            Groups.Add(group);
        }
    }

    public async Task DeleteAsync(long id)
    {
        await _repository.DeleteAsync(id);
        foreach (var group in Groups)
        {
            var row = group.Items.FirstOrDefault(r => r.Id == id);
            if (row != null)
            {
                group.Items.Remove(row);
                if (group.Items.Count == 0)
                    Groups.Remove(group);
                return;
            }
        }
    }

    public void OnNewDictation(Dictation d)
    {
        _totalWordsRaw += d.WordCount;
        TotalWordsDisplay = FormatWordCount(_totalWordsRaw);

        var today = DateTime.UtcNow.Date;
        var yesterday = today.AddDays(-1);

        var todayGroup = Groups.FirstOrDefault(g => g.Label == "TODAY");
        if (todayGroup == null)
        {
            todayGroup = new DictationGroupViewModel { Label = "TODAY" };
            Groups.Insert(0, todayGroup);
        }

        todayGroup.Items.Insert(0, new DictationRowViewModel
        {
            Id = d.Id,
            TimeDisplay = d.Timestamp.ToLocalTime().ToString("HH:mm"),
            Text = d.TranscribedText ?? d.ErrorMessage ?? "(empty)",
            Success = d.Success,
        });
    }

    private static string FormatWordCount(int count) =>
        count >= 10_000 ? $"{count / 1000.0:0.#}K words"
        : count >= 1_000 ? $"{count / 1000.0:0.0}K words"
        : $"{count} words";

    private static int ComputeWeeksStreak(List<HeatmapStats> heatmap)
    {
        var dates = heatmap
            .Select(h => DateTime.Parse(h.Date).Date)
            .ToHashSet();

        var today = DateTime.UtcNow.Date;
        int daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
        var currentWeekStart = today.AddDays(-daysFromMonday);

        int streak = 0;
        for (int i = 0; i < 52; i++)
        {
            var weekStart = currentWeekStart.AddDays(-7 * i);
            bool hasAny = Enumerable.Range(0, 7).Any(offset => dates.Contains(weekStart.AddDays(offset)));
            if (hasAny)
                streak++;
            else
                break;
        }
        return streak;
    }
}
