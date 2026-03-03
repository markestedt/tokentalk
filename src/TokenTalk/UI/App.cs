namespace TokenTalk.UI;

public class App : System.Windows.Application
{
    private CancellationTokenSource? _cts;

    public App()
    {
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/UI/Theme/TokenTalkTheme.xaml")
        });
    }

    public void SetCancellationSource(CancellationTokenSource cts) => _cts = cts;

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _cts?.Cancel();
        base.OnExit(e);
    }
}
