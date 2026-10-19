using System.Globalization;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Monitors;

/// <summary>Where the current brightness value comes from.</summary>
public enum BrightnessSource
{
    None,
    DdcCi,
    Wmi,
}

/// <summary>Immutable brightness snapshot.</summary>
/// <param name="Percent">Brightness 0–100, or 0 when unsupported.</param>
/// <param name="Source">Provider that produced the value.</param>
/// <param name="IsSupported">Whether any brightness control is available on this machine.</param>
public sealed record BrightnessSnapshot(int Percent, BrightnessSource Source, bool IsSupported);

/// <summary>Pure DDC/CI brightness math.</summary>
public static class BrightnessMath
{
    /// <summary>Converts <c>GetMonitorBrightness</c> min/current/max to a percentage.</summary>
    public static int? FromDdc(uint minimum, uint current, uint maximum)
    {
        if (maximum <= minimum)
        {
            return null;
        }

        var clamped = Math.Clamp(current, minimum, maximum);
        var percent = (int)Math.Round((clamped - minimum) * 100.0 / (maximum - minimum),
            MidpointRounding.AwayFromZero);
        return Math.Clamp(percent, 0, 100);
    }

    /// <summary>Converts a percentage to an absolute DDC value, clamped into [minimum, maximum].</summary>
    public static uint ToDdc(int percent, uint minimum, uint maximum)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        var clampedPercent = Math.Clamp(percent, 0, 100);
        var value = minimum + (uint)Math.Round((maximum - minimum) * (clampedPercent / 100.0),
            MidpointRounding.AwayFromZero);
        return Math.Clamp(value, minimum, maximum);
    }
}

/// <summary>
/// Pure parser for the WMI fallback value (<c>WmiMonitorBrightness.CurrentBrightness</c>).
/// WMI can hand back byte/int/long/string/byte-array variants depending on the provider.
/// </summary>
public static class BrightnessWmiParser
{
    /// <summary>Parses a raw WMI brightness value into 0–100, or null when the shape is unknown.</summary>
    public static int? Parse(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case byte b:
                return Clamp(b);
            case sbyte sb:
                return Clamp(sb);
            case short s:
                return Clamp(s);
            case ushort us:
                return Clamp(us);
            case int i:
                return Clamp(i);
            case uint ui:
                return Clamp(ui);
            case long l:
                return Clamp(l);
            case ulong ul:
                return ul > int.MaxValue ? null : Clamp((long)ul);
            case float f:
                return Clamp((int)Math.Round(f, MidpointRounding.AwayFromZero));
            case double d:
                return Clamp((int)Math.Round(d, MidpointRounding.AwayFromZero));
            case string text:
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? Clamp(parsed)
                    : null;
            case byte[] bytes when bytes.Length > 0:
                return Clamp(bytes[0]);
            case Array array when array.Length > 0 && array.GetValue(0) is IConvertible convertible:
                try
                {
                    return Clamp(convertible.ToInt32(CultureInfo.InvariantCulture));
                }
                catch (Exception)
                {
                    return null;
                }

            default:
                return null;
        }
    }

    private static int? Clamp(long value) => value < 0 ? null : (int)Math.Clamp(value, 0, 100);
}

/// <summary>Provider abstraction so the monitor loop is testable and future providers can be added.</summary>
public interface IBrightnessProvider
{
    /// <summary>Provider name for logs/diagnostics.</summary>
    string Name { get; }

    /// <summary>Reads the current brightness on the poll thread. Returns null when unsupported.</summary>
    int? TryGetPercent();

    /// <summary>Sets the brightness on the poll thread. Returns false when unsupported.</summary>
    bool TrySetPercent(int percent);
}

/// <summary>DDC/CI provider (dxva2). Acquires and destroys physical-monitor handles per query.</summary>
internal sealed class DdcBrightnessProvider : IBrightnessProvider
{
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;

    public DdcBrightnessProvider(IPlatformLogger logger, PlatformCounters counters)
    {
        _logger = logger;
        _counters = counters;
    }

    public string Name => "ddc";

