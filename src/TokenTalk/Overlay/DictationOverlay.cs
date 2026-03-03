using System.Windows.Forms;

namespace TokenTalk.Overlay;

public sealed class DictationOverlay : IDisposable
{
    private DictationOverlayForm? _form;

    /// <summary>Must be called on the STA thread (inside TrayIconManager.Run).</summary>
    public void Initialize()
    {
        _form = new DictationOverlayForm();
    }

    public void StartRecording()
    {
        var form = _form;
        if (form?.IsHandleCreated == true)
            form.BeginInvoke(form.ShowRecording);
    }

    public void StopRecording()
    {
        var form = _form;
        if (form?.IsHandleCreated == true)
            form.BeginInvoke(form.HideRecording);
    }

    public void StartProcessing()
    {
        var form = _form;
        if (form?.IsHandleCreated == true)
            form.BeginInvoke(form.ShowProcessing);
    }

    public void StopProcessing()
    {
        var form = _form;
        if (form?.IsHandleCreated == true)
            form.BeginInvoke(form.HideRecording);
    }

    /// <summary>
    /// Called from the NAudio thread. Writes directly to a volatile field —
    /// no BeginInvoke needed; the UI paint timer reads it at ~30fps.
    /// </summary>
    public void PushAmplitude(float amp)
    {
        _form?.PushAmplitude(amp);
    }

    public void Dispose()
    {
        var form = _form;
        _form = null;
        if (form != null && form.IsHandleCreated)
        {
            form.BeginInvoke(() =>
            {
                form.Close();
                form.Dispose();
            });
        }
        else
        {
            form?.Dispose();
        }
    }
}
