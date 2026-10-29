using FluidBar.Core.Telemetry;

namespace FluidBar.Core.Tests.Telemetry;

public class TelemetryQueueTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fluidbar-tq-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "queue.tsv");

    private TelemetryRun Run(int minute, string installId = "install") => new()
    {
        InstallId = installId,
        StartedAt = new DateTimeOffset(2026, 10, 29, 12, minute, 0, TimeSpan.Zero),
        StoppedAt = new DateTimeOffset(2026, 10, 29, 12, minute, 30, TimeSpan.Zero),
        StartupMs = 100 + minute,
        FirstRun = minute == 0,
        Reason = TelemetryStopReason.User,
    };

    [Fact]
    public void EmptyQueue_TakesNothing()
    {
        var queue = new TelemetryQueue(Path_);

        Assert.Empty(queue.TakeBatch());
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Enqueue_RoundTripsEveryField()
    {
        var queue = new TelemetryQueue(Path_);
        queue.Enqueue(Run(3, "abc"));

        var run = Assert.Single(queue.TakeBatch());

        Assert.Equal("abc", run.InstallId);
        Assert.Equal(103, run.StartupMs);
        Assert.Equal(30, run.UptimeSeconds);
        Assert.Equal(TelemetryStopReason.User, run.Reason);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 12, 3, 0, TimeSpan.Zero), run.StartedAt);
    }

    [Fact]
    public void TakeBatch_RemovesWhatItReturns_OldestFirst()
    {
        var queue = new TelemetryQueue(Path_);
        for (var i = 0; i < 5; i++)
        {
            queue.Enqueue(Run(i));
        }

        var batch = queue.TakeBatch(2);

        Assert.Equal(2, batch.Count);
        Assert.Equal(100, batch[0].StartupMs);
        Assert.Equal(101, batch[1].StartupMs);
        Assert.Equal(3, queue.Count);
    }

    [Fact]
    public void Queue_DropsTheOldestBeyondCapacity()
    {
        var queue = new TelemetryQueue(Path_, capacity: 3);
        for (var i = 0; i < 6; i++)
        {
            queue.Enqueue(Run(i));
        }

        Assert.Equal(3, queue.Count);
        var batch = queue.TakeBatch();
        Assert.Equal(new[] { 103, 104, 105 }, batch.Select(r => r.StartupMs).ToArray());
    }

    [Fact]
    public void Queue_SkipsCorruptLinesInsteadOfThrowing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllLines(Path_, ["garbage", "also	broken", string.Empty]);
        var queue = new TelemetryQueue(Path_);
        queue.Enqueue(Run(7));

        var run = Assert.Single(queue.TakeBatch());
        Assert.Equal(107, run.StartupMs);
    }

    [Fact]
    public void Clear_DeletesTheQueueFile()
    {
        var queue = new TelemetryQueue(Path_);
        queue.Enqueue(Run(1));

        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.False(File.Exists(Path_));
    }

    [Fact]
    public void InstallId_IsStableThenChangesAfterReset()
    {
        var idPath = Path.Combine(_directory, "install-id");
        var first = TelemetryInstallId.GetOrCreate(idPath);
        var second = TelemetryInstallId.GetOrCreate(idPath);
        TelemetryInstallId.Reset(idPath);
        var third = TelemetryInstallId.GetOrCreate(idPath);

        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
        Assert.Equal(32, first.Length);
        Assert.True(Guid.TryParseExact(first, "N", out _));
    }

    public void Dispose()
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
    }
}
