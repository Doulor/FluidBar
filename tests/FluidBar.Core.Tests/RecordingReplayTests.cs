using FluidBar.Core.Tests.TestSupport;
using FluidBar.Soak.Recording;

namespace FluidBar.Core.Tests;

/// <summary>
/// Deterministic recording/replay contract tests shared with the soak harness and consumable by UI
/// tests. Each fixture feeds the real Core pipeline and asserts the expected presentation
/// snapshots, so UI tests can snapshot view models without Platform/UI setup.
/// </summary>
public class RecordingReplayTests
{
    public static TheoryData<string> FixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in EventRecordingCatalog.Names)
        {
            data.Add(name);
        }

        return data;
    }

    [Fact]
    public void Catalog_ContainsTheDocumentedFixtures()
    {
        var names = EventRecordingCatalog.Names;
        Assert.Equal(FluidBar.Soak.SoakBudgets.FixtureCount, names.Count);
        Assert.Contains("media-session", names);
        Assert.Contains("notification-burst", names);
        Assert.Contains("clipboard-agent-mix", names);

        var recordings = EventRecordingCatalog.LoadAll();
        Assert.All(recordings, recording =>
        {
            Assert.Equal(EventRecording.ExpectedFormat, recording.Format);
            Assert.False(recording.Events.IsDefaultOrEmpty);
            Assert.False(recording.Expected.IsDefaultOrEmpty);
        });
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Fixture_ReplayMatchesExpectedPresentationSnapshots(string name)
    {
        var recording = EventRecordingCatalog.Load(name);

        var failures = EventRecordingReplay.Validate(recording);

        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Fixture_ReplayIsDeterministic(string name)
    {
        var recording = EventRecordingCatalog.Load(name);

        var first = EventRecordingReplay.Replay(recording);
        var second = EventRecordingReplay.Replay(recording);
        Assert.Equal(first.Length, second.Length);

        for (var i = 0; i < first.Length; i++)
        {
            Assert.Equal(EventRecordingReplay.Snapshot(first[i]), EventRecordingReplay.Snapshot(second[i]));
            Assert.Equal(first[i].SchedulerState.ActivityCount, second[i].SchedulerState.ActivityCount);
            Assert.Equal(first[i].Update.Kind, second[i].Update.Kind);
        }
    }

    [Fact]
    public void Fixture_MediaSession_ProducesExpectedPrimarySequence()
    {
        var recording = EventRecordingCatalog.Load("media-session");
        var steps = EventRecordingReplay.Replay(recording);

        Assert.Equal(6, steps.Length);
        Assert.Equal("Midnight City", steps[0].View.Primary?.Title);
        Assert.Equal(10, steps[1].View.Primary?.ProgressPercent);
        Assert.Equal("Out of Touch", steps[3].View.Primary?.Title);
        Assert.Null(steps[5].View.Primary);
        Assert.True(steps[0].View.Primary?.ShowsWave);
    }

    [Fact]
    public void Fixture_NotificationBurst_PreemptionDropsTransientNotification()
    {
        var recording = EventRecordingCatalog.Load("notification-burst");
        var steps = EventRecordingReplay.Replay(recording);

        // After the clipboard arrives it takes the primary slot; the transient notification was dropped.
        Assert.Equal(FluidBar.Core.Activities.ActivityKind.Clipboard, steps[2].View.Primary?.Kind);
        Assert.Equal(1, steps[2].SchedulerState.ActivityCount);
        // Media joins as a ghost behind clipboard.
        Assert.Single(steps[3].View.Ghosts);
        Assert.Equal(FluidBar.Core.Activities.ActivityKind.Media, steps[4].View.Primary?.Kind);
        Assert.Null(steps[5].View.Primary);
    }

    [Fact]
    public void Fixture_ClipboardAgentMix_ExpiresTransientLayers()
    {
        var recording = EventRecordingCatalog.Load("clipboard-agent-mix");
        var steps = EventRecordingReplay.Replay(recording);

        Assert.Equal(2, steps[2].View.Ghosts.Length);
        Assert.Equal("Hud.Volume", steps[4].View.Hud?.LabelKey);
        // Persistent reservation keeps the running Agent ahead of the newly arrived Media.
        Assert.Equal(FluidBar.Core.Activities.ActivityKind.Agent, steps[5].View.Primary?.Kind);
        Assert.Equal(FluidBar.Core.Activities.ActivityKind.Agent, steps[6].View.Primary?.Kind);
    }

    [Fact]
    public void Replay_DoesNotUseAmbientTime()
    {
        // Replaying the same fixture at different wall-clock times must produce identical snapshots.
        var recording = EventRecordingCatalog.Load("clipboard-agent-mix");
        var before = EventRecordingReplay.Replay(recording).Select(EventRecordingReplay.Snapshot).ToArray();
        Thread.Sleep(5);
        var after = EventRecordingReplay.Replay(recording).Select(EventRecordingReplay.Snapshot).ToArray();

        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i], after[i]);
        }
    }
}
