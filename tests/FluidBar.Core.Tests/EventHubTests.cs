using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Events;
using FluidBar.Core.Tests.TestSupport;

namespace FluidBar.Core.Tests;

public class EventHubTests
{
    private static IslandEvent Notification(string source, long sequence)
        => TestData.NewEvent(ActivityKind.Notification, source, "n", sequence);

    [Fact]
    public void Publish_DeliversToSubscriber()
    {
        using var hub = new EventHub();
        var received = new List<IslandEvent>();
        using var subscription = hub.Subscribe(received.Add);

        Assert.True(hub.TryPublish(Notification("a", 1)));

        var single = Assert.Single(received);
        Assert.Equal(IslandEventKind.New, single.Kind);
        Assert.Equal("a", single.Key.Source);
    }

    [Fact]
    public void Publish_AfterDispose_ReturnsFalse()
    {
        var hub = new EventHub();
        hub.Dispose();
        Assert.False(hub.TryPublish(Notification("a", 1)));
    }

    [Fact]
    public void IncrementalEvents_AreCoalescedLatestWins()
    {
        var dispatcher = new ManualDispatcher();
        var counters = new DiagnosticCounters();
        using var hub = new EventHub(dispatcher: dispatcher, counters: counters);
        var received = new List<IslandEvent>();
        using var subscription = hub.Subscribe(received.Add);

        var key = TestData.Key(ActivityKind.Media, "media:gsmtc");
        Assert.True(hub.TryPublish(IslandEvent.Incremental(key, TestData.Payload("p1"), 1)));
        Assert.True(hub.TryPublish(IslandEvent.Incremental(key, TestData.Payload("p2"), 2)));
        Assert.True(hub.TryPublish(IslandEvent.Incremental(key, TestData.Payload("p3"), 3)));
        Assert.Empty(received);

        var metricsBefore = hub.Metrics;
        Assert.Equal(2, metricsBefore.Coalesced);
        Assert.Equal(1, metricsBefore.QueueDepth);
        Assert.Equal(2, counters.Get(CounterKind.EventsCoalesced));

        dispatcher.RunAll();

        var single = Assert.Single(received);
        Assert.Equal(3, single.Sequence);
        Assert.Equal("p3", single.Payload.Title);
    }

    [Fact]
    public void StaleIncremental_IsDiscarded()
    {
        var dispatcher = new ManualDispatcher();
        using var hub = new EventHub(dispatcher: dispatcher);
        var received = new List<IslandEvent>();
        using var subscription = hub.Subscribe(received.Add);

        var key = TestData.Key(ActivityKind.Media, "media:gsmtc");
        hub.TryPublish(IslandEvent.Incremental(key, TestData.Payload("newer"), 5));
        hub.TryPublish(IslandEvent.Incremental(key, TestData.Payload("older"), 3));
        dispatcher.RunAll();

        var single = Assert.Single(received);
        Assert.Equal(5, single.Sequence);
        Assert.Equal(1, hub.Metrics.Dropped);
    }

    [Fact]
    public void NewEvents_AreNeverCoalesced()
    {
        using var hub = new EventHub();
        var received = new List<IslandEvent>();
        using var subscription = hub.Subscribe(received.Add);

        var key = TestData.Key(ActivityKind.Notification, "notif:toast");
        hub.TryPublish(IslandEvent.New(key, TestData.Payload("one"), 1));
        hub.TryPublish(IslandEvent.New(key, TestData.Payload("two"), 2));

        Assert.Equal(2, received.Count);
        Assert.Equal(0, hub.Metrics.Coalesced);
    }

    [Fact]
    public void BoundedQueue_DropsOldestAndCounts()
    {
        var dispatcher = new ManualDispatcher();
        using var hub = new EventHub(new EventBusOptions { Capacity = 4 }, dispatcher: dispatcher);
        var received = new List<IslandEvent>();
        using var subscription = hub.Subscribe(received.Add);

        for (var i = 1; i <= 10; i++)
        {
            hub.TryPublish(TestData.NewEvent(ActivityKind.Notification, "src" + i, "n" + i, i));
        }

        var metrics = hub.Metrics;
        Assert.Equal(4, metrics.QueueDepth);
        Assert.Equal(6, metrics.Dropped);

        dispatcher.RunAll();
        Assert.Equal(4, received.Count);
        Assert.Equal("src7", received[0].Key.Source);
        Assert.Equal("src10", received[3].Key.Source);
    }

