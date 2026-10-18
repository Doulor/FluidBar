using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Display;

/// <summary>
/// Event-driven display service: enumerates monitors, work areas and per-monitor DPI, republishes an
/// immutable <see cref="MonitorSet"/> on <c>WM_DISPLAYCHANGE</c>/<c>WM_SETTINGCHANGE</c> and exposes
/// the queries the App layer needs for placement.
/// </summary>
/// <remarks>
/// <para>Enumeration and DPI queries run on the thread pool (never on the UI thread). Monitor changes
/// are debounced and coalesced; a burst of display messages produces one snapshot.</para>
/// <para>WM_DPICHANGED is delivered to the window being moved. The App's island window will receive it
/// and should call <see cref="RefreshAsync"/>; this service also refreshes on the broadcast messages.</para>
/// </remarks>
public sealed class MonitorService : PlatformMonitor<MonitorSet>, IMonitorService
{
    private readonly PlatformMessageHost _host;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private PlatformWindow? _window;
    private ITimer? _debounceTimer;
    private readonly object _debounceGate = new();

    public MonitorService(PlatformMessageHost host, IPlatformLogger logger, PlatformCounters counters,
        TimeProvider? timeProvider = null)
        : base("display", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Physical rect of the primary work area, or an empty rect when no monitors were enumerated.</summary>
    public PixelRect PrimaryWorkArea => Current?.Primary?.WorkArea ?? default;

    /// <summary>The primary monitor, or null before the first enumeration.</summary>
    public MonitorInfo? Primary => Current?.Primary;

    /// <summary>Named alias for <see cref="PlatformMonitor{TSnapshot}.SnapshotChanged"/> used by the App host.</summary>
    public event EventHandler<SnapshotEventArgs<MonitorSet>>? MonitorsChanged
    {
        add => SnapshotChanged += value;
        remove => SnapshotChanged -= value;
    }

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        _window = await _host.CreateWindowAsync("display", PlatformWindowKind.HiddenTopLevel,
            OnWindowMessage, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        lock (_debounceGate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        var window = _window;
        _window = null;
        if (window is not null)
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    private IntPtr? OnWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case NativeMethods.WM_DISPLAYCHANGE:
                ScheduleRefresh("display-change");
                return IntPtr.Zero;
            case NativeMethods.WM_SETTINGCHANGE:
                // wParam carries the SPI_* action; work area/DPI changes arrive as SPI_SETWORKAREA or 0.
                ScheduleRefresh("setting-change");
                return IntPtr.Zero;
            case NativeMethods.WM_DPICHANGED:
                ScheduleRefresh("dpi-change");
                return IntPtr.Zero;
            default:
                return null;
        }
    }

    private void ScheduleRefresh(string reason)
    {
        lock (_debounceGate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = TimeProvider.CreateTimer(
                static state =>
                {
                    var self = (MonitorService)state!;
                    _ = self.RefreshSafeAsync();
                },
                this,
                TimeSpan.FromMilliseconds(300),
                Timeout.InfiniteTimeSpan);
        }

        Logger.LogDebug(PlatformLogCategory.Monitor, "display.refresh-scheduled",
            "Display change scheduled a monitor refresh.", new Dictionary<string, object?> { ["reason"] = reason });
    }

    /// <summary>Re-enumerates monitors. Single-flight: concurrent calls coalesce into the running refresh.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var set = await Task.Run(EnumerateMonitors, cancellationToken).ConfigureAwait(false);
            Publish(set);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshSafeAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Counters.IncrementMonitorCallbackFailures();
            Logger.LogError(PlatformLogCategory.Monitor, "display.refresh-failed",
                "Failed to refresh the monitor set.", ex);
        }
    }

    private MonitorSet EnumerateMonitors()
    {
        var builder = ImmutableArray.CreateBuilder<MonitorInfo>();
        var index = 0;
        var enumDelegate = new NativeMethods.MonitorEnumProc((IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect,
            IntPtr data) =>
        {
            var info = new NativeMethods.MONITORINFOEXW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEXW>(),
                szDevice = string.Empty,
            };

            if (NativeMethods.User32.GetMonitorInfoW(hMonitor, ref info))
            {
                var dpiX = 96u;
                var dpiY = 96u;
                if (NativeMethods.Shcore.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out var x, out var y) == 0)
                {
                    dpiX = x == 0 ? 96u : x;
                    dpiY = y == 0 ? dpiX : y;
                }

                var isPrimary = (info.dwFlags & 1u) != 0;
                builder.Add(new MonitorInfo(
                    Id: string.IsNullOrWhiteSpace(info.szDevice) ? $"\\\\.\\DISPLAY{index + 1}" : info.szDevice,
                    Index: index++,
                    Bounds: new PixelRect(info.rcMonitor.Left, info.rcMonitor.Top,
                        info.rcMonitor.Width, info.rcMonitor.Height),
                    WorkArea: new PixelRect(info.rcWork.Left, info.rcWork.Top,
                        info.rcWork.Width, info.rcWork.Height),
                    DpiX: dpiX,
                    DpiY: dpiY,
                    IsPrimary: isPrimary));
            }

            return true;
        });

        if (!NativeMethods.User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, enumDelegate, IntPtr.Zero))
        {
            Counters.IncrementWin32Failures();
            Logger.LogWarning(PlatformLogCategory.Monitor, "display.enum-failed",
                $"EnumDisplayMonitors failed: {Win32Error.Format(Win32Error.LastError)}");
        }

        GC.KeepAlive(enumDelegate);
        if (builder.Count == 0)
        {
            Logger.LogWarning(PlatformLogCategory.Monitor, "display.no-monitors",
                "No display monitors were enumerated; falling back to an empty monitor set.");
        }

        return new MonitorSet(builder.ToImmutable());
    }

    /// <summary>Monitor containing the given physical point, or the nearest/primary fallback.</summary>
    public MonitorInfo? MonitorAtPoint(int physicalX, int physicalY)
    {
        var set = Current;
        if (set is null || set.Count == 0)
        {
            return null;
        }

        return DisplayMetrics.MonitorAtPoint(set, physicalX, physicalY)
               ?? DisplayMetrics.SelectMonitor(set, new PixelRect(physicalX, physicalY, 1, 1));
    }

    /// <summary>Monitor with the largest overlap with a physical rect.</summary>
    public MonitorInfo? MonitorForRect(PixelRect physicalRect)
    {
        var set = Current;
        return set is null || set.Count == 0 ? null : DisplayMetrics.SelectMonitor(set, physicalRect);
    }

    /// <summary>Monitor currently hosting a window (uses <c>MonitorFromWindow</c> + bounds).</summary>
    public MonitorInfo? MonitorForWindow(IntPtr hwnd)
    {
        var set = Current;
        if (set is null || set.Count == 0)
        {
            return null;
        }

        var hMonitor = NativeMethods.User32.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
        {
            return set.Primary;
        }

        var info = new NativeMethods.MONITORINFOEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEXW>(),
            szDevice = string.Empty,
        };

        if (!NativeMethods.User32.GetMonitorInfoW(hMonitor, ref info))
        {
            return set.Primary;
        }

        var rect = new PixelRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Width, info.rcMonitor.Height);
        foreach (var monitor in set.Monitors)
        {
            if (monitor.Bounds == rect)
            {
                return monitor;
            }
        }

        return DisplayMetrics.SelectMonitor(set, rect);
    }
}
