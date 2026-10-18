using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Threading;

/// <summary>Event payload for a published immutable snapshot.</summary>
public sealed class SnapshotEventArgs<T> : EventArgs
{
    public SnapshotEventArgs(T snapshot) => Snapshot = snapshot;

    /// <summary>The immutable snapshot that changed. Never mutated after publication.</summary>
    public T Snapshot { get; }
}

/// <summary>
/// Change-only, coalescing publisher for immutable snapshots.
/// </summary>
/// <remarks>
/// <para>Producers (COM callbacks, WinRT callbacks, WMI, thread-pool timers) call <see cref="Publish"/>.
/// Duplicate values are dropped; bursts are coalesced so at most one dispatch of the latest value is
/// in flight at any time. Subscribers are invoked on a thread-pool thread, never on a producer thread
/// and never on the UI thread.</para>
/// <para>Single subscriber exception cannot break the pipeline: it is logged and counted.</para>
/// </remarks>
public sealed class SnapshotPublisher<TSnapshot> : IDisposable
    where TSnapshot : class, IEquatable<TSnapshot>
{
    private readonly object _gate = new();
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly string _owner;
    private TSnapshot? _current;
    private TSnapshot? _pending;
    private bool _dispatchScheduled;
    private int _disposed;

    public SnapshotPublisher(string owner, IPlatformLogger logger, PlatformCounters counters)
    {
        _owner = owner;
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
    }

    /// <summary>Raised on a thread-pool thread for every (coalesced) change.</summary>
    public event EventHandler<SnapshotEventArgs<TSnapshot>>? Changed;

    /// <summary>Last published value (thread-safe).</summary>
    public TSnapshot? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Publishes a snapshot. Returns <c>false</c> when the value equals the last published one.
    /// </summary>
    public bool Publish(TSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            if (snapshot.Equals(_current))
            {
                _counters.IncrementSnapshotsCoalesced();
                return false;
            }

            _pending = snapshot;
            if (_dispatchScheduled)
            {
                _counters.IncrementSnapshotsCoalesced();
                return true;
            }

            _dispatchScheduled = true;
        }

        ThreadPool.QueueUserWorkItem(static state => ((SnapshotPublisher<TSnapshot>)state!).Dispatch(), this);
        return true;
    }

    private void Dispatch()
    {
        while (true)
        {
            TSnapshot snapshot;
            lock (_gate)
            {
                if (_pending is null)
                {
                    _dispatchScheduled = false;
                    return;
                }

                snapshot = _pending;
                _pending = null;
                _current = snapshot;
            }

            _counters.IncrementSnapshotsPublished();
            try
            {
                Changed?.Invoke(this, new SnapshotEventArgs<TSnapshot>(snapshot));
            }
            catch (Exception ex)
            {
                _counters.IncrementSnapshotDispatchFailures();
                _logger.LogError(PlatformLogCategory.Platform, "snapshot.subscriber-failed",
                    $"A subscriber of '{_owner}' threw while handling a snapshot.", ex);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_gate)
        {
            _pending = null;
            Changed = null;
        }
    }
}
