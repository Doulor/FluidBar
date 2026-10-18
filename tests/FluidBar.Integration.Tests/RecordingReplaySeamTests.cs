using FluidBar.Core.Activities;
using FluidBar.Integration.Tests.TestSupport;
using FluidBar.Soak.Recording;

namespace FluidBar.Integration.Tests;

/// <summary>
/// Seam 6: the soak recording fixture (<c>media-session</c>) replays deterministically through the Core
/// pipeline using the shared recording contract linked from the soak project.
/// </summary>
public class RecordingReplaySeamTests
{
    [Fact]
    public void Media_session_fixture_replays_and_matches_presentation_snapshots()
    {
        var recording = EventRecordingCatalog.Load("media-session");

        var failures = EventRecordingReplay.Validate(recording);

        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures));
        Assert.Equal(6, recording.Events.Length);
        Assert.Equal(6, recording.Expected.Length);
    }

    [Fact]
    public void Fixture_events_fed_through_the_app_pipeline_match_replay_snapshots()
    {
        var recording = EventRecordingCatalog.Load("media-session");
        var expectedSteps = EventRecordingReplay.Replay(recording);

        using var pipeline = new CorePipelineHarness();
        var offset = TimeSpan.Zero;
        for (var i = 0; i < recording.Events.Length; i++)
        {
            var recorded = recording.Events[i];
            var targetOffset = TimeSpan.FromMilliseconds(recorded.OffsetMs);
            pipeline.Advance(targetOffset - offset);
            offset = targetOffset;

            pipeline.PublishCoreBatch([recorded.ToEvent()]);

            var actual = Snapshot(pipeline.View, i);
            var expected = EventRecordingReplay.Snapshot(expectedSteps[i]);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Replay_is_deterministic_across_instances()
    {
        var recording = EventRecordingCatalog.Load("media-session");
        var first = EventRecordingReplay.Replay(recording);
        var second = EventRecordingReplay.Replay(recording);

        Assert.Equal(first.Length, second.Length);
        for (var i = 0; i < first.Length; i++)
        {
            Assert.Equal(EventRecordingReplay.Snapshot(first[i]), EventRecordingReplay.Snapshot(second[i]));
        }
    }

    private static ExpectedSnapshot Snapshot(FluidBar.Core.Presentation.IslandViewModel view, int index) => new()
    {
        AtEventIndex = index,
        StateKind = view.StateKind,
        Page = view.Page,
        PrimaryKind = view.Primary?.Kind,
        Title = view.Primary?.Title,
        GhostCount = view.Ghosts.Length,
        BadgeCount = view.OverflowCount,
        ProgressPercent = view.Primary?.ProgressPercent,
        ShowsWave = view.Primary?.ShowsWave ?? false,
        HudLabelKey = view.Hud?.LabelKey,
    };
}
