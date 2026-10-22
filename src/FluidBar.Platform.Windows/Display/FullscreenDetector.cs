using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Display;

/// <summary>
/// Detects a foreground window that covers its monitor edge-to-edge. QUNS alone is not enough:
/// browsers in F11 / borderless video players report QUNS_ACCEPTS_NOTIFICATIONS, yet a topmost island
/// still covers their picture. The rectangle check is intentionally tolerant (2 px) because DWM and
/// maximized borderless windows often round by a pixel.
/// </summary>
public static class FullscreenDetector
{
    private const int TolerancePx = 2;
    private const long WsPopup = 0x80000000L;
    private const long WsCaption = 0x00C00000L;

    /// <summary>Returns true when the foreground window occupies its whole monitor.</summary>
    public static bool IsForegroundFullscreen()
    {
        var hwnd = NativeMethods.User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        // The island's own borderless windows can be foreground on top of another app; never let
        // our own process suppress the island.
        NativeMethods.User32.GetWindowThreadProcessId(hwnd, out var processId);
        if (ShouldIgnoreProcess(processId, (uint)Environment.ProcessId))
        {
            return false;
        }

        // The desktop/shell itself is never a fullscreen app.
        var className = new char[128];
        var length = NativeMethods.User32.GetClassNameW(hwnd, className, className.Length);
        if (length > 0)
        {
            var name = new string(className, 0, length);
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return false;
            }
        }

        if (!NativeMethods.User32.GetWindowRect(hwnd, out var window))
        {
            return false;
        }

        // A normal maximized window also covers the monitor, but the island must stay visible over
        // ordinary desktop apps. Only borderless/popup windows (games, F11 video, presentation) count.
        var style = NativeMethods.User32.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
        if (!IsFullscreenStyle(style))
        {
            return false;
        }

        var monitor = NativeMethods.User32.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFOEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEXW>(),
        };
        if (!NativeMethods.User32.GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        return CoversMonitor(window, info.rcMonitor);
    }

    /// <summary>
    /// True for borderless/popup windows. A normal maximized window keeps WS_CAPTION and no WS_POPUP,
    /// so maximizing a browser or Explorer must not hide the island.
    /// </summary>
    /// <summary>True when the foreground window belongs to this process (island/search/settings).</summary>
    internal static bool ShouldIgnoreProcess(uint foregroundProcessId, uint currentProcessId)
        => foregroundProcessId != 0 && foregroundProcessId == currentProcessId;

    internal static bool IsFullscreenStyle(long style)
        => (style & WsPopup) != 0 || (style & WsCaption) == 0;

    /// <summary>Pure rectangle check used by the detector and its tests.</summary>
    internal static bool CoversMonitor(NativeMethods.RECT window, NativeMethods.RECT monitor, int tolerance = TolerancePx)
        => window.Left <= monitor.Left + tolerance
            && window.Top <= monitor.Top + tolerance
            && window.Right >= monitor.Right - tolerance
            && window.Bottom >= monitor.Bottom - tolerance;
}
