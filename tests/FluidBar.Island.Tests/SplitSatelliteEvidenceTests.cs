using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Deterministic evidence for the split satellites. Skipped unless FLUIDBAR_SATELLITE_EVIDENCE=1;
/// writes 4x/1.5x renders, the geometry and the pixel samples used by the audit report.
/// </summary>
public sealed class SplitSatelliteEvidenceTests
{
    /// <summary>Fixed ring angle for deterministic captures (arc across the lower right).</summary>
    private const float RingAngle = 30f;

    [Fact]
    public void Emit_satellite_polish_frames()
    {
        if (Environment.GetEnvironmentVariable("FLUIDBAR_SATELLITE_EVIDENCE") != "1")
        {
            return;
        }

        var dir = Environment.GetEnvironmentVariable("FLUIDBAR_SATELLITE_DIR")
            ?? Path.Combine(IslandTestFixtures.RepoRoot, "build", "reports", "ui-audit", "satellite");
        Directory.CreateDirectory(dir);
        var tag = Environment.GetEnvironmentVariable("FLUIDBAR_SATELLITE_TAG") ?? "after";

        var state = BuildState();
        var shell = IslandMetrics.ShellRect(state);
        var balls = IslandMetrics.SplitIsletRects(state);
        var media = balls[1];
        var agent = balls[0];
        var frame = IslandMetrics.SplitIsletThumbnailRect(media);
        var lines = new List<string>
        {
            "shell=" + F(shell.Left) + "," + F(shell.Top) + "," + F(shell.Width) + "," + F(shell.Height),
            "agentBall=" + F(agent.Left) + "," + F(agent.Top) + "," + F(agent.Width) + "," + F(agent.Height),
            "mediaBall=" + F(media.Left) + "," + F(media.Top) + "," + F(media.Width) + "," + F(media.Height),
            "innerFrame=" + F(frame.Left) + "," + F(frame.Top) + "," + F(frame.Width) + "," + F(frame.Height),
            "innerFrameRadius=" + F(IslandMetrics.SplitIsletThumbnailRadius(frame)),
            "gap=" + F(balls[0].Left - shell.Right),
        };

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme, null,
            new IslandRendererOptions { SplitRingAngleDegrees = RingAngle });
        foreach (var (name, scale) in new[] { ("4x", 4f), ("1x5", 1.5f) })
        {
            using var bitmap = renderer.RenderToBitmap(state, scale);
            IslandTestFixtures.SavePng(bitmap, Path.Combine(dir, "satellite-" + tag + "-" + name + ".png"));
        }

        using var big = renderer.RenderToBitmap(state, 4f);
        SKColor At(float xDip, float yDip)
            => big.Pixels[((int)Math.Round(yDip * 4) * big.Width) + (int)Math.Round(xDip * 4)];

        var pill = At(shell.MidX, shell.Top + 4);
        var ballTop = At(media.Left + 2, media.MidY);
        lines.Add("pillFill=" + C(pill));
        lines.Add("ballFill=" + C(ballTop));
        lines.Add("ballFillEqualsPill=" + (pill == ballTop));

        // Top rim: first opaque scanline inside the circle at the top centre.
        var rim = SKColors.Transparent;
        for (var dy = 0f; dy < 3f; dy += 0.25f)
        {
            var pixel = At(agent.MidX, agent.Top + dy);
            if (pixel.Alpha > 200)
            {
                rim = pixel;
                break;
            }
        }

        lines.Add("topRim=" + C(rim));
        lines.Add("topRimLift=" + (rim.Red - ballTop.Red));
        lines.Add("topRimImpliedWhiteAlpha=" +
            F((rim.Red - ballTop.Red) / (double)Math.Max(1, 255 - ballTop.Red)));

        var bottom = At(agent.MidX, agent.Bottom - 3);
        lines.Add("bottomShade=" + C(bottom));
        lines.Add("bottomDrop=" + (ballTop.Red - bottom.Red));

        // Cover hairline: the frame's inner edge (1 DIP) must be lighter than the cover behind it.
        var hairline = At(frame.MidX, frame.Top + 0.35f);
        var justInside = At(frame.MidX, frame.Top + 2.2f);
        lines.Add("frameHairline=" + C(hairline));
        lines.Add("frameInterior=" + C(justInside));
        lines.Add("frameHairlineLift=" + (hairline.Red + hairline.Green + hairline.Blue - justInside.Red - justInside.Green - justInside.Blue));