    [Fact]
    public void SubscriberException_IsIsolatedAndCounted()
    {
        var counters = new DiagnosticCounters();
        var logger = new RecordingAppLogger();
        using var hub = new EventHub(counters: counters, logger: logger);
        var received = new List<IslandEvent>();
        using var bad = hub.Subscribe(_ => throw new InvalidOperationException("boom"));
        using var good = hub.Subscribe(received.Add);

        Assert.True(hub.TryPublish(Notification("a", 1)));

        Assert.Single(received);
        Assert.Equal(1L, counters.Get(CounterKind.EventSubscriberFaults));
        Assert.True(logger.HasEvent("eventbus.subscriber.fault"));
    }

    [Fact]
    public void Unsubscribe_StopsDelivery()
    {
        using var hub = new EventHub();
        var received = new List<IslandEvent>();
        var subscription = hub.Subscribe(received.Add);
        hub.TryPublish(Notification("a", 1));
        subscription.Dispose();
        hub.TryPublish(Notification("b", 2));

        var single = Assert.Single(received);
        Assert.Equal("a", single.Key.Source);
        Assert.Equal(0, hub.Metrics.SubscriberCount);
    }

    [Fact]
    public void Dispatcher_MarshalsDelivery()
    {
        var dispatcher = new ManualDispatcher();
        using var hub = new EventHub(dispatcher: dispatcher);
        var received = new List<IslandEvent>();
        using var subscription = hub.Subscribe(received.Add);

        hub.TryPublish(Notification("a", 1));
        Assert.Empty(received);
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.RunAll();
        Assert.Single(received);
    }

    [Fact]
    public async Task ReadAllAsync_ReceivesEvents()
    {
        using var hub = new EventHub();
        await using var enumerator = hub.ReadAllAsync("test").GetAsyncEnumerator();
        var pending = enumerator.MoveNextAsync();

        Assert.True(hub.TryPublish(Notification("reader", 42)));
        Assert.True(await pending);
        Assert.Equal("reader", enumerator.Current.Key.Source);
        Assert.Equal(1, hub.Metrics.ReaderCount);
    }

    [Fact]
    public async Task ReadAllAsync_HonorsCancellation()
    {
        using var hub = new EventHub();
        using var cts = new CancellationTokenSource();
        await using var enumerator = hub.ReadAllAsync("cancelled", cts.Token).GetAsyncEnumerator();
        var pending = enumerator.MoveNextAsync().AsTask();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public void Dispose_CompletesAndClears()
    {
        var hub = new EventHub();
        hub.TryPublish(Notification("a", 1));
        hub.Dispose();
        Assert.False(hub.TryPublish(Notification("b", 2)));
        Assert.Equal(0, hub.Metrics.QueueDepth);
    }

    [Fact]
    public void EventSourceHost_StartsAndStopsAllSources()
    {
        var first = new RecordingSource("first");
        var second = new RecordingSource("second");
        using var host = new EventSourceHost([first, second]);

        host.Start();
        host.Start();
        host.Stop();

        Assert.Equal(1, first.StartCount);
        Assert.Equal(1, first.StopCount);
        Assert.Equal(1, second.StartCount);
        Assert.Equal(1, second.StopCount);
    }

    [Fact]
    public void EventSourceHost_IsolatesFailures()
    {
        var logger = new RecordingAppLogger();
        var throwing = new ThrowingSource();
        var good = new RecordingSource("good");
        using var host = new EventSourceHost([throwing, good], logger);

        host.Start();

        Assert.Equal(1, good.StartCount);
        Assert.True(logger.HasEvent("eventsource.start.failed"));
    }

    private sealed class RecordingSource(string id) : IEventSource
    {
        public string SourceId { get; } = id;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public void Start() => StartCount++;

        public void Stop() => StopCount++;
    }

    private sealed class ThrowingSource : IEventSource
    {
        public string SourceId => "throwing";

        public void Start() => throw new InvalidOperationException("cannot start");

        public void Stop()
        {
        }
    }
}
