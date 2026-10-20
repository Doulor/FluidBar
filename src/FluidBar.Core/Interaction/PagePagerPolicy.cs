namespace FluidBar.Core.Interaction;

/// <summary>
/// How a release resolved to a page. <see cref="None"/> means no page change (snap back or
/// rubber-band rebound at a boundary).
/// </summary>
public enum PagerSnapKind
{
    /// <summary>No page change: snap back to the start page (small drag or edge rebound).</summary>
    None,

    /// <summary>Page change decided by drag distance.</summary>
    Distance,

    /// <summary>Page change decided by a quick flick (capped at one page).</summary>
    Flick,

    /// <summary>Page change decided by a wheel/trackpad notch.</summary>
    Wheel,
}

/// <summary>
/// Result of applying an in-progress drag: damped content offset and the rubber-banded visual
/// offset the shell should render while the pointer is down.
/// </summary>
public readonly record struct PagerDragState
{
    /// <summary>Page the drag started on (clamped).</summary>
    public int StartPage { get; init; }

    /// <summary>Accumulated raw drag in DIP (positive = towards the next page).</summary>
    public double RawDragDip { get; init; }

    /// <summary>Drag after pointer damping (<see cref="PagerConstants.PointerDamping"/>).</summary>
    public double DampedDragDip { get; init; }

    /// <summary>Start offset + damped drag, before edge rubber-banding.</summary>
    public double RawOffsetDip { get; init; }

    /// <summary>Visual content offset to render (rubber-banded beyond the first/last page).</summary>
    public double DisplayOffsetDip { get; init; }

    /// <summary>Fractional page position derived from <see cref="DisplayOffsetDip"/>.</summary>
    public double PagePosition { get; init; }

    /// <summary>Whether the drag is rubber-banded before the first page.</summary>
    public bool AtStartEdge { get; init; }

    /// <summary>Whether the drag is rubber-banded after the last page.</summary>
    public bool AtEndEdge { get; init; }
}

/// <summary>Release decision for the horizontal pager.</summary>
public readonly record struct PagerSnapDecision
{
    /// <summary>Page the gesture started on (clamped).</summary>
    public int StartPage { get; init; }

    /// <summary>Page to settle on (always clamped to <c>[0, pageCount-1]</c>).</summary>
    public int TargetPage { get; init; }

    /// <summary>Snap target content offset in DIP (<c>TargetPage × pitch</c>).</summary>
    public double TargetOffsetDip { get; init; }

    /// <summary>Why the target was chosen.</summary>
    public PagerSnapKind Kind { get; init; }

    /// <summary>Signed page delta (0 for <see cref="PagerSnapKind.None"/>).</summary>
    public int PageDelta => TargetPage - StartPage;

    /// <summary>Whether the decision changes page.</summary>
    public bool ChangesPage => TargetPage != StartPage;

    /// <summary>Creates a snap-back decision for <paramref name="page"/>.</summary>
    public static PagerSnapDecision SnapBack(int page, double pagePitchDip)
        => new()
        {
            StartPage = page,
            TargetPage = page,
            TargetOffsetDip = page * pagePitchDip,
            Kind = PagerSnapKind.None,
        };
}

/// <summary>
/// Pure, deterministic horizontal pager policy for the expanded panel (replaces the rejected left
/// rail; user feedback 2026-10-18).
/// <para>
/// Sign conventions: <c>dragDip</c> is accumulated pointer drag in DIP where <b>positive means
/// towards the next page</b> (i.e. the UI feeds <c>-pointerDeltaX</c> when dragging content left
/// advances the page). Offsets are content offsets in DIP: page <c>p</c> is at <c>p × pitch</c>.
/// </para>
/// </summary>
public static class PagePagerPolicy
{
    /// <summary>Clamps a page index to <c>[0, pageCount-1]</c>.</summary>
    /// <param name="page">Requested page.</param>
    /// <param name="pageCount">Number of pages (&gt;= 1).</param>
    public static int ClampPage(int page, int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        return Math.Clamp(page, 0, pageCount - 1);
    }

    /// <summary>Content offset of a page in DIP (clamped page index).</summary>
    public static double PageToOffset(int page, int pageCount, double pagePitchDip = PagerConstants.PagePitchDip)
    {
        ValidatePitch(pagePitchDip);
        return ClampPage(page, pageCount) * pagePitchDip;
    }

