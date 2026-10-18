using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Events;
using FluidBar.Core.Layout;

namespace FluidBar.Soak;

/// <summary>Shared pipeline plumbing for the scenarios (no UI/Platform dependencies).</summary>
public static class SoakHarness
{
    /// <summary>Fixed virtual recording origin (deterministic across machines).</summary>
    public static DateTimeOffset Origin { get; } = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Fixed monitor used by pipeline/layout measurements.</summary>
    public static MonitorInfo Monitor { get; } =
        MonitorInfo.Create("soak-1920", new PixelRect(0, 0, 1920, 1040), 96, isPrimary: true);

    /// <summary>Computes the layout for the current island state (exercises the full layout path).</summary>
    public static IslandLayoutResult Layout(IslandState state)
        => IslandLayout.Calculate(
            IslandLayoutRequest.FromState(state, IslandMode.Float, IslandAnchor.TopCenter),
            Monitor);

    /// <summary>Creates a fresh deterministic pipeline with metrics counters.</summary>
    public static Pipeline CreatePipeline(DiagnosticCounters? counters = null, int queueCapacity = 256)
    {
        var dispatcher = new ManualDispatcher();
        var hub = new EventHub(new EventBusOptions { Capacity = queueCapacity }, counters: counters, dispatcher: dispatcher);
        var scheduler = new ActivityScheduler(counters: counters);
        var controller = new IslandStateController(
            IslandBehaviorOptions.Default with { AutoHideMs = 0, AutoCollapseMs = 0 });
        return new Pipeline(hub, dispatcher, scheduler, controller);
    }
}

/// <summary>Aggregate holding the pure Core pipeline used by the soak scenarios.</summary>
public sealed class Pipeline : IDisposable
{
    private readonly IDisposable _subscription;
    private DateTimeOffset _now;
    private int _disposed;

    /// <summary>Creates a pipeline and wires the single subscriber (simulated UI consumer).</summary>
    public Pipeline(
        EventHub hub,
        ManualDispatcher dispatcher,
        ActivityScheduler scheduler,
        IslandStateController controller)
    {
        Hub = hub;
        Dispatcher = dispatcher;
        Scheduler = scheduler;
        Controller = controller;
        _now = SoakHarness.Origin;
        _subscription = hub.Subscribe(OnEvent);
    }

    /// <summary>Event hub.</summary>
    public EventHub Hub { get; }

    /// <summary>Manual dispatcher.</summary>
    public ManualDispatcher Dispatcher { get; }

    /// <summary>Activity scheduler.</summary>
    public ActivityScheduler Scheduler { get; }

    /// <summary>Island state controller.</summary>
    public IslandStateController Controller { get; }

    /// <summary>Current scheduler snapshot.</summary>
    public ActivitySchedulerState State { get; set; } = ActivitySchedulerState.Empty;

    /// <summary>Last scheduler update applied by the consumer.</summary>
    public ActivitySchedulerUpdate LastUpdate { get; private set; } = ActivitySchedulerUpdate.None(ActivitySchedulerState.Empty);

    /// <summary>Number of events delivered to the consumer.</summary>
    public int DeliveredCount { get; private set; }

    /// <summary>Pumps the manual dispatcher at <paramref name="now"/> (simulated UI thread).</summary>
    public int Pump(DateTimeOffset now)
    {
        _now = now;
        var before = DeliveredCount;
        Dispatcher.RunAll();
        return DeliveredCount - before;
    }

    /// <summary>Applies TTL expiry at <paramref name="now"/>.</summary>
    public ActivitySchedulerUpdate Tick(DateTimeOffset now)
    {
        var update = Scheduler.Tick(State, now);
        if (update.HasChange)
        {
            State = update.State;
            LastUpdate = update;
            Controller.Apply(IslandInput.ForActivityUpdate(update), now);
        }

        return update;
    }

    /// <summary>Directly applies an event without going through the hub (scenario convenience).</summary>
    public ActivitySchedulerUpdate ApplyDirect(in IslandEvent evt, DateTimeOffset now)
    {
        var update = Scheduler.Update(State, evt, now);
        State = update.State;
        LastUpdate = update;
        Controller.Apply(IslandInput.ForActivityUpdate(update), now);
        return update;
    }

    private void OnEvent(IslandEvent evt)
    {
        var update = Scheduler.Update(State, evt, _now);
        State = update.State;
        LastUpdate = update;
        Controller.Apply(IslandInput.ForActivityUpdate(update), _now);
        DeliveredCount++;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _subscription.Dispose();
        Hub.Dispose();
        Controller.Dispose();
    }
}

