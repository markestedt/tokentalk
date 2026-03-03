using System.Collections.ObjectModel;
using TokenTalk.PostProcessing;

namespace TokenTalk.UI.ViewModels;

public class DictionaryEntryViewModel : ViewModelBase
{
    private readonly DictionaryEntry _entry;
    private readonly DictionaryViewModel _parent;

    public string Original => _entry.Original;
    public string Replacement => _entry.Replacement;
    public bool IsMapping => _entry.IsMapping;

    public DictionaryEntryViewModel(DictionaryEntry entry, DictionaryViewModel parent)
    {
        _entry = entry;
        _parent = parent;
    }

    public void Delete() => _parent.DeleteEntry(this);

    public DictionaryEntry Entry => _entry;
}

public class DictionaryViewModel : ViewModelBase
{
    private readonly DictionaryService _service;
    private readonly CustomDictionary _dictionary;
    private string _newOriginal = "";
    private string _newReplacement = "";

    public string NewOriginal { get => _newOriginal; set => SetProperty(ref _newOriginal, value); }
    public string NewReplacement { get => _newReplacement; set => SetProperty(ref _newReplacement, value); }

    public ObservableCollection<DictionaryEntryViewModel> Entries { get; } = [];

    public DictionaryViewModel(DictionaryService service, CustomDictionary dictionary)
    {
        _service = service;
        _dictionary = dictionary;

        foreach (var entry in dictionary.Entries)
            Entries.Add(new DictionaryEntryViewModel(entry, this));
    }

    public void AddEntry()
    {
        var original = NewOriginal.Trim();
        var replacement = NewReplacement.Trim();

        // If only the left box is filled, treat it as a simple term
        if (string.IsNullOrWhiteSpace(replacement))
        {
            if (string.IsNullOrWhiteSpace(original)) return;
            replacement = original;
            original = "";
        }

        var entry = new DictionaryEntry
        {
            Original = original,
            Replacement = replacement,
            IsMapping = !string.IsNullOrWhiteSpace(original)
        };

        _dictionary.Entries.Add(entry);
        Entries.Add(new DictionaryEntryViewModel(entry, this));
        _service.Save("", _dictionary);

        NewOriginal = "";
        NewReplacement = "";
    }

    public void DeleteEntry(DictionaryEntryViewModel vm)
    {
        _dictionary.Entries.Remove(vm.Entry);
        Entries.Remove(vm);
        _service.Save("", _dictionary);
    }
}
