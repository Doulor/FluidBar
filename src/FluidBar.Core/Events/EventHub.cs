using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluidBar.Core.Diagnostics;

namespace FluidBar.Core.Events;

/// <summary>
/// Bounded, coalescing event bus contract (spec 5.3). Producers call
/// <see cref="TryPublish"/> from any thread; a single consumer (UI thread) drains through
/// <c>EventHub.ReadAllAsync</c> or push subscriptions.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Non-blocking publish. Returns <see langword="false"/> only when the bus is disposed.
    /// Full queues drop the oldest event and increment the dropped counter.
    /// </summary>
    bool TryPublish(in IslandEvent evt);

    /// <summary>
    /// Registers a push subscriber. Handlers run on the drain thread (dispatcher when injected)
    /// and must be fast; exceptions are isolated and counted.
    /// </summary>
    IDisposable Subscribe(Action<IslandEvent> handler);

    /// <summary>Current queue metrics snapshot.</summary>
    EventBusMetrics Metrics { get; }
}

/// <summary>Event bus configuration.</summary>
public sealed record EventBusOptions
{
    /// <summary>Shared defaults.</summary>
    public static EventBusOptions Default { get; } = new();

    /// <summary>Bounded main queue capacity (spec 5.3: 256).</summary>
    public int Capacity { get; init; } = 256;

    /// <summary>Per-reader bounded channel capacity for <see cref="EventHub.ReadAllAsync"/>.</summary>
    public int ReaderCapacity { get; init; } = 64;

    /// <summary>Merge same-key <see cref="IslandEventKind.Incremental"/> events (latest wins).</summary>
    public bool CoalesceIncremental { get; init; } = true;
}

/// <summary>Point-in-time event bus metrics; feeds the diagnostics counters.</summary>
public readonly record struct EventBusMetrics(
    int QueueDepth,
    int SubscriberCount,
    int ReaderCount,
    long Published,
    long Coalesced,
    long Dropped,
    long Delivered);

/// <summary>
/// Producer-side event source with explicit lifecycle (spec 5.3). Implementations own their
/// native subscriptions and must be idempotent on start/stop.
/// </summary>
public interface IEventSource
{
    /// <summary>Stable source identity used in logs/coalescing (e.g. <c>gsmtc</c>, <c>clipboard</c>).</summary>
    string SourceId { get; }

    /// <summary>Starts producing events. Must be idempotent.</summary>
    void Start();

    /// <summary>Stops producing events; safe to call when not started.</summary>
    void Stop();
}

/// <summary>
/// Owns the lifecycle of a set of <see cref="IEventSource"/> instances and isolates failures:
/// one source that throws on start/stop does not prevent the others from running.
/// </summary>
public sealed class EventSourceHost : IDisposable
{
    private readonly object _gate = new();
    private readonly ImmutableArray<IEventSource> _sources;
    private readonly IAppLogger _logger;
    private readonly DiagnosticCounters? _counters;
    private bool _started;
    private bool _disposed;

    /// <summary>Creates a host for the supplied sources.</summary>
    /// <param name="sources">Producer sources to own.</param>
    /// <param name="logger">Optional structured logger.</param>
    /// <param name="counters">Optional diagnostics counters.</param>
    public EventSourceHost(IEnumerable<IEventSource> sources, IAppLogger? logger = null, DiagnosticCounters? counters = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = [.. sources];
        _logger = logger ?? NullAppLogger.Instance;
        _counters = counters;
    }

