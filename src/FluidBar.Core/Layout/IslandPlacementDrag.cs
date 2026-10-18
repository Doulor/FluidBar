using FluidBar.Core.Activities;

namespace FluidBar.Core.Layout;

/// <summary>
/// Persisted per-monitor placement produced by a drag release (spec 2.4/2.6): the anchor the island
/// snapped to (or kept) plus the offsets that reproduce the dropped position.
/// </summary>
/// <param name="Anchor">Anchor within the monitor work area.</param>
/// <param name="OffsetX">Horizontal offset from the anchored edge/centre in DIP.</param>
/// <param name="OffsetY">
/// Vertical offset from the anchored edge in DIP; <see langword="null"/> selects the mode default
/// (also used when the release snapped exactly onto the anchor).
/// </param>
/// <param name="Snapped">Whether the release snapped onto the anchor's natural position.</param>
public readonly record struct IslandDragPlacement(
    IslandAnchor Anchor,
    double OffsetX,
    double? OffsetY,
    bool Snapped);

/// <summary>
/// Pure drag-release placement for the island shell: decides the anchor a drop resolves to and the
/// offsets that must be persisted for that monitor.
/// <para>
/// Rules (spec 2.4/2.6):
/// <list type="bullet">
/// <item>Release within <see cref="SnapThresholdDip"/> (24 DIP) of an anchor's natural position snaps
/// onto it and persists zero offsets (vertical default kept <see langword="null"/>).</item>
/// <item>Otherwise the nearest anchor is kept and the free offset is persisted for that monitor.</item>
/// <item>Float allows horizontal + vertical dragging; flush is horizontal only (the vertical half of
/// the anchor is preserved); dock disables dragging.</item>
/// <item>Offsets are clamped so the dropped host window stays inside the work area.</item>
/// </list>
/// </para>
/// <para>
/// The drop point is the <b>host window</b> rectangle, not the visual shell: the window is the only
/// thing the drag actually moves, and using it makes peek inflation, form changes and dock/flush host
/// adjustments irrelevant. Layout math is DIP-only, so the result is DPI-independent (the DIP work
/// area shrinks at higher DPI and the clamp follows it).
/// </para>
/// </summary>
public static class IslandPlacementDrag
{
    /// <summary>Release distance (DIP) under which the shell snaps onto the nearest anchor (24).</summary>
    public const double SnapThresholdDip = 24;

    /// <summary>Whether a drag can move the island in this mode (dock is fixed).</summary>
    /// <param name="mode">Presentation mode.</param>
    public static bool AllowsDrag(IslandMode mode) => mode != IslandMode.Dock;

    /// <summary>Whether a drag can move the island vertically (only float; flush is horizontal).</summary>
    /// <param name="mode">Presentation mode.</param>
    public static bool AllowsVerticalDrag(IslandMode mode) => mode == IslandMode.Float;

    /// <summary>
    /// Resolves a dropped host window into an anchor + offsets for the monitor it was dropped on.
    /// </summary>
    /// <param name="windowDip">Host window rectangle in work-area-relative DIP at the drop point.</param>
    /// <param name="monitor">Monitor the window was dropped on (work area and DPI).</param>
    /// <param name="mode">Presentation mode.</param>
    /// <param name="compactKind">Compact content kind (selects the compact pill size); null = idle.</param>
    /// <param name="currentAnchor">Anchor in effect before the drag (preserved for flush's vertical half).</param>
    /// <param name="snapThresholdDip">Snap distance in DIP (defaults to <see cref="SnapThresholdDip"/>).</param>
    public static IslandDragPlacement Solve(
        in VisualRect windowDip,
        MonitorInfo monitor,
        IslandMode mode,
        ActivityKind? compactKind,
        IslandAnchor currentAnchor,
        double snapThresholdDip = SnapThresholdDip)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentOutOfRangeException.ThrowIfNegative(snapThresholdDip);

        var work = monitor.WorkAreaDip;
        if (!AllowsDrag(mode) || windowDip.IsEmpty || work.IsEmpty)
        {
            // Dock (or a degenerate snapshot): keep the anchor and its natural offsets.
            return new IslandDragPlacement(currentAnchor, 0, null, Snapped: false);
        }

