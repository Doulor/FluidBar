using System.Globalization;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// The satellite glasses fade on hover instead of swapping colour inside one frame: a single progress
/// drives the hover alpha and the 6 % grow, 120 ms ease-out in, 160 ms ease-out out.
/// </summary>
public sealed class SatelliteHoverFadeTests
{
    private const float RenderScale = 4f;

    [Fact]
    public void Hover_eases_in_and_out_monotonically_without_a_one_frame_flip()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var hovered = IslandTestFixtures.RailInteractionState(1, hover: MiniRailSlot.Previous);
        var plain = IslandTestFixtures.RailInteractionState(1, hover: MiniRailSlot.None);
        var shell = IslandMetrics.ShellRect(hovered);
        var frame = MiniRailLayout.Measure(hovered, shell, new SKRect[3], new SKRect[3]);
        var band = new SKRect(
            frame.Previous.Left - 6, frame.Previous.Top - 6, frame.Previous.Right + 6, frame.Previous.Bottom + 6);

        // Every RenderToBitmap draws one frame, and the strip advances its hover fade by the frame delta
        // (1/120 s), so rendering eight times samples eight real frames.
        // 1/120 s per rendered frame: 16 frames cover the 120 ms enter, 20 the 160 ms leave.
        var enter = Sample(renderer, hovered, band, frames: 16);
        var leave = Sample(renderer, plain, band, frames: 20);

        // Enter: monotonic, no single frame may jump more than 60 % of the range (a one-frame flip).
        for (var i = 1; i < enter.Count; i++)
        {
            Assert.True(enter[i].Progress >= enter[i - 1].Progress,
                $"hover enter must never fall (frame {i}: {enter[i - 1].Progress:0.###} -> {enter[i].Progress:0.###})");
            Assert.True(enter[i].Progress > enter[i - 1].Progress || enter[i].Progress >= 0.999f,
                $"hover enter must keep rising until it settles (frame {i})");
            Assert.True(enter[i].Progress - enter[i - 1].Progress < 0.6,
                $"hover enter must not flip in one frame (frame {i} jumped {enter[i].Progress - enter[i - 1].Progress:0.###})");
        }

        Assert.True(enter[^1].Progress > 0.99, $"hover enter must settle at 1 (got {enter[^1].Progress:0.###})");
        Assert.InRange(leave[0].Progress, 0.70, 0.99); // one 1/120 s frame into the 160 ms leave (ease-out)
        for (var i = 1; i < leave.Count; i++)
        {
            Assert.True(leave[i].Progress <= leave[i - 1].Progress,
                $"hover leave must never rise (frame {i}: {leave[i - 1].Progress:0.###} -> {leave[i].Progress:0.###})");
            Assert.True(leave[i].Progress < leave[i - 1].Progress || leave[i].Progress <= 0.001f,
                $"hover leave must keep falling until it settles (frame {i})");
            Assert.True(leave[i - 1].Progress - leave[i].Progress < 0.6,
                $"hover leave must not flip in one frame (frame {i})");
        }

        Assert.True(leave[^1].Progress < 0.01, $"hover leave must settle at 0 (got {leave[^1].Progress:0.###})");

        // One progress drives both channels: alpha is the progress itself and the glass grows 6 %.
        foreach (var sample in enter.Concat(leave))
        {
            Assert.Equal(1f + (0.06f * sample.Progress), sample.Scale, 4);
        }

        WriteSheet(Thin(enter), Thin(leave));
    }

    /// <summary>Eight evenly spaced samples for the contact sheet (the asserts use every frame).</summary>
    private static List<(float Progress, float Scale, SKBitmap Image)> Thin(
        List<(float Progress, float Scale, SKBitmap Image)> all)
    {
        var result = new List<(float Progress, float Scale, SKBitmap Image)>(8);
        for (var i = 0; i < 8; i++)
        {
            result.Add(all[(int)MathF.Round(i * (all.Count - 1) / 7f)]);
        }

        return result;
    }

    private static List<(float Progress, float Scale, SKBitmap Image)> Sample(
        IslandRenderer renderer, IslandRenderState state, SKRect band, int frames)
    {
        var samples = new List<(float Progress, float Scale, SKBitmap Image)>(frames);
        for (var i = 0; i < frames; i++)
        {
            using var full = renderer.RenderToBitmap(state, RenderScale);
            var crop = new SKRect(
                band.Left * RenderScale,
                band.Top * RenderScale,
                band.Right * RenderScale,
                band.Bottom * RenderScale);
            var bitmap = new SKBitmap(new SKImageInfo(
                (int)MathF.Ceiling(crop.Width), (int)MathF.Ceiling(crop.Height),
                SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(new SKColor(12, 12, 15));
                canvas.DrawBitmap(full, crop, new SKRect(0, 0, bitmap.Width, bitmap.Height));
                canvas.Flush();
            }

            samples.Add((renderer.SatelliteHoverProgress, renderer.SatelliteHoverScale, bitmap));
        }

        return samples;
    }

    private static void WriteSheet(
        List<(float Progress, float Scale, SKBitmap Image)> enter,
        List<(float Progress, float Scale, SKBitmap Image)> leave)
    {
        const int cell = 150;
        const int label = 34;
        var width = 8 * cell;
        var height = (2 * (cell + label)) + 46;
        using var sheet = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(10, 10, 12));
        using var text = new SKPaint
        {
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI"),
            Color = new SKColor(235, 235, 240),
            TextSize = 16,
        };
        using var small = new SKPaint
        {
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI"),
            Color = new SKColor(165, 165, 175),
            TextSize = 11,
        };

        canvas.DrawText("satellite hover — enter 120 ms ease-out (top) / leave 160 ms ease-out (bottom), 8 frames each, 4x",
            14, 26, text);
        DrawRow(canvas, enter, 40, "enter", text, small, cell, label);
        DrawRow(canvas, leave, 40 + cell + label, "leave", text, small, cell, label);

        var dir = System.IO.Path.Combine(IslandTestFixtures.RepoRoot, "build", "reports", "island-motion");
        System.IO.Directory.CreateDirectory(dir);
        using var image = SKImage.FromBitmap(sheet);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = System.IO.File.Create(System.IO.Path.Combine(dir, "satellite-hover-8f.png"));
        data.SaveTo(stream);
    }

    private static void DrawRow(
        SKCanvas canvas,
        List<(float Progress, float Scale, SKBitmap Image)> samples,
        int top,
        string phase,
        SKPaint text,
        SKPaint small,
        int cell,
        int label)
    {
        canvas.DrawText(phase, 14, top + 14, text);
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var left = (i * cell) + 6;
            canvas.DrawBitmap(sample.Image, new SKRect(left, top, left + cell - 12, top + cell - 12));
            canvas.DrawText(string.Create(CultureInfo.InvariantCulture,
                $"f{i}  p={sample.Progress:0.00}  x{sample.Scale:0.000}  a={sample.Progress:0.00}"),
                left, top + cell + 4, small);
        }
    }
}
