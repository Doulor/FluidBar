using System.Collections.Immutable;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Monitors;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Shell;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

public sealed class ClipboardHistoryTests
{
    private static ClipboardEntry Entry(uint sequence, string text)
        => new(sequence, DateTimeOffset.UnixEpoch.AddSeconds(sequence), ClipboardContentKind.Text, text,
            text.Length, ImmutableArray<string>.Empty);

    [Fact]
    public void TryAdd_NewEntryBecomesLatest()
    {
        var history = new ClipboardHistory();
        Assert.True(history.TryAdd(Entry(1, "hello"), out var snapshot));
        Assert.Equal("hello", snapshot.Latest!.Text);
        Assert.True(history.TryAdd(Entry(2, "world"), out snapshot));
        Assert.Equal("world", snapshot.Latest!.Text);
        Assert.Equal(2, snapshot.History.Length);
        Assert.Equal("world", snapshot.History[0].Text);
    }

    [Fact]
    public void TryAdd_SuppressesConsecutiveDuplicates()
    {
        var history = new ClipboardHistory();
        Assert.True(history.TryAdd(Entry(1, "same"), out _));
        Assert.False(history.TryAdd(Entry(2, "same"), out var snapshot));
        Assert.Equal(1, snapshot.History.Length);
    }

    [Fact]
    public void TryAdd_AllowsDuplicateAfterDifferentEntry()
    {
        var history = new ClipboardHistory();
        history.TryAdd(Entry(1, "a"), out _);
        history.TryAdd(Entry(2, "b"), out _);
        Assert.True(history.TryAdd(Entry(3, "a"), out _));
    }

    [Fact]
    public void TryAdd_EvictsBeyondCapacity()
    {
        var history = new ClipboardHistory(maxEntries: 2);
        history.TryAdd(Entry(1, "a"), out _);
        history.TryAdd(Entry(2, "b"), out var snapshot);
        Assert.True(history.TryAdd(Entry(3, "c"), out snapshot));
        Assert.Equal(2, snapshot.History.Length);
        Assert.Equal(1, history.Evictions);
        Assert.DoesNotContain(snapshot.History, entry => entry.Text == "a");
    }

    [Fact]
    public void TryAdd_FilesCompareByPathList()
    {
        var first = new ClipboardEntry(1, DateTimeOffset.UnixEpoch, ClipboardContentKind.Files, null, 0,
            ImmutableArray.Create("C:\\a.txt"));
        var second = first with { Sequence = 2 };
        Assert.True(ClipboardHistory.IsDuplicate(first, second));
        var third = first with { FilePaths = ImmutableArray.Create("C:\\b.txt") };
        Assert.False(ClipboardHistory.IsDuplicate(first, third));
    }

    [Fact]
    public void TryAdd_KeepsOnlyTheNewestImageEntries()
    {
        var history = new ClipboardHistory(maxEntries: 20, maxImageEntries: 2);
        for (uint sequence = 1; sequence <= 3; sequence++)
        {
            history.TryAdd(new ClipboardEntry(sequence, DateTimeOffset.UnixEpoch.AddSeconds(sequence),
                ClipboardContentKind.Image, null, 0, ImmutableArray<string>.Empty,
                [checked((byte)sequence)]), out _);
        }

        var snapshot = history.Snapshot();
        Assert.Equal(2, snapshot.History.Count(entry => entry.Kind == ClipboardContentKind.Image));
        Assert.DoesNotContain(snapshot.History, entry => entry.Sequence == 1);
    }

    [Fact]
    public void Snapshot_IsEmptyForFreshHistory()
    {
        var history = new ClipboardHistory();
        Assert.True(history.Snapshot().IsEmpty);
        history.Clear();
        Assert.True(history.Snapshot().IsEmpty);
    }
}

