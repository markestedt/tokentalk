using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TokenTalk.Overlay;

internal sealed class DictationOverlayForm : Form
{
    private const int RingSize = 24;
    private const int OverlayWidth = 160;
    private const int ProcessingWidth = 70;
    private const int OverlayHeight = 34;
    private const int BottomMargin = 20;
    private const int CornerRadius = 10;

    private readonly float[] _amplitudeRing = new float[RingSize];
    private int _ringHead;
    private volatile float _latestAmplitude;
    private bool _isProcessing;

    private readonly System.Windows.Forms.Timer _paintTimer;
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private bool _fadingIn;

    private const int WS_EX_NOACTIVATE  = 0x08000000;
    private const int WS_EX_TOOLWINDOW  = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;  // pass mouse events through transparent pixels
    private const int WM_MOUSEACTIVATE  = 0x0021;
    private const int MA_NOACTIVATE     = 3;

    public DictationOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        // Black is the transparency key — the pill background is drawn explicitly in OnPaint
        // so GDI+ anti-aliasing produces smooth edges rather than the aliased Region clipping.
        BackColor = Color.Black;
        TransparencyKey = Color.Black;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        Size = new Size(OverlayWidth, OverlayHeight);
        PositionAtBottom();

        Opacity = 0;

        _paintTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _paintTimer.Tick += (_, _) => Invalidate();

        _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _fadeTimer.Tick += OnFadeTick;

        // Force HWND creation before Application.Run() so BeginInvoke works.
        // CreateControl() is a no-op when Visible=false; accessing Handle always forces it.
        _ = Handle;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }

    public void ShowRecording()
    {
        _isProcessing = false;
        Array.Clear(_amplitudeRing, 0, RingSize);
        _ringHead = 0;
        _latestAmplitude = 0f;
        _fadingIn = true;
        Size = new Size(OverlayWidth, OverlayHeight);
        PositionAtBottom();
        Show();
        _paintTimer.Start();
        _fadeTimer.Start();
    }

    public void ShowProcessing()
    {
        _isProcessing = true;
        _latestAmplitude = 0f;
        Size = new Size(ProcessingWidth, OverlayHeight);
        PositionAtBottom();
        // Overlay is typically already visible from recording; just switch mode.
        // Handle edge case where it may have already faded out or was never shown.
        if (!Visible)
        {
            Show();
            _paintTimer.Start();
        }
        _fadingIn = true;
        if (!_fadeTimer.Enabled)
            _fadeTimer.Start();
    }

    public void HideRecording()
    {
        _fadingIn = false;
        if (!_fadeTimer.Enabled)
            _fadeTimer.Start();
    }

    public void PushAmplitude(float amp)
    {
        _latestAmplitude = amp;
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        if (_fadingIn)
        {
            Opacity = Math.Min(1.0, Opacity + 0.08);
            if (Opacity >= 1.0)
                _fadeTimer.Stop();
        }
        else
        {
            Opacity = Math.Max(0.0, Opacity - 0.08);
            if (Opacity <= 0.0)
            {
                _fadeTimer.Stop();
                _paintTimer.Stop();
                Hide();
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Push latest amplitude into ring
        float amp = _latestAmplitude;
        _amplitudeRing[_ringHead] = amp;
        _ringHead = (_ringHead + 1) % RingSize;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        int padding = 6;

        // Clear to the transparency key so the window background is fully see-through.
        e.Graphics.Clear(Color.Black);

        // Draw the pill with GDI+ anti-aliasing — smooth edges, no aliased Region clipping.
        using var pillPath = RoundedRect(new Rectangle(0, 0, w, h), CornerRadius);
        using var pillBrush = new SolidBrush(Color.FromArgb(32, 32, 32));
        g.FillPath(pillBrush, pillPath);

        double time = Environment.TickCount64 / 1000.0;
        float centerY = h / 2f;

        if (_isProcessing)
        {
            // 3 bouncing dots — universally understood "working" indicator
            const int dotCount = 3;
            const float dotRadius = 4f;
            const float dotGap = 10f;
            const float bounceAmp = 4f;
            const double period = 0.5;

            float totalWidth = dotCount * dotRadius * 2 + (dotCount - 1) * dotGap;
            float startX = (w - totalWidth) / 2f + dotRadius;

            using var dotBrush = new SolidBrush(Color.FromArgb(200, 150, 150, 150));
            for (int i = 0; i < dotCount; i++)
            {
                double phase = i * (Math.PI * 2.0 / dotCount);
                float offsetY = -(float)(bounceAmp * Math.Sin(time * (Math.PI * 2.0 / period) - phase));
                float cx = startX + i * (dotRadius * 2 + dotGap);
                g.FillEllipse(dotBrush, cx - dotRadius, centerY - dotRadius + offsetY, dotRadius * 2, dotRadius * 2);
            }
        }
        else
        {
            // Draw waveform bars
            int barCount = RingSize;
            float barAreaWidth = w - padding * 2;
            float barWidth = barAreaWidth / barCount;
            float maxBarHalf = centerY - 4;

            using var barBrush = new SolidBrush(Color.FromArgb(180, 140, 140, 140));

            for (int i = 0; i < barCount; i++)
            {
                int idx = (_ringHead + i) % RingSize;
                float barAmp = _amplitudeRing[idx];

                // Idle breathing sine pulse when amplitude is near zero
                if (barAmp < 0.02f)
                {
                    double phase = (i / (double)barCount) * Math.PI * 2;
                    barAmp = (float)(0.04 + 0.03 * Math.Sin(time * 2.5 + phase));
                }

                // Scale up raw RMS (typically 0.01–0.10 for speech) so bars visibly react
                float scaledAmp = Math.Min(1f, barAmp * 6f);
                float halfHeight = Math.Max(2f, scaledAmp * maxBarHalf);
                float x = padding + i * barWidth + barWidth * 0.1f;
                float bw = barWidth * 0.8f;

                // Mirrored top/bottom from center
                var barRect = new RectangleF(x, centerY - halfHeight, bw, halfHeight * 2);
                using var barPath = RoundedRectF(barRect, Math.Min(bw / 2f, halfHeight));
                g.FillPath(barBrush, barPath);
            }
        }
    }

    private void PositionAtBottom()
    {
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(
            workArea.Left + (workArea.Width - Width) / 2,
            workArea.Bottom - OverlayHeight - BottomMargin);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath RoundedRectF(RectangleF bounds, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _paintTimer.Dispose();
            _fadeTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
