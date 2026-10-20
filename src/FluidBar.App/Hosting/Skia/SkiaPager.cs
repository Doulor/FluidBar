using FluidBar.Core.Interaction;

namespace FluidBar.App.Hosting.Skia;

/// <summary>What one wheel event did to the pager.</summary>
/// <param name="Consumed">Whether the pager/scroll chain consumed the event.</param>
/// <param name="Paged">Whether the committed page changed.</param>
/// <param name="Page">Committed page after the event.</param>
/// <param name="ContentScroll">Whether a scrollable content region owns the gesture (no page step).</param>
/// <param name="Locked">Whether a running page snap froze the chain.</param>
public readonly record struct SkiaWheelOutcome(bool Consumed, bool Paged, int Page, bool ContentScroll, bool Locked)
{
    /// <summary>Nothing consumed the event.</summary>
    public static SkiaWheelOutcome None { get; } = new(false, false, 0, false, false);
}

/// <summary>
/// Horizontal page pager for the Skia island. All decisions are Core policies
/// (<see cref="PagePagerPolicy"/>, <see cref="PagerWheelPolicy"/>, <see cref="PagerScrollChainPolicy"/>,
/// <see cref="PagerSnapMotion"/>); this type only sequences them and owns the visual offset.
/// </summary>
public sealed class SkiaPager
{
    private const double ClickCancelThresholdDip = 6;

    private readonly PagerSnapMotion _snap;
    private PagerWheelPolicy _wheel;
    private readonly PagerScrollChainPolicy _chain = new();

    private bool _dragging;
    private int _dragStartPage;
    private double _dragStartX;
    private double _dragStartOffset;
    private double _accumulatedDragDip;
    private double _velocityDipPerMs;
    private double _lastDragX;
    private long _lastDragTimestampMs;

    /// <summary>Creates the pager at a page.</summary>
    /// <param name="initialPage">Initial committed page.</param>
    /// <param name="pageCount">Number of pages.</param>
    public SkiaPager(int initialPage = 0, int pageCount = PagerConstants.PageCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        PageCount = pageCount;
        SelectedIndex = PagePagerPolicy.ClampPage(initialPage, pageCount);
        _snap = new PagerSnapMotion(PagePagerPolicy.PageToOffset(SelectedIndex, PagerConstants.PagePitchDip, pageCount));
        _wheel = new PagerWheelPolicy(pageCount);
    }

    /// <summary>Number of pages.</summary>
    public int PageCount { get; private set; }

    /// <summary>Updates the page count; clamps the committed page and returns whether it changed.</summary>
    public bool SetPageCount(int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        if (pageCount == PageCount)
        {
            return false;
        }

        PageCount = pageCount;
        _wheel = new PagerWheelPolicy(pageCount);
        var clamped = PagePagerPolicy.ClampPage(SelectedIndex, pageCount);
        if (clamped == SelectedIndex)
        {
            return false;
        }

        SelectedIndex = clamped;
        _snap.JumpTo(PagePagerPolicy.PageToOffset(clamped, PagerConstants.PagePitchDip, pageCount));
        return true;
    }

    /// <summary>Committed page index.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>Continuous page position (0..pageCount-1).</summary>
    public double PagePosition => _snap.PagePosition;

    /// <summary>Visual content offset in DIP.</summary>
    public double OffsetDip => _snap.CurrentOffsetDip;

    /// <summary>Whether a pointer drag is in flight.</summary>
    public bool IsDragging => _dragging;

    /// <summary>Whether the snap spring is running.</summary>
    public bool IsAnimating => !_snap.IsAtRest;

    /// <summary>Signed accumulated drag in DIP (positive = towards the next page).</summary>
    public double DragDistanceDip => _accumulatedDragDip;

    /// <summary>Whether the gesture moved far enough to cancel page-internal clicks.</summary>
    public bool MovedPastClickThreshold => Math.Abs(_accumulatedDragDip) > ClickCancelThresholdDip;

    /// <summary>Raised when the committed page changes.</summary>
    public event Action<int>? PageChanged;