public sealed class NotificationTests
{
    [Theory]
    [InlineData("Your code is 123456", "123456")]
    [InlineData("验证码：654321，5分钟内有效", "654321")]
    [InlineData("1234567 is not a code", null)]
    [InlineData("No digits here", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void VerificationCodeDetector_DetectsSixDigitCodes(string? body, string? expected)
    {
        Assert.Equal(expected, VerificationCodeDetector.Detect(body));
    }

    [Fact]
    public void VerificationCodeDetector_PrefersCodeNearKeyword()
    {
        const string text = "订单号 123456 已发货，验证码 998877";
        Assert.Equal("998877", VerificationCodeDetector.Detect(text));
    }

    [Fact]
    public void VerificationCodeDetector_DetectsKeyword()
    {
        Assert.True(VerificationCodeDetector.ContainsVerificationKeyword("你的验证码"));
        Assert.True(VerificationCodeDetector.ContainsVerificationKeyword("Verification Code"));
        Assert.False(VerificationCodeDetector.ContainsVerificationKeyword("hello"));
    }

    [Fact]
    public void NotificationHistory_PrimedDoesNotReplayExistingIds()
    {
        var history = new NotificationHistory();
        history.Prime(new uint[] { 1, 2, 3 });
        var added = history.Process(new[]
        {
            Candidate(2), Candidate(3), Candidate(4),
        });
        Assert.Single(added);
        Assert.Equal(4u, added[0].Id);
    }

    [Fact]
    public void NotificationHistory_ProcessSortsByCreationAndAttachesCodes()
    {
        var history = new NotificationHistory();
        history.Prime(Array.Empty<uint>());
        var added = history.Process(new[]
        {
            new NotificationCandidate(7, "App", "Title", "code 111222", DateTimeOffset.UnixEpoch.AddSeconds(7)),
            new NotificationCandidate(5, "App", "Title", "older", DateTimeOffset.UnixEpoch.AddSeconds(5)),
        });
        Assert.Equal(2, added.Count);
        Assert.Equal(5u, added[0].Id);
        Assert.Null(added[0].VerificationCode);
        Assert.Equal("111222", added[1].VerificationCode);
    }

    [Fact]
    public void NotificationHistory_BoundsSeenIdsAndWatermark()
    {
        var history = new NotificationHistory(maxSeenIds: 2, maxBatch: 1);
        history.Prime(Array.Empty<uint>());
        history.Process(new[] { Candidate(1) });
        history.Process(new[] { Candidate(2) });
        history.Process(new[] { Candidate(3) });
        Assert.True(history.SeenCount <= 2);
        Assert.Equal(3u, history.Watermark);
    }

    [Fact]
    public void NotificationHistory_ProcessBeforePrimeIsSilent()
    {
        var history = new NotificationHistory();
        Assert.Empty(history.Process(new[] { Candidate(1) }));
        Assert.True(history.IsPrimed);
        Assert.Empty(history.Process(new[] { Candidate(1) }));
    }

    [Fact]
    public void NotificationHistory_ResetClearsState()
    {
        var history = new NotificationHistory();
        history.Prime(new uint[] { 1 });
        history.Reset();
        Assert.False(history.IsPrimed);
        Assert.Equal(0u, history.Watermark);
        Assert.Equal(0, history.SeenCount);
    }

    private static NotificationCandidate Candidate(uint id)
        => new(id, "App", "Title", "Body", DateTimeOffset.UnixEpoch.AddSeconds(id));
}

public sealed class StartupRegistrationTests
{
    [Theory]
    [InlineData(@"C:\Apps\FluidBar.exe", null, @"C:\Apps\FluidBar.exe")]
    [InlineData(@"C:\Program Files\FluidBar\FluidBar.exe", null, "\"C:\\Program Files\\FluidBar\\FluidBar.exe\"")]
    [InlineData(@"C:\Apps\FluidBar.exe", "--minimized", @"C:\Apps\FluidBar.exe --minimized")]
    public void BuildCommand_QuotesWhenNeeded(string path, string? arguments, string expected)
    {
        Assert.Equal(expected, StartupCommandBuilder.BuildCommand(path, arguments));
    }

    [Fact]
    public void BuildCommand_RejectsRelativePath()
    {
        Assert.Throws<ArgumentException>(() => StartupCommandBuilder.BuildCommand(@"FluidBar.exe"));
        Assert.Throws<ArgumentException>(() => StartupCommandBuilder.BuildCommand(""));
    }

    [Fact]
    public async Task EnableQueryDisable_RoundTripsThroughStore()
    {
        var store = new FakeRegistryValueStore();
        var service = new StartupRegistrationService(store, new CapturingLogger());
        var executable = @"C:\Apps\FluidBar.exe";

        Assert.False((await service.QueryAsync(executable)).IsEnabled);

        await service.EnableAsync(executable);
        var state = await service.QueryAsync(executable);
        Assert.True(state.IsEnabled);
        Assert.True(state.MatchesCurrentExecutable);
        Assert.Equal(1, store.SetCalls);

        await service.DisableAsync();
        Assert.False((await service.QueryAsync(executable)).IsEnabled);
        Assert.Equal(1, store.DeleteCalls);
    }

    [Fact]
    public async Task Enable_PropagatesStoreFailure()
    {
        var store = new FakeRegistryValueStore { ThrowOnSet = true };
        var service = new StartupRegistrationService(store, new CapturingLogger());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnableAsync(@"C:\Apps\FluidBar.exe"));
    }

    [Fact]
    public async Task Disable_IsIdempotent()
    {
        var store = new FakeRegistryValueStore();
        var service = new StartupRegistrationService(store, new CapturingLogger());
        await service.DisableAsync();
        await service.DisableAsync();
        Assert.Equal(2, store.DeleteCalls);
    }
}

public sealed class RollingLoggerTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FluidBarTests", Guid.NewGuid().ToString("N"));

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Log_WritesJsonLinesAndFlushes()
    {
        var counters = new PlatformCounters();
        var options = new RollingLogOptions
        {
            DirectoryPath = _directory,
            RateLimitPerCategoryPerSecond = 1000,
        };
        await using var logger = new RollingFileLogger(options, counters: counters);
        await logger.StartAsync();

        for (var index = 0; index < 25; index++)
        {
            logger.LogInfo(PlatformLogCategory.Monitor, "test.event", $"entry {index}",
                new Dictionary<string, object?> { ["index"] = index });
        }

        await logger.FlushAsync();
        var files = Directory.GetFiles(_directory, "*.jsonl");
        Assert.NotEmpty(files);
        var lines = files.SelectMany(ReadAllLinesShared).ToArray();
        Assert.Equal(25, lines.Length);
        foreach (var line in lines)
        {
            using var document = System.Text.Json.JsonDocument.Parse(line);
            Assert.True(document.RootElement.TryGetProperty("ts", out _));
            Assert.True(document.RootElement.TryGetProperty("level", out _));
            Assert.True(document.RootElement.TryGetProperty("cat", out _));
            Assert.True(document.RootElement.TryGetProperty("evtId", out _));
        }

        Assert.Equal(25, counters.Snapshot().LogEntries);
    }

