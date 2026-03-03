using System.Reflection;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using TokenTalk.Overlay;

namespace TokenTalk.Tray;

public class TrayIconManager : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly CancellationTokenSource _cts;
    private readonly Action _openWindowCallback;
    private readonly ILogger<TrayIconManager> _logger;

    public TrayIconManager(CancellationTokenSource cts, Action openWindowCallback, ILogger<TrayIconManager> logger)
    {
        _cts = cts;
        _openWindowCallback = openWindowCallback;
        _logger = logger;
    }

    public void Run(DictationOverlay? overlay = null)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        overlay?.Initialize();

        _notifyIcon = new NotifyIcon
        {
            Text = "TokenTalk - Voice Dictation",
            Visible = true,
            Icon = LoadIcon(),
        };

        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open TokenTalk");
        openItem.Click += (_, _) =>
        {
            try { _openWindowCallback(); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to open window"); }
        };

        var separator = new ToolStripSeparator();

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) =>
        {
            _cts.Cancel();
            Application.ExitThread();
        };

        menu.Items.Add(openItem);
        menu.Items.Add(separator);
        menu.Items.Add(quitItem);
        _notifyIcon.ContextMenuStrip = menu;

        // Run the Windows Forms message loop on this STA thread
        Application.Run();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("TokenTalk.Tray.tokentalk.ico");
            if (stream != null)
                return new System.Drawing.Icon(stream);
        }
        catch { }

        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