    public int? TryGetPercent()
    {
        try
        {
            int? result = null;
            var enumeration = new NativeMethods.MonitorEnumProc((IntPtr hMonitor, IntPtr hdc,
                ref NativeMethods.RECT rect, IntPtr data) =>
            {
                if (result is not null)
                {
                    return true;
                }

                if (!NativeMethods.Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count)
                    || count == 0)
                {
                    return true;
                }

                var monitors = new NativeMethods.PHYSICAL_MONITOR[count];
                if (!NativeMethods.Dxva2.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                {
                    return true;
                }

                try
                {
                    foreach (var monitor in monitors)
                    {
                        if (NativeMethods.Dxva2.GetMonitorBrightness(monitor.hPhysicalMonitor,
                                out var minimum, out var current, out var maximum))
                        {
                            result = BrightnessMath.FromDdc(minimum, current, maximum);
                            break;
                        }
                    }
                }
                finally
                {
                    NativeMethods.Dxva2.DestroyPhysicalMonitors(count, monitors);
                }

                return true;
            });

            NativeMethods.User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, enumeration, IntPtr.Zero);
            GC.KeepAlive(enumeration);
            return result;
        }
        catch (Exception ex)
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Monitor, "brightness.ddc-read-failed",
                "DDC/CI brightness read failed.", ex);
            return null;
        }
    }

    public bool TrySetPercent(int percent)
    {
        try
        {
            var applied = false;
            var enumeration = new NativeMethods.MonitorEnumProc((IntPtr hMonitor, IntPtr hdc,
                ref NativeMethods.RECT rect, IntPtr data) =>
            {
                if (applied)
                {
                    return true;
                }

                if (!NativeMethods.Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count)
                    || count == 0)
                {
                    return true;
                }

                var monitors = new NativeMethods.PHYSICAL_MONITOR[count];
                if (!NativeMethods.Dxva2.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                {
                    return true;
                }

                try
                {
                    foreach (var monitor in monitors)
                    {
                        if (!NativeMethods.Dxva2.GetMonitorBrightness(monitor.hPhysicalMonitor,
                                out var minimum, out _, out var maximum))
                        {
                            continue;
                        }

                        var target = BrightnessMath.ToDdc(percent, minimum, maximum);
                        if (NativeMethods.Dxva2.SetMonitorBrightness(monitor.hPhysicalMonitor, target))
                        {
                            applied = true;
                            break;
                        }
                    }
                }
                finally
                {
                    NativeMethods.Dxva2.DestroyPhysicalMonitors(count, monitors);
                }

                return true;
            });

            NativeMethods.User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, enumeration, IntPtr.Zero);
            GC.KeepAlive(enumeration);
            return applied;
        }
        catch (Exception ex)
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Monitor, "brightness.ddc-write-failed",
                "DDC/CI brightness write failed.", ex);
            return false;
        }
    }
}

/// <summary>
/// WMI fallback provider (<c>root\WMI</c>, <c>WmiMonitorBrightness</c>/<c>WmiMonitorBrightnessMethods</c>).
/// Uses late-bound COM on the dedicated poll thread; every COM object is released explicitly.
/// </summary>
internal sealed class WmiBrightnessProvider : IBrightnessProvider, IDisposable
{
    private const string BrightnessQuery = "SELECT CurrentBrightness FROM WmiMonitorBrightness";
    private const string BrightnessMethodQuery = "SELECT * FROM WmiMonitorBrightnessMethods";

    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private object? _locator;
    private object? _service;
    private bool _unavailable;

    public WmiBrightnessProvider(IPlatformLogger logger, PlatformCounters counters)
    {
        _logger = logger;
        _counters = counters;
    }

    public string Name => "wmi";

    public int? TryGetPercent()
    {
        if (_unavailable)
        {
            return null;
        }

        try
        {
            if (!EnsureService())
            {
                return null;
            }

            dynamic results = ((dynamic)_service!).ExecQuery(BrightnessQuery);
            try
            {
                foreach (dynamic instance in results)
                {
                    try
                    {
                        // SWbemObject does not expose the WMI property as a direct IDispatch member:
                        // instance.CurrentBrightness resolves to null on every machine. The value lives
                        // behind Properties_.Item("CurrentBrightness").Value.
                        dynamic properties = instance.Properties_;
                        try
                        {
                            dynamic property = properties.Item("CurrentBrightness");
                            try
                            {
                                object? raw = property.Value;
                                return BrightnessWmiParser.Parse(raw);
                            }
                            finally
                            {
                                ReleaseCom(property);
                            }
                        }
                        finally
                        {
                            ReleaseCom(properties);
                        }
                    }
                    finally
                    {
                        ReleaseCom(instance);
                    }
                }

                return null;
            }
            finally
            {
                ReleaseCom(results);
            }
        }
        catch (Exception ex)
        {
            MarkUnavailable(ex);
            return null;
        }
    }

    public bool TrySetPercent(int percent)
    {
        if (_unavailable)
        {
            return false;
        }

        try
        {
            if (!EnsureService())
            {
                return false;
            }

            dynamic results = ((dynamic)_service!).ExecQuery(BrightnessMethodQuery);
            try
            {
                foreach (dynamic instance in results)
                {
                    try
                    {
                        // WmiSetBrightness(Timeout, Brightness) with timeout 0 = immediate.
                        instance.WmiSetBrightness(0, percent);
                        return true;
                    }
                    finally
                    {
                        ReleaseCom(instance);
                    }
                }

                return false;
            }
            finally
            {
                ReleaseCom(results);
            }
        }
        catch (Exception ex)
        {
            MarkUnavailable(ex);
            return false;
        }
    }

    private bool EnsureService()
    {
        if (_service is not null)
        {
            return true;
        }

        var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
        if (locatorType is null)
        {
            _logger.LogWarning(PlatformLogCategory.Monitor, "brightness.wmi-unavailable",
                "WbemScripting.SWbemLocator is not registered; WMI brightness fallback disabled.");
            _unavailable = true;
            return false;
        }

        dynamic locator = Activator.CreateInstance(locatorType)!;
        dynamic service = locator.ConnectServer(".", @"root\WMI");
        _locator = locator;
        _service = service;
        return true;
    }