    [Fact]
    public async Task Log_RateLimitsPerCategoryButNotErrors()
    {
        var counters = new PlatformCounters();
        var options = new RollingLogOptions
        {
            DirectoryPath = _directory,
            RateLimitPerCategoryPerSecond = 5,
        };
        await using var logger = new RollingFileLogger(options, counters: counters);
        await logger.StartAsync();

        for (var index = 0; index < 20; index++)
        {
            logger.LogInfo(PlatformLogCategory.Render, "rate.test", "info");
        }

        logger.LogError(PlatformLogCategory.Render, "rate.error", "error", new InvalidOperationException("boom"));
        await logger.FlushAsync();

        var lines = Directory.GetFiles(_directory, "*.jsonl").SelectMany(ReadAllLinesShared).ToArray();
        Assert.Equal(6, lines.Length); // 5 info + 1 error
        Assert.True(counters.Snapshot().LogRateLimited > 0);
    }

    [Fact]
    public async Task Log_RotatesAndHonoursMaxFiles()
    {
        var options = new RollingLogOptions
        {
            DirectoryPath = _directory,
            MaxFileBytes = 256,
            MaxFiles = 3,
            RateLimitPerCategoryPerSecond = 10_000,
        };
        await using var logger = new RollingFileLogger(options);
        await logger.StartAsync();

        for (var index = 0; index < 60; index++)
        {
            logger.LogInfo(PlatformLogCategory.Render, "rotate.test", new string('x', 40),
                new Dictionary<string, object?> { ["index"] = index });
        }

        await logger.FlushAsync();
        var files = Directory.GetFiles(_directory, "*.jsonl");
        Assert.InRange(files.Length, 1, 4);
        foreach (var file in files)
        {
            Assert.True(new FileInfo(file).Length <= 1024, $"{file} exceeded the rotation budget");
        }
    }

