using System.Collections.Concurrent;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Tests;

/// <summary>Logger that records entries for assertions (never throws).</summary>
public sealed class CapturingLogger : IPlatformLogger
{
    private readonly ConcurrentQueue<PlatformLogEntry> _entries = new();

    public IReadOnlyCollection<PlatformLogEntry> Entries => _entries.ToArray();

    public bool IsEnabled(PlatformLogLevel level) => true;

    public void Log(in PlatformLogEntry entry) => _entries.Enqueue(entry);

    public int CountOf(PlatformLogLevel level) => _entries.Count(entry => entry.Level == level);
}

/// <summary>Minimal <see cref="TimeProvider"/> with manually advanced time and one-shot timers.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset start) => _utcNow = start;

    public ManualTimeProvider() : this(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _utcNow.UtcTicks;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        Register(timer);
        return timer;
    }

    /// <summary>Advances time, firing every timer that becomes due (in due order).</summary>
    public void Advance(TimeSpan delta)
    {
        lock (_gate)
        {
            var target = _utcNow + delta;
            while (true)
            {
                var due = _timers.Where(timer => timer.Enabled && timer.DueAt <= target)
                    .OrderBy(timer => timer.DueAt)
                    .FirstOrDefault();
                if (due is null)
                {
                    break;
                }

                _utcNow = due.DueAt;
                due.Fire();
            }

            _utcNow = target;
        }
    }

    public void Advance(TimeSpan delta, int times)
    {
        for (var index = 0; index < times; index++)
        {
            Advance(delta);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private bool _disposed;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public DateTimeOffset DueAt { get; private set; }

        public TimeSpan Period { get; private set; }

        public bool Enabled { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
            {
                return false;
            }

            Enabled = dueTime != Timeout.InfiniteTimeSpan;
            DueAt = dueTime == Timeout.InfiniteTimeSpan
                ? DateTimeOffset.MaxValue
                : _owner.GetUtcNow() + dueTime;
            Period = period;
            return true;
        }

        public void Fire()
        {
            if (!Enabled)
            {
                return;
            }

            _owner.GetUtcNow();
            _callback(_state);
            if (Period == Timeout.InfiniteTimeSpan || !Enabled)
            {
                Enabled = false;
            }
            else
            {
                DueAt = _owner.GetUtcNow() + Period;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Enabled = false;
            lock (_owner._gate)
            {
                _owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private void Register(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Add(timer);
        }
    }
}

/// <summary>In-memory registry store for startup-registration tests.</summary>
public sealed class FakeRegistryValueStore : FluidBar.Platform.Windows.Shell.IRegistryValueStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public int SetCalls { get; private set; }

    public int DeleteCalls { get; private set; }

    public bool ThrowOnSet { get; set; }

    public string? GetString(string subKeyPath, string valueName)
        => _keys.TryGetValue(subKeyPath, out var values) && values.TryGetValue(valueName, out var value)
            ? value
            : null;

    public void SetString(string subKeyPath, string valueName, string value)
    {
        if (ThrowOnSet)
        {
            throw new InvalidOperationException("Registry write failed (simulated).");
        }

        SetCalls++;
        if (!_keys.TryGetValue(subKeyPath, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _keys[subKeyPath] = values;
        }

        values[valueName] = value;
    }

    public bool DeleteValue(string subKeyPath, string valueName)
    {
        DeleteCalls++;
        return _keys.TryGetValue(subKeyPath, out var values) && values.Remove(valueName);
    }
}

/// <summary>Waits for a condition without busy-looping; fails the test on timeout.</summary>
public static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }
}
