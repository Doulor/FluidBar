namespace FluidBar.Core.Interaction;

/// <summary>Wheel direction over the expanded panel (Down = towards later content/next page).</summary>
public enum PagerScrollDirection
{
    /// <summary>Wheel up / towards earlier content (previous page).</summary>
    Up = -1,

    /// <summary>Wheel down / towards later content (next page).</summary>
    Down = 1,
}

/// <summary>Where the scrollable content under the pointer currently is.</summary>
public enum PagerScrollBoundary
{
    /// <summary>The content can still scroll in the wheel direction (has room).</summary>
    None = 0,

    /// <summary>The content is at its top edge (no room for wheel-up).</summary>
    Top = 1,

    /// <summary>The content is at its bottom edge (no room for wheel-down).</summary>
    Bottom = 2,
}

/// <summary>Decision of the wheel scroll-chain: who owns this wheel gesture.</summary>
public enum PagerScrollChainAction
{
    /// <summary>Nothing happens (accumulating overscroll/dwell, reversed, or page-locked).</summary>
    None = 0,

    /// <summary>The scrollable content consumes the wheel delta.</summary>
    ScrollContent = 1,

    /// <summary>The content cannot scroll further; the pager advances to the next page.</summary>
    PageNext = 2,

    /// <summary>The content cannot scroll further; the pager returns to the previous page.</summary>
    PagePrevious = 3,
}

/// <summary>One wheel event fed into <see cref="PagerScrollChainPolicy"/>.</summary>
public readonly record struct PagerScrollChainRequest
{
    /// <summary>Signed wheel delta in DIP (positive/negative is the scroll direction).</summary>
    public double WheelDeltaDip { get; init; }

    /// <summary>Whether the content under the pointer is scrollable at all.</summary>
    public bool ContentScrollable { get; init; }

    /// <summary>Boundary state of the scrollable content in the wheel direction.</summary>
    public PagerScrollBoundary ContentBoundary { get; init; }

    /// <summary>Current page index (used to clamp page steps).</summary>
    public int CurrentPage { get; init; }

    /// <summary>Number of pages (defaults to <see cref="FluidBar.Core.Activities.PageRef.BuiltinPages.Count"/>).</summary>
    public int PageCount { get; init; }

    /// <summary>Injected current time (no ambient clock).</summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>True while an animated page snap is running; the chain is frozen while locked.</summary>
    public bool PageAnimationLocked { get; init; }

    /// <summary>Horizontal wheel (or trackpad horizontal swipe) always pages, bypassing content scroll.</summary>
    public bool HorizontalWheel { get; init; }

    /// <summary>Shift modifier forces page navigation instead of content scrolling.</summary>
    public bool ShiftHeld { get; init; }

    /// <summary>Signed scroll direction of this event (0 for a zero delta).</summary>
    public PagerScrollDirection Direction => WheelDeltaDip < 0 ? PagerScrollDirection.Up : PagerScrollDirection.Down;

    /// <summary>Absolute wheel delta in DIP.</summary>
    public double MagnitudeDip => Math.Abs(WheelDeltaDip);
}

/// <summary>Result of one scroll-chain decision.</summary>
public readonly record struct PagerScrollChainDecision
{
    /// <summary>Who owns the gesture for this event.</summary>
    public PagerScrollChainAction Action { get; init; }

    /// <summary>Page the gesture started from (clamped).</summary>
    public int CurrentPage { get; init; }

    /// <summary>Target page after a page action (clamped; equals <see cref="CurrentPage"/> when no change).</summary>
    public int TargetPage { get; init; }

    /// <summary>Whether the direction reversed and cancelled a pending page step.</summary>
    public bool ReverseCancelled { get; init; }

    /// <summary>Whether the page-animation lock was active (chain frozen).</summary>
    public bool PageLocked { get; init; }

    /// <summary>Overscroll DIP accumulated at the boundary after this event.</summary>
    public double AccumulatedOverscrollDip { get; init; }

    /// <summary>Whether the event was consumed by the scroll chain (scroll owner or page step).</summary>
    public bool Consumed => Action is PagerScrollChainAction.ScrollContent
        or PagerScrollChainAction.PageNext
        or PagerScrollChainAction.PagePrevious;
}

/// <summary>
/// Pure wheel scroll-chain policy: decides whether a wheel event scrolls the content under the
/// pointer or flips a panel page. Pager v2 replaces raw wheel paging with a scroll chain so
/// scrollable content (lyrics, notification list, clipboard history…) keeps native wheel behaviour:
/// <list type="bullet">
/// <item>scrollable content with room in the wheel direction → <see cref="PagerScrollChainAction.ScrollContent"/>;</item>
/// <item>at the content boundary: accumulate overscroll; after <b>≥40 DIP</b> accumulated <b>and
/// ≥120 ms</b> dwell at that boundary → one page step in the wheel direction;</item>
/// <item>a direction reversal before the page step cancels the pending step;</item>
/// <item>non-scrollable content pages immediately;</item>
/// <item>horizontal wheel / Shift bypasses content and pages immediately;</item>
/// <item>while a page snap animation is running (<c>PageAnimationLocked</c>) the whole chain is frozen.</item>
/// </list>
/// Deterministic: time is injected, no timers/IO/UI. One instance per expanded panel gesture stream;
/// call <see cref="Reset"/> on pointer leave/panel close.
/// </summary>
public sealed class PagerScrollChainPolicy
{
    private double _accumulatedOverscrollDip;
    private DateTimeOffset? _dwellStartedAt;
    private PagerScrollDirection? _pendingDirection;

