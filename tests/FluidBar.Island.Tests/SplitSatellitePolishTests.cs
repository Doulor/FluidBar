using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// iOS-grade polish of the split satellites: a 1 DIP top rim highlight (white 6-10 %), a faint bottom
/// shade, a 1 DIP cover hairline with a recessed top shade, and an agent ring that is the only
/// chromatic element (2 DIP, round caps). The body never picks up the accent tint.
/// </summary>
public sealed class SplitSatellitePolishTests
{
    /// <summary>32x32 synthetic cover (warm gradient + light disc), generated so the test needs no
    /// base64 blob and the contain-fit geometry stays meaningful.</summary>
    private static byte[] CoverPng()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            using var paint = new SKPaint { IsAntialias = true };
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(32, 32),
                [new SKColor(0x3A, 0x1E, 0x6E), new SKColor(0xE0, 0x5A, 0x4E)],
                [0f, 1f], SKShaderTileMode.Clamp);
            canvas.DrawRect(new SKRect(0, 0, 32, 32), paint);
            paint.Shader = null;
            paint.Color = new SKColor(0xFF, 0xD9, 0x8A, 0xC0);
            canvas.DrawCircle(17, 14, 7, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static IslandRenderState State(bool withMedia = true, IslandRenderState.IslandSatelliteFrame[]? frames = null)
    {
        var islets = new List<IslandSplitIslet>
        {
            new(ActivityKind.Agent, IconKind.Agent, "Agent", "运行中", PageRef.Agent, 0, null, true),
        };
        if (withMedia)
        {
            islets.Add(new IslandSplitIslet(ActivityKind.Media, IconKind.Media, "媒体", "Midnight Drive",
                PageRef.Media, 0, null, false, CoverPng(), "test-cover"));
        }

        var state = IslandTestFixtures.MediaCompact() with
        {
            SplitProgress = 1,
            SplitIsletHeight = 40,
            SplitIslets = islets.ToArray(),
        };
        return frames is null ? state : state with { SatelliteFrames = frames };
    }

    private static SKColor At(SKBitmap bitmap, float xDip, float yDip, float scale)
        => bitmap.Pixels[((int)Math.Round(yDip * scale) * bitmap.Width) + (int)Math.Round(xDip * scale)];

    [Fact]
    public void Media_ball_has_a_one_dip_top_rim_and_a_bottom_shade_without_any_accent()
    {
        var state = State();
        var ball = IslandMetrics.SplitIsletRects(state)[1];
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, 4f);
        // Baseline = the Agent ball's top margin: a media ball's centre is cover artwork and its margins
        // sit under the cover's contact shadow, so neither is a neutral "ball fill" reference.
        var agentBall = IslandMetrics.SplitIsletRects(state)[0];
        var fill = At(bitmap, agentBall.MidX, agentBall.Top + 2, 4f);
        var rim = SKColors.Transparent;
        for (var dy = 0f; dy < 2.5f; dy += 0.25f)
        {
            var pixel = At(bitmap, ball.MidX, ball.Top + dy, 4f);
            if (pixel.Alpha > 200)
            {
                rim = pixel;
                break;
            }
        }

        var impliedWhite = (rim.Red - fill.Red) / (double)Math.Max(1, 255 - fill.Red);
        Assert.InRange(impliedWhite, 0.05, 0.11);
        Assert.True(Math.Abs(rim.Red - rim.Green) <= 2 && Math.Abs(rim.Green - rim.Blue) <= 2,
            "the rim highlight must be neutral white, got " + rim);

        // Bottom shade: the last opaque row at the bottom edge must be much darker than the top rim
        // (volume). Reading the very edge avoids the ball's contact shadow and the cover, which both
        // tint the interior margins.
        var bottom = SKColors.Transparent;
        for (var dy = 3f; dy >= 0f; dy -= 0.25f)
        {
            var pixel = At(bitmap, agentBall.MidX, agentBall.Bottom - dy, 4f);
            if (pixel.Alpha > 200)
            {
                bottom = pixel;
                break;
            }
        }

        Assert.True(rim.Red - bottom.Red >= 12, "the bottom shade is missing: rim " + rim + " vs bottom " + bottom);

        // No accent anywhere in the media ball (a cover + neutral light only).
        var accent = IslandTestFixtures.Theme.Accent;
        var tinted = 0;
        for (var y = (int)ball.Top; y < (int)Math.Ceiling(ball.Bottom); y++)
        {
            for (var x = (int)ball.Left; x < (int)Math.Ceiling(ball.Right); x++)
            {
                var pixel = At(bitmap, x + 0.5f, y + 0.5f, 4f);
                if (pixel.Alpha > 40
                    && Math.Abs(pixel.Red - accent.Red) <= 22
                    && Math.Abs(pixel.Green - accent.Green) <= 22
                    && Math.Abs(pixel.Blue - accent.Blue) <= 22)
                {
                    tinted++;
                }
            }
        }

        Assert.Equal(0, tinted);
    }

    [Fact]
    public void Agent_ball_is_a_two_dip_round_capped_ring_and_nothing_else()
    {
        var state = State(withMedia: false);
        var ball = IslandMetrics.SplitIsletRects(state)[0];
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme, null,
            new IslandRendererOptions { SplitRingAngleDegrees = 30 });
        using var bitmap = renderer.RenderToBitmap(state, 4f);

        // Radial run of the ring along the middle of the arc must be 2 DIP (+/- rounding).
        var angle = (30f + (SplitSatelliteStyle.RingSweepDegrees / 2)) * Math.PI / 180;
        var run = 0;
        for (var r = 0f; r < ball.Width / 2; r += 0.125f)
        {
            var pixel = At(bitmap, ball.MidX + (float)(Math.Cos(angle) * r), ball.MidY + (float)(Math.Sin(angle) * r), 4f);
            if (pixel.Blue > pixel.Red + 40 && pixel.Alpha > 100)
            {
                run++;
            }
        }

        Assert.InRange(run * 0.125f, 1.6f, 2.6f);

        // The middle of the ball stays empty: the arc is a ring, not a filled disc.
        var centre = At(bitmap, ball.MidX, ball.MidY, 4f);
        Assert.False(centre.Blue > centre.Red + 40, "the ring must not fill the ball: " + centre);

        // Round caps: pixels just outside the arc ends keep a partial (anti-aliased) alpha instead of
        // a hard perpendicular edge, i.e. the end is a disc, not a line.
        var endAngle = 30f * Math.PI / 180;
        var ringRadius = (ball.Width / 2) - SplitSatelliteStyle.RingInsetDip;
        var beforeEnd = At(bitmap, ball.MidX + (float)(Math.Cos(endAngle) * ringRadius) - 1.2f,
            ball.MidY + (float)(Math.Sin(endAngle) * ringRadius), 4f);
        Assert.True(beforeEnd.Blue > beforeEnd.Red + 20,
            "round cap missing: the ring must extend beyond the nominal start angle, got " + beforeEnd);
    }

    [Fact]
    public void Cover_well_has_a_one_dip_hairline_and_a_recessed_top_shade()
    {
        var state = State(withMedia: true);
        var ball = IslandMetrics.SplitIsletRects(state)[1];
        var frame = IslandMetrics.SplitIsletThumbnailRect(ball);
        // Round 2: inset 5 DIP per side, so the well is the ball minus 10 (30 DIP on a 40 DIP ball).
        Assert.Equal(ball.Width - 10f, frame.Width, 0.001f);
        Assert.Equal(frame.Width * 0.225f, IslandMetrics.SplitIsletThumbnailRadius(frame), 0.001f);

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, 4f);
        var hairline = At(bitmap, frame.Left + 0.35f, frame.MidY, 4f);
        var interior = At(bitmap, frame.Left + 2.2f, frame.MidY, 4f);
        Assert.True(hairline.Red + hairline.Green + hairline.Blue >= interior.Red + interior.Green + interior.Blue + 8,
            "cover hairline missing: " + hairline + " vs " + interior);

        var top = At(bitmap, frame.MidX, frame.Top + 1, 4f);
        var lower = At(bitmap, frame.MidX, frame.Top + (frame.Height * 0.7f), 4f);
        Assert.True(top.Red + top.Green + top.Blue < lower.Red + lower.Green + lower.Blue,
            "the cover's top inner shade must darken the top edge: " + top + " vs " + lower);
    }

    [Fact]
    public void Contract_frames_reproduce_the_settled_legacy_look()
    {
        var legacy = State();
        var ball = IslandMetrics.SplitIsletRects(legacy)[1];
        var frame = IslandMetrics.SplitIsletThumbnailRect(ball);
        var shellRect = IslandMetrics.ShellRect(legacy);
        var agentBall = IslandMetrics.SplitIsletRects(legacy)[0];
        var frames = new[]
        {
            new IslandRenderState.IslandSatelliteFrame(
                SplitSatelliteState.Idle,
                agentBall.MidX - shellRect.MidX,
                agentBall.MidY - shellRect.MidY,
                1, 0, 0, 0, 0, 30, 0),
            new IslandRenderState.IslandSatelliteFrame(
                SplitSatelliteState.Idle,
                ball.MidX - shellRect.MidX,
                ball.MidY - shellRect.MidY,
                1, 0, 0, 0, 0, 30, 0),
        };
        var legacyState = legacy;
        var contractState = legacy with { SatelliteFrames = frames };

        using var rendererA = new IslandRenderer(IslandTestFixtures.Theme, null,
            new IslandRendererOptions { SplitRingAngleDegrees = 30 });
        using var rendererB = new IslandRenderer(IslandTestFixtures.Theme, null,
            new IslandRendererOptions { SplitRingAngleDegrees = 30 });
        using var a = rendererA.RenderToBitmap(legacyState, 1.5f);
        using var b = rendererB.RenderToBitmap(contractState, 1.5f);
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        var different = 0;
        for (var i = 0; i < a.Pixels.Length; i++)
        {
            if (a.Pixels[i] != b.Pixels[i])
            {
                different++;
            }
        }

        Assert.True(different < a.Pixels.Length / 200,
            "the contract path must reproduce the settled legacy pixels, " + different + " differ");
        Assert.True(frame.Width > 0);
    }

    [Fact]
    public void Contract_frames_apply_scale_stretch_press_and_ring_angle()
    {
        var baseline = State();
        var shell = IslandMetrics.ShellRect(baseline);
        var ball = IslandMetrics.SplitIsletRects(baseline)[1];
        var dx = ball.MidX - shell.MidX;
        var dy = ball.MidY - shell.MidY;
        var agentBall = IslandMetrics.SplitIsletRects(baseline)[0];
        var agentDx = agentBall.MidX - shell.MidX;
        var agentDy = agentBall.MidY - shell.MidY;

        IslandRenderState WithFrames(double scale, double stretchX, double ring, double press) => baseline with
        {
            SatelliteFrames =
            [
                new IslandRenderState.IslandSatelliteFrame(SplitSatelliteState.Idle, agentDx, agentDy, 1, 0, 0, 0, 0, ring, 0),
                new IslandRenderState.IslandSatelliteFrame(SplitSatelliteState.Idle, dx, dy, scale, stretchX, 0, 0, 0, 30, press),
            ],
        };

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme, null,
            new IslandRendererOptions { SplitRingAngleDegrees = 30 });

        int Width(float scale, float stretchX)
        {
            using var bitmap = renderer.RenderToBitmap(WithFrames(scale, stretchX, 30, 0), 1.5f);
            var min = int.MaxValue;
            var max = int.MinValue;
            for (var y = (int)((ball.Top - 6) * 1.5f); y < (int)((ball.Bottom + 6) * 1.5f); y++)
            {
                for (var x = (int)((ball.Left - 6) * 1.5f); x < (int)((ball.Right + 6) * 1.5f); x++)
                {
                    if (bitmap.Pixels[(y * bitmap.Width) + x].Alpha > 120)
                    {
                        min = Math.Min(min, x);
                        max = Math.Max(max, x);
                    }
                }
            }

            return max - min;
        }

        Assert.True(Width(1f, 0f) > Width(0.9f, 0f), "Scale must shrink the ball");
        Assert.True(Width(1f, 0.08f) > Width(1f, 0f), "StretchX must widen the ball");

        // Ring angle: the blue pixel mask must move with RingAngleDegrees. (The previous version
        // compared only the X centroid, which is symmetric for a 180° arc at 0° vs 180°, so it could
        // never see the rotation; the mask difference is a strictly stronger check.)
        HashSet<(int X, int Y)> BlueMask(float ring)
        {
            using var bitmap = renderer.RenderToBitmap(WithFrames(1, 0, ring, 0), 1.5f);
            var mask = new HashSet<(int X, int Y)>();
            for (var y = (int)((agentBall.Top - 6) * 1.5f); y < (int)((agentBall.Bottom + 6) * 1.5f); y++)
            {
                for (var x = (int)((agentBall.Left - 6) * 1.5f); x < (int)((agentBall.Right + 6) * 1.5f); x++)
                {
                    var pixel = bitmap.Pixels[(y * bitmap.Width) + x];
                    if (pixel.Blue > pixel.Red + 40 && pixel.Alpha > 100)
                    {
                        mask.Add((x, y));
                    }
                }
            }

            Assert.True(mask.Count > 40, "agent ring missing in the frames path");
            return mask;
        }

        var straight = BlueMask(0);
        var rotated = BlueMask(90);
        var differing = straight.Count(m => !rotated.Contains(m)) + rotated.Count(m => !straight.Contains(m));
        Assert.True(differing > straight.Count / 2, "RingAngleDegrees must rotate the arc, differing pixels " + differing);
    }
}