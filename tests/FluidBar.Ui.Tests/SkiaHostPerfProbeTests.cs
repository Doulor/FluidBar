using FluidBar.App.Hosting.Skia;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Guards for the perf-audit instrumentation: histogram percentiles, dropped-frame accounting,
/// allocation attribution, and the hidden-island frame-loop stop.
/// </summary>
public sealed class SkiaHostPerfProbeTests
{
    private static SkiaHostPerfProbe CreateProbe(RecordingAppLogger logger, IslandRenderer renderer)
        => new(logger, renderer, () => 1, () => 1_686_528, TimeProvider.System);

    [Fact]
    public void Dump_reports_percentiles_dropped_frames_and_attribution()
    {
        using var renderer = new IslandRenderer();
        var logger = new RecordingAppLogger();
        var probe = CreateProbe(logger, renderer);

        for (var i = 0; i < 60; i++)
        {
            probe.RecordSample(8.0, 1.0, full: true);
        }

        for (var i = 0; i < 35; i++)
        {
            probe.RecordSample(16.0, 2.0, full: false);
        }

        for (var i = 0; i < 4; i++)
        {
            probe.RecordSample(30.0, 3.0, full: false, allocAdvanceBytes: 0, allocRenderBytes: 0);
        }

        probe.RecordSample(200.0, 1.0, full: true); // idle gap between animation bursts
        probe.Dump();

        var entry = logger.Snapshot().Last(e => e.EventId == "island.skia.perf");
        var data = entry.Data!;
        Assert.Equal(100L, Convert.ToInt64(data["frames"]));
        Assert.Equal(5L, Convert.ToInt64(data["droppedFrames"]));
        Assert.Equal(99L, Convert.ToInt64(data["activeFrames"]));
        Assert.Equal(8.5, Convert.ToDouble(data["intervalP50Ms"]));
        Assert.Equal(16.5, Convert.ToDouble(data["intervalP95Ms"]));
        Assert.Equal(99L, Convert.ToInt64(data["activeFrames"]));
        Assert.Equal(30.5, Convert.ToDouble(data["activeIntervalP99Ms"]));
        Assert.Equal(0L, Convert.ToInt64(data["allocAdvanceBytes"]));
        Assert.Equal(0L, Convert.ToInt64(data["allocRenderBytes"]));
        Assert.True(Convert.ToInt64(data["workingSetBytes"]) > 0);
    }

    [Fact]
    public void Dump_with_no_frames_proves_an_idle_second_has_zero_redraws()
    {
        using var renderer = new IslandRenderer();
        var logger = new RecordingAppLogger();
        var probe = CreateProbe(logger, renderer);
        probe.Dump();

        var entry = logger.Snapshot().Last(e => e.EventId == "island.skia.perf");
        Assert.Equal(0L, Convert.ToInt64(entry.Data!["framesInWindow"]));
        Assert.Equal(0d, Convert.ToDouble(entry.Data!["redrawsPerSec"]));
    }

    [Fact]
    public void Shell_morphing_flag_is_set_only_while_the_spring_runs()
    {
        using var fixture = ControllerFixture.Create();
        fixture.Controller.ApplyInput(IslandInput.PrimaryClick());
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.True(fixture.Controller.RenderState.ShellMorphing,
            "the render state must tell the renderer a morph is in flight");

        fixture.Settle();
        Assert.False(fixture.Controller.RenderState.ShellMorphing,
            "settled frames must use the exact shadow bucket so goldens stay pixel-stable");
    }

    [Fact]
    public void Hidden_island_with_media_stops_requesting_wave_frames()
    {
        using var fixture = ControllerFixture.Create();
        fixture.Controller.PublishActivity(
            ActivityKind.Media,
            new ActivityPayload
            {
                Title = "Track",
                Subtitle = "Artist",
                IsActive = true,
                ShowsAudioWave = true,
                ProgressPercent = 40,
            },
            sequence: 1);
        fixture.Controller.Motion.SnapToTarget();
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.True(fixture.Controller.IsAnimating, "a visible playing pill animates its waveform");

        fixture.Controller.ApplyInput(IslandInput.HideHotkey());
        for (var i = 0; i < 200 && fixture.Controller.IsAnimating; i++)
        {
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        }

        Assert.Equal(ShellForm.Hidden, fixture.Controller.RenderState.Form);
        Assert.False(fixture.Controller.IsAnimating,
            "a hidden island must not keep a 30 fps wave animation alive");
    }
}