        var vertical = AllowsVerticalDrag(mode);
        var bestAnchor = currentAnchor;
        var bestDistance = double.PositiveInfinity;
        var bestWindow = default(VisualRect);
        foreach (var anchor in Candidates(mode, currentAnchor))
        {
            var natural = NaturalPlacement(anchor, monitor, mode, compactKind).WindowVisual;
            var dx = windowDip.X - natural.X;
            var dy = vertical ? windowDip.Y - natural.Y : 0;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestAnchor = anchor;
                bestWindow = natural;
            }
        }

        if (bestDistance <= snapThresholdDip)
        {
            // Snap: zero offsets; OffsetY null keeps the mode's natural edge offset.
            return new IslandDragPlacement(bestAnchor, 0, null, Snapped: true);
        }

        var deltaX = windowDip.X - bestWindow.X;
        var deltaY = windowDip.Y - bestWindow.Y;
        var offsetX = IsRight(bestAnchor) ? -deltaX : deltaX;
        double? offsetY = null;
        if (vertical)
        {
            // Increasing OffsetY moves the window down for top anchors and up for bottom anchors.
            var naturalOffset = LayoutTokens.DefaultFloatOffsetY;
            offsetY = IsBottom(bestAnchor)
                ? naturalOffset - deltaY
                : naturalOffset + deltaY;
        }

        ClampOffsets(ref offsetX, ref offsetY, windowDip, bestWindow, bestAnchor, mode, work);
        return new IslandDragPlacement(bestAnchor, offsetX, offsetY, Snapped: false);
    }

    private static void ClampOffsets(
        ref double offsetX,
        ref double? offsetY,
        in VisualRect windowDip,
        in VisualRect naturalWindow,
        IslandAnchor anchor,
        IslandMode mode,
        in VisualRect work)
    {
        // Keep the window inside the work area, but never move a valid natural placement: top
        // anchors intentionally overhang the work area by the 24 DIP transparent shadow bleed.
        var dx = IsRight(anchor) ? -offsetX : offsetX;
        var predictedX = naturalWindow.X + dx;
        var minX = Math.Min(work.X, naturalWindow.X);
        var maxX = Math.Max(work.Right - windowDip.Width, naturalWindow.X);
        var clampedX = Math.Clamp(predictedX, minX, maxX);
        offsetX = IsRight(anchor) ? naturalWindow.X - clampedX : clampedX - naturalWindow.X;

        if (!AllowsVerticalDrag(mode) || offsetY is not { } value)
        {
            offsetY = null;
            return;
        }

        var naturalOffset = LayoutTokens.DefaultFloatOffsetY;
        var dy = IsBottom(anchor) ? -(value - naturalOffset) : value - naturalOffset;
        var predictedY = naturalWindow.Y + dy;
        var minY = Math.Min(work.Y, naturalWindow.Y);
        var maxY = Math.Max(work.Bottom - windowDip.Height, naturalWindow.Y);
        var clampedY = Math.Clamp(predictedY, minY, maxY);
        offsetY = IsBottom(anchor)
            ? naturalOffset - (clampedY - naturalWindow.Y)
            : naturalOffset + (clampedY - naturalWindow.Y);
    }

    private static WindowPlacement NaturalPlacement(
        IslandAnchor anchor,
        MonitorInfo monitor,
        IslandMode mode,
        ActivityKind? compactKind)
    {
        var request = new IslandLayoutRequest
        {
            Form = ShellForm.Compact,
            CompactKind = compactKind,
            GhostCount = 0,
            Stacking = StackingStrategy.LatestOnly,
            Mode = mode,
            Anchor = anchor,
            OffsetX = 0,
            OffsetY = double.NaN,
            HiddenHoverStrip = false,
        };
        return IslandLayout.Calculate(request, monitor).Placement;
    }

    private static IslandAnchor[] Candidates(IslandMode mode, IslandAnchor currentAnchor)
        => mode == IslandMode.Flush
            ? IsBottom(currentAnchor)
                ? [IslandAnchor.BottomLeft, IslandAnchor.BottomCenter, IslandAnchor.BottomRight]
                : [IslandAnchor.TopLeft, IslandAnchor.TopCenter, IslandAnchor.TopRight]
            : [
                IslandAnchor.TopLeft,
                IslandAnchor.TopCenter,
                IslandAnchor.TopRight,
                IslandAnchor.BottomLeft,
                IslandAnchor.BottomCenter,
                IslandAnchor.BottomRight,
            ];

    private static bool IsBottom(IslandAnchor anchor)
        => anchor is IslandAnchor.BottomLeft or IslandAnchor.BottomCenter or IslandAnchor.BottomRight;

    private static bool IsRight(IslandAnchor anchor)
        => anchor is IslandAnchor.TopRight or IslandAnchor.BottomRight;
}
