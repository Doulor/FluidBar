using FluidBar.Core.Activities;
using FluidBar.Island;
using SkiaSharp;
using Xunit;

namespace FluidBar.Island.Tests;

/// <summary>
/// Stacked agent glyph (user spec 2026-10-24): one ring per live session, up to three — the second ring
/// counter-rotates inside the first, the third agent is a glowing centre dot, and anything above three is
/// not drawn.
/// </summary>
public sealed class AgentRingStackTests
{
    [Fact]
    public void A_second_live_agent_adds_an_inner_ring()
    {
        var one = CountRingPixels(1, out _);
        var two = CountRingPixels(2, out _);

        Assert.True(two > one * 1.25, $"a second agent must add ring pixels (one={one}, two={two})");
    }

    [Fact]
    public void A_third_live_agent_paints_the_centre_dot()
    {
        _ = CountRingPixels(2, out var centreTwo);
        _ = CountRingPixels(3, out var centreThree);

        var distance = Math.Abs(centreThree.Red - centreTwo.Red)
                       + Math.Abs(centreThree.Green - centreTwo.Green)
                       + Math.Abs(centreThree.Blue - centreTwo.Blue);

        Assert.True(distance > 20,
            $"the third agent must paint a centre dot (from {centreTwo} to {centreThree}, distance {distance})");
    }

    [Fact]
    public void More_than_three_agents_are_clamped()
    {
        Assert.Equal(3, AgentRingStack.Clamp(9));
        Assert.Equal(1, AgentRingStack.Clamp(0));
        Assert.Equal(2, AgentRingStack.Clamp(2));
    }

    private static int CountRingPixels(int liveAgents, out SKColor centre)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Agent() with
        {
            AgentAnimationSeconds = 0.42,
            Compact = new CompactActivity(
                ActivityKind.Agent,
                new CompactModel
                {
                    Title = "Refactor island renderer",
                    StatusText = "Running",
                    DurationText = "2m",
                    Status = CompactStatus.Info,
                    AgentLiveCount = liveAgents,
                }),
        };

        SKBitmap? last = null;
        for (var frame = 0; frame < 24; frame++)
        {
            last?.Dispose();
            last = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        }

        using var bitmap = last!;
        var shell = IslandMetrics.ShellRect(state);
        var ring = new SKRect(
            (float)shell.Left + 10,
            (float)shell.Top + ((float)shell.Height - 20) / 2f,
            (float)shell.Left + 30,
            (float)shell.Top + ((float)shell.Height + 20) / 2f);
        var scale = IslandTestFixtures.Scale;

        centre = bitmap.GetPixel((int)(ring.MidX * scale), (int)(ring.MidY * scale));

        var count = 0;
        for (var y = (int)(ring.Top * scale); y < (int)(ring.Bottom * scale); y++)
        {
            for (var x = (int)(ring.Left * scale); x < (int)(ring.Right * scale); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha > 60 && pixel.Blue > pixel.Red + 25)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
