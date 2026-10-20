using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Measured transient-rail geometry (host-local DIP): the 3-island row plus the far-page dots.
/// Produced by <see cref="IslandRenderer.MeasureStrip"/> for host integration (UIA/click-through) and
/// tests; the render loop uses an allocation-free internal copy of the same rectangles, and both come
/// from <see cref="MiniRailLayout.Measure"/>.
/// </summary>
public sealed record IslandStripLayout
{
    /// <summary>Rail envelope (islands + dots).</summary>
    public required SKRect Strip { get; init; }

    /// <summary>Previous island (26×26); empty when page 0 is active.</summary>
    public required SKRect Previous { get; init; }

    /// <summary>Current capsule (64..104 DIP wide × 28, icon + label).</summary>
    public required SKRect Current { get; init; }

    /// <summary>Resolved current capsule width in DIP (morphs between pages, 64..104).</summary>
    public float CapsuleWidth { get; init; }

    /// <summary>Next island (26×26); empty when the last page is active.</summary>
    public required SKRect Next { get; init; }

    /// <summary>Committed page index.</summary>
    public required int Page { get; init; }

    /// <summary>Total page count represented by the rail.</summary>
    public int PageCount { get; init; }

    /// <summary>Left overflow ellipsis (empty when every page fits).</summary>
    public SKRect OverflowLeft { get; init; } = SKRect.Empty;

    /// <summary>Right overflow ellipsis (empty when every page fits).</summary>
    public SKRect OverflowRight { get; init; } = SKRect.Empty;

    /// <summary>Far-page dots on the left (nearest first).</summary>
    public required SKRect[] DotsLeft { get; init; }

    /// <summary>Far-page dots on the right (nearest first).</summary>
    public required SKRect[] DotsRight { get; init; }

    /// <summary>
    /// Target page for a left dot (or -1 when the cluster is empty). Every dot in a cluster jumps to
    /// the farthest page in that direction — the prototype sends the left cluster to page 0 (01 Home).
    /// </summary>
    public int LeftDotPage(int index) => index >= 0 && index < DotsLeft.Length ? 0 : -1;

    /// <summary>
    /// Target page for a right dot (or -1 when the cluster is empty). The right cluster always jumps to
    /// the last page (06 Widgets).
    /// </summary>
    public int RightDotPage(int index)
        => index >= 0 && index < DotsRight.Length ? PageCount - 1 : -1;

    /// <summary>Hit-tests the rail layout (tests/host hit-region building).</summary>
    public IslandHitTarget HitTest(float x, float y)
    {
        if (!OverflowLeft.IsEmpty && OverflowLeft.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailOverflow, Index: 0);
        }

        if (!OverflowRight.IsEmpty && OverflowRight.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailOverflow, Index: 1);
        }

        if (Page > 0 && Previous.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, Page - 1);
        }

        if (Current.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, Page);
        }

        if (Page < PageCount - 1 && Next.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, Page + 1);
        }

        for (var i = 0; i < DotsLeft.Length; i++)
        {
            if (DotHitRect(DotsLeft[i]).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.DotLeft, LeftDotPage(i));
            }
        }

        for (var i = 0; i < DotsRight.Length; i++)
        {
            if (DotHitRect(DotsRight[i]).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.DotRight, RightDotPage(i));
            }
        }

        return default;
    }

    /// <summary>Prototype `.far-dot::after` hit area: 7 DIP wide, 15 DIP tall, centred on the dot.</summary>
    public static SKRect DotHitRect(SKRect dot)
    {
        var dx = Math.Max(0, (MiniRailLayout.DotHitWidth - dot.Width) / 2f);
        var dy = Math.Max(0, (MiniRailLayout.DotHitHeight - dot.Height) / 2f);
        return new SKRect(dot.Left - dx, dot.Top - dy, dot.Right + dx, dot.Bottom + dy);
    }
}