/// <summary>Deterministic mixed-event generator for scenarios A and C (single seed).</summary>
public sealed class MixedEventGenerator
{
    private readonly Random _random;
    private readonly Dictionary<string, long> _sequences = new(StringComparer.Ordinal);
    private readonly ActivityKey[] _mediaKeys;
    private readonly ActivityKey[] _notificationKeys;
    private readonly ActivityKey[] _clipboardKeys;
    private readonly ActivityKey[] _agentKeys;
    private readonly string[] _titles =
    [
        "Build finished", "New message", "Track changed", "Caps Lock on", "Battery low",
        "Downloading update", "Agent finished", "Copied", "Volume", "Focus mode",
    ];

    /// <summary>Creates a generator; all randomness comes from <paramref name="seed"/>.</summary>
    public MixedEventGenerator(int seed)
    {
        _random = new Random(seed);
        _mediaKeys =
        [
            new ActivityKey(ActivityKind.Media, "media:gsmtc"),
            new ActivityKey(ActivityKind.Media, "media:spotify"),
            new ActivityKey(ActivityKind.Media, "media:browser"),
        ];
        _notificationKeys =
        [
            new ActivityKey(ActivityKind.Notification, "notif:toast-a"),
            new ActivityKey(ActivityKind.Notification, "notif:toast-b"),
            new ActivityKey(ActivityKind.Notification, "notif:toast-c"),
            new ActivityKey(ActivityKind.Notification, "notif:toast-d"),
            new ActivityKey(ActivityKind.Notification, "notif:toast-e"),
        ];
        _clipboardKeys = [new ActivityKey(ActivityKind.Clipboard, "clipboard:host")];
        _agentKeys = [new ActivityKey(ActivityKind.Agent, "agent:hook")];
    }

    /// <summary>Simulated producer clock in milliseconds since the harness origin.</summary>
    public long NowMs { get; private set; }

    /// <summary>Generates the next event for scenario A (mix chosen so coalescing is exercised).</summary>
    public IslandEvent Next(int index)
    {
        NowMs += 10;
        var roll = index % 100;
        if (roll < 40)
        {
            var key = _mediaKeys[(index / 40) % _mediaKeys.Length];
            var payload = new ActivityPayload
            {
                Title = "Now playing",
                Subtitle = "Soak artist",
                ProgressPercent = (index * 7) % 101,
                IsActive = true,
                ShowsAudioWave = true,
                PositionTicks = (index * 10_000_000L) % 3_000_000_000L,
                EndTicks = 3_000_000_000L,
            };
            return IslandEvent.Incremental(key, payload, NextSequence(key), TimeSpan.FromMilliseconds(NowMs / 10));
        }

        if (roll < 65)
        {
            var key = _notificationKeys[(index / 65) % _notificationKeys.Length];
            var payload = new ActivityPayload
            {
                Title = _titles[index % _titles.Length],
                Body = "notification body " + (index % 17),
            };
            return (index / 25) % 2 == 0
                ? IslandEvent.New(key, payload, NextSequence(key), TimeSpan.FromMilliseconds(NowMs / 10))
                : IslandEvent.Replacement(key, payload, NextSequence(key), TimeSpan.FromMilliseconds(NowMs / 10));
        }

        if (roll < 78)
        {
            var key = _clipboardKeys[0];
            var payload = new ActivityPayload
            {
                Title = "Copied",
                Body = "clipboard text " + (index % 13),
            };
            return IslandEvent.Replacement(key, payload, NextSequence(key), TimeSpan.FromMilliseconds(NowMs / 10));
        }

        if (roll < 88)
        {
            var key = new ActivityKey(ActivityKind.Volume, "system:volume");
            var payload = new ActivityPayload { Title = "Volume", Level = (index * 3) % 101, IsActive = true };
            return IslandEvent.Incremental(key, payload, NextSequence(key), TimeSpan.FromMilliseconds(NowMs / 10));
        }

        if (roll < 92)
        {
            var key = new ActivityKey(ActivityKind.BatteryLow, "system:battery");
            var payload = new ActivityPayload { Title = "Battery low", Level = 12, IsActive = false };
            return IslandEvent.New(key, payload, NextSequence(key), TimeSpan.FromMilliseconds(NowMs / 10));
        }

        if (roll < 96)
        {
            var key = new ActivityKey(ActivityKind.LockKeys, "system:lockkeys");
            var payload = new ActivityPayload { Title = "Caps Lock on" };
            return IslandEvent.New(key, payload, NextSequence(key), TimeSpan.FromMilliseconds(NowMs / 10));
        }

        var retractKey = _notificationKeys[index % _notificationKeys.Length];
        return IslandEvent.Retract(retractKey, NextSequence(retractKey), TimeSpan.FromMilliseconds(NowMs / 10));
    }

