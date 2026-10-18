using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Monitors;

/// <summary>Where the machine currently gets its power.</summary>
public enum BatteryPowerSource
{
    Unknown,
    Ac,
    Battery,
}

/// <summary>Immutable battery snapshot published by <see cref="BatteryMonitor"/>.</summary>
/// <param name="HasBattery">False on desktops without a battery.</param>
/// <param name="Percent">Charge percentage 0–100, or -1 when Windows reports no value.</param>
/// <param name="IsCharging">Whether the battery is charging.</param>
/// <param name="Source">AC vs battery vs unknown.</param>
/// <param name="IsLow">Below the configured low threshold and not charging.</param>
/// <param name="IsCritical">Windows reports the critical battery flag.</param>
/// <param name="MinutesRemaining">Estimated minutes left (null when unknown).</param>
/// <param name="MinutesToFull">Estimated minutes to full while charging (null when unknown).</param>
public sealed record BatterySnapshot(
    bool HasBattery,
    int Percent,
    bool IsCharging,
    BatteryPowerSource Source,
    bool IsLow,
    bool IsCritical,
    int? MinutesRemaining,
    int? MinutesToFull);

/// <summary>Options for battery evaluation.</summary>
public sealed class BatteryMonitorOptions
{
    /// <summary>Battery percentage at or below which <see cref="BatterySnapshot.IsLow"/> is true.</summary>
    public int LowThresholdPercent { get; init; } = 20;
}

/// <summary>
/// Pure conversion from a <c>SYSTEM_POWER_STATUS</c> reading to an immutable snapshot.
/// Fully unit tested: unknown flags, >100% readings (v1 bug), desktops and time estimates.
/// </summary>
public static class BatteryStatusParser
{
    private const byte BatteryFlagHigh = 0x01;
    private const byte BatteryFlagLow = 0x02;
    private const byte BatteryFlagCritical = 0x04;
    private const byte BatteryFlagCharging = 0x08;
    private const byte BatteryFlagNoBattery = 0x80;
    private const byte BatteryFlagUnknown = 0xFF;
    private const uint UnknownTime = 0xFFFFFFFF;

    /// <summary>Parses raw power-status fields.</summary>
    public static BatterySnapshot Parse(byte acLineStatus, byte batteryFlag, byte batteryLifePercent,
        uint batteryLifeTimeSeconds, uint batteryFullLifeTimeSeconds, int lowThresholdPercent = 20)
    {
        var hasBattery = (batteryFlag & BatteryFlagNoBattery) == 0 && batteryFlag != BatteryFlagUnknown;

        var percent = batteryLifePercent switch
        {
            255 => -1,
            > 100 => 100,
            _ => batteryLifePercent,
        };

        var source = acLineStatus switch
        {
            0 => BatteryPowerSource.Battery,
            1 => BatteryPowerSource.Ac,
            _ => BatteryPowerSource.Unknown,
        };

        var isCharging = (batteryFlag & BatteryFlagCharging) != 0 || source == BatteryPowerSource.Ac && hasBattery && percent >= 0 && percent < 100;
        var isCritical = (batteryFlag & BatteryFlagCritical) != 0;
        var isLow = hasBattery && percent >= 0 && percent <= Math.Clamp(lowThresholdPercent, 1, 100) && !isCharging;

        var minutesRemaining = DeriveMinutes(batteryLifeTimeSeconds, source == BatteryPowerSource.Battery && !isCharging);
        var minutesToFull = DeriveMinutes(batteryFullLifeTimeSeconds, isCharging && percent is >= 0 and < 100);

        return new BatterySnapshot(hasBattery, percent, isCharging, source, isLow, isCritical,
            minutesRemaining, minutesToFull);
    }

    private static int? DeriveMinutes(uint seconds, bool allowed)
    {
        if (!allowed || seconds == UnknownTime || seconds > 60u * 60u * 24u * 30u)
        {
            return null;
        }

        return (int)Math.Round(seconds / 60.0);
    }

    /// <summary>True when either the low or critical flag is set (used for activity prioritization).</summary>
    public static bool IsLowOrCritical(byte batteryFlag)
        => (batteryFlag & (BatteryFlagLow | BatteryFlagCritical)) != 0;

