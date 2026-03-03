using System.Windows;
using TokenTalk.UI.ViewModels;

namespace TokenTalk.UI.Pages;

public partial class HomePage : System.Windows.Controls.UserControl
{
    private readonly HomeViewModel _vm;

    public HomePage(HomeViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

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
