using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace TokenTalk.Platform;

public enum HotkeyEventType { Pressed, Released }

public record HotkeyEvent(HotkeyEventType Type);

public class HotkeyListener : IDisposable
{
    private readonly Channel<HotkeyEvent> _channel;
    private IntPtr _hookHandle = IntPtr.Zero;
    private uint _hookThreadId;
    private Thread? _hookThread;
    private NativeMethods.LowLevelKeyboardProc? _proc;

    // Parsed hotkey
    private bool _requireCtrl;
    private bool _requireShift;
    private bool _requireAlt;
    private bool _requireWin;
    private int _triggerVk; // 0 for modifier-only combos

    // Modifier state tracked directly in the hook callback — avoids
    // GetAsyncKeyState unreliability when called from a background thread.
    private bool _ctrlDown;
    private bool _shiftDown;
    private bool _altDown;
    private bool _winDown;

    private bool _isPressed;

    public HotkeyListener()
    {
        _channel = Channel.CreateUnbounded<HotkeyEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
    }

    public ChannelReader<HotkeyEvent> Events => _channel.Reader;

    public void Start(string hotkey)
    {
        ParseHotkey(hotkey);

        _hookThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "HotkeyHookThread"
        };
        _hookThread.Start();
    }

    private void ParseHotkey(string hotkey)
    {
        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToLower())
            {
                case "ctrl":
                case "control":
                    _requireCtrl = true;
                    break;
                case "shift":
                    _requireShift = true;
                    break;
                case "alt":
                    _requireAlt = true;
                    break;
                case "win":
                case "windows":
                    _requireWin = true;
                    break;
                default:
                    _triggerVk = VkFromString(part);
                    break;
            }
        }
    }

    private static int VkFromString(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpper(key[0]);
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= '0' && c <= '9') return c;
        }
        return key.ToLower() switch
        {
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "escape" or "esc" => 0x1B,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "f1" => 0x70,
            "f2" => 0x71,
            "f3" => 0x72,
            "f4" => 0x73,
            "f5" => 0x74,
            "f6" => 0x75,
            "f7" => 0x76,
            "f8" => 0x77,
            "f9" => 0x78,
            "f10" => 0x79,
            "f11" => 0x7A,
            "f12" => 0x7B,
            _ => 0
        };
    }

    private void RunMessageLoop()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        _proc = HookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to install keyboard hook. Error: {Marshal.GetLastWin32Error()}");
        }

        // Message pump required for WH_KEYBOARD_LL to fire
        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kbInfo = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)kbInfo.vkCode;
            bool isKeyDown = wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN;
            bool isKeyUp   = wParam == NativeMethods.WM_KEYUP   || wParam == NativeMethods.WM_SYSKEYUP;

            // Update tracked modifier state from the event stream itself.
            // This is reliable across all applications, unlike GetAsyncKeyState
            // which reads from the hook thread's own (unupdated) key state table.
            UpdateModifierState(vk, isKeyDown, isKeyUp);

            bool modifiersMatch = ModifiersMatch();

            if (_triggerVk != 0)
            {
                // Standard combo: modifiers + a non-modifier trigger key (e.g. Ctrl+Shift+V)
                if (vk == _triggerVk)
                {
                    if (isKeyDown && modifiersMatch && !_isPressed)
                    {
                        _isPressed = true;
                        _channel.Writer.TryWrite(new HotkeyEvent(HotkeyEventType.Pressed));
                    }
                    else if (isKeyUp && _isPressed)
                    {
                        _isPressed = false;
                        _channel.Writer.TryWrite(new HotkeyEvent(HotkeyEventType.Released));
                    }
                }
                // Release if a required modifier is lifted while trigger was held
                else if (isKeyUp && _isPressed && IsRequiredModifier(vk))
                {
                    _isPressed = false;
                    _channel.Writer.TryWrite(new HotkeyEvent(HotkeyEventType.Released));
                }
            }
            else
            {
                // Modifier-only combo (e.g. Ctrl+Win): fire when the combo
                // becomes fully satisfied, release when it breaks.
                if (isKeyDown && IsRequiredModifier(vk) && modifiersMatch && !_isPressed)
                {
                    _isPressed = true;
                    _channel.Writer.TryWrite(new HotkeyEvent(HotkeyEventType.Pressed));
                }
                else if (isKeyUp && _isPressed && IsRequiredModifier(vk) && !modifiersMatch)
                {
                    _isPressed = false;
                    _channel.Writer.TryWrite(new HotkeyEvent(HotkeyEventType.Released));
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void UpdateModifierState(int vk, bool isKeyDown, bool isKeyUp)
    {
        bool down = isKeyDown;
        switch (vk)
        {
            case NativeMethods.VK_LCONTROL:
            case NativeMethods.VK_RCONTROL:
            case NativeMethods.VK_CONTROL:
                if (isKeyDown || isKeyUp) _ctrlDown = down;
                break;
            case NativeMethods.VK_LSHIFT:
            case NativeMethods.VK_RSHIFT:
            case NativeMethods.VK_SHIFT:
                if (isKeyDown || isKeyUp) _shiftDown = down;
                break;
            case NativeMethods.VK_LMENU:
            case NativeMethods.VK_RMENU:
            case NativeMethods.VK_MENU:
                if (isKeyDown || isKeyUp) _altDown = down;
                break;
            case NativeMethods.VK_LWIN:
            case NativeMethods.VK_RWIN:
                if (isKeyDown || isKeyUp) _winDown = down;
                break;
        }
    }

    private bool IsRequiredModifier(int vk) => vk switch
    {
        NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL or NativeMethods.VK_CONTROL
            => _requireCtrl,
        NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT or NativeMethods.VK_SHIFT
            => _requireShift,
        NativeMethods.VK_LMENU or NativeMethods.VK_RMENU or NativeMethods.VK_MENU
            => _requireAlt,
        NativeMethods.VK_LWIN or NativeMethods.VK_RWIN
            => _requireWin,
        _ => false
    };

    private bool ModifiersMatch() =>
        (!_requireCtrl  || _ctrlDown)  && (_ctrlDown  == _requireCtrl)  &&
        (!_requireShift || _shiftDown) && (_shiftDown == _requireShift) &&
        (!_requireAlt   || _altDown)   && (_altDown   == _requireAlt)   &&
        (!_requireWin   || _winDown)   && (_winDown   == _requireWin);

    public void Stop()
    {
        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        _channel.Writer.TryComplete();
    }

    public void Dispose()
    {
        Stop();
        _hookThread?.Join(TimeSpan.FromSeconds(2));
    }
}
