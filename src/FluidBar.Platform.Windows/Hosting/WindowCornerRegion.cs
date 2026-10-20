using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// Window-region helper for top-level windows whose visible shape is a rounded rectangle drawn by the
/// content (the settings window). Without a region the native window stays rectangular, so wherever the
/// window surface is opaque (DWM composition disabled, redirection-surface fallback, a Win32 peer that
/// ignores the transparency hint) the corners show as square black wedges outside the painted radius.
/// <para>
/// The region is set from physical pixels and updated only when the size/DPI changes — never per frame.
/// It is the same technique the layered island already uses for its squircle
/// (<c>IslandWindowInterop.UpdateWindowRegionOnSettle</c>), reused here for a normal window.
/// </para>
/// </summary>
public static class WindowCornerRegion
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmTransitionsForceDisabled = 3;
    private const int DwmBorderColor = 34;
    private const int DwmCornerDoNotRound = 1;

    /// <summary>Converts a DIP corner radius to the pixel radius used by <c>CreateRoundRectRgn</c>.</summary>
    /// <param name="radiusDip">Corner radius in DIP (the painted content radius).</param>
    /// <param name="scaling">Monitor scaling factor (1.0 = 96 DPI).</param>
    public static int RadiusPx(double radiusDip, double scaling)
    {
        if (!double.IsFinite(radiusDip) || radiusDip <= 0)
        {
            return 0;
        }

        if (!double.IsFinite(scaling) || scaling <= 0)
        {
            scaling = 1.0;
        }

        return (int)Math.Round(radiusDip * scaling, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Clips <paramref name="hwnd"/> to a rounded rectangle of its full client area. The system owns the
    /// region on success; nothing else has to be freed.
    /// </summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="widthPx">Window width in physical pixels.</param>
    /// <param name="heightPx">Window height in physical pixels.</param>
    /// <param name="radiusPx">Corner radius in physical pixels (0 clears the region).</param>
    /// <param name="error">Failure description when the call returns false.</param>
    /// <returns>True when the window region now matches the rounded shape.</returns>
    public static bool TryApply(IntPtr hwnd, int widthPx, int heightPx, int radiusPx, out string? error)
    {
        error = null;
        if (hwnd == IntPtr.Zero)
        {
            error = "window handle is zero";
            return false;
        }

        if (widthPx <= 0 || heightPx <= 0)
        {
            error = $"invalid window size {widthPx}x{heightPx}";
            return false;
        }

        // A zero/oversized radius means "no rounding to apply": clear any earlier region.
        var radius = Math.Max(0, Math.Min(radiusPx, Math.Min(widthPx, heightPx) / 2));
        if (radius <= 0)
        {
            return Clear(hwnd, out error);
        }

        // DWM must not add its own (8 px) rounding on top of our region: that would leave a sliver of
        // the window surface visible between the two curves on Windows 11.
        TryDisableDwmRounding(hwnd);

        var diameter = radius * 2;
        var region = NativeMethods.Gdi32.CreateRoundRectRgn(0, 0, widthPx + 1, heightPx + 1, diameter, diameter);
        if (region == IntPtr.Zero)
        {
            error = "CreateRoundRectRgn failed. " + Win32Error.Format(Win32Error.LastError);
            return false;
        }

        // On success the system owns the region; on failure we must delete it ourselves.
        if (NativeMethods.User32.SetWindowRgn(hwnd, region, bRedraw: true) == 0)
        {
            NativeMethods.Gdi32.DeleteObject(region);
            error = "SetWindowRgn failed. " + Win32Error.Format(Win32Error.LastError);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Asks DWM to round the window's own corners (Windows 11). This is the only shape a
    /// DComp-composited window actually honours: <c>SetWindowRgn</c> reports success but the compositor
    /// ignores it, so the corners of the window surface stay visible unless DWM cuts them.
    /// </summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="error">Failure description when the call returns false (e.g. Windows 10).</param>
    /// <returns>True when DWM will round the window corners.</returns>
    public static bool TryEnableDwmRounding(IntPtr hwnd, out string? error)
    {
        error = null;
        if (hwnd == IntPtr.Zero)
        {
            error = "window handle is zero";
            return false;
        }

        var round = 2; // DWMWCP_ROUND
        var hr = NativeMethods.DwmApi.DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round,
            Marshal.SizeOf<int>());
        if (hr < 0)
        {
            error = $"DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE) failed: 0x{hr:X8}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Removes the 1 px border DWM draws around a rounded window (<c>DWMWA_BORDER_COLOR</c> =
    /// <c>DWMWA_COLOR_NONE</c>). On a dark panel that border reads as a second rim just inside the corner.
    /// </summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="error">Failure description when the call returns false.</param>
    /// <returns>True when DWM accepted the attribute.</returns>
    public static bool TryRemoveDwmBorder(IntPtr hwnd, out string? error)
    {
        error = null;
        if (hwnd == IntPtr.Zero)
        {
            error = "window handle is zero";
            return false;
        }

        var none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        var hr = NativeMethods.DwmApi.DwmSetWindowAttribute(hwnd, DwmBorderColor, ref none, Marshal.SizeOf<int>());
        if (hr < 0)
        {
            error = $"DwmSetWindowAttribute(DWMWA_BORDER_COLOR) failed: 0x{hr:X8}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Disables the DWM window-open/close transition for this window. Windows animates a new window by
    /// drawing its (system) background brush over the client area, which is the grey square that flashes
    /// before Avalonia's first frame.
    /// </summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="error">Failure description when the call returns false.</param>
    /// <returns>True when DWM accepted the attribute.</returns>
    public static bool TryDisableTransitions(IntPtr hwnd, out string? error)
    {
        error = null;
        if (hwnd == IntPtr.Zero)
        {
            error = "window handle is zero";
            return false;
        }

        var enabled = 1; // TRUE
        var hr = NativeMethods.DwmApi.DwmSetWindowAttribute(hwnd, DwmTransitionsForceDisabled, ref enabled,
            Marshal.SizeOf<int>());
        if (hr < 0)
        {
            error = $"DwmSetWindowAttribute(DWMWA_TRANSITIONS_FORCEDISABLED) failed: 0x{hr:X8}";
            return false;
        }

        return true;
    }

    /// <summary>Removes a previously applied region (window becomes rectangular again).</summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="error">Failure description when the call returns false.</param>
    /// <returns>True when the region was cleared.</returns>
    public static bool Clear(IntPtr hwnd, out string? error)
    {
        error = null;
        if (hwnd == IntPtr.Zero)
        {
            error = "window handle is zero";
            return false;
        }

        // SetWindowRgn(null) removes the region; the previous region is deleted by the system.
        if (NativeMethods.User32.SetWindowRgn(hwnd, IntPtr.Zero, bRedraw: true) == 0)
        {
            error = "SetWindowRgn(null) failed. " + Win32Error.Format(Win32Error.LastError);
            return false;
        }

        return true;
    }

    private static void TryDisableDwmRounding(IntPtr hwnd)
    {
        try
        {
            var preference = DwmCornerDoNotRound;
            _ = NativeMethods.DwmApi.DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref preference,
                Marshal.SizeOf<int>());
        }
        catch (DllNotFoundException)
        {
            // Windows 10 without the newer dwmapi entry point: the region alone already clips the corners.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }
}