    /// <summary>Nearest page index for a content offset (rounded, clamped).</summary>
    public static int OffsetToPage(double offsetDip, int pageCount, double pagePitchDip = PagerConstants.PagePitchDip)
    {
        ValidatePitch(pagePitchDip);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        if (!double.IsFinite(offsetDip))
        {
            return 0;
        }

        var page = (int)Math.Round(offsetDip / pagePitchDip, MidpointRounding.AwayFromZero);
        return ClampPage(page, pageCount);
    }

    /// <summary>
    /// Applies pointer damping and edge rubber-banding to an in-progress drag. Pure: call once per
    /// pointer move with the accumulated drag and render <see cref="PagerDragState.DisplayOffsetDip"/>.
    /// </summary>
    /// <param name="startPage">Page the gesture started on.</param>
    /// <param name="accumulatedDragDip">Accumulated drag in DIP (positive = next page).</param>
    /// <param name="pageCount">Number of pages.</param>
    /// <param name="pagePitchDip">Page pitch in DIP.</param>
    /// <param name="pointerDamping">Pointer damping factor (0.90 in production).</param>
    /// <param name="edgeResistance">Rubber-band factor beyond the first/last page (0.35 in production).</param>
    public static PagerDragState Drag(
        int startPage,
        double accumulatedDragDip,
        int pageCount,
        double pagePitchDip = PagerConstants.PagePitchDip,
        double pointerDamping = PagerConstants.PointerDamping,
        double edgeResistance = PagerConstants.EdgeResistance)
    {
        ValidatePitch(pagePitchDip);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pointerDamping, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pointerDamping, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(edgeResistance, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(edgeResistance, 1);

        var start = ClampPage(startPage, pageCount);
        var drag = double.IsFinite(accumulatedDragDip) ? accumulatedDragDip : 0;
        var damped = drag * pointerDamping;
        var startOffset = start * pagePitchDip;
        var rawOffset = startOffset + damped;
        var maxOffset = (pageCount - 1) * pagePitchDip;

        double display;
        if (rawOffset < 0)
        {
            display = rawOffset * edgeResistance;
        }
        else if (rawOffset > maxOffset)
        {
            display = maxOffset + ((rawOffset - maxOffset) * edgeResistance);
        }
        else
        {
            display = rawOffset;
        }

        return new PagerDragState
        {
            StartPage = start,
            RawDragDip = drag,
            DampedDragDip = damped,
            RawOffsetDip = rawOffset,
            DisplayOffsetDip = display,
            PagePosition = display / pagePitchDip,
            AtStartEdge = display < -1e-9,
            AtEndEdge = display > maxOffset + 1e-9,
        };
    }

    /// <summary>
    /// Resolves a released drag into a snap target using prototype2's <c>onPointerUp</c> rules, so the
    /// landing page is the one the finger actually left behind. The island used to flip a page after
    /// only 26 DIP of travel, which read as an unrequested jump and made slow drags rebound.
    /// <list type="bullet">
    /// <item><b>sub-threshold</b> travel (&lt; <see cref="PagerConstants.PageThresholdDip"/>) → snap back;</item>
    /// <item><b>long drag</b>: the damped travel reaches <see cref="PagerConstants.LongDragPageFraction"/>
    /// (0.75) of a page → round the damped travel, so several pages stay reachable;</item>
    /// <item><b>flick</b>: release speed ≥ <see cref="PagerConstants.FlickVelocityDipPerMs"/> with at
    /// least <see cref="PagerConstants.FlickMinimumDragDip"/> of travel → exactly one page (never more);
    /// a flick whose velocity reverses against the travel is ignored and falls through;</item>
    /// <item><b>otherwise</b> the nearest page wins (round of the damped travel), so a slow drag below
    /// half a page settles back instead of jumping forward.</item>
    /// </list>
    /// </summary>
    /// <param name="startPage">Page the gesture started on.</param>
    /// <param name="accumulatedDragDip">Accumulated raw pointer drag in DIP (positive = next page).</param>
    /// <param name="velocityDipPerMs">Release velocity in DIP/ms (positive = next page).</param>
    /// <param name="pageCount">Number of pages.</param>
    /// <param name="pagePitchDip">Page pitch in DIP.</param>
    /// <param name="pageThresholdDip">Minimum travel to change page.</param>
    /// <param name="flickVelocityDipPerMs">Velocity above which a short drag is a flick.</param>
    /// <param name="pointerDamping">Pointer follow ratio used to derive the damped travel.</param>
    public static PagerSnapDecision Release(
        int startPage,
        double accumulatedDragDip,
        double velocityDipPerMs,
        int pageCount,
        double pagePitchDip = PagerConstants.PagePitchDip,
        double pageThresholdDip = PagerConstants.PageThresholdDip,
        double flickVelocityDipPerMs = PagerConstants.FlickVelocityDipPerMs,
        double pointerDamping = PagerConstants.PointerDamping)
    {
        ValidatePitch(pagePitchDip);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(pageThresholdDip);
        ArgumentOutOfRangeException.ThrowIfNegative(flickVelocityDipPerMs);
        ArgumentOutOfRangeException.ThrowIfNegative(pointerDamping);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pointerDamping, 1);

        var start = ClampPage(startPage, pageCount);
        var drag = double.IsFinite(accumulatedDragDip) ? accumulatedDragDip : 0;
        var velocity = double.IsFinite(velocityDipPerMs) ? velocityDipPerMs : 0;
        var absolute = Math.Abs(drag);

        // (a) too small: snap back, no page change (a stray click-drag never flips a page).
        if (absolute < pageThresholdDip)
        {
            return PagerSnapDecision.SnapBack(start, pagePitchDip);
        }

        // The page the finger is over: the content follows the pointer at the damping ratio.
        var dampedPages = (drag * pointerDamping) / pagePitchDip;

        // (b) long drag: distance decides, rounded the same way the visual offset rounds.
        if (Math.Abs(dampedPages) >= PagerConstants.LongDragPageFraction)
        {
            var crossed = (int)Math.Round(dampedPages, MidpointRounding.AwayFromZero);
            if (crossed == 0)
            {
                crossed = dampedPages > 0 ? 1 : -1;
            }

            return Decision(start, start + crossed, pageCount, pagePitchDip, PagerSnapKind.Distance);
        }

        // (c) flick: exactly one page in the release direction, only when the finger was still
        // travelling that way (prototype2 seeds the direction from the release velocity).
        if (Math.Abs(velocity) >= flickVelocityDipPerMs
            && absolute >= PagerConstants.FlickMinimumDragDip
            && Math.Sign(velocity) == Math.Sign(drag))
        {
            return Decision(start, start + Math.Sign(velocity), pageCount, pagePitchDip, PagerSnapKind.Flick);
        }

        // (d) nearest page: a slow drag that never crossed half a page settles back.
        return Decision(
            start,
            start + (int)Math.Round(dampedPages, MidpointRounding.AwayFromZero),
            pageCount,
            pagePitchDip,
            PagerSnapKind.Distance);
    }

