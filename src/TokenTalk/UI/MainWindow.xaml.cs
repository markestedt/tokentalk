using System.ComponentModel;
using System.Windows;
using TokenTalk.UI.Pages;
using TokenTalk.UI.ViewModels;
using WpfButton = System.Windows.Controls.Button;

namespace TokenTalk.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _mainVm;
    private readonly HomePage _homePage;
    private readonly HistoryPage _historyPage;
    private readonly DictionaryPage _dictionaryPage;
    private readonly SettingsPage _settingsPage;
    private readonly StatisticsPage _statisticsPage;

    private WpfButton? _activeNavButton;

    // Kept alive so Windows can reference the HICON handles for the taskbar
    private System.Drawing.Icon? _taskbarIcon;
    private System.Drawing.Icon? _titleBarIcon;

    public MainWindow(MainViewModel mainVm)
    {
        InitializeComponent();
        _mainVm = mainVm;
        DataContext = mainVm;

        // Force-set window class icon via P/Invoke to work around Windows 11
        // taskbar bug (dotnet/wpf#11308) where the icon shows as default when
        // there is startup delay before window creation.
        SourceInitialized += (_, _) =>
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("TokenTalk.Tray.tokentalk.ico");
                if (stream == null) return;

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

                _taskbarIcon = new System.Drawing.Icon(stream, 32, 32);
                Platform.NativeMethods.SetClassLongPtr(hwnd, Platform.NativeMethods.GCLP_HICON, _taskbarIcon.Handle);

                stream.Position = 0;
                _titleBarIcon = new System.Drawing.Icon(stream, 16, 16);
                Platform.NativeMethods.SetClassLongPtr(hwnd, Platform.NativeMethods.GCLP_HICONSM, _titleBarIcon.Handle);
            }
            catch { }
        };

        _homePage = new HomePage(mainVm.HomeVm);
        _historyPage = new HistoryPage(mainVm.HistoryVm);
        _dictionaryPage = new DictionaryPage(mainVm.DictionaryVm);
        _settingsPage = new SettingsPage(mainVm.SettingsVm);
        _statisticsPage = new StatisticsPage(mainVm.StatisticsVm);

        _contentHost.Content = _homePage;
        _activeNavButton = _btnHome;

        Loaded += async (_, _) => await mainVm.HomeVm.LoadAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void BtnHome_Click(object sender, RoutedEventArgs e)
        => SetActivePage(_btnHome, _homePage, AppPage.Home);

    private void BtnHistory_Click(object sender, RoutedEventArgs e)
    {
        SetActivePage(_btnHistory, _historyPage, AppPage.History);
        _ = _mainVm.HistoryVm.LoadAsync();
    }

    private void BtnDictionary_Click(object sender, RoutedEventArgs e)
        => SetActivePage(_btnDictionary, _dictionaryPage, AppPage.Dictionary);

    private void BtnStatistics_Click(object sender, RoutedEventArgs e)
    {
        SetActivePage(_btnStatistics, _statisticsPage, AppPage.Statistics);
        _ = _mainVm.StatisticsVm.LoadAsync();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
        => SetActivePage(_btnSettings, _settingsPage, AppPage.Settings);

    private void SetActivePage(WpfButton navBtn, object page, AppPage appPage)
    {
        if (_activeNavButton != null)
            _activeNavButton.Style = (Style)FindResource("NavButtonStyle");

        navBtn.Style = (Style)FindResource("NavButtonActiveStyle");
        _activeNavButton = navBtn;

        _contentHost.Content = page;
        _mainVm.CurrentPage = appPage;
    }
}
