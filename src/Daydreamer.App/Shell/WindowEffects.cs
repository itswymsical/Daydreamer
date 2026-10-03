using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Daydreamer.App.Shell;

/// <summary>
/// Small requests to the Windows compositor (DWM): dark window frames and rounded corners.
/// They're cosmetic, so failures (e.g. on older Windows) are ignored.
/// </summary>
internal static class WindowEffects
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Dark title bar and border for standard windows (dialogs), and a dark border/shadow for our own.</summary>
    public static void UseDarkFrame(Window window, bool dark)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    /// <summary>Windows 11 rounded corners (no effect on Windows 10).</summary>
    public static void UseRoundedCorners(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int value = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
    }
}
