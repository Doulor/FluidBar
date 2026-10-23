using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// Foreground-window context for the smart-yield engine (design doc §5.1/§6.3): is the window in front
/// of the island maximized or fullscreen, and does it actually reach under the island's rectangle?
/// Queried on demand only — never enumerates the window list.
/// </summary>
public static class ForegroundWindowProbe
{
    /// <summary>
    /// Whether the foreground window is maximized (or covers the monitor) and overlaps the given screen
    /// rectangle. Returns false when the foreground window is one of our own.
    /// </summary>
    /// <param name="x">Island rect left (physical px).</param>
    /// <param name="y">Island rect top.</param>
    /// <param name="width">Island rect width.</param>
    /// <param name="height">Island rect height.</param>
    public static bool IsForegroundMaximizedOver(int x, int y, int width, int height)
        => Query(x, y, width, height, out var maximized, out _) && maximized;

    /// <summary>Whether a fullscreen app (game/video/presentation) owns the foreground.</summary>
    public static bool IsForegroundFullscreen()
    {
        var hwnd = NativeMethods.User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd) || !TryGetRect(hwnd, out var rect))
        {
            return false;
        }

        var monitor = NativeMethods.User32.MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFOEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEXW>(),
            szDevice = string.Empty,
        };
        if (!NativeMethods.User32.GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        var bounds = info.rcMonitor;
        return rect.Left <= bounds.Left && rect.Top <= bounds.Top
            && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
    }

    /// <summary>Pure overlap test (visible for tests): half-open rectangles.</summary>
    /// <param name="a">First rectangle.</param>
    /// <param name="b">Second rectangle.</param>
    internal static bool Overlaps(NativeMethods.RECT a, NativeMethods.RECT b)
        => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    private static bool Query(int x, int y, int width, int height, out bool maximized, out bool fullscreen)
    {
        maximized = false;
        fullscreen = false;
        var hwnd = NativeMethods.User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd) || !TryGetRect(hwnd, out var rect))
        {
            return false;
        }

        var island = new NativeMethods.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
        if (!Overlaps(rect, island))
        {
            return false;
        }

        var monitor = NativeMethods.User32.MonitorFromWindow(hwnd, 2);
        var info = new NativeMethods.MONITORINFOEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEXW>(),
            szDevice = string.Empty,
        };
        var hasMonitor = monitor != IntPtr.Zero && NativeMethods.User32.GetMonitorInfoW(monitor, ref info);
        var work = hasMonitor ? info.rcWork : default;

        // Maximized: the window covers (almost) the whole work area. Using geometry instead of IsZoomed
        // also catches windows that size themselves to the work area without the maximized flag, and the
        // 90 % area rule tolerates DPI rounding and thin borders.
        if (hasMonitor)
        {
            var windowArea = (double)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            var workArea = (double)(work.Right - work.Left) * (work.Bottom - work.Top);
            maximized = workArea > 0 && windowArea >= workArea * 0.9;
        }

        return true;
    }

    private static bool TryGetRect(IntPtr hwnd, out NativeMethods.RECT rect)
    {
        rect = default;
        return NativeMethods.User32.GetWindowRect(hwnd, out rect);
    }

    private static bool IsOwnWindow(IntPtr hwnd)
    {
        NativeMethods.User32.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == NativeMethods.Kernel32.GetCurrentProcessId();
    }
}