    /// <summary>Starts a drag; a running snap is frozen at its visual offset first.</summary>
    /// <param name="xDip">Pointer X in host-local DIP.</param>
    /// <param name="timestampMs">Monotonic timestamp in milliseconds.</param>
    public void BeginDrag(double xDip, long timestampMs)
    {
        _dragging = true;
        _accumulatedDragDip = 0;
        _velocityDipPerMs = 0;
        _dragStartX = xDip;
        _lastDragX = xDip;
        _lastDragTimestampMs = timestampMs;
        _dragStartOffset = _snap.Interrupt();
        _dragStartPage = PagePagerPolicy.OffsetToPage(_dragStartOffset);
    }

    /// <summary>Updates the drag from the pointer X; returns the new visual offset.</summary>
    /// <param name="xDip">Pointer X in host-local DIP.</param>
    /// <param name="timestampMs">Monotonic timestamp in milliseconds.</param>
    public double DragTo(double xDip, long timestampMs)
    {
        if (!_dragging)
        {
            return OffsetDip;
        }

        var dt = Math.Max(1, timestampMs - _lastDragTimestampMs) / 1000.0;
        var dx = xDip - _lastDragX;
        _lastDragX = xDip;
        _lastDragTimestampMs = timestampMs;

        // Low-pass smoothed velocity (prototype2: 72 % previous / 28 % new sample), in DIP/ms with
        // positive = towards the next page. The previous formula negated the whole accumulator on
        // every sample, so the smoothed speed oscillated around ~20 % of the real one and the flick
        // threshold could never be reached.
        var instantDipPerMs = (-dx / dt) / 1000.0;
        _velocityDipPerMs = (_velocityDipPerMs * 0.72) + (instantDipPerMs * 0.28);
        _accumulatedDragDip = -(xDip - _dragStartX);

        var drag = PagePagerPolicy.Drag(_dragStartPage, _accumulatedDragDip);
        var deltaFromPage = drag.DisplayOffsetDip - PagePagerPolicy.PageToOffset(_dragStartPage);
        var visual = _dragStartOffset + deltaFromPage;
        _snap.JumpTo(visual);
        return visual;
    }

    /// <summary>Ends the drag, commits the release decision and starts the snap; returns the target page.</summary>
    /// <param name="timestampMs">Monotonic timestamp in milliseconds (unused by the pure policy; kept for parity).</param>
    public int EndDrag(long timestampMs)
    {
        _ = timestampMs;
        if (!_dragging)
        {
            return SelectedIndex;
        }

        _dragging = false;
        var decision = PagePagerPolicy.Release(_dragStartPage, _accumulatedDragDip, _velocityDipPerMs);
        CommitPage(decision.TargetPage);

        // Momentum handoff (prototype2 seeds its spring with the release velocity, clamped to ±14
        // pages/s): a flick keeps travelling instead of stopping dead and easing from rest.
        var pagesPerSecond = Math.Clamp(
            _velocityDipPerMs * PagerConstants.PointerDamping * 1000.0 / PagerConstants.PagePitchDip,
            -PagerConstants.MaxReleaseVelocityPagesPerSecond,
            PagerConstants.MaxReleaseVelocityPagesPerSecond);
        _snap.SnapToOffset(decision.TargetOffsetDip, pagesPerSecond * PagerConstants.PagePitchDip);
        return decision.TargetPage;
    }

    /// <summary>Animates to a page (archipelago click, keyboard jump).</summary>
    /// <param name="page">Target page.</param>
    public void SelectPage(int page)
    {
        var target = PagePagerPolicy.ClampPage(page, PageCount);
        CommitPage(target);
        _snap.SnapToOffset(PagePagerPolicy.PageToOffset(target, PagerConstants.PagePitchDip, PageCount));
    }

    /// <summary>Steps one page left/right (keyboard arrows).</summary>
    /// <param name="delta">Signed page delta.</param>
    public void MoveBy(int delta) => SelectPage(SelectedIndex + Math.Sign(delta));

