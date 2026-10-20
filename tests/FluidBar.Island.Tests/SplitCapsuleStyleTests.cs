using FluidBar.Core.Activities;
using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Split previews are minimal pure circles: one element each, no labels/badges/status dots/action
/// buttons/borders. Agent = blue 180° ring only; media = circular cover or one neutral note.
/// </summary>
public sealed class SplitCapsuleStyleTests
{
    private static IslandRenderState CircleState(bool withMedia = false, byte[]? cover = null)
    {
        var islets = new List<IslandSplitIslet>
        {
            new(ActivityKind.Agent, IconKind.Agent, "Agent", "运行中", PageRef.Agent,
                0, null, true),
        };
        if (withMedia)
        {
            islets.Add(new IslandSplitIslet(ActivityKind.Media, IconKind.Media, "媒体", "Midnight Drive",
                PageRef.Media, 0, null, false, cover, cover is null ? null : "test-cover"));
        }

        return IslandTestFixtures.MediaCompact() with
        {
            SplitProgress = 1,
            SplitIsletHeight = 40,
            AgentAnimationSeconds = 0.225,
            SplitIslets = islets.ToArray(),
        };
    }

    [Fact]
    public void Split_targets_are_pure_circles_on_the_primary_baseline()
    {
        var state = CircleState(withMedia: true);
        var shell = IslandMetrics.ShellRect(state);
        var balls = IslandMetrics.SplitIsletRects(state);
        Assert.Equal(2, balls.Length);
        foreach (var ball in balls)
        {
            Assert.Equal(IslandMetrics.SplitIsletDiameter(state), ball.Width, 0.001);
            Assert.Equal(ball.Width, ball.Height, 0.001);
            Assert.Equal(shell.MidY, ball.MidY, 0.001);
            Assert.True(ball.Left >= 0 && ball.Right <= LayoutTokens.HostSurfaceWidth);
        }
    }

    [Fact]
    public void Agent_circle_contains_only_the_blue_ring()
    {
        var state = CircleState();
        var ball = IslandMetrics.SplitIsletRects(state)[0];
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var blue = 0;
        var white = 0;
        var red = 0;
        var scale = IslandTestFixtures.Scale;
        for (var y = (int)(ball.Top * scale); y < (int)(ball.Bottom * scale); y++)
        {
            for (var x = (int)(ball.Left * scale); x < (int)(ball.Right * scale); x++)
            {
                var pixel = bitmap.Pixels[(y * bitmap.Width) + x];
                if (pixel.Alpha < 100) continue;
                if (pixel.Blue > 150 && pixel.Blue > pixel.Red + 60 && pixel.Blue > pixel.Green + 20) blue++;
                if (pixel.Red > 200 && pixel.Green > 200 && pixel.Blue > 200) white++;
                if (pixel.Red > 150 && pixel.Red > pixel.Green + 60 && pixel.Red > pixel.Blue + 60) red++;
            }
        }

        Assert.True(blue > 30, "agent ring missing: " + blue);
        Assert.Equal(0, white);
        Assert.Equal(0, red);
    }

    [Fact]
    public void Media_circle_without_cover_is_a_single_white_note()
    {
        var state = CircleState(withMedia: true);
        var ball = IslandMetrics.SplitIsletRects(state)[1];
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var white = 0;
        var red = 0;
        var scale = IslandTestFixtures.Scale;
        for (var y = (int)(ball.Top * scale); y < (int)(ball.Bottom * scale); y++)
        {
            for (var x = (int)(ball.Left * scale); x < (int)(ball.Right * scale); x++)
            {
                var pixel = bitmap.Pixels[(y * bitmap.Width) + x];
                if (pixel.Alpha < 100) continue;
                if (pixel.Red > 200 && pixel.Green > 200 && pixel.Blue > 200) white++;
                if (pixel.Red > 150 && pixel.Red > pixel.Green + 60) red++;
            }
        }

        Assert.True(white > 15, "fallback music note missing: " + white);
        Assert.Equal(0, red);
    }

    [Fact]
    public void Media_cover_is_clipped_to_the_circle()
    {
        // 2x2 red PNG: proves the cover path paints and the circle clip leaves the corners clean.
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAIAAABLbSncAAAAFElEQVR4nGO8o2HDgA0wYRUdtBIA/ygBUI2ZtV0AAAAASUVORK5CYII=");
        var state = CircleState(withMedia: true, cover: png);
        var ball = IslandMetrics.SplitIsletRects(state)[1];
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var scale = IslandTestFixtures.Scale;
        var redInside = 0;
        for (var y = (int)(ball.Top * scale); y < (int)(ball.Bottom * scale); y++)
        {
            for (var x = (int)(ball.Left * scale); x < (int)(ball.Right * scale); x++)
            {
                var pixel = bitmap.Pixels[(y * bitmap.Width) + x];
                if (pixel.Red > 180 && pixel.Green < 120 && pixel.Blue < 120) redInside++;
            }
        }

        Assert.True(redInside > 100, "cover was not painted inside the circle: " + redInside);
        var corner = bitmap.Pixels[(int)((ball.Top + 1) * scale) * bitmap.Width + (int)((ball.Left + 1) * scale)];
        Assert.True(corner.Alpha < 40, "cover leaked into the square corner: " + corner.Alpha);
    }

    [Fact]
    public void Circle_background_is_the_primary_pill_colour()
    {
        var state = CircleState();
        var shell = IslandMetrics.ShellRect(state);
        var ball = IslandMetrics.SplitIsletRects(state)[0];
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var scale = IslandTestFixtures.Scale;
        SKColor At(float x, float y)
            => bitmap.Pixels[((int)Math.Round(y * scale) * bitmap.Width) + (int)Math.Round(x * scale)];

        // Sample an empty top-centre pixel of each surface (away from ring/icon).
        var primary = At(shell.MidX, shell.Top + 4);
        var circle = At(ball.MidX, ball.Top + 4);
        Assert.Equal(primary.Red, circle.Red);
        Assert.Equal(primary.Green, circle.Green);
        Assert.Equal(primary.Blue, circle.Blue);
    }

    [Fact]
    public void Circle_hit_rect_is_the_whole_circle_and_nothing_outside()
    {
        var state = CircleState();
        var ball = IslandMetrics.SplitIsletRects(state)[0];
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        Assert.Equal(IslandHitKind.SplitIslet,
            renderer.HitTest(state, ball.MidX, ball.MidY).Kind);
        Assert.NotEqual(IslandHitKind.SplitIslet,
            renderer.HitTest(state, ball.MidX, ball.Top - 6).Kind);
        // The square corner outside the circle must be click-through too.
        Assert.NotEqual(IslandHitKind.SplitIslet,
            renderer.HitTest(state, ball.Left + 1, ball.Top + 1).Kind);
    }
}
