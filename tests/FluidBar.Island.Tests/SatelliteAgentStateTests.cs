using FluidBar.Core.Activities;
using FluidBar.Island;
using SkiaSharp;
using Xunit;

namespace FluidBar.Island.Tests;

/// <summary>
/// Agent satellite end states: the ring only spins while the activity runs (Info), and Completed /
/// Failed / Cancelled render a static neutral marker with a frozen angle.
/// </summary>
public sealed class SatelliteAgentStateTests
{
    [Theory]
    [InlineData(CompactStatus.Info, true)]
    [InlineData(CompactStatus.Success, false)]
    [InlineData(CompactStatus.Danger, false)]
    [InlineData(CompactStatus.Warning, false)]
    [InlineData(CompactStatus.Neutral, false)]
    public void Only_running_agents_keep_the_ring_spinning(CompactStatus status, bool expected)
    {
        var state = State(status);
        Assert.Equal(expected, IslandMetrics.HasSpinningSplitRing(state));
    }

    [Fact]
    public void Completed_agent_renders_a_neutral_marker_without_blue()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var ball = IslandMetrics.SplitIsletRects(State(CompactStatus.Success))[0];
        using var bitmap = renderer.RenderToBitmap(State(CompactStatus.Success), 4f);
        var blue = 0;
        for (var y = (int)(ball.Top * 4); y < (int)(ball.Bottom * 4); y++)
        {
            for (var x = (int)(ball.Left * 4); x < (int)(ball.Right * 4); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Blue > pixel.Red + 40 && pixel.Alpha > 100)
                {
                    blue++;
                }
            }
        }

        Assert.Equal(0, blue);
    }

    [Fact]
    public void Running_agent_still_paints_the_blue_ring()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var ball = IslandMetrics.SplitIsletRects(State(CompactStatus.Info))[0];
        using var bitmap = renderer.RenderToBitmap(State(CompactStatus.Info), 4f);
        var blue = 0;
        for (var y = (int)(ball.Top * 4); y < (int)(ball.Bottom * 4); y++)
        {
            for (var x = (int)(ball.Left * 4); x < (int)(ball.Right * 4); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Blue > pixel.Red + 40 && pixel.Alpha > 100)
                {
                    blue++;
                }
            }
        }

        Assert.True(blue > 40, "the running ring must still be painted, blue pixels " + blue);
    }

    [Fact]
    public void Completed_freezes_the_ring_angle_and_stops_the_clock()
    {
        var running = State(CompactStatus.Info);
        var frozenAt = IslandMetrics.SplitRingDegrees(running.AgentAnimationSeconds);
        var completed = State(CompactStatus.Success) with { AgentRingFrozenDegrees = frozenAt };

        Assert.False(IslandMetrics.HasSpinningSplitRing(completed));
        Assert.Equal(frozenAt, completed.AgentRingFrozenDegrees!.Value, 6);

        // Two later frames keep the same frozen angle: nothing is advanced and nothing resets to 0.
        var nextFrame = completed with { AgentAnimationSeconds = running.AgentAnimationSeconds + 0.5 };
        Assert.Equal(frozenAt, nextFrame.AgentRingFrozenDegrees!.Value, 6);
        Assert.NotEqual(0d, nextFrame.AgentRingFrozenDegrees!.Value, 6);
    }

    [Fact]
    public void Ring_centre_line_sits_inside_the_cover_well_with_margin()
    {
        var ball = IslandMetrics.SplitIsletRects(State(CompactStatus.Info))[0];
        var frame = IslandMetrics.SplitIsletThumbnailRect(ball);
        var centreRadius = (ball.Width / 2) * IslandMetrics.SplitRingRadiusFactor;
        Assert.InRange(centreRadius, 10.9f, 11.2f);

        // Outer edge of the 2 DIP ring vs the well's rounded-corner arc (side/2 - r + r*sqrt2 ...).
        var cornerRadius = IslandMetrics.SplitIsletThumbnailRadius(frame);
        var cornerArcDistance = ((frame.Width / 2) - cornerRadius) * Math.Sqrt(2) + cornerRadius;
        // Stroke thickened to 2.75 DIP; the outer edge must still clear the well's corner arc by >= 1.5.
        Assert.Equal(2.75f, IslandMetrics.SplitRingStrokeDip, 0.001f);
        Assert.Equal(2.25f, IslandMetrics.SplitRingEndMarkStrokeDip, 0.001f);
        var outer = centreRadius + (IslandMetrics.SplitRingStrokeDip / 2);
        Assert.True(cornerArcDistance - outer >= 1.5f,
            $"ring outer {outer:0.0} must clear the corner arc {cornerArcDistance:0.0} by >= 1.5 DIP");
    }

    private static IslandRenderState State(CompactStatus status)
        => IslandTestFixtures.Compact(
            new CompactActivity(
                ActivityKind.Agent,
                new CompactModel { Title = "Agent", Subtitle = "运行中", IsActive = status == CompactStatus.Info }))
            with
        {
            SplitProgress = 1,
            SplitIsletHeight = 40,
            AgentAnimationSeconds = 0.85,
            SplitIslets =
            [
                new(ActivityKind.Agent, IconKind.Agent, "Agent", "运行中", PageRef.Agent, 0, null, true, null, null, status),
            ],
        };
}
