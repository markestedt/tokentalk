using System.Collections.ObjectModel;
using TokenTalk.Storage;

namespace TokenTalk.UI.ViewModels;

public class HistoryRowViewModel
{
    public long Id { get; init; }
    public string TimeDisplay { get; init; } = "";
    public string Text { get; init; } = "";
    public bool Success { get; init; }
    public string WordCount { get; init; } = "";
}

public class HistoryViewModel : ViewModelBase
{
    private const int PageSize = 25;

    private readonly DictationRepository _repository;
    private int _currentPage;
    private int _totalPages;
    private bool _canGoPrev;
    private bool _canGoNext;
    private bool _isLoading;

    public int CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }
    public int TotalPages { get => _totalPages; private set => SetProperty(ref _totalPages, value); }
    public bool CanGoPrev { get => _canGoPrev; private set => SetProperty(ref _canGoPrev, value); }
    public bool CanGoNext { get => _canGoNext; private set => SetProperty(ref _canGoNext, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    public ObservableCollection<HistoryRowViewModel> Items { get; } = [];

    public HistoryViewModel(DictationRepository repository)
    {
        _repository = repository;
    }

    public async Task LoadAsync() => await LoadPageAsync(0);

    public async Task LoadPageAsync(int page)
    {
        IsLoading = true;
        try
        {
            var (items, total) = await _repository.GetHistoryAsync(PageSize, page * PageSize);
            CurrentPage = page;
            TotalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)PageSize);
            CanGoPrev = page > 0;
            CanGoNext = page < TotalPages - 1;

            Items.Clear();
            foreach (var d in items)
            {
                Items.Add(new HistoryRowViewModel
                {
                    Id = d.Id,
                    TimeDisplay = d.Timestamp.ToLocalTime().ToString("MMM d, HH:mm"),
                    Text = d.TranscribedText ?? d.ErrorMessage ?? "(empty)",
                    Success = d.Success,
                    WordCount = d.WordCount > 0 ? $"{d.WordCount}w" : "",
                });
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task DeleteAsync(long id)
    {
        await _repository.DeleteAsync(id);
        var row = Items.FirstOrDefault(r => r.Id == id);
        if (row != null) Items.Remove(row);
    }

    public async Task NextPageAsync()
    {
        if (CanGoNext) await LoadPageAsync(CurrentPage + 1);
    }

    public async Task PrevPageAsync()
    {
        if (CanGoPrev) await LoadPageAsync(CurrentPage - 1);
    }
}
