using System.Windows;
using TokenTalk.UI.ViewModels;

namespace TokenTalk.UI.Pages;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private readonly SettingsViewModel _vm;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // Pre-populate PasswordBox (can't data-bind PasswordBox.Password for security)
        _apiKeyBox.Password = vm.ApiKey;
    }

    private void ApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.ApiKey = _apiKeyBox.Password;

    private void Save_Click(object sender, RoutedEventArgs e)
        => _vm.Save();

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ModelCatalogItem item)
            _ = _vm.DownloadModelAsync(item);
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ModelCatalogItem item)
            _vm.CancelDownload(item);
    }

    private void SelectModel_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ModelCatalogItem item)
            _vm.SelectModel(item);
    }

    private void DeleteModel_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ModelCatalogItem item)
            _vm.DeleteModel(item);
    }
}