    /// <summary>Generates a churn event (scenario C) biased toward persistent/transient gaps.</summary>
    public IslandEvent NextChurn(int index, IReadOnlyList<ActivityKey> liveKeys)
    {
        NowMs += 50;
        var roll = _random.Next(100);
        if (roll < 35 || liveKeys.Count == 0)
        {
            var key = NextNewKey(index);
            return IslandEvent.New(key, PayloadFor(key.Kind, index), NextSequence(key), TimeSpan.FromMilliseconds(NowMs / 10));
        }

        var live = liveKeys[_random.Next(liveKeys.Count)];
        if (roll < 55)
        {
            return IslandEvent.Replacement(live, PayloadFor(live.Kind, index), NextSequence(live), TimeSpan.FromMilliseconds(NowMs / 10));
        }

        if (roll < 75)
        {
            return IslandEvent.Incremental(live, PayloadFor(live.Kind, index), NextSequence(live), TimeSpan.FromMilliseconds(NowMs / 10));
        }

        // Retract only long-lived kinds so transient activities survive to their TTL and exercise
        // the expiry path; fall back to any live key when none are retractable.
        var retractable = liveKeys
            .Where(key => key.Kind is ActivityKind.Media or ActivityKind.Device or ActivityKind.Bluetooth
                or ActivityKind.Focus or ActivityKind.Progress or ActivityKind.Timer)
            .ToList();
        var target = retractable.Count > 0
            ? retractable[_random.Next(retractable.Count)]
            : live;
        return IslandEvent.Retract(target, NextSequence(target), TimeSpan.FromMilliseconds(NowMs / 10));
    }

    private long NextSequence(ActivityKey key)
    {
        var next = _sequences.TryGetValue(key.ToString(), out var current) ? current + 1 : 1;
        _sequences[key.ToString()] = next;
        return next;
    }

    private ActivityKey NextNewKey(int index)
    {
        return ((index / 35) % 12) switch
        {
            0 => new ActivityKey(ActivityKind.Media, "media:gsmtc"),
            1 => new ActivityKey(ActivityKind.Media, "media:spotify"),
            2 => new ActivityKey(ActivityKind.Notification, "notif:a"),
            3 => new ActivityKey(ActivityKind.Notification, "notif:b"),
            4 => new ActivityKey(ActivityKind.Clipboard, "clipboard:host"),
            5 => new ActivityKey(ActivityKind.Agent, "agent:hook"),
            6 => new ActivityKey(ActivityKind.Progress, "system:progress"),
            7 => new ActivityKey(ActivityKind.Timer, "system:timer"),
            8 => new ActivityKey(ActivityKind.BatteryLow, "system:battery"),
            9 => new ActivityKey(ActivityKind.Focus, "system:focus"),
            10 => new ActivityKey(ActivityKind.Device, "system:usb"),
            _ => new ActivityKey(ActivityKind.Bluetooth, "system:bt"),
        };
    }

    private ActivityPayload PayloadFor(ActivityKind kind, int index) => kind switch
    {
        ActivityKind.Media => new ActivityPayload
        {
            Title = "Now playing " + (index % 5),
            Subtitle = "Soak artist",
            ProgressPercent = (index * 3) % 101,
            IsActive = true,
            ShowsAudioWave = true,
        },
        ActivityKind.Notification => new ActivityPayload { Title = _titles[index % _titles.Length], Body = "body " + (index % 11) },
        ActivityKind.Clipboard => new ActivityPayload { Title = "Copied", Body = "clip " + (index % 7) },
        ActivityKind.Agent => new ActivityPayload { Title = "Agent finished", IsActive = index % 2 == 0 },
        ActivityKind.Progress => new ActivityPayload { Title = "Downloading", ProgressPercent = (index * 5) % 101 },
        ActivityKind.Timer => new ActivityPayload { Title = "Pomodoro", Level = (index * 2) % 101 },
        ActivityKind.BatteryLow => new ActivityPayload { Title = "Battery low", Level = 12 },
        ActivityKind.Focus => new ActivityPayload { Title = "Focus mode" },
        ActivityKind.Device => new ActivityPayload { Title = "USB device" },
        ActivityKind.Bluetooth => new ActivityPayload { Title = "Headphones" },
        ActivityKind.LockKeys => new ActivityPayload { Title = "Caps Lock on" },
        ActivityKind.Volume => new ActivityPayload { Title = "Volume", Level = (index * 3) % 101, IsActive = true },
        _ => new ActivityPayload { Title = "Activity" },
    };
}