        // Accent scan: the satellites must not contain the theme accent anywhere except the agent ring.
        var accent = IslandTestFixtures.Theme.Accent;
        var ring = RingAnnulus(agent);
        var accentOutsideRing = 0;
        var accentInsideRing = 0;
        for (var y = (int)agent.Top; y < (int)Math.Ceiling(agent.Bottom); y++)
        {
            for (var x = (int)agent.Left; x < (int)Math.Ceiling(agent.Right); x++)
            {
                var pixel = At(x + 0.5f, y + 0.5f);
                if (pixel.Alpha < 40)
                {
                    continue;
                }

                if (Math.Abs(pixel.Red - accent.Red) > 26
                    || Math.Abs(pixel.Green - accent.Green) > 26
                    || Math.Abs(pixel.Blue - accent.Blue) > 26)
                {
                    continue;
                }

                if (ring.Contains(x + 0.5f, y + 0.5f))
                {
                    accentInsideRing++;
                }
                else
                {
                    accentOutsideRing++;
                }
            }
        }

        lines.Add("accentPixelsOutsideRing=" + accentOutsideRing);
        lines.Add("accentPixelsInRing=" + accentInsideRing);

        // Ring width along a radial ray through the middle of the arc.
        var angleDeg = RingAngle + (SplitSatelliteStyle.RingSweepDegrees / 2);
        var angle = angleDeg * Math.PI / 180.0;
        var blueRun = 0;
        for (var r = 0f; r < agent.Width / 2; r += 0.25f)
        {
            var pixel = At(
                (float)(agent.MidX + (Math.Cos(angle) * r)),
                (float)(agent.MidY + (Math.Sin(angle) * r)));
            if (pixel.Blue > pixel.Red + 40 && pixel.Alpha > 100)
            {
                blueRun++;
            }
        }

        lines.Add("ringWidthDip=" + F(blueRun * 0.25f * 2));   // 4x: 0.25 DIP steps per sample pair
        lines.Add("ringSweepDegrees=" + F(SplitSatelliteStyle.RingSweepDegrees));


        // 8x plates rendered region-by-region (a full-surface 8x pass is 12 MP and stalls the run).
        SavePlate(renderer, state, agent, 8f, 6f, Path.Combine(dir, "satellite-" + tag + "-ring-8x.png"));
        SavePlate(renderer, state, media, 8f, 6f, Path.Combine(dir, "satellite-" + tag + "-media-8x.png"));

        var ringRect = SplitSatelliteStyle.RingRect(agent);
        var ringRadius = ringRect.Width / 2;
        var ringBlue = 0;
        var inner = double.MaxValue;
        var outer = 0d;
        var bins = new bool[360];
        using (var plate = RenderPlate(renderer, state, agent, 8f, 6f))
        {
            var centreX = (agent.MidX - agent.Left + 6) * 8;
            var centreY = (agent.MidY - agent.Top + 6) * 8;
            for (var y = 0; y < plate.Height; y++)
            {
                for (var x = 0; x < plate.Width; x++)
                {
                    var pixel = plate.Pixels[(y * plate.Width) + x];
                    if (pixel.Alpha <= 100 || pixel.Blue <= pixel.Red + 40)
                    {
                        continue;
                    }

                    var dx = ((x + 0.5) - centreX) / 8.0;
                    var dy = ((y + 0.5) - centreY) / 8.0;
                    var r = Math.Sqrt((dx * dx) + (dy * dy));
                    if (r > ringRadius + 4 || r < ringRadius - 4)
                    {
                        continue;
                    }

                    ringBlue++;
                    inner = Math.Min(inner, r);
                    outer = Math.Max(outer, r);
                    var degrees = ((Math.Atan2(dy, dx) * 180 / Math.PI) + 360) % 360;
                    bins[(int)degrees % 360] = true;
                }
            }
        }

        var coveredDegrees = bins.Count(b => b);
        var fullRingPixels = 2 * Math.PI * ringRadius * (outer - inner) * 64;
        lines.Add("ringBluePixels8x=" + ringBlue);
        lines.Add("ringRadialWidthDip=" + F(outer - inner));
        lines.Add("ringMeanRadiusDip=" + F((outer + inner) / 2));
        lines.Add("ringCoveredDegrees=" + coveredDegrees);
        lines.Add("ringArcFractionByAngle=" + F(coveredDegrees / 360.0));
        lines.Add("ringArcFractionByArea=" + F(ringBlue / fullRingPixels));

