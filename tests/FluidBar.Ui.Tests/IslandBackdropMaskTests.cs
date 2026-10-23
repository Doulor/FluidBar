using FluidBar.App.Hosting.Skia;
using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The composed capture contains the island itself; every island surface (shell and split capsules) has
/// to be smeared away before baking, otherwise a rotating agent capsule is captured into the glass and
/// makes the main pill blink with it.
/// </summary>
public sealed class IslandBackdropMaskTests
{
    private static BackdropFrame Frame()
    {
        const int width = 40;
        const int height = 12;
        // Left half dark, right half bright: a masked rect in between must be filled with a gradient,
        // which proves the smear ran instead of leaving the island's own pixels behind.
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                var value = (byte)(x < width / 2 ? 10 : 200);
                pixels[i] = value;
                pixels[i + 1] = value;
                pixels[i + 2] = value;
                pixels[i + 3] = 255;
            }
        }

        return new BackdropFrame(width, height, width * 4, pixels);
    }

    [Fact]
    public void Every_masked_rect_is_cleared()
    {
        var frame = Frame();
        (int X, int Y, int Width, int Height)[] masks = [(10, 2, 6, 4), (18, 6, 4, 3)];

        foreach (var mask in masks)
        {
            IslandBackdropProvider.MaskIslandArea(frame, mask);
        }

        foreach (var mask in masks)
        {
            var offset = ((mask.Y + 1) * frame.Stride) + ((mask.X + 1) * 4);
            var value = frame.Pixels[offset];
            Assert.True(value < 190, $"rect at {mask.X},{mask.Y} must be smeared, got {value}");
        }
    }
}