    private void MarkUnavailable(Exception ex)
    {
        _counters.IncrementComFailures();
        _logger.LogWarning(PlatformLogCategory.Monitor, "brightness.wmi-failed",
            "WMI brightness query failed; WMI fallback disabled until the monitor restarts.", ex);
        _unavailable = true;
        ReleaseService();
    }

    private static void ReleaseCom(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
        catch (Exception)
        {
            // Already released; nothing to do.
        }
    }

    private void ReleaseService()
    {
        ReleaseCom(_service);
        _service = null;
        ReleaseCom(_locator);
        _locator = null;
    }

    public void Dispose()
    {
        ReleaseService();
        // Allow a later StartAsync to re-create the automation objects.
        _unavailable = false;
    }
}

/// <summary>
/// Brightness monitor: a dedicated MTA background thread polls DDC/CI (preferred) or the WMI fallback
/// every 2 s, publishes change-only snapshots and supports cancellable writes. The UI thread never
/// blocks on a device query (v1 F-01).
/// </summary>
public sealed class BrightnessMonitor : PlatformMonitor<BrightnessSnapshot>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly List<IBrightnessProvider> _providers;
    private BackgroundPoller? _poller;
    private BrightnessSnapshot _lastPublished = new(0, BrightnessSource.None, false);
    private bool _publishInitial = true;

    public BrightnessMonitor(IPlatformLogger logger, PlatformCounters counters, TimeProvider? timeProvider = null)
        : this(logger, counters,
            new IBrightnessProvider[] { new DdcBrightnessProvider(logger, counters), new WmiBrightnessProvider(logger, counters) },
            timeProvider)
    {
    }

    /// <summary>Test/DI constructor with explicit providers (ordered by preference).</summary>
    public BrightnessMonitor(IPlatformLogger logger, PlatformCounters counters,
        IEnumerable<IBrightnessProvider> providers, TimeProvider? timeProvider = null)
        : base("brightness", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        if (_providers.Count == 0)
        {
            throw new ArgumentException("At least one brightness provider is required.", nameof(providers));
        }
    }

    protected override ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        _publishInitial = true;
        _lastPublished = new BrightnessSnapshot(0, BrightnessSource.None, false);
        _poller = new BackgroundPoller("brightness", PollInterval, PollOnce, _logger,
            onStopped: () => (ProvidersDisposable()).ForEach(provider => provider.Dispose()));
        _poller.Start();
        return ValueTask.CompletedTask;
    }

    private List<IDisposable> ProvidersDisposable()
        => _providers.OfType<IDisposable>().ToList();

    private void PollOnce()
    {
        var (percent, source) = QueryCore();
        var snapshot = percent is null
            ? new BrightnessSnapshot(0, BrightnessSource.None, false)
            : new BrightnessSnapshot(percent.Value, source, true);

        if (_publishInitial)
        {
            _publishInitial = false;
            _lastPublished = snapshot;
            Publish(snapshot);
            return;
        }

        if (!snapshot.Equals(_lastPublished))
        {
            _lastPublished = snapshot;
            Publish(snapshot);
        }
    }

    private (int? Percent, BrightnessSource Source) QueryCore()
    {
        foreach (var provider in _providers)
        {
            var value = provider.TryGetPercent();
            if (value is not null)
            {
                return (value, provider.Name switch
                {
                    "ddc" => BrightnessSource.DdcCi,
                    "wmi" => BrightnessSource.Wmi,
                    _ => BrightnessSource.None,
                });
            }
        }

        return (null, BrightnessSource.None);
    }

    /// <summary>Forces an immediate re-read (e.g. after display hot-plug).</summary>
    public void TriggerRefresh() => _poller?.TriggerNow();

    /// <summary>Sets brightness (preferred provider first). Runs on the poll thread; never on the UI thread.</summary>
    public async Task<bool> SetPercentAsync(int percent, CancellationToken cancellationToken = default)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var poller = _poller;
        if (poller is null)
        {
            throw new InvalidOperationException("Brightness monitor is not running.");
        }

        var applied = false;
        await poller.InvokeAsync(() =>
        {
            foreach (var provider in _providers)
            {
                if (provider.TrySetPercent(clamped))
                {
                    applied = true;
                    break;
                }
            }

            if (applied)
            {
                var (value, source) = QueryCore();
                if (value is not null)
                {
                    var snapshot = new BrightnessSnapshot(value.Value, source, true);
                    _lastPublished = snapshot;
                    Publish(snapshot);
                }
            }
        }).WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!applied)
        {
            _logger.LogInfo(PlatformLogCategory.Monitor, "brightness.set-unsupported",
                "Brightness could not be changed on any display.");
        }

        return applied;
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        var poller = _poller;
        _poller = null;
        if (poller is not null)
        {
            // Join the poll thread off the caller; its cleanup releases WMI COM objects on the same
            // thread that created them.
            await Task.Run(poller.Dispose, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
