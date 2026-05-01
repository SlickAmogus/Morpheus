using System;
using System.Runtime.InteropServices;

namespace Morpheus.Ui.Widgets;

public static class ClipboardHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;

    public static string? GetText()
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero)) return null;
            var h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) { CloseClipboard(); return null; }
            var p = GlobalLock(h);
            var s = Marshal.PtrToStringUni(p);
            GlobalUnlock(h);
            CloseClipboard();
            return s;
        }
        catch { return null; }
    }
}
