using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Display;

/// <summary>
/// Public display service contract for the App host: current monitor set, change notifications,
/// primary accessor and physical-coordinate queries. Implemented by <see cref="MonitorService"/>.
/// </summary>
public interface IMonitorService : IPlatformMonitor<MonitorSet>
{
    /// <summary>The primary monitor, or null before the first enumeration.</summary>
    MonitorInfo? Primary { get; }

    /// <summary>Physical rect of the primary work area (empty before the first enumeration).</summary>
    PixelRect PrimaryWorkArea { get; }

    /// <summary>
    /// Raised (thread pool) when displays are added/removed or resolution/DPI/work area changes.
    /// Same payload as <see cref="IPlatformMonitor{TSnapshot}.SnapshotChanged"/>; kept as a named
    /// alias so the App host reads clearly.
    /// </summary>
    event EventHandler<SnapshotEventArgs<MonitorSet>>? MonitorsChanged;

    /// <summary>Forces a re-enumeration (e.g. after <c>WM_DPICHANGED</c> on the island window).</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Monitor containing a physical point, or the nearest monitor.</summary>
    MonitorInfo? MonitorAtPoint(int physicalX, int physicalY);

    /// <summary>Monitor with the largest overlap with a physical rect.</summary>
    MonitorInfo? MonitorForRect(PixelRect physicalRect);

    /// <summary>Monitor hosting a window (physical pixels).</summary>
    MonitorInfo? MonitorForWindow(IntPtr hwnd);
}