    [Fact]
    public async Task Log_TruncatesOversizedEntries()
    {
        var options = new RollingLogOptions
        {
            DirectoryPath = _directory,
            MaxEntryBytes = 512,
            RateLimitPerCategoryPerSecond = 1000,
        };
        await using var logger = new RollingFileLogger(options);
        await logger.StartAsync();
        logger.LogWarning(PlatformLogCategory.Render, "big.entry", new string('y', 5000));
        await logger.FlushAsync();
        var line = Directory.GetFiles(_directory, "*.jsonl").SelectMany(ReadAllLinesShared).Single();
        Assert.True(line.Length < 2000);
        Assert.Contains("truncated", line, StringComparison.Ordinal);
    }

    private static string[] ReadAllLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }

    [Fact]
    public async Task IsEnabled_HonoursMinimumLevel()
    {
        var options = new RollingLogOptions { DirectoryPath = _directory, MinimumLevel = PlatformLogLevel.Warning };
        await using var logger = new RollingFileLogger(options);
        Assert.False(logger.IsEnabled(PlatformLogLevel.Info));
        Assert.True(logger.IsEnabled(PlatformLogLevel.Warning));
    }
}



public sealed class MonitorLifecycleTests
{
    private sealed record TestSnapshot(int Value);

    private sealed class FakeMonitor : PlatformMonitor<TestSnapshot>
    {
        public FakeMonitor(CapturingLogger logger, PlatformCounters counters)
            : base("fake", logger, counters, TimeProvider.System)
        {
        }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public bool ThrowOnStart { get; set; }

        protected override ValueTask OnStartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            if (ThrowOnStart)
            {
                throw new InvalidOperationException("simulated start failure");
            }

            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnStopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        public void PublishValue(int value) => Publish(new TestSnapshot(value));
    }

    [Fact]
    public async Task StartStop_AreIdempotent()
    {
        var counters = new PlatformCounters();
        await using var monitor = new FakeMonitor(new CapturingLogger(), counters);
        await monitor.StartAsync();
        await monitor.StartAsync();
        Assert.Equal(1, monitor.StartCount);
        Assert.Equal(PlatformMonitorState.Running, monitor.State);

        await monitor.StopAsync();
        await monitor.StopAsync();
        Assert.Equal(1, monitor.StopCount);
        Assert.Equal(PlatformMonitorState.Stopped, monitor.State);
    }

    [Fact]
    public async Task StartFailure_TransitionsToFailedAndLogs()
    {
        var logger = new CapturingLogger();
        var counters = new PlatformCounters();
        await using var monitor = new FakeMonitor(logger, counters) { ThrowOnStart = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.StartAsync());
        Assert.Equal(PlatformMonitorState.Failed, monitor.State);
        Assert.Equal(1, counters.Snapshot().MonitorStartFailures);
        Assert.Contains(logger.Entries, entry => entry.EventId == "fake.start-failed");
    }

    [Fact]
    public async Task Publish_IsChangeOnlyAndCoalesced()
    {
        await using var monitor = new FakeMonitor(new CapturingLogger(), new PlatformCounters());
        var received = new List<int>();
        monitor.SnapshotChanged += (_, args) => received.Add(args.Snapshot.Value);
        await monitor.StartAsync();

        monitor.PublishValue(1);
        await TestWait.UntilAsync(() => monitor.Current is { Value: 1 } && received.Contains(1));

        monitor.PublishValue(1); // duplicate after the value settled → dropped
        monitor.PublishValue(2);
        await TestWait.UntilAsync(() => monitor.Current is { Value: 2 } && received.Contains(2));

        // A burst is coalesced: the last value wins and no duplicate dispatch for the same value occurs.
        monitor.PublishValue(3);
        await TestWait.UntilAsync(() => monitor.Current is { Value: 3 });
        Assert.Equal(new[] { 1, 2, 3 }, received);
        Assert.Equal(3, monitor.Current!.Value);
    }

