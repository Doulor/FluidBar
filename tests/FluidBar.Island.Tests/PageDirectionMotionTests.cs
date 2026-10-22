using System.Globalization;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Page-switch direction: the incoming page slides in 6 DIP from the travel direction and is 1.8 %
/// smaller at the far edge of the transition; the outgoing page does the opposite. The pose is a pure
/// function of the continuous page position, so a rapid second switch continues from where the eye is.
/// </summary>
public sealed class PageDirectionMotionTests
{
    [Fact]
    public void Pages_slide_in_from_the_travel_direction_and_continue_on_a_rapid_switch()
    {
        // Settled page keeps the identity pose.
        Assert.Equal((0f, 1f), IslandRenderer.PageEnterPose(0, 0));
        Assert.Equal((0f, 1f), IslandRenderer.PageEnterPose(2, 2));

        // Forward at the half-way point: page 1 is still 0.5 pages ahead, so it sits 3 DIP to the right
        // (travel direction) and is 0.9 % smaller; the outgoing page 0 mirrors it.
        var incomingForward = IslandRenderer.PageEnterPose(0.5, 1);
        var outgoingForward = IslandRenderer.PageEnterPose(0.5, 0);
        Assert.Equal(3f, incomingForward.SlideDip, 3);
        Assert.Equal(0.991f, incomingForward.Scale, 4);
        Assert.Equal(-3f, outgoingForward.SlideDip, 3);
        Assert.Equal(0.991f, outgoingForward.Scale, 4);

        // Backward is the mirror image: the sign of (pagePosition - page) flips with the travel direction.
        var incomingBackward = IslandRenderer.PageEnterPose(0.5, 0);
        Assert.Equal(-3f, incomingBackward.SlideDip, 3);
        Assert.Equal(incomingForward.Scale, incomingBackward.Scale, 4);

        // Continuity: no jump at a mid-transition reversal, so a rapid switch continues from the pose.
        var before = IslandRenderer.PageEnterPose(0.5, 1).SlideDip;
        var after = IslandRenderer.PageEnterPose(0.5001, 1).SlideDip;
        Assert.True(Math.Abs(after - before) < 0.01, "the enter pose must be continuous in pagePosition");
        Assert.Equal(IslandRenderer.PageEnterPose(0.5, 1).SlideDip, IslandRenderer.PageEnterPose(0.5, 1).SlideDip, 4);

        // Reduced motion never slides or scales.
        Assert.Equal((0f, 1f), IslandRenderer.PageEnterPose(0.5, 1, reducedMotion: true));

        WriteSheet();
    }

    private static void WriteSheet()
    {
        const int frames = 8;
        const int cellWidth = 120;
        const int cellHeight = 260;
        var width = frames * cellWidth;
        var height = (2 * (cellHeight + 40)) + 46;
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
            TextSize = 10,
        };

        canvas.DrawText("page direction — forward (top) / backward (bottom), 8 frames each, content seam at 1x, 6 DIP slide + 1.8 % scale",
            14, 26, text);

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        DrawRow(canvas, renderer, forward: true, top: 40, text, small, cellWidth, cellHeight);
        DrawRow(canvas, renderer, forward: false, top: 40 + cellHeight + 40, text, small, cellWidth, cellHeight);

        var dir = System.IO.Path.Combine(IslandTestFixtures.RepoRoot, "build", "reports", "island-motion");
        System.IO.Directory.CreateDirectory(dir);
        using var image = SKImage.FromBitmap(sheet);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = System.IO.File.Create(System.IO.Path.Combine(dir, "page-direction-8f.png"));
        data.SaveTo(stream);
    }

    private static void DrawRow(
        SKCanvas canvas, IslandRenderer renderer, bool forward, int top, SKPaint text, SKPaint small,
        int cellWidth, int cellHeight)
    {
        canvas.DrawText(forward ? "forward" : "backward", 14, top + 12, text);
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(IslandTestFixtures.Expanded(0)));
        var crop = new SKRect(content.MidX - 60, content.Top, content.MidX + 60, content.Bottom);

        for (var i = 0; i < 8; i++)
        {
            var t = i / 7.0;
            var position = forward ? t : 1 - t;
            var selected = forward ? 0 : 1;
            var state = IslandTestFixtures.Expanded(selected, pagePosition: position);
            using var full = renderer.RenderToBitmap(state, 1f);
            var left = (i * cellWidth) + 4;
            using (var cell = new SKBitmap(new SKImageInfo(cellWidth, cellHeight, SKColorType.Bgra8888, SKAlphaType.Premul)))
            {
                using (var cellCanvas = new SKCanvas(cell))
                {
                    cellCanvas.Clear(new SKColor(12, 12, 15));
                    cellCanvas.DrawBitmap(full, crop, new SKRect(0, 0, cellWidth, cellHeight));
                    cellCanvas.Flush();
                }

                canvas.DrawBitmap(cell, left, top + 18);
            }

            var pose = IslandRenderer.PageEnterPose(position, forward ? 1 : 0);
            canvas.DrawText(string.Create(CultureInfo.InvariantCulture,
                $"f{i} p={position:0.00} slide={pose.SlideDip:0.0} x{pose.Scale:0.000}"),
                left, top + cellHeight + 32, small);
        }
    }
}