        // Black margin between the ball edge and the cover well, measured on the mid line.
        var marginLeft = 0f;
        for (var x = media.Left; x < media.MidX; x += 0.25f)
        {
            var pixel = At(x, media.MidY);
            if (pixel.Alpha > 200)
            {
                marginLeft = x - media.Left;
                break;
            }
        }

        var frameStart = frame.Left - media.Left;
        lines.Add("blackMarginLeftDip=" + F(frameStart));
        lines.Add("ballEdgeDip=" + F(marginLeft));
        lines.Add("frameSideDip=" + F(frame.Width));

        File.WriteAllLines(Path.Combine(dir, "satellite-" + tag + "-samples.txt"), lines);
        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }
    }


    private static SKBitmap RenderPlate(IslandRenderer renderer, in IslandRenderState state, SKRect areaDip,
        float scale, float padDip)
    {
        var left = areaDip.Left - padDip;
        var top = areaDip.Top - padDip;
        var width = areaDip.Width + (padDip * 2);
        var height = areaDip.Height + (padDip * 2);
        var info = new SKImageInfo(
            (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
            SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Translate((float)(-left * scale), (float)(-top * scale));
        renderer.Render(canvas, state, scale, new SKRect((float)left, (float)top, (float)(left + width), (float)(top + height)));
        canvas.Flush();
        return bitmap;
    }

    private static void SavePlate(IslandRenderer renderer, in IslandRenderState state, SKRect areaDip, float scale,
        float padDip, string path)
    {
        using var bitmap = RenderPlate(renderer, state, areaDip, scale, padDip);
        IslandTestFixtures.SavePng(bitmap, path);
    }

    private static void SaveCrop(SKBitmap source, float scale, SKRect areaDip, string path, float padDip)
    {
        var left = (int)Math.Max(0, (areaDip.Left - padDip) * scale);
        var top = (int)Math.Max(0, (areaDip.Top - padDip) * scale);
        var right = (int)Math.Min(source.Width, (areaDip.Right + padDip) * scale);
        var bottom = (int)Math.Min(source.Height, (areaDip.Bottom + padDip) * scale);
        var info = new SKImageInfo(Math.Max(1, right - left), Math.Max(1, bottom - top), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var crop = new SKBitmap(info);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.DrawBitmap(source, new SKRect(left, top, right, bottom), new SKRect(0, 0, info.Width, info.Height));
        }

        IslandTestFixtures.SavePng(crop, path);
    }

    private static SKRect RingAnnulus(SKRect ball)
    {
        var rect = SplitSatelliteStyle.RingRect(ball);
        var half = SplitSatelliteStyle.RingWidthDip + 1f;
        return new SKRect(rect.Left - half, rect.Top - half, rect.Right + half, rect.Bottom + half);
    }

    private static IslandRenderState BuildState()
    {
        var cover = CoverPng();
        var islets = new List<IslandSplitIslet>
        {
            new(ActivityKind.Agent, IconKind.Agent, "Agent", "运行中", PageRef.Agent, 0, null, true),
            new(ActivityKind.Media, IconKind.Media, "媒体", "Midnight Drive", PageRef.Media, 0,
                "46", false, cover, "audit-cover"),
        };
        return IslandTestFixtures.MediaCompact() with
        {
            SplitProgress = 1,
            SplitIsletHeight = 40,
            SplitIslets = islets.ToArray(),
        };
    }

    /// <summary>96x96 synthetic album cover: warm gradient plus a light disc, so contain-fit and the
    /// inner hairline are both measurable.</summary>
    private static byte[] CoverPng()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(96, 96, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            using var paint = new SKPaint { IsAntialias = true };
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(96, 96),
                [new SKColor(0x3A, 0x1E, 0x6E), new SKColor(0xE0, 0x5A, 0x4E)],
                [0f, 1f], SKShaderTileMode.Clamp);
            canvas.DrawRect(new SKRect(0, 0, 96, 96), paint);
            paint.Shader = null;
            paint.Color = new SKColor(0xFF, 0xD9, 0x8A, 0xC0);
            canvas.DrawCircle(52, 40, 22, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string F(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string C(SKColor color) => $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
}