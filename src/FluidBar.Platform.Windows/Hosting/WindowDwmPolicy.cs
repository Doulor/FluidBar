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

    private const int GwlStyle = -16;
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    /// <summary>
    /// Gives the window the frame styles (<c>WS_CAPTION | WS_THICKFRAME</c>) without changing how it is
    /// painted: DWM only applies its rounded corner and system shadow to windows it considers framed, and
    /// the client area is kept full-window by handling <c>WM_NCCALCSIZE</c> (the usual borderless-framed
    /// recipe). Call it before asking for the rounded corner.
    /// </summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="error">Failure description when the call returns false.</param>
    /// <returns>True when the style was applied.</returns>
    public static bool TryApplyFramedStyle(IntPtr hwnd, out string? error)
    {
        error = null;
        if (hwnd == IntPtr.Zero)
        {
            error = "window handle is zero";
            return false;
        }

        var style = NativeMethods.User32.GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        var framed = style | WsCaption | WsThickFrame;
        if (framed == style)
        {
            return true;
        }

        _ = NativeMethods.User32.SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(framed));
        if (!NativeMethods.User32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged))
        {
            error = $"SetWindowPos(SWP_FRAMECHANGED) failed: {Marshal.GetLastWin32Error()}";
            return false;
        }

        return true;
    }


    /// <summary>Asks DWM to round the window's own corners (Windows 11).</summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="error">Failure description when the call returns false (e.g. Windows 10).</param>
    /// <returns>True when DWM will round the window corners.</returns>
    public static bool TryEnableRounding(IntPtr hwnd, out string? error)
        => TrySetPreference(hwnd, 2, "ROUND", out error);

    /// <summary>Asks DWM for the small rounded corner variant (<c>DWMWCP_ROUNDSMALL</c>).</summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="error">Failure description when the call returns false.</param>
    /// <returns>True when DWM accepted the preference.</returns>
    public static bool TryEnableRoundingSmall(IntPtr hwnd, out string? error)
        => TrySetPreference(hwnd, 3, "ROUNDSMALL", out error);

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
