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
    private const int DwmSystemBackdropType = 38;

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

    /// <summary>Enables the Windows 11 acrylic system backdrop (blurred desktop behind a transparent client).</summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="error">Failure description when the call returns false.</param>
    /// <returns>True when DWM accepted the system-backdrop attribute.</returns>
    public static bool TryEnableAcrylicBackdrop(IntPtr hwnd, out string? error)
    {
        error = null;
        if (hwnd == IntPtr.Zero)
        {
            error = "window handle is zero";
            return false;
        }

        var backdrop = 3; // DWMSBT_TRANSIENTWINDOW (acrylic)
        var hr = NativeMethods.DwmApi.DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop,
            Marshal.SizeOf<int>());
        if (hr < 0)
        {
            error = $"DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE=acrylic) failed: 0x{hr:X8}";
            return false;
        }

        return true;
    }

    /// <summary>Extends the DWM frame into the whole client area so the system backdrop fills it.</summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="error">Failure description when the call returns false.</param>
    /// <returns>True when DWM accepted the frame extension.</returns>
    public static bool TryExtendFrameIntoClientArea(IntPtr hwnd, out string? error)
    {
        error = null;
        if (hwnd == IntPtr.Zero)
        {
            error = "window handle is zero";
            return false;
        }

        var margins = new NativeMethods.DwmApi.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var hr = NativeMethods.DwmApi.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (hr < 0)
        {
            error = $"DwmExtendFrameIntoClientArea failed: 0x{hr:X8}";
            return false;
        }

        return true;
    }

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
