using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Split capsules follow the primary pill's glass language: same height/baseline, content-adaptive
/// width, all content inside the capsule, optional red count badge and action, no external labels.
/// </summary>
public sealed class SplitCapsuleStyleTests
{
    private static IslandRenderState CapsuleState()
    {
        var media = IslandTestFixtures.MediaCompact() with
        {
            SplitProgress = 1,
            SplitIsletHeight = 40,
            SplitIslets =
            [
                new IslandSplitIslet(ActivityKind.Agent, IconKind.Agent, "Agent", "运行中", IslandPage.Agent,
                    0xFF4CC9F0, "6", ShowAction: true),
                new IslandSplitIslet(ActivityKind.Timer, IconKind.Clock, "计时器", "Pomodoro", IslandPage.Home,
                    0xFF4CC9F0),
            ],
        };
        return media;
    }

    [Fact]
    public void Capsules_match_the_primary_height_and_share_its_baseline()
    {
        var state = CapsuleState();
        var shell = IslandMetrics.ShellRect(state);
        var balls = IslandMetrics.SplitIsletRects(state);
        Assert.Equal(2, balls.Length);
        foreach (var ball in balls)
        {
            Assert.Equal(IslandMetrics.SplitIsletHeight(state), ball.Height, 0.001);
            Assert.Equal(shell.MidY, ball.MidY, 0.001);
            Assert.True(ball.Width >= IslandMetrics.SplitIsletMinWidth, "capsule narrower than 56 DIP");
            Assert.True(ball.Left >= 0 && ball.Right <= LayoutTokens.HostSurfaceWidth);
        }
    }

    [Fact]
    public void Badge_and_action_grow_the_capsule_and_paint_inside_it()
    {
        var state = CapsuleState();
        var balls = IslandMetrics.SplitIsletRects(state);
        Assert.True(balls[0].Width > balls[1].Width, "badge/action capsule must be content-adaptive");
        Assert.True(IslandMetrics.SplitIsletBadgeWidth("6") > 0);

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var red = 0;
        var left = (int)Math.Floor(balls[0].Left * IslandTestFixtures.Scale);
        var top = (int)Math.Floor(balls[0].Top * IslandTestFixtures.Scale);
        var right = (int)Math.Ceiling(balls[0].Right * IslandTestFixtures.Scale);
        var bottom = (int)Math.Ceiling(balls[0].Bottom * IslandTestFixtures.Scale);
        for (var y = Math.Max(0, top); y < Math.Min(bitmap.Height, bottom); y++)
        {
            for (var x = Math.Max(0, left); x < Math.Min(bitmap.Width, right); x++)
            {
                var pixel = bitmap.Pixels[(y * bitmap.Width) + x];
                if (pixel.Red > 180 && pixel.Green < 110 && pixel.Blue < 110 && pixel.Alpha > 100)
                {
                    red++;
                }
            }
        }

        Assert.True(red > 30, "red count badge did not paint inside the capsule: " + red);
    }

    [Fact]
    public void Capsule_pixels_are_neutral_except_the_red_semantic_badge()
    {
        var state = CapsuleState();
        var balls = IslandMetrics.SplitIsletRects(state);
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var scale = IslandTestFixtures.Scale;
        var violations = 0;
        var red = 0;
        for (var i = 0; i < balls.Length; i++)
        {
            var badge = IslandMetrics.SplitIsletBadgeRect(state, state.SplitIslets[i], balls[i]);
            badge = new SKRect(badge.Left - 2, badge.Top - 2, badge.Right + 2, badge.Bottom + 2);
            var left = (int)Math.Floor(balls[i].Left * scale);
            var top = (int)Math.Floor(balls[i].Top * scale);
            var right = (int)Math.Ceiling(balls[i].Right * scale);
            var bottom = (int)Math.Ceiling(balls[i].Bottom * scale);
            for (var y = Math.Max(0, top); y < Math.Min(bitmap.Height, bottom); y++)
            {
                for (var x = Math.Max(0, left); x < Math.Min(bitmap.Width, right); x++)
                {
                    var pixel = bitmap.Pixels[(y * bitmap.Width) + x];
                    if (pixel.Alpha <= 100)
                    {
                        continue;
                    }

                    var max = Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
                    var min = Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
                    if (max > 150 && pixel.Red == max && pixel.Green < 110 && pixel.Blue < 110)
                    {
                        red++;
                    }

                    var insideBadge = x >= badge.Left * scale && x <= badge.Right * scale
                        && y >= badge.Top * scale && y <= badge.Bottom * scale;
                    if (insideBadge)
                    {
                        continue;
                    }

                    // Near-black pixels are the shared palette background (#08080A): at 3% luminance
                    // HSV saturation is numerically noisy and identical to the main pill, so the
                    // neutral scan only judges visible content (V >= 40), not the flat backdrop.
                    if (max < 40)
                    {
                        continue;
                    }

                    var saturation = max <= 0 ? 0 : (max - min) / (float)max;
                    if (saturation >= 0.08f)
                    {
                        violations++;
                    }
                }
            }
        }

        Assert.True(red > 30, "the red semantic badge is missing: " + red);
        Assert.True(violations == 0, "saturated pixels outside the semantic badge: " + violations);
    }

    [Fact]
    public void Capsule_background_matches_the_primary_pill_exactly()
    {
        var state = CapsuleState();
        var shell = IslandMetrics.ShellRect(state);
        var balls = IslandMetrics.SplitIsletRects(state);
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var scale = IslandTestFixtures.Scale;
        SKColor At(float xDip, float yDip)
            => bitmap.Pixels[((int)Math.Round(yDip * scale) * bitmap.Width) + (int)Math.Round(xDip * scale)];

        var primary = At(shell.MidX, shell.Top + 3);
        var capsule = At(balls[0].MidX, balls[0].Top + 3);
        Assert.Equal(primary.Red, capsule.Red);
        Assert.Equal(primary.Green, capsule.Green);
        Assert.Equal(primary.Blue, capsule.Blue);
    }

    [Fact]
    public void Capsule_hit_rect_is_the_whole_capsule_and_nothing_outside()
    {
        var state = CapsuleState();
        var balls = IslandMetrics.SplitIsletRects(state);
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        Assert.Equal(IslandHitKind.SplitIslet, renderer.HitTest(state, balls[0].MidX, balls[0].MidY).Kind);
        Assert.NotEqual(IslandHitKind.SplitIslet,
            renderer.HitTest(state, balls[0].MidX, balls[0].Top - 6).Kind);
    }
}
