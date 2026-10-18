using FluidBar.Core.Activities;
using FluidBar.Core.Events;
using FluidBar.Core.Presentation;
using SdkEvent = FluidBar.Sdk.IslandEvent;

namespace FluidBar.Integration.Tests.TestSupport;

/// <summary>Manual dispatcher for deterministic pumping of the Core event hub.</summary>
public sealed class ManualTestDispatcher : FluidBar.Core.IDispatcher
{
    private readonly Queue<Action> _pending = new();

    public bool IsOnDispatcherThread { get; private set; }

    public int PendingCount
    {
        get
        {
            lock (_pending)
            {
                return _pending.Count;
            }
        }
    }

    public void Post(Action action)
    {
        lock (_pending)
        {
            _pending.Enqueue(action);
        }
    }

    public int RunAll()
    {
        var ran = 0;
        IsOnDispatcherThread = true;
        try
        {
            while (true)
            {
                Action action;
                lock (_pending)
                {
                    if (_pending.Count == 0)
                    {
                        break;
                    }

                    action = _pending.Dequeue();
                }

                action();
                ran++;
            }
        }
        finally
        {
            IsOnDispatcherThread = false;
        }

        return ran;
    }
}

/// <summary>
/// The App's UI-thread pipeline under test: SDK events → SdkCoreEventAdapter → EventHub →
/// ActivityScheduler → IslandStateMachine → ActivityPresenter, with an injected virtual clock.
/// </summary>
public sealed class CorePipelineHarness : IDisposable
{
    private readonly EventHub _hub;
    private readonly ManualTestDispatcher _dispatcher;
    private readonly ActivityScheduler _scheduler;
    private readonly IslandStateController _controller;
    private readonly IDisposable _subscription;
    private readonly SdkCoreEventAdapter _adapter = new();
    private readonly List<ActivitySchedulerUpdate> _updates = [];

    public CorePipelineHarness(ActivityStackPolicy? stackPolicy = null, EventBusOptions? busOptions = null)
    {
        Time = TestTimeProvider.StartingAtEpoch();
        _dispatcher = new ManualTestDispatcher();
        _hub = new EventHub(busOptions, dispatcher: _dispatcher);
        _scheduler = new ActivityScheduler(stackPolicy);
        _controller = new IslandStateController(IslandBehaviorOptions.Default with
        {
            AutoHideMs = 0,
            AutoCollapseMs = 0,
            HoverReveal = false,
        });
        _subscription = _hub.Subscribe(OnEvent);
    }

    /// <summary>Virtual clock fed to the scheduler.</summary>
    public TestTimeProvider Time { get; }

    /// <summary>The adapter instance (sequence tracking per key).</summary>
    public SdkCoreEventAdapter Adapter => _adapter;

    /// <summary>Current scheduler snapshot.</summary>
    public ActivitySchedulerState SchedulerState { get; private set; } = ActivitySchedulerState.Empty;

    /// <summary>Current island state.</summary>
    public IslandState IslandState => _controller.Current;

    /// <summary>All scheduler updates observed (ordered).</summary>
    public IReadOnlyList<ActivitySchedulerUpdate> Updates => _updates;

    /// <summary>Last scheduler update.</summary>
    public ActivitySchedulerUpdate LastUpdate => _updates.Count == 0
        ? ActivitySchedulerUpdate.None(ActivitySchedulerState.Empty)
        : _updates[^1];

    /// <summary>Current presentation view model (what the UI renders).</summary>
    public IslandViewModel View => ActivityPresenter.PresentIsland(SchedulerState, _controller.Current);

    /// <summary>Hub metrics (published/coalesced/dropped/delivered).</summary>
    public EventBusMetrics Metrics => _hub.Metrics;

    /// <summary>Publishes one SDK event and drains the hub (realistic single UI-thread step).</summary>
    public void Publish(SdkEvent islandEvent)
    {
        _hub.TryPublish(_adapter.Adapt(islandEvent));
        Drain();
    }

    /// <summary>Publishes a batch of SDK events and drains once (exercises hub coalescing).</summary>
    public void PublishBatch(IEnumerable<SdkEvent> events)
    {
        foreach (var islandEvent in events)
        {
            _hub.TryPublish(_adapter.Adapt(islandEvent));
        }

        Drain();
    }

    /// <summary>Publishes pre-adapted Core events and drains once.</summary>
    public void PublishCoreBatch(IEnumerable<IslandEvent> events)
    {
        foreach (var coreEvent in events)
        {
            _hub.TryPublish(coreEvent);
        }

        Drain();
    }

    /// <summary>Advances the virtual clock and applies TTL expiry (host timer tick).</summary>
    public ActivitySchedulerUpdate Advance(TimeSpan delta)
    {
        Time.Advance(delta);
        var update = _scheduler.Tick(SchedulerState, Time.UtcNow);
        if (update.HasChange)
        {
            SchedulerState = update.State;
            _controller.Apply(IslandInput.ForActivityUpdate(update), Time.UtcNow);
            _updates.Add(update);
        }

        return update;
    }

    private void Drain()
    {
        _dispatcher.RunAll();
    }

    private void OnEvent(IslandEvent coreEvent)
    {
        var update = _scheduler.Update(SchedulerState, coreEvent, Time.UtcNow);
        SchedulerState = update.State;
        _controller.Apply(IslandInput.ForActivityUpdate(update), Time.UtcNow);
        _updates.Add(update);
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _hub.Dispose();
        _controller.Dispose();
    }
}
