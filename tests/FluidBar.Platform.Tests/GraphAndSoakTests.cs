using System.Diagnostics;
using System.Text.Json;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Smoke test for the composition root (part of the default suite: fast, headless-safe) and the
/// M6 soak scenarios. Soak tests are <b>guarded</b> by <c>FLUIDBAR_SOAK=1</c> and are never long-running
/// in normal CI; <c>build/perf-baseline.ps1</c> and the release docs invoke them explicitly.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PlatformServiceGraphTests
{
    [Fact]
    public async Task GraphSmoke_StartsInDependencyOrderAndDisposesIdempotently()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        var logDirectory = Path.Combine(Path.GetTempPath(), "FluidBarTests", "graph", Guid.NewGuid().ToString("N"));
        var graph = new PlatformServiceGraph(new PlatformServiceGraphOptions
        {
            LogDirectory = logDirectory,
            EnableMedia = false,
            EnableLyrics = false,
            EnableClipboard = false,
            EnableNotifications = false,
            EnableAgentInbox = false,
            EnableAgentHooks = false,
            EnableUsb = false,
            EnableBluetooth = false,
            EnableUpdateCheck = false,
        });

        try
        {
            await graph.StartAsync();
            Assert.NotNull(graph.Display);
            Assert.NotNull(graph.Volume);
            Assert.NotNull(graph.Clock);
            Assert.Equal(PlatformMonitorState.Running, graph.Clock!.State);
            Assert.True(graph.Hotkeys.IsRunning);

            await graph.StartAsync(); // idempotent

            await TestWait.UntilAsync(() => graph.Clock.Current is not null);
            Assert.NotNull(graph.Clock.Current);
            Assert.NotEqual(0u, graph.Host.ThreadId);

            await graph.StopAsync();
            Assert.Equal(PlatformMonitorState.Stopped, graph.Clock.State);
            Assert.False(graph.Hotkeys.IsRunning);
            await graph.StopAsync(); // idempotent
        }
        finally
        {
            await graph.DisposeAsync();
            await graph.DisposeAsync(); // idempotent
            if (Directory.Exists(logDirectory))
            {
                Directory.Delete(logDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GraphSmoke_DegradesWhenOptionalServicesAreDisabled()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        var graph = new PlatformServiceGraph(new PlatformServiceGraphOptions
        {
            LogDirectory = null,
            EnableDisplay = false,
            EnableMonitors = false,
            EnableMedia = false,
            EnableLyrics = false,
            EnableClipboard = false,
            EnableNotifications = false,
            EnableAgentInbox = false,
            EnableAgentHooks = false,
            EnableUsb = false,
            EnableBluetooth = false,
            EnableUpdateCheck = false,
        });

        try
        {
            await graph.StartAsync();
            Assert.Null(graph.Display);
            Assert.Null(graph.Media);
            Assert.Null(graph.Clipboard);
            Assert.NotNull(graph.Hotkeys);
            await graph.StopAsync();
        }
        finally
        {
            await graph.DisposeAsync();
        }
    }
}

/// <summary>Shared sampling/report helper for the soak scenarios (JSON evidence for docs).</summary>
internal static class SoakReporter
{
    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_SOAK"), "1", StringComparison.Ordinal);

    public static int Minutes(string variable, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(variable), out var minutes) && minutes > 0
            ? minutes
            : fallback;

    public static string ReportPath(string scenario)
    {
        var directory = Environment.GetEnvironmentVariable("FLUIDBAR_SOAK_REPORT_DIR")
                        ?? Path.Combine(Path.GetTempPath(), "fluidbar-soak");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{scenario}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
    }

    public static ProcessSample Sample(Process process) => new(
        TimestampUtc: DateTimeOffset.UtcNow,
        WorkingSetBytes: process.WorkingSet64,
        PrivateBytes: process.PrivateMemorySize64,
        HandleCount: process.HandleCount,
        ThreadCount: process.Threads.Count,
        CpuSeconds: process.TotalProcessorTime.TotalSeconds,
        ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
        Gen2Collections: GC.CollectionCount(2));

    /// <summary>Runs samples for <paramref name="duration"/> and writes a JSON report; returns the summary.</summary>
    public static async Task<SoakSummary> RunAsync(string scenario, TimeSpan duration, TimeSpan? interval = null,
        Action<int>? onSample = null, DateTimeOffset? startupAt = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var process = Process.GetCurrentProcess();
        var sampleInterval = interval ?? TimeSpan.FromSeconds(5);
        var samples = new List<ProcessSample>();
        var cpuStart = process.TotalProcessorTime;
        var wallStart = Stopwatch.StartNew();
        var index = 0;
        while (wallStart.Elapsed < duration)
        {
            samples.Add(Sample(process));
            onSample?.Invoke(index++);
            var remaining = duration - wallStart.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(remaining < sampleInterval ? remaining : sampleInterval).ConfigureAwait(false);
        }

        samples.Add(Sample(process));
        wallStart.Stop();
        var cpuUsed = (process.TotalProcessorTime - cpuStart).TotalSeconds;
        var summary = new SoakSummary
        {
            Scenario = scenario,
            StartedAtUtc = (startupAt ?? DateTimeOffset.UtcNow).ToString("O"),
            DurationSeconds = Math.Round(wallStart.Elapsed.TotalSeconds, 1),
            Machine = new MachineInfo
            {
                MachineName = Environment.MachineName,
                ProcessorCount = Environment.ProcessorCount,
                OsVersion = Environment.OSVersion.VersionString,
                DotnetVersion = Environment.Version.ToString(),
                Is64BitProcess = Environment.Is64BitProcess,
            },
            SampleIntervalSeconds = Math.Round(sampleInterval.TotalSeconds, 1),
            Samples = samples,
            AverageCpuPercent = Math.Round(cpuUsed / Math.Max(0.001, wallStart.Elapsed.TotalSeconds)
                / Environment.ProcessorCount * 100.0, 3),
            WorkingSetStartBytes = samples[0].WorkingSetBytes,
            WorkingSetEndBytes = samples[^1].WorkingSetBytes,
            HandleStart = samples[0].HandleCount,
            HandleEnd = samples[^1].HandleCount,
            ThreadStart = samples[0].ThreadCount,
            ThreadEnd = samples[^1].ThreadCount,
            Gen2Collections = samples[^1].Gen2Collections - samples[0].Gen2Collections,
            Metadata = metadata,
        };

        var path = ReportPath(scenario);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(summary,
            new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        summary.ReportPath = path;
        return summary;
    }

    public sealed record ProcessSample(
        DateTimeOffset TimestampUtc,
        long WorkingSetBytes,
        long PrivateBytes,
        int HandleCount,
        int ThreadCount,
        double CpuSeconds,
        long ManagedHeapBytes,
        int Gen2Collections);

    public sealed record MachineInfo
    {
        public string MachineName { get; init; } = string.Empty;
        public int ProcessorCount { get; init; }
        public string OsVersion { get; init; } = string.Empty;
        public string DotnetVersion { get; init; } = string.Empty;
        public bool Is64BitProcess { get; init; }
    }

    public sealed class SoakSummary
    {
        public string Scenario { get; init; } = string.Empty;
        public string StartedAtUtc { get; init; } = string.Empty;
        public double DurationSeconds { get; init; }
        public MachineInfo Machine { get; init; } = new();
        public double SampleIntervalSeconds { get; init; }
        public List<ProcessSample> Samples { get; init; } = new();
        public double AverageCpuPercent { get; init; }
        public long WorkingSetStartBytes { get; init; }
        public long WorkingSetEndBytes { get; init; }
        public int HandleStart { get; init; }
        public int HandleEnd { get; init; }
        public int ThreadStart { get; init; }
        public int ThreadEnd { get; init; }
        public int Gen2Collections { get; init; }
        public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string ReportPath { get; set; } = string.Empty;
    }
}

/// <summary>
/// M6 soak evidence. Guarded by <c>FLUIDBAR_SOAK=1</c>: the default test run executes these in
/// milliseconds (they return immediately), while <c>build/perf-baseline.ps1 -Soak</c> runs them for real.
/// </summary>
[Trait("Category", "Soak")]
public sealed class PlatformSoakTests
{
    [Fact]
    public async Task Soak_IdlePlatformGraph_SamplesHandlesMemoryCpu()
    {
        if (!SoakReporter.Enabled)
        {
            return;
        }

        var minutes = SoakReporter.Minutes("FLUIDBAR_SOAK_MINUTES", 10);
        var root = Path.Combine(Path.GetTempPath(), "fluidbar-soak-data", Guid.NewGuid().ToString("N"));
        var graph = new PlatformServiceGraph(new PlatformServiceGraphOptions
        {
            LogDirectory = Path.Combine(root, "logs"),
            DataDirectory = root,
            EnableMedia = false,
            EnableLyrics = false,
            EnableAgentHooks = false,
            EnableUpdateCheck = false,
        });

        var startupWatch = Stopwatch.StartNew();
        try
        {
            await graph.StartAsync();
            startupWatch.Stop();
            await TestWait.UntilAsync(() => graph.Display?.Current is not null, TimeSpan.FromSeconds(15));
            await TestWait.UntilAsync(() => graph.Clock?.Current is not null, TimeSpan.FromSeconds(15));

            var summary = await SoakReporter.RunAsync("soak-idle-platform", TimeSpan.FromMinutes(minutes),
                metadata: new Dictionary<string, object?>
                {
                    ["startupMs"] = startupWatch.ElapsedMilliseconds,
                    ["monitors"] = new[]
                    {
                        "display", "volume", "brightness", "battery", "clock", "inputmethod", "lockkeys",
                        "network", "theme", "clipboard", "notifications", "agent-inbox", "usb", "bluetooth",
                    },
                    ["counters"] = graph.Counters.Snapshot().ToDictionary(),
                });

            // Guardrail thresholds (generous; long runs keep them tight in M6).
            Assert.True(summary.HandleEnd - summary.HandleStart < 100,
                $"handle growth {summary.HandleEnd - summary.HandleStart}");
            Assert.True(summary.WorkingSetEndBytes - summary.WorkingSetStartBytes < 96L * 1024 * 1024,
                "working-set growth exceeded 96 MB");
            Assert.True(summary.AverageCpuPercent < 10.0,
                $"average idle CPU {summary.AverageCpuPercent}% exceeded 10%");
        }
        finally
        {
            await graph.DisposeAsync();
            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public async Task Soak_MediaService_IdleTimeline()
    {
        if (!SoakReporter.Enabled)
        {
            return;
        }

        var minutes = SoakReporter.Minutes("FLUIDBAR_MEDIA_SOAK_MINUTES", 30);
        var root = Path.Combine(Path.GetTempPath(), "fluidbar-soak-media", Guid.NewGuid().ToString("N"));
        var graph = new PlatformServiceGraph(new PlatformServiceGraphOptions
        {
            LogDirectory = Path.Combine(root, "logs"),
            DataDirectory = root,
            EnableDisplay = false,
            EnableMonitors = false,
            EnableClipboard = false,
            EnableNotifications = false,
            EnableAgentInbox = false,
            EnableAgentHooks = false,
            EnableUsb = false,
            EnableBluetooth = false,
            EnableLyrics = false,
            EnableUpdateCheck = false,
        });

        var startupWatch = Stopwatch.StartNew();
        try
        {
            await graph.StartAsync();
            startupWatch.Stop();
            var summary = await SoakReporter.RunAsync("soak-media-idle", TimeSpan.FromMinutes(minutes),
                metadata: new Dictionary<string, object?>
                {
                    ["startupMs"] = startupWatch.ElapsedMilliseconds,
                    ["note"] = "GSMTC service running idle (no guaranteed playback session on the soak machine)",
                    ["counters"] = graph.Counters.Snapshot().ToDictionary(),
                });

            Assert.True(summary.HandleEnd - summary.HandleStart < 100,
                $"handle growth {summary.HandleEnd - summary.HandleStart}");
            Assert.True(summary.WorkingSetEndBytes - summary.WorkingSetStartBytes < 96L * 1024 * 1024,
                "working-set growth exceeded 96 MB");
        }
        finally
        {
            await graph.DisposeAsync();
            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public async Task Soak_Clipboard_1000Events()
    {
        if (!SoakReporter.Enabled)
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "fluidbar-soak-clipboard", Guid.NewGuid().ToString("N"));
        var graph = new PlatformServiceGraph(new PlatformServiceGraphOptions
        {
            LogDirectory = Path.Combine(root, "logs"),
            DataDirectory = root,
            EnableDisplay = false,
            EnableMonitors = false,
            EnableMedia = false,
            EnableLyrics = false,
            EnableNotifications = false,
            EnableAgentInbox = false,
            EnableAgentHooks = false,
            EnableUsb = false,
            EnableBluetooth = false,
            EnableUpdateCheck = false,
        });

        try
        {
            await graph.StartAsync();
            var clipboard = graph.Clipboard!;
            var stopwatch = Stopwatch.StartNew();
            var succeeded = 0;
            for (var index = 0; index < 1000; index++)
            {
                if (await clipboard.SetTextAsync($"soak-clipboard-event-{index}"))
                {
                    succeeded++;
                }
            }

            stopwatch.Stop();
            await TestWait.UntilAsync(() => clipboard.Current?.Latest is not null, TimeSpan.FromSeconds(10));
            var summary = await SoakReporter.RunAsync("soak-clipboard-1000", TimeSpan.FromSeconds(2),
                metadata: new Dictionary<string, object?>
                {
                    ["attemptedWrites"] = 1000,
                    ["successfulWrites"] = succeeded,
                    ["writeMilliseconds"] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
                    ["historyLength"] = clipboard.Current?.History.Length ?? 0,
                    ["latestSequence"] = clipboard.Current?.Latest?.Sequence ?? 0,
                    ["clipboardEventsCounter"] = graph.Counters.Snapshot().ClipboardEvents,
                    ["clipboardDedupedCounter"] = graph.Counters.Snapshot().ClipboardDeduped,
                    ["counters"] = graph.Counters.Snapshot().ToDictionary(),
                });

            Assert.InRange(clipboard.Current?.History.Length ?? 0, 0, 50);
            Assert.True(summary.HandleEnd - summary.HandleStart < 100,
                $"handle growth {summary.HandleEnd - summary.HandleStart}");
        }
        finally
        {
            await graph.DisposeAsync();
            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public async Task Soak_NotificationStorm_5000Candidates()
    {
        if (!SoakReporter.Enabled)
        {
            return;
        }

        var history = new NotificationHistory(maxSeenIds: 256, maxBatch: 20);
        history.Prime(Array.Empty<uint>());
        var stopwatch = Stopwatch.StartNew();
        var accepted = 0;
        for (var index = 0; index < 5000; index++)
        {
            var candidates = new[]
            {
                new NotificationCandidate((uint)(index + 1), "App", "Title", $"验证码 {100000 + (index % 900000)}",
                    DateTimeOffset.UnixEpoch.AddSeconds(index)),
            };
            accepted += history.Process(candidates).Count;
        }

        stopwatch.Stop();
        var summary = await SoakReporter.RunAsync("soak-notification-storm", TimeSpan.FromSeconds(1),
            metadata: new Dictionary<string, object?>
            {
                ["candidates"] = 5000,
                ["acceptedEntries"] = accepted,
                ["seenIdsRetained"] = history.SeenCount,
                ["watermark"] = history.Watermark,
                ["processingMilliseconds"] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
                ["managedHeapBytes"] = GC.GetTotalMemory(forceFullCollection: false),
            });

        Assert.InRange(history.SeenCount, 0, 256);
        Assert.Equal(5000u, history.Watermark);
        Assert.True(summary.HandleEnd - summary.HandleStart < 100,
            $"handle growth {summary.HandleEnd - summary.HandleStart}");
    }
}
