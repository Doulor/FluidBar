using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Measured transient-strip geometry (host-local DIP): the 3-island row plus the far-page dots.
/// Produced by <see cref="IslandRenderer.MeasureStrip"/> for host integration (UIA/click-through) and
/// tests; the render loop uses an allocation-free internal copy of the same rectangles.
/// </summary>
public sealed record IslandStripLayout
{
    /// <summary>Strip envelope (islands + dots).</summary>
    public required SKRect Strip { get; init; }

    /// <summary>Previous mini-island (26×26); empty when page 0 is active.</summary>
    public required SKRect Previous { get; init; }

    /// <summary>Current mini-island (64×28, icon + label).</summary>
    public required SKRect Current { get; init; }

    /// <summary>Next mini-island (26×26); empty when the last page is active.</summary>
    public required SKRect Next { get; init; }

    /// <summary>Committed page index.</summary>
    public required int Page { get; init; }

    /// <summary>Far-page dots on the left (nearest first).</summary>
    public required SKRect[] DotsLeft { get; init; }

    /// <summary>Far-page dots on the right (nearest first).</summary>
    public required SKRect[] DotsRight { get; init; }

    /// <summary>Target page for a left dot index (or -1).</summary>
    public int LeftDotPage(int index) => index >= 0 && index < DotsLeft.Length ? Page - 2 - index : -1;

    /// <summary>Target page for a right dot index (or -1).</summary>
    public int RightDotPage(int index) => index >= 0 && index < DotsRight.Length ? Page + 2 + index : -1;

    /// <summary>Hit-tests the untransformed layout (tests/host hit-region building).</summary>
    public IslandHitTarget HitTest(float x, float y)
    {
        if (Page > 0 && Previous.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, Page - 1);
        }

        if (Current.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, Page);
        }

        if (Page < FluidBar.Core.Interaction.PagerConstants.PageCount - 1 && Next.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, Page + 1);
        }

        for (var i = 0; i < DotsLeft.Length; i++)
        {
            if (DotsLeft[i].Inflate((float)IslandMetrics.DotHitSize / 2).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.DotLeft, LeftDotPage(i));
            }
        }

        for (var i = 0; i < DotsRight.Length; i++)
        {
            if (DotsRight[i].Inflate((float)IslandMetrics.DotHitSize / 2).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.DotRight, RightDotPage(i));
            }
        }

        return default;
    }
}
