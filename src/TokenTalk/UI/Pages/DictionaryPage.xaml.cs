using System.Windows;
using System.Windows.Input;
using TokenTalk.UI.ViewModels;

namespace TokenTalk.UI.Pages;

public partial class DictionaryPage : System.Windows.Controls.UserControl
{
    private readonly DictionaryViewModel _vm;

    public DictionaryPage(DictionaryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
        => _vm.AddEntry();

    private void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _vm.AddEntry();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is DictionaryEntryViewModel entry)
            _vm.DeleteEntry(entry);
    }
}
