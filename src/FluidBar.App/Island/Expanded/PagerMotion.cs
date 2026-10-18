using FluidBar.Core.Interaction;

namespace FluidBar.App.Island.Expanded;

/// <summary>
/// UI adapter over the frozen Core pager contract (<see cref="PagePagerPolicy"/>,
/// <see cref="PagerSnapMotion"/>, <see cref="PagerWheelPolicy"/>, <see cref="PagerConstants"/>).
/// The App never re-implements drag/snap math: this class only tracks gesture bookkeeping and exposes
/// an offset/pages API for the panel and headless tests.
/// </summary>
public sealed class PagerMotion
{
    /// <summary>Number of pages (Core constant).</summary>
    public const int DefaultPageCount = PagerConstants.PageCount;

    /// <summary>Page pitch in DIP (Core constant).</summary>
    public const double PagePitch = PagerConstants.PagePitchDip;

    /// <summary>Drag distance (DIP) below which a gesture snaps back.</summary>
    public const double SmallDragThreshold = PagerConstants.PageThresholdDip;

    /// <summary>Movement above this cancels page-internal clicks.</summary>
    public const double ClickCancelThreshold = 6;

    /// <summary>Wheel travel that equals one page notch.</summary>
    public const double WheelNotchDip = PagerConstants.WheelNotchDip;

    /// <summary>Minimum gap between two wheel-driven page changes.</summary>
    public const int WheelCooldownMs = PagerConstants.WheelCooldownMs;

    private readonly PagerSnapMotion _motion;
    private readonly PagerWheelPolicy _wheel;

    private bool _isDragging;
    private int _dragStartPage;
    private double _dragStartX;
    private double _dragStartOffset;
    private double _accumulatedDragDip;
    private double _dragVelocityDipPerMs;
    private long _lastDragTimestamp;
    private double _lastDragX;