    /// <summary>True when the raw flags indicate "no system battery".</summary>
    public static bool IndicatesNoBattery(byte batteryFlag) => (batteryFlag & BatteryFlagNoBattery) != 0;

    /// <summary>True when the raw flags indicate "high" (not low/critical).</summary>
    public static bool IndicatesHigh(byte batteryFlag) => (batteryFlag & BatteryFlagHigh) != 0;
}

/// <summary>
/// Event-driven battery monitor: <c>GetSystemPowerStatus</c> for the value plus
/// <c>RegisterPowerSettingNotification</c> (AC/DC + percentage) and <c>WM_POWERBROADCAST</c> as the
/// change source. Zero polling.
/// </summary>
public sealed class BatteryMonitor : PlatformMonitor<BatterySnapshot>
{
    private static readonly Guid AcDcPowerSourceGuid = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    private static readonly Guid BatteryPercentageRemainingGuid = new("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");

    private readonly PlatformMessageHost _host;
    private readonly BatteryMonitorOptions _options;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private PlatformWindow? _window;
    private SafePowerNotificationHandle? _acDcNotification;
    private SafePowerNotificationHandle? _percentageNotification;

    public BatteryMonitor(PlatformMessageHost host, IPlatformLogger logger, PlatformCounters counters,
        BatteryMonitorOptions? options = null, TimeProvider? timeProvider = null)
        : base("battery", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? new BatteryMonitorOptions();
    }

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        _window = await _host.CreateWindowAsync("battery", PlatformWindowKind.HiddenTopLevel,
            OnWindowMessage, cancellationToken).ConfigureAwait(false);

        var handle = _window.Handle;
        (_acDcNotification, _percentageNotification) = await _host.InvokeAsync(() =>
        {
            var ac = SafePowerNotificationHandle.Register(handle, AcDcPowerSourceGuid);
            var battery = SafePowerNotificationHandle.Register(handle, BatteryPercentageRemainingGuid);
            return (ac, battery);
        }, cancellationToken).ConfigureAwait(false);

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        var ac = _acDcNotification;
        var percentage = _percentageNotification;
        _acDcNotification = null;
        _percentageNotification = null;

        if (ac is not null || percentage is not null)
        {
            try
            {
                await _host.InvokeAsync(() =>
                {
                    ac?.Dispose();
                    percentage?.Dispose();
                    return true;
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(PlatformLogCategory.Monitor, "battery.unregister-failed",
                    "Failed to unregister power notifications.", ex);
            }
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
        if (message != NativeMethods.WM_POWERBROADCAST)
        {
            return null;
        }

        var powerEvent = (uint)wParam.ToInt64();
        if (powerEvent is NativeMethods.PBT_POWERSETTINGCHANGE or NativeMethods.PBT_APMPOWERSTATUSCHANGE
            or NativeMethods.PBT_APMRESUMEAUTOMATIC or NativeMethods.PBT_APMRESUMESUSPEND)
        {
            _ = RefreshSafeAsync();
        }

        return IntPtr.Zero;
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
            Logger.LogError(PlatformLogCategory.Monitor, "battery.refresh-failed", "Battery refresh failed.", ex);
        }
    }

    /// <summary>Reads the current power status and publishes it if changed (single-flight).</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var snapshot = await Task.Run(ReadSnapshot, cancellationToken).ConfigureAwait(false);
            Publish(snapshot);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private BatterySnapshot ReadSnapshot()
    {
        if (!NativeMethods.Kernel32.GetSystemPowerStatus(out var status))
        {
            Counters.IncrementWin32Failures();
            Logger.LogWarning(PlatformLogCategory.Monitor, "battery.power-status-failed",
                "GetSystemPowerStatus failed. " + Win32Error.Format(Win32Error.LastError));
            return Current ?? new BatterySnapshot(false, -1, false, BatteryPowerSource.Unknown, false, false, null, null);
        }

        return BatteryStatusParser.Parse(status.ACLineStatus, status.BatteryFlag, status.BatteryLifePercent,
            status.BatteryLifeTime, status.BatteryFullLifeTime, _options.LowThresholdPercent);
    }
}
