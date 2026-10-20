using FluidBar.Island;
using SkiaSharp;
using Xunit;

namespace FluidBar.Island.Tests;

/// <summary>
/// Task-40: the expanded layered window carries a large transparent hit-test margin. The shadow used
/// to be painted into that margin, which showed up on the desktop as a translucent grey block left of
/// the panel. The surface alpha outside the expanded shell must be exactly zero.
/// </summary>
public sealed class IslandSurfaceAlphaTests
{
    private const float DpiScale = 1.5f;

    [Fact]
    public void Expanded_surface_padding_is_fully_transparent()
    {
        var state = IslandTestFixtures.Expanded(4);
        using var renderer = new IslandRenderer();
        using var bitmap = renderer.RenderToBitmap(state, DpiScale);

        var shell = IslandMetrics.ShellRect(state);
        var left = (int)Math.Floor(shell.Left * DpiScale) - 2;
        var top = (int)Math.Floor(shell.Top * DpiScale) - 2;
        var right = (int)Math.Ceiling(shell.Right * DpiScale) + 2;
        var bottom = (int)Math.Ceiling(shell.Bottom * DpiScale) + 2;

        var opaque = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (x >= left && x <= right && y >= top && y <= bottom)
                {
                    continue;
                }

                if (bitmap.GetPixel(x, y).Alpha != 0)
                {
                    opaque++;
                }
            }
        }

        Assert.Equal(0, opaque);

        // The same samples the user reported on the left of the panel.
        foreach (var (x, y) in new[] { (12, 288), (24, 288), (left - 6, 288), (bitmap.Width / 2, 12) })
        {
            Assert.Equal(0, bitmap.GetPixel(x, y).Alpha);
        }
    }
}