    /// <summary>Starts every source; failures are logged and isolated.</summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _started = true;
            foreach (var source in _sources)
            {
                try
                {
                    source.Start();
                }
                catch (Exception ex)
                {
                    _counters?.Increment(CounterKind.EventSubscriberFaults);
                    _logger.Error(LogCategories.Platform, "eventsource.start.failed",
                        $"Event source '{source.SourceId}' failed to start.", ex);
                }
            }
        }
    }

    /// <summary>Stops every source; failures are logged and isolated.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            foreach (var source in _sources)
            {
                try
                {
                    source.Stop();
                }
                catch (Exception ex)
                {
                    _counters?.Increment(CounterKind.EventSubscriberFaults);
                    _logger.Error(LogCategories.Platform, "eventsource.stop.failed",
                        $"Event source '{source.SourceId}' failed to stop.", ex);
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Bounded, coalescing event hub (spec 5.3).
/// <list type="bullet">
/// <item>Queue is a bounded FIFO (default 256, drop-oldest).</item>
/// <item>Same-key <see cref="IslandEventKind.Incremental"/> events are merged in place; older
/// sequences are discarded, so progress/lyrics events never queue up.</item>
/// <item>Marshaling is delegated to the injected <see cref="IDispatcher"/>; without one the hub
/// drains inline on the publishing thread (deterministic in tests).</item>
/// <item>Subscribers are isolated: one throwing handler cannot break producers.</item>
/// </list>
/// </summary>
public sealed class EventHub : IEventBus, IDisposable
{
    private readonly object _gate = new();
    private readonly EventBusOptions _options;
    private readonly IAppLogger _logger;
    private readonly DiagnosticCounters? _counters;
    private readonly IDispatcher? _dispatcher;
    private readonly LinkedList<IslandEvent> _queue;
    private readonly List<Subscription> _subscriptions = [];
    private readonly List<ReaderRegistration> _readers = [];
    private bool _drainScheduled;
    private bool _isDraining;
    private bool _disposed;
    private long _published;
    private long _coalesced;
    private long _dropped;
    private long _delivered;

    /// <summary>Creates an event hub.</summary>
    /// <param name="options">Queue/coalescing options.</param>
    /// <param name="logger">Optional structured logger.</param>
    /// <param name="counters">Optional diagnostics counters.</param>
    /// <param name="dispatcher">Optional dispatcher; when provided, delivery is marshaled to it.</param>
    public EventHub(
        EventBusOptions? options = null,
        IAppLogger? logger = null,
        DiagnosticCounters? counters = null,
        IDispatcher? dispatcher = null)
    {
        _options = options ?? EventBusOptions.Default;
        _logger = logger ?? NullAppLogger.Instance;
        _counters = counters;
        _dispatcher = dispatcher;
        _queue = new LinkedList<IslandEvent>();
    }

    /// <inheritdoc />
    public EventBusMetrics Metrics
    {
        get
        {
            lock (_gate)
            {
                return new EventBusMetrics(
                    _queue.Count,
                    _subscriptions.Count,
                    _readers.Count,
                    _published,
                    _coalesced,
                    _dropped,
                    _delivered);
            }
        }
    }

    /// <summary>Whether un-drained events are pending.</summary>
    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count > 0;
            }
        }
    }

    /// <inheritdoc />
    public bool TryPublish(in IslandEvent evt)
    {
        var schedule = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            _published++;
            _counters?.Increment(CounterKind.EventsPublished);
            EnqueueLocked(evt);

            if (_dispatcher is not null && !_dispatcher.IsOnDispatcherThread)
            {
                // Only one drain is scheduled at a time; the drain loop empties the queue.
                if (!_drainScheduled)
                {
                    _drainScheduled = true;
                    schedule = true;
                }
            }
        }

        if (schedule)
        {
            _dispatcher!.Post(DrainFromDispatcher);
        }
        else if (_dispatcher is null || _dispatcher.IsOnDispatcherThread)
        {
            Drain();
        }

        return true;
    }

    /// <summary>
    /// Drains all pending events to subscribers and readers. Public for deterministic host/test
    /// pumping; safe to call from any thread. Events published reentrantly during delivery are
    /// picked up by the same drain loop; nested drains are no-ops.
    /// </summary>
    /// <returns>Number of events delivered to at least one subscriber/reader.</returns>
    public int Drain()
    {
        lock (_gate)
        {
            if (_disposed || _isDraining)
            {
                return 0;
            }

            _isDraining = true;
        }

        var delivered = 0;
        try
        {
            while (true)
            {
                IslandEvent[] batch;
                Subscription[] subscriptions;
                ReaderRegistration[] readers;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        break;
                    }

                    batch = [.. _queue];
                    _queue.Clear();
                    subscriptions = [.. _subscriptions];
                    readers = [.. _readers];
                }

                foreach (var evt in batch)
                {
                    var wasDelivered = false;
                    foreach (var subscription in subscriptions)
                    {
                        try
                        {
                            subscription.Handler(evt);
                            wasDelivered = true;
                        }
                        catch (Exception ex)
                        {
                            _counters?.Increment(CounterKind.EventSubscriberFaults);
                            _logger.Error(LogCategories.Activity, "eventbus.subscriber.fault",
                                "An event subscriber threw; the hub isolated the failure.", ex,
                                new Dictionary<string, object?> { ["event"] = evt.ToString() });
                        }
                    }

                    foreach (var reader in readers)
                    {
                        if (reader.Channel.Writer.TryWrite(evt))
                        {
                            wasDelivered = true;
                        }
                        else
                        {
                            _counters?.Increment(CounterKind.EventsDropped);
                            _logger.Warning(LogCategories.Activity, "eventbus.reader.backpressure",
                                "An event reader fell behind; the oldest pending event was dropped.",
                                data: new Dictionary<string, object?> { ["reader"] = reader.Name });
                        }
                    }

                    if (wasDelivered)
                    {
                        delivered++;
                    }
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _isDraining = false;
                _delivered += delivered;
                if (delivered > 0)
                {
                    _counters?.Add(CounterKind.EventsDelivered, delivered);
                }
            }
        }

        return delivered;
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action<IslandEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(this, handler);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    /// <summary>
    /// Pull-based delivery for the single UI consumer. The reader owns a bounded channel
    /// (drop-oldest) so a stalled consumer cannot grow memory without bound.
    /// </summary>
    /// <param name="readerName">Diagnostics name for this reader.</param>
    /// <param name="cancellationToken">Cancellation token; completes the enumeration.</param>
    public async IAsyncEnumerable<IslandEvent> ReadAllAsync(
        string readerName = "ui",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var registration = new ReaderRegistration(
            readerName,
            Channel.CreateBounded<IslandEvent>(new BoundedChannelOptions(Math.Max(1, _options.ReaderCapacity))
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            }));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _readers.Add(registration);
        }

        try
        {
            await foreach (var evt in registration.Channel.Reader
                               .ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            lock (_gate)
            {
                _readers.Remove(registration);
            }

            registration.Channel.Writer.TryComplete();
        }
    }

    /// <summary>Requests a drain, marshaling through the dispatcher when one is configured.</summary>
    public void Flush()
    {
        if (_dispatcher is not null && !_dispatcher.IsOnDispatcherThread)
        {
            _dispatcher.Post(() => Drain());
            return;
        }

        Drain();
    }

    private void DrainFromDispatcher()
    {
        lock (_gate)
        {
            _drainScheduled = false;
        }

        Drain();
    }

    private void EnqueueLocked(in IslandEvent evt)
    {
        if (_options.CoalesceIncremental && evt.Kind == IslandEventKind.Incremental)
        {
            var node = _queue.First;
            while (node is not null)
            {
                var candidate = node.Value;
                if (candidate.Kind == IslandEventKind.Incremental && candidate.Key.Equals(evt.Key))
                {
                    if (candidate.Sequence <= evt.Sequence)
                    {
                        node.Value = evt;
                        _coalesced++;
                        _counters?.Increment(CounterKind.EventsCoalesced);
                    }
                    else
                    {
                        _dropped++;
                        _counters?.Increment(CounterKind.EventsDropped);
                    }

                    return;
                }

                node = node.Next;
            }
        }

        _queue.AddLast(evt);
        var capacity = Math.Max(1, _options.Capacity);
        while (_queue.Count > capacity)
        {
            _queue.RemoveFirst();
            _dropped++;
            _counters?.Increment(CounterKind.EventsDropped);
        }
    }

    private void Unsubscribe(Subscription subscription) => Unsubscribe(subscription, removeImmediately: true);

    private void Unsubscribe(Subscription subscription, bool removeImmediately)
    {
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<Subscription> subscriptions;
        List<ReaderRegistration> readers;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.Clear();
            subscriptions = [.. _subscriptions];
            readers = [.. _readers];
            _subscriptions.Clear();
            _readers.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }

        foreach (var reader in readers)
        {
            reader.Channel.Writer.TryComplete();
        }
    }

    private sealed class Subscription(EventHub owner, Action<IslandEvent> handler) : IDisposable
    {
        private bool _disposed;

        public Action<IslandEvent> Handler { get; } = handler;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.Unsubscribe(this);
        }
    }

    private sealed record ReaderRegistration(string Name, Channel<IslandEvent> Channel);
}