    /// <summary>Clamps a requested landing page and classifies it (snap-back when unchanged).</summary>
    private static PagerSnapDecision Decision(
        int start, int requested, int pageCount, double pagePitchDip, PagerSnapKind kind)
    {
        var target = ClampPage(requested, pageCount);
        return target == start
            ? PagerSnapDecision.SnapBack(start, pagePitchDip)
            : new PagerSnapDecision
            {
                StartPage = start,
                TargetPage = target,
                TargetOffsetDip = target * pagePitchDip,
                Kind = kind,
            };
    }

    /// <summary>Release overload that consumes the accumulated drag from a <see cref="PagerDragState"/>.</summary>
    public static PagerSnapDecision Release(
        in PagerDragState drag,
        double velocityDipPerMs,
        int pageCount,
        double pagePitchDip = PagerConstants.PagePitchDip)
        => Release(drag.StartPage, drag.RawDragDip, velocityDipPerMs, pageCount, pagePitchDip);

    /// <summary>Builds a wheel-driven snap decision (clamped page, <see cref="PagerSnapKind.Wheel"/>).</summary>
    /// <param name="startPage">Current page.</param>
    /// <param name="pageDelta">Wheel page delta (host already applied cooldown/accumulation).</param>
    /// <param name="pageCount">Number of pages.</param>
    /// <param name="pagePitchDip">Page pitch in DIP.</param>
    public static PagerSnapDecision ReleaseWheel(
        int startPage,
        int pageDelta,
        int pageCount,
        double pagePitchDip = PagerConstants.PagePitchDip)
    {
        ValidatePitch(pagePitchDip);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        var start = ClampPage(startPage, pageCount);
        var target = ClampPage(start + pageDelta, pageCount);
        return new PagerSnapDecision
        {
            StartPage = start,
            TargetPage = target,
            TargetOffsetDip = target * pagePitchDip,
            Kind = target == start ? PagerSnapKind.None : PagerSnapKind.Wheel,
        };
    }

    private static void ValidatePitch(double pagePitchDip)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pagePitchDip, 0);
        if (!double.IsFinite(pagePitchDip))
        {
            throw new ArgumentOutOfRangeException(nameof(pagePitchDip), "Page pitch must be finite.");
        }
    }
}

