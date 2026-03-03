using System.Runtime.InteropServices;

namespace TokenTalk.Platform;

public class ClipboardService
{
    // Clipboard operations must run on an STA thread.
    // We use Task.Run with STA marshaling via a dedicated helper.

    public string GetText()
    {
        return RunOnStaThread(() =>
        {
            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
                return string.Empty;

            try
            {
                var hData = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
                if (hData == IntPtr.Zero)
                    return string.Empty;

                var ptr = NativeMethods.GlobalLock(hData);
                if (ptr == IntPtr.Zero)
                    return string.Empty;

                try
                {
                    return Marshal.PtrToStringUni(ptr) ?? string.Empty;
                }
                finally
                {
                    NativeMethods.GlobalUnlock(hData);
                }
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        });
    }

    public bool SetText(string text)
    {
        return RunOnStaThread(() =>
        {
            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
                return false;

            try
            {
                NativeMethods.EmptyClipboard();

                // Allocate global memory for the text (UTF-16 + null terminator)
                int byteCount = (text.Length + 1) * 2;
                var hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)byteCount);
                if (hGlobal == IntPtr.Zero)
                    return false;

                var ptr = NativeMethods.GlobalLock(hGlobal);
                if (ptr == IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(hGlobal);
                    return false;
                }

                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                    // Write null terminator
                    Marshal.WriteInt16(ptr, text.Length * 2, 0);
                }
                finally
                {
                    NativeMethods.GlobalUnlock(hGlobal);
                }

                var result = NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hGlobal);
                if (result == IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(hGlobal);
                    return false;
                }

                return true;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        });
    }

    private static T RunOnStaThread<T>(Func<T> func)
    {
        T result = default!;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception e) { ex = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
            throw ex;

        return result;
    }
}