    [Fact]
    public async Task Dispose_StopsMonitor()
    {
        var monitor = new FakeMonitor(new CapturingLogger(), new PlatformCounters());
        await monitor.StartAsync();
        await monitor.DisposeAsync();
        Assert.Equal(1, monitor.StopCount);
        Assert.Equal(PlatformMonitorState.Disposed, monitor.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => monitor.StartAsync());
    }
}

public sealed class SnapshotPublisherTests
{
    private sealed record Snapshot(int Value);

    [Fact]
    public async Task Publish_DuplicateReturnsFalse()
    {
        using var publisher = new SnapshotPublisher<Snapshot>("test", new CapturingLogger(), new PlatformCounters());
        Assert.True(publisher.Publish(new Snapshot(1)));
        await TestWait.UntilAsync(() => publisher.Current is { Value: 1 });
        Assert.False(publisher.Publish(new Snapshot(1)));
        Assert.Equal(new Snapshot(1), publisher.Current);
    }

    [Fact]
    public async Task Publish_BurstCoalescesToLatest()
    {
        using var publisher = new SnapshotPublisher<Snapshot>("test", new CapturingLogger(), new PlatformCounters());
        var received = new List<int>();
        publisher.Changed += (_, args) =>
        {
            lock (received)
            {
                received.Add(args.Snapshot.Value);
            }
        };

        for (var index = 1; index <= 50; index++)
        {
            publisher.Publish(new Snapshot(index));
        }

        await TestWait.UntilAsync(() =>
        {
            lock (received)
            {
                return received.Count > 0 && received[^1] == 50;
            }
        });

        lock (received)
        {
            Assert.True(received.Count <= 50);
        }
    }

    [Fact]
    public async Task SubscriberException_IsLoggedNotPropagated()
    {
        var logger = new CapturingLogger();
        var counters = new PlatformCounters();
        using var publisher = new SnapshotPublisher<Snapshot>("test", logger, counters);
        publisher.Changed += (_, _) => throw new InvalidOperationException("subscriber boom");
        Assert.True(publisher.Publish(new Snapshot(1)));
        await TestWait.UntilAsync(() => counters.Snapshot().SnapshotDispatchFailures >= 1);
        Assert.Contains(logger.Entries, entry => entry.EventId == "snapshot.subscriber-failed");
    }
}

public sealed class ClockMonitorTests
{
    [Fact]
    public async Task ClockMonitor_PublishesOnMinuteBoundaryOnly()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 18, 12, 34, 30, TimeSpan.Zero));
        var logger = new CapturingLogger();
        await using var monitor = new ClockMonitor(logger, new PlatformCounters(), time);
        var snapshots = new List<ClockSnapshot>();
        monitor.SnapshotChanged += (_, args) =>
        {
            lock (snapshots)
            {
                snapshots.Add(args.Snapshot);
            }
        };

        await monitor.StartAsync();
        await TestWait.UntilAsync(() =>
        {
            lock (snapshots)
            {
                return snapshots.Count == 1;
            }
        });
        Assert.Equal(34, snapshots[0].LocalTime.Minute);

        // Not yet at the boundary.
        time.Advance(TimeSpan.FromSeconds(20));
        await Task.Delay(50);
        lock (snapshots)
        {
            Assert.Single(snapshots);
        }

        // Cross the minute boundary.
        time.Advance(TimeSpan.FromSeconds(11));
        await TestWait.UntilAsync(() =>
        {
            lock (snapshots)
            {
                return snapshots.Count >= 2;
            }
        });
        lock (snapshots)
        {
            Assert.Equal(35, snapshots[^1].LocalTime.Minute);
        }
    }
}