    /// <summary>Creates the pager for <paramref name="pageCount"/> pages.</summary>
    public PagerMotion(int pageCount = PagerConstants.PageCount, double pageWidth = PagerConstants.PagePitchDip, int initialPage = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pageWidth, 0);
        PageCount = pageCount;
        PageWidth = pageWidth;
        SelectedIndex = PagePagerPolicy.ClampPage(initialPage, pageCount);
        _motion = new PagerSnapMotion(PagePagerPolicy.PageToOffset(SelectedIndex, pageWidth, pageCount));
        _wheel = new PagerWheelPolicy();
    }

    /// <summary>Number of pages.</summary>
    public int PageCount { get; }

    /// <summary>Page pitch in DIP.</summary>
    public double PageWidth { get; }

    /// <summary>Current visual offset in DIP.</summary>
    public double Offset => _motion.CurrentOffsetDip;

    /// <summary>Committed page index.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>Whether a pointer drag is in flight.</summary>
    public bool IsDragging => _isDragging;

    /// <summary>Whether the snap spring is running.</summary>
    public bool IsAnimating => !_motion.IsAtRest;

    /// <summary>Signed accumulated drag in DIP during the gesture (positive = next page).</summary>
    public double DragDistance => _accumulatedDragDip;

    /// <summary>Whether the gesture moved far enough to cancel page-internal clicks.</summary>
    public bool MovedPastClickThreshold => Math.Abs(_accumulatedDragDip) > ClickCancelThreshold;

    /// <summary>Whether a wheel notch is currently suppressed because a control owns the gesture.</summary>
    public bool IsWheelBlocked { get; set; }

    /// <summary>Maximum content offset in DIP.</summary>
    public double MaxOffset => PagePagerPolicy.PageToOffset(PageCount - 1, PageWidth, PageCount);

    /// <summary>Raised when the committed page changes.</summary>
    public event Action<int>? PageChanged;

    /// <summary>Starts a drag at panel-local <paramref name="x"/>; interrupts any running snap.</summary>
    public void BeginDrag(double x, long timestampMs)
    {
        _isDragging = true;
        _accumulatedDragDip = 0;
        _dragVelocityDipPerMs = 0;
        _dragStartX = x;
        _lastDragX = x;
        _lastDragTimestamp = timestampMs;
        _dragStartOffset = _motion.Interrupt();
        _dragStartPage = PagePagerPolicy.OffsetToPage(_dragStartOffset, PageWidth, PageCount);
    }

    /// <summary>Updates the drag; returns the new visual offset.</summary>
    public double DragTo(double x, long timestampMs)
    {
        if (!_isDragging)
        {
            return Offset;
        }

        var dt = Math.Max(1, timestampMs - _lastDragTimestamp) / 1000.0;
        var dx = x - _lastDragX;
        _lastDragX = x;
        _lastDragTimestamp = timestampMs;
        _dragVelocityDipPerMs = -((_dragVelocityDipPerMs * 0.6) + (((dx / dt) * 0.4) / 1000.0));
        _accumulatedDragDip = -(x - _dragStartX);

        var state = PagePagerPolicy.Drag(_dragStartPage, _accumulatedDragDip, PageCount, PageWidth);
        var deltaFromPage = state.DisplayOffsetDip - PagePagerPolicy.PageToOffset(_dragStartPage, PageWidth, PageCount);
        var visual = _dragStartOffset + deltaFromPage;
        _motion.JumpTo(visual);
        return visual;
    }

    /// <summary>Ends the drag, commits the snap target and returns it.</summary>
    public int EndDrag(long timestampMs)
    {
        if (!_isDragging)
        {
            return SelectedIndex;
        }

        _isDragging = false;
        var decision = PagePagerPolicy.Release(_dragStartPage, _accumulatedDragDip, _dragVelocityDipPerMs, PageCount, PageWidth);
        CommitPage(decision.TargetPage);
        _motion.SnapToOffset(decision.TargetOffsetDip);
        return decision.TargetPage;
    }

    /// <summary>Resolves the target page for a completed gesture (pure policy delegation).</summary>
    public int ResolveTargetPage(double dragDip, double velocityDipPerMs)
        => PagePagerPolicy.Release(SelectedIndex, dragDip, velocityDipPerMs, PageCount, PageWidth).TargetPage;

    /// <summary>Applies a wheel delta (notch-sized); returns the page after the change.</summary>
    public int Wheel(double deltaX, double deltaY, long timestampMs)
    {
        if (_isDragging)
        {
            return SelectedIndex;
        }

        var delta = Math.Abs(deltaY) > Math.Abs(deltaX) ? deltaY : deltaX;
        var now = DateTimeOffset.UnixEpoch.AddMilliseconds(timestampMs);
        var result = _wheel.Accumulate(delta * WheelNotchDip, now, IsWheelBlocked);
        if (result.OwnerSuppressed || !result.Consumed || result.PageDelta == 0)
        {
            return SelectedIndex;
        }

        var decision = PagePagerPolicy.ReleaseWheel(SelectedIndex, result.PageDelta, PageCount, PageWidth);
        CommitPage(decision.TargetPage);
        _motion.SnapToOffset(decision.TargetOffsetDip);
        return decision.TargetPage;
    }

    /// <summary>Animated jump to a page (dots, keyboard 1–6, Home/End).</summary>
    public void SelectPage(int index)
    {
        var target = PagePagerPolicy.ClampPage(index, PageCount);
        CommitPage(target);
        _motion.SnapToPage(target, PageCount);
    }

    /// <summary>One page left/right (keyboard arrows).</summary>
    public void MoveBy(int delta) => SelectPage(SelectedIndex + Math.Sign(delta));

    /// <summary>Steps the snap spring; returns the visual offset.</summary>
    public double Step(double dtSeconds) => _motion.Step(dtSeconds);

    /// <summary>Instantly jumps to a page (reduce motion / restore / teardown).</summary>
    public void SnapToPage(int index)
    {
        var target = PagePagerPolicy.ClampPage(index, PageCount);
        _motion.JumpTo(PagePagerPolicy.PageToOffset(target, PageWidth, PageCount));
        CommitPage(target);
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
