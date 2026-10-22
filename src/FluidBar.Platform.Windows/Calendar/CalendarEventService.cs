using System.Collections.Immutable;
using FluidBar.Platform.Windows.Diagnostics;
using Windows.ApplicationModel.Appointments;

namespace FluidBar.Platform.Windows.Calendar;

/// <summary>
/// Read-only Windows calendar event counts for the widgets month grid. The AppointmentStore is
/// best-effort: unpackaged apps may not have access, in which case the service stays silent (no dots)
/// instead of inventing events or crashing the island.
/// </summary>
public sealed class CalendarEventService : IAsyncDisposable
{
    private readonly IPlatformLogger _logger;
    private readonly TimeProvider _time;
    private readonly Func<CancellationToken, Task<IReadOnlyList<DateTimeOffset>>> _readStarts;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private ITimer? _timer;
    private int _started;
    private int _disposed;
    private ImmutableDictionary<DateOnly, int> _counts = ImmutableDictionary<DateOnly, int>.Empty;

    /// <summary>Creates the service; call <see cref="StartAsync"/> once.</summary>
    public CalendarEventService(
        IPlatformLogger logger,
        TimeProvider? timeProvider = null,
        Func<CancellationToken, Task<IReadOnlyList<DateTimeOffset>>>? readStarts = null)
    {
        _logger = logger ?? NullPlatformLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;
        _readStarts = readStarts ?? ReadWindowsStartsAsync;
        _lifetimeToken = _lifetime.Token;
    }

    /// <summary>Event counts by local calendar day; empty when the store is unavailable.</summary>
    public IReadOnlyDictionary<DateOnly, int> Counts => Volatile.Read(ref _counts);

    /// <summary>Raised after a refresh changed the counts.</summary>
    public event EventHandler? Changed;

    /// <summary>Starts the initial refresh and a 15-minute refresh timer.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        _timer = _time.CreateTimer(
            static state => ((CalendarEventService)state!).RefreshInBackground(),
            this,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(15));
        return Task.CompletedTask;
    }

    private void RefreshInBackground() => _ = RefreshAsync(_lifetimeToken);

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var starts = await _readStarts(cancellationToken);
            var next = CountByDay(starts);
            if (!CountsEqual(next, _counts))
            {
                Volatile.Write(ref _counts, next);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception)
        {
            // AppointmentStore.GetDefaultAsync is commonly denied for unpackaged processes. Clear
            // previously published dots so a revoked/unavailable permission cannot leave stale
            // calendar counts on the month grid.
            if (_counts.Count > 0)
            {
                Volatile.Write(ref _counts, ImmutableDictionary<DateOnly, int>.Empty);
                Changed?.Invoke(this, EventArgs.Empty);
            }

            _logger.LogDebug(PlatformLogCategory.Monitor, "widgets.calendar", "Calendar events unavailable.");
        }
    }

    internal static bool CountsEqual(
        IReadOnlyDictionary<DateOnly, int> left,
        IReadOnlyDictionary<DateOnly, int> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || value != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    internal Task RefreshForTestAsync() => RefreshAsync(_lifetimeToken);

    private static async Task<IReadOnlyList<DateTimeOffset>> ReadWindowsStartsAsync(CancellationToken cancellationToken)
    {
        var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
        if (store is null)
        {
            return [];
        }

        var start = new DateTimeOffset(DateTime.Today);
        var appointments = await store.FindAppointmentsAsync(start, TimeSpan.FromDays(62));
        return appointments.Select(appointment => appointment.StartTime).ToArray();
    }

    /// <summary>Pure date bucketing used by the refresh loop and unit tests.</summary>
    public static ImmutableDictionary<DateOnly, int> CountByDay(IEnumerable<DateTimeOffset> starts)
    {
        ArgumentNullException.ThrowIfNull(starts);
        var counts = new Dictionary<DateOnly, int>();
        foreach (var start in starts)
        {
            var day = DateOnly.FromDateTime(start.LocalDateTime);
            counts[day] = counts.TryGetValue(day, out var existing) ? existing + 1 : 1;
        }

        return counts.ToImmutableDictionary();
    }

    /// <summary>Stops the refresh timer and cancels an in-flight read.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timer?.Dispose();
        _timer = null;
        _lifetime.Cancel();
        _lifetime.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
