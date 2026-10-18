using System.Collections.Immutable;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Events;
using FluidBar.Core.Settings;
using FluidBar.Core.Tests.TestSupport;
using FluidBar.Soak;

namespace FluidBar.Core.Tests;

/// <summary>
/// In-process regression guards for the M6 soak budgets: the same bounds the soak harness enforces
/// are asserted here on a smaller, deterministic workload so CI catches a budget regression
/// without needing to run the full soak harness.
/// </summary>
public class SoakBudgetRegressionTests
{
    [Fact]
    public void EventHub_QueueCapacityIsBoundedUnderFlood()
    {
        var dispatcher = new ManualDispatcher();
        using var hub = new EventHub(new EventBusOptions { Capacity = 64 }, dispatcher: dispatcher);

        for (var i = 0; i < 500; i++)
        {
            hub.TryPublish(TestData.NewEvent(ActivityKind.Notification, "flood" + i, "n" + i, i + 1));
        }

        var metrics = hub.Metrics;
        Assert.Equal(64, metrics.QueueDepth);
        Assert.Equal(436, metrics.Dropped);
        Assert.True(metrics.Published <= 500);

        var delivered = 0;
        using var subscription = hub.Subscribe(_ => delivered++);
        dispatcher.RunAll();
        Assert.Equal(64, delivered);
    }

    [Fact]
    public void EventHub_CoalescesIncrementalFloodIntoOnePendingEvent()
    {
        var dispatcher = new ManualDispatcher();
        using var hub = new EventHub(dispatcher: dispatcher);
        var key = TestData.Key(ActivityKind.Media, "media:flood");
        var received = new List<IslandEvent>();
        using var subscription = hub.Subscribe(received.Add);

        for (var i = 0; i < SoakBudgets.IncrementalUpdateCount; i++)
        {
            var payload = new ActivityPayload { Title = "Track", ProgressPercent = i % 101 };
            hub.TryPublish(IslandEvent.Incremental(key, payload, i + 1));
        }

        var metrics = hub.Metrics;
        Assert.Equal(1, metrics.QueueDepth);
        Assert.Equal(SoakBudgets.IncrementalUpdateCount - 1, metrics.Coalesced);
        Assert.Equal(0, metrics.Dropped);

        dispatcher.RunAll();
        var single = Assert.Single(received);
        Assert.Equal(SoakBudgets.IncrementalUpdateCount, single.Sequence);
    }

    [Fact]
    public void Scheduler_ChurnStaysWithinRetainedStateBudgets()
    {
        var scheduler = new ActivityScheduler();
        var state = ActivitySchedulerState.Empty;
        var random = new Random(20261018);
        var keys = new[]
        {
            new ActivityKey(ActivityKind.Media, "media:a"),
            new ActivityKey(ActivityKind.Notification, "notif:a"),
            new ActivityKey(ActivityKind.Clipboard, "clipboard:host"),
            new ActivityKey(ActivityKind.Timer, "system:timer"),
            new ActivityKey(ActivityKind.Progress, "system:progress"),
            new ActivityKey(ActivityKind.BatteryLow, "system:battery"),
            new ActivityKey(ActivityKind.Focus, "system:focus"),
            new ActivityKey(ActivityKind.Agent, "agent:hook"),
            new ActivityKey(ActivityKind.LockKeys, "system:lockkeys"),
            new ActivityKey(ActivityKind.Device, "system:usb"),
        };
        var now = TestData.Now;
        long sequence = 1;

        for (var i = 0; i < 2_000; i++)
        {
            now = now.AddSeconds(1);
            var key = keys[random.Next(keys.Length)];
            var action = random.Next(4);
            state = action switch
            {
                0 or 1 => scheduler.Update(
                    state,
                    IslandEvent.New(key, new ActivityPayload { Title = "t" + i }, sequence++),
                    now).State,
                2 => scheduler.Update(
                    state,
                    IslandEvent.Replacement(key, new ActivityPayload { Title = "r" + i }, sequence++),
                    now).State,
                _ => scheduler.Update(state, IslandEvent.Retract(key, sequence++), now).State,
            };
            state = scheduler.Tick(state, now).State;

            Assert.InRange(state.Visible.Length, 0, SoakBudgets.MaxVisibleActivities);
            Assert.InRange(state.Background.Length, 0, SoakBudgets.MaxBackgroundActivities);
            Assert.InRange(state.ActivityCount, 0, SoakBudgets.MaxRetainedActivities);
            Assert.Equal(state.ActivityCount, state.Visible.Length + state.Background.Length);
        }

        Assert.True(state.LastSequences.Count <= SoakBudgets.MaxRetainedActivities);

        // Drain: everything must be releasable; no activity can leak past a full-day expiry.
        var liveKeys = state.Visible.Select(r => r.Key).Concat(state.Background.Select(r => r.Key)).ToArray();
        foreach (var key in liveKeys)
        {
            state = scheduler.Update(state, IslandEvent.Retract(key, sequence++), now).State;
        }

        state = scheduler.Tick(state, now.AddDays(1)).State;
        Assert.Equal(0, state.ActivityCount);
    }

    [Fact]
    public void Motion_SteppingAfterWarmupAllocatesNothing()
    {
        var spring = new FluidBar.Core.Motion.Spring(FluidBar.Core.Motion.MotionTokens.Expand.Spring);
        const double frame = 1.0 / 300.0;
        spring.Retarget(25, frame);
        for (var i = 0; i < 1_000; i++)
        {
            if (i % 300 == 299)
            {
                spring.Retarget(i % 600 == 599 ? 25 : 10, frame);
            }

            spring.Step(frame);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            if (i % 300 == 299)
            {
                spring.Retarget(i % 600 == 599 ? 25 : 10, frame);
            }

            spring.Step(frame);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 4 * 1024);
    }

    [Fact]
    public void SettingsStore_RepeatedSavesStayBoundedAndCorrect()
    {
        var fileSystem = new InMemorySettingsFileSystem();
        var counters = new DiagnosticCounters();
        var options = SettingsStoreOptions.ForAppData("C:\\appdata\\FluidBar");
        var store = new SettingsStore(fileSystem, options, counters: counters);
        var settings = FluidBarSettings.CreateDefault();
        var heapAfterWarmup = 0L;

        for (var i = 0; i < 1_000; i++)
        {
            settings = settings with
            {
                Appearance = settings.Appearance with
                {
                    Theme = i % 2 == 0 ? AppTheme.Dark : AppTheme.Light,
                    FontScale = 0.8 + ((i % 80) * 0.01),
                },
            };
            Assert.True(store.Save(settings).Success);

            if (i % 100 == 0)
            {
                fileSystem.ClearOperations();
            }

            if (i == 100)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                heapAfterWarmup = GC.GetTotalMemory(forceFullCollection: true);
            }
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var heapAfter = GC.GetTotalMemory(forceFullCollection: true);
        Assert.InRange(heapAfter - heapAfterWarmup, long.MinValue, SoakBudgets.SettingsMaxHeapGrowthBytes);

        Assert.Equal(1_000, (int)counters.Get(CounterKind.SettingsSaves));
        Assert.False(fileSystem.FileExists(options.EffectiveTempPath));
        Assert.True(fileSystem.FileExists(options.EffectiveBackupPath));

        var loaded = store.Load();
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.Equal(settings.Appearance.FontScale, loaded.Settings.Appearance.FontScale, 1e-9);
        Assert.Equal(settings.Appearance.Theme, loaded.Settings.Appearance.Theme);
    }
}