    /// <summary>Creates a scroll-chain policy.</summary>
    /// <param name="overscrollThresholdDip">Overscroll DIP required at a boundary (40).</param>
    /// <param name="dwellMs">Dwell milliseconds required at a boundary (120).</param>
    public PagerScrollChainPolicy(
        double overscrollThresholdDip = PagerConstants.ScrollChainOverscrollDip,
        int dwellMs = PagerConstants.ScrollChainDwellMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(overscrollThresholdDip, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(dwellMs);
        OverscrollThresholdDip = overscrollThresholdDip;
        DwellMs = dwellMs;
    }

    /// <summary>Overscroll DIP required before a page step.</summary>
    public double OverscrollThresholdDip { get; }

    /// <summary>Dwell milliseconds required before a page step.</summary>
    public int DwellMs { get; }

    /// <summary>Overscroll DIP accumulated at the current boundary.</summary>
    public double AccumulatedOverscrollDip => _accumulatedOverscrollDip;

    /// <summary>When the current boundary dwell started, if any.</summary>
    public DateTimeOffset? DwellStartedAt => _dwellStartedAt;

    /// <summary>Whether a page step is being accumulated (direction known).</summary>
    public bool HasPendingPageStep => _pendingDirection is not null;

    /// <summary>Feeds one wheel event and returns who owns it.</summary>
    public PagerScrollChainDecision Handle(in PagerScrollChainRequest request)
    {
        var pageCount = request.PageCount;
        var currentPage = PagePagerPolicy.ClampPage(request.CurrentPage, pageCount);
        var magnitude = request.MagnitudeDip;
        if (!double.IsFinite(magnitude) || magnitude <= 0)
        {
            return MakeDecision(PagerScrollChainAction.None, currentPage, currentPage);
        }

        var direction = request.Direction;

        // (f) A running page animation freezes the whole chain: no content scroll, no page step.
        if (request.PageAnimationLocked)
        {
            Reset();
            return MakeDecision(PagerScrollChainAction.None, currentPage, currentPage) with { PageLocked = true };
        }

        // (e) Horizontal wheel / Shift force page navigation and bypass content scrolling.
        if (request.HorizontalWheel || request.ShiftHeld)
        {
            Reset();
            return PageStep(currentPage, pageCount, direction, request);
        }

        // (d) Non-scrollable content: page immediately.
        if (!request.ContentScrollable)
        {
            Reset();
            return PageStep(currentPage, pageCount, direction, request);
        }

        // (c) Pending overscroll reversed before the page step: cancel, let the content take over.
        if (_pendingDirection is { } pending && pending != direction)
        {
            Reset();
            return MakeDecision(PagerScrollChainAction.None, currentPage, currentPage) with { ReverseCancelled = true };
        }

        // (b) Content can scroll in the wheel direction: the content owns the gesture.
        if (!IsAtBoundaryFor(request.ContentBoundary, direction))
        {
            Reset();
            return MakeDecision(PagerScrollChainAction.ScrollContent, currentPage, currentPage);
        }

        // (a) At the boundary: accumulate overscroll and dwell, then page once.
        _pendingDirection = direction;
        _dwellStartedAt ??= request.Now;
        _accumulatedOverscrollDip += magnitude;

        var dwellElapsed = request.Now - _dwellStartedAt.Value;
        if (_accumulatedOverscrollDip >= OverscrollThresholdDip
            && dwellElapsed >= TimeSpan.FromMilliseconds(DwellMs))
        {
            Reset();
            return PageStep(currentPage, pageCount, direction, request);
        }

        return MakeDecision(PagerScrollChainAction.None, currentPage, currentPage, _accumulatedOverscrollDip);
    }

    /// <summary>Clears pending overscroll/dwell state (pointer leave, panel close, page animation start).</summary>
    public void Reset()
    {
        _accumulatedOverscrollDip = 0;
        _dwellStartedAt = null;
        _pendingDirection = null;
    }

    /// <summary>Maps a wheel/trackpad delta DIP to text/log token (<c>up</c>/<c>down</c>).</summary>
    public static string DirectionToken(double wheelDeltaDip) => wheelDeltaDip < 0 ? "up" : "down";

    private static bool IsAtBoundaryFor(PagerScrollBoundary boundary, PagerScrollDirection direction)
        => direction == PagerScrollDirection.Down
            ? boundary == PagerScrollBoundary.Bottom
            : boundary == PagerScrollBoundary.Top;

    private static PagerScrollChainDecision PageStep(
        int currentPage,
        int pageCount,
        PagerScrollDirection direction,
        in PagerScrollChainRequest request)
    {
        var delta = direction == PagerScrollDirection.Down ? 1 : -1;
        var targetPage = PagePagerPolicy.ClampPage(currentPage + delta, pageCount);
        var action = targetPage == currentPage
            ? PagerScrollChainAction.None
            : direction == PagerScrollDirection.Down
                ? PagerScrollChainAction.PageNext
                : PagerScrollChainAction.PagePrevious;

        return MakeDecision(action, currentPage, targetPage);
    }

    private static PagerScrollChainDecision MakeDecision(
        PagerScrollChainAction action,
        int currentPage,
        int targetPage,
        double accumulatedOverscrollDip = 0)
        => new()
        {
            Action = action,
            CurrentPage = currentPage,
            TargetPage = targetPage,
            AccumulatedOverscrollDip = accumulatedOverscrollDip,
        };
}

