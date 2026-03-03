namespace TokenTalk.Platform;

public class PasteService
{
    private readonly ClipboardService _clipboard;

    public PasteService(ClipboardService clipboard)
    {
        _clipboard = clipboard;
    }

    public async Task PasteTextAsync(string text, CancellationToken ct = default)
    {
        // Save current clipboard
        string original = string.Empty;
        try { original = _clipboard.GetText(); }
        catch { /* ignore */ }

        // Set clipboard to new text
        _clipboard.SetText(text);

        // Wait for clipboard to be ready
        await Task.Delay(50, ct);

        // Simulate Ctrl+V
        SendCtrlV();

        // Wait for target app to process paste
        await Task.Delay(100, ct);

        // Restore clipboard
        if (!string.IsNullOrEmpty(original))
        {
            try { _clipboard.SetText(original); }
            catch { /* ignore */ }
        }
    }

    private static void SendCtrlV()
    {
        var inputs = new NativeMethods.INPUT[]
        {
            // Ctrl down
            new() {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion {
                    ki = new NativeMethods.KEYBDINPUT {
                        wVk = (ushort)NativeMethods.VK_CONTROL,
                        dwExtraInfo = NativeMethods.GetMessageExtraInfo()
                    }
                }
            },
            // V down
            new() {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion {
                    ki = new NativeMethods.KEYBDINPUT {
                        wVk = (ushort)NativeMethods.VK_V,
                        dwExtraInfo = NativeMethods.GetMessageExtraInfo()
                    }
                }
            },
            // V up
            new() {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion {
                    ki = new NativeMethods.KEYBDINPUT {
                        wVk = (ushort)NativeMethods.VK_V,
                        dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                        dwExtraInfo = NativeMethods.GetMessageExtraInfo()
                    }
                }
            },
            // Ctrl up
            new() {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion {
                    ki = new NativeMethods.KEYBDINPUT {
                        wVk = (ushort)NativeMethods.VK_CONTROL,
                        dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                        dwExtraInfo = NativeMethods.GetMessageExtraInfo()
                    }
                }
            },
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
