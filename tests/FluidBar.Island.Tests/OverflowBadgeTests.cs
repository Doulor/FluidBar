using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Compact "+N" overflow chip: the pill reserves a right-hand column, paints the chip inside it and
/// hit-tests the chip before the expand target. Hidden when the scheduler has nothing queued.
/// </summary>
public sealed class OverflowBadgeTests
{
    [Fact]
    public void Overflow_chip_is_painted_inside_the_reserved_column()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Overflow(3);
        var badge = IslandMetrics.OverflowBadgeRect(state);
        var shell = IslandMetrics.ShellRect(state);
        Assert.True(badge.Width > 0 && badge.Height > 0);
        Assert.True(badge.Right <= shell.Right + 0.01f);
        Assert.Equal(state.ShellWidth - LayoutTokens.OverflowBadgeReserve, IslandMetrics.CompactContentWidth(state), 3);

        using var withBadge = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        using var withoutBadge = renderer.RenderToBitmap(IslandTestFixtures.Overflow(0), IslandTestFixtures.Scale);
        Assert.True(Ink(withBadge, badge) > Ink(withoutBadge, badge),
            "the +N chip did not paint ink inside its rectangle");
    }

    [Fact]
    public void Overflow_chip_is_hit_before_the_expand_target()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Overflow(3);
        var badge = IslandMetrics.OverflowBadgeRect(state);
        Assert.Equal(IslandHitKind.OverflowBadge,
            renderer.HitTest(state, badge.MidX, badge.MidY).Kind);

        var shell = IslandMetrics.ShellRect(state);
        Assert.Equal(IslandHitKind.Compact, renderer.HitTest(state, shell.Left + 12, shell.MidY).Kind);

        var plain = state with { OverflowCount = 0, ShellWidth = state.ShellWidth - LayoutTokens.OverflowBadgeReserve };
        Assert.Equal(IslandHitKind.Compact, renderer.HitTest(plain, badge.MidX, badge.MidY).Kind);
    }

    private static int Ink(SKBitmap bitmap, SKRect rectDip)
    {
        var scale = IslandTestFixtures.Scale;
        var left = Math.Max(0, (int)Math.Floor(rectDip.Left * scale));
        var top = Math.Max(0, (int)Math.Floor(rectDip.Top * scale));
        var right = Math.Min(bitmap.Width, (int)Math.Ceiling(rectDip.Right * scale));
        var bottom = Math.Min(bitmap.Height, (int)Math.Ceiling(rectDip.Bottom * scale));
        var pixels = bitmap.Pixels;
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            var row = y * bitmap.Width;
            for (var x = left; x < right; x++)
            {
                if (pixels[row + x].Alpha > 40)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
