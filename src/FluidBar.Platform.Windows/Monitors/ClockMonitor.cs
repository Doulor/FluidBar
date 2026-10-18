using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Monitors;

/// <summary>Immutable clock snapshot (local time truncated to the minute).</summary>
public sealed record ClockSnapshot(DateTimeOffset LocalTime)
{
    /// <summary>Local date.</summary>
    public DateOnly Date => DateOnly.FromDateTime(LocalTime.LocalDateTime);

    /// <summary>Local time of day (seconds and below are zero).</summary>
    public TimeOnly Time => TimeOnly.FromDateTime(LocalTime.LocalDateTime);

    /// <summary>Minutes since local midnight (stable for change detection).</summary>
    public int MinutesSinceMidnight => (LocalTime.LocalDateTime.Hour * 60) + LocalTime.LocalDateTime.Minute;
}

/// <summary>Pure minute-alignment math for the clock monitor (no polling).</summary>
public static class ClockAlignment
{
    /// <summary>Truncates a timestamp to the start of its local minute.</summary>
    public static DateTimeOffset TruncateToMinute(DateTimeOffset value)
    {
        var local = value.ToLocalTime().DateTime;
        var truncated = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, local.Kind);
        return new DateTimeOffset(truncated, TimeZoneInfo.Local.GetUtcOffset(truncated));
    }

    /// <summary>Delay until the next local minute boundary; always in (0, 60] seconds.</summary>
    public static TimeSpan DelayUntilNextMinute(DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        var elapsedInMinute = TimeSpan.FromMilliseconds((local.Second * 1000) + local.Millisecond);
        var remaining = TimeSpan.FromMinutes(1) - elapsedInMinute;
        return remaining <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : remaining;
    }
}

/// <summary>
/// Clock monitor: one aligned timer per minute boundary instead of a 10 s poll. Publishes only when
/// the minute (or the date) changes, so the UI wakes up once per minute at most.
/// </summary>
public sealed class ClockMonitor : PlatformMonitor<ClockSnapshot>
{
    private readonly TimeProvider _timeProvider;
    private ITimer? _timer;
    private int _started;

    public ClockMonitor(IPlatformLogger logger, PlatformCounters counters, TimeProvider? timeProvider = null)
        : base("clock", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _started, 1);
        PublishCurrent();
        ScheduleNextBoundary();
        return ValueTask.CompletedTask;
    }

    private void ScheduleNextBoundary()
    {
        if (Volatile.Read(ref _started) == 0)
        {
            return;
        }

        var now = _timeProvider.GetLocalNow();
        var delay = ClockAlignment.DelayUntilNextMinute(now);
        _timer?.Dispose();
        _timer = _timeProvider.CreateTimer(static state =>
        {
            var self = (ClockMonitor)state!;
            self.PublishCurrent();
            self.ScheduleNextBoundary();
        }, this, delay, Timeout.InfiniteTimeSpan);
    }

    private void PublishCurrent()
    {
        try
        {
            var snapshot = new ClockSnapshot(ClockAlignment.TruncateToMinute(_timeProvider.GetLocalNow()));
            Publish(snapshot);
        }
        catch (Exception ex)
        {
            Counters.IncrementMonitorCallbackFailures();
            Logger.LogError(PlatformLogCategory.Monitor, "clock.publish-failed", "Clock tick failed.", ex);
        }
    }

    protected override ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _started, 0);
        var timer = _timer;
        _timer = null;
        timer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
