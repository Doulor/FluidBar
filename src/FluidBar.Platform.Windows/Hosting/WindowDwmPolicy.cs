using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// DWM window-corner policy. Used by the forced-alpha experiment (and the fallback path) to control
/// whether DWM draws its own rounded corner and edge band on top of the client area:
/// <list type="bullet">
/// <item><c>DWMWCP_ROUND</c> (2) - DWM rounds the window itself (opaque fallback);</item>
/// <item><c>DWMWCP_DONOTROUND</c> (1) - DWM draws neither corner nor edge, so the client alpha is the
/// only shape (per-pixel-alpha path).</item>
/// </list>
/// </summary>
public static class WindowDwmPolicy
{
    private const int DwmWindowCornerPreference = 33;

    /// <summary>Asks DWM to round the window's own corners (Windows 11).</summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="error">Failure description when the call returns false (e.g. Windows 10).</param>
    /// <returns>True when DWM will round the window corners.</returns>
    public static bool TryEnableRounding(IntPtr hwnd, out string? error)
        => TrySetPreference(hwnd, 2, "ROUND", out error);

    /// <summary>Tells DWM not to draw its own corner and edge band (per-pixel-alpha path).</summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="error">Failure description when the call returns false.</param>
    /// <returns>True when DWM accepted the attribute.</returns>
    public static bool TryDisableRounding(IntPtr hwnd, out string? error)
        => TrySetPreference(hwnd, 1, "DONOTROUND", out error);

    /// <summary>Reads back the DWM corner preference (diagnostics).</summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <returns>The preference value (1 = DONOTROUND, 2 = ROUND, 3 = ROUNDSMALL), or -1.</returns>
    public static int GetPreference(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return -1;
        }

        var value = -1;
        var hr = NativeMethods.DwmApi.DwmGetWindowAttribute(hwnd, DwmWindowCornerPreference, ref value,
            Marshal.SizeOf<int>());
        return hr < 0 ? -1 : value;
    }

    private static bool TrySetPreference(IntPtr hwnd, int preference, string name, out string? error)
    {
        error = null;
        if (hwnd == IntPtr.Zero)
        {
            error = "window handle is zero";
            return false;
        }

        var value = preference;
        var hr = NativeMethods.DwmApi.DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref value,
            Marshal.SizeOf<int>());
        if (hr < 0)
        {
            error = $"DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE={name}) failed: 0x{hr:X8}";
            return false;
        }

        return true;
    }
}