    /// <summary>Jumps to a page without animation (restore/mode change/reduced motion).</summary>
    /// <param name="page">Target page.</param>
    public void JumpToPage(int page)
    {
        var target = PagePagerPolicy.ClampPage(page, PageCount);
        CommitPage(target);
        _snap.JumpTo(PagePagerPolicy.PageToOffset(target, PagerConstants.PagePitchDip, PageCount));
    }

    /// <summary>
    /// Feeds one wheel event. Non-scrollable content is notch-gated by <see cref="PagerWheelPolicy"/>
    /// (140 DIP notch, 180 ms cooldown) and then resolved by <see cref="PagerScrollChainPolicy"/>, so a
    /// trackpad cannot flip several pages per gesture. Scrollable content keeps the chain's
    /// scroll/overscroll/dwell hand-off.
    /// </summary>
    /// <param name="deltaDip">Wheel delta in DIP (positive = towards the next page).</param>
    /// <param name="horizontal">Whether the event came from the horizontal wheel axis.</param>
    /// <param name="shift">Whether Shift was held (forces paging).</param>
    /// <param name="now">Injected current time.</param>
    /// <param name="contentScrollable">Whether the page under the pointer has scrollable content.</param>
    public SkiaWheelOutcome Wheel(double deltaDip, bool horizontal, bool shift, DateTimeOffset now, bool contentScrollable = false)
    {
        if (_dragging)
        {
            return SkiaWheelOutcome.None;
        }

        // Queue from the page the snap is heading to (prototype2: `P.animating ? P.target : round(offset)`).
        // The previous build froze the whole chain while the spring ran, so every notch during the
        // ~0.5 s snap was swallowed and a fast gesture could only ever move one page.
        var basePage = IsAnimating
            ? PagePagerPolicy.OffsetToPage(_snap.TargetOffsetDip)
            : SelectedIndex;

        var chainDelta = deltaDip;
        if (!contentScrollable && !horizontal && !shift)
        {
            var notch = _wheel.Accumulate(deltaDip, now);
            if (notch.PageDelta == 0)
            {
                return SkiaWheelOutcome.None;
            }

            // The notch policy already decided the step: step from the queued page, not from the
            // (possibly stale) committed page.
            SelectPage(basePage + notch.PageDelta);
            return new SkiaWheelOutcome(true, true, SelectedIndex, false, false);
        }

        var decision = _chain.Handle(new PagerScrollChainRequest
        {
            WheelDeltaDip = chainDelta,
            ContentScrollable = contentScrollable,
            ContentBoundary = PagerScrollBoundary.None,
            CurrentPage = basePage,
            PageCount = PageCount,
            Now = now,
            PageAnimationLocked = false,
            HorizontalWheel = horizontal,
            ShiftHeld = shift,
        });

        if (decision.PageLocked)
        {
            return new SkiaWheelOutcome(false, false, SelectedIndex, false, true);
        }

        if (decision.Action == PagerScrollChainAction.ScrollContent)
        {
            return new SkiaWheelOutcome(true, false, SelectedIndex, true, false);
        }

        if (decision.Action is PagerScrollChainAction.PageNext or PagerScrollChainAction.PagePrevious)
        {
            SelectPage(decision.TargetPage);
            return new SkiaWheelOutcome(true, true, SelectedIndex, false, false);
        }

        return new SkiaWheelOutcome(decision.Consumed, false, SelectedIndex, false, false);
    }

    /// <summary>Steps the snap spring.</summary>
    /// <param name="deltaSeconds">Frame delta in seconds.</param>
    public double Step(double deltaSeconds) => _snap.Step(deltaSeconds);

    /// <summary>Clears wheel/scroll-chain accumulation (panel close, pointer leave).</summary>
    public void Reset()
    {
        _wheel.Reset();
        _chain.Reset();
    }

    private void CommitPage(int target)
    {
        if (target == SelectedIndex)
        {
            return;
        }

        SelectedIndex = target;
        PageChanged?.Invoke(target);
    }
}
