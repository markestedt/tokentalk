using System.Windows;
using TokenTalk.UI.ViewModels;

namespace TokenTalk.UI.Pages;

public partial class HistoryPage : System.Windows.Controls.UserControl
{
    private readonly HistoryViewModel _vm;

    public HistoryPage(HistoryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await _vm.LoadAsync();

    private async void Prev_Click(object sender, RoutedEventArgs e)
        => await _vm.PrevPageAsync();

    private async void Next_Click(object sender, RoutedEventArgs e)
        => await _vm.NextPageAsync();

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        var text = btn.Tag as string;
        if (string.IsNullOrEmpty(text)) return;
        System.Windows.Clipboard.SetText(text);
        var original = btn.Content;
        btn.Content = "✓";
        await Task.Delay(1500);
        btn.Content = original;
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not long id) return;
        await _vm.DeleteAsync(id);
    }
}
