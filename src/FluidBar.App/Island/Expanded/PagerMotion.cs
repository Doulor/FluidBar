using FluidBar.Core.Interaction;
using FluidBar.Core.Motion;

namespace FluidBar.App.Island.Expanded;

/// <summary>
/// UI adapter for the horizontal pager + Page Archipelago navigation. All snap/drag math stays in Core
/// <see cref="PagePagerPolicy"/>; the App owns the version-A tuning requested by the user
/// (2026-10-18): pointer follow 0.80, snap spring T=0.44 s ζ=0.97, wheel notch 140 DIP / 180 ms
/// cooldown, 26 DIP snap-back threshold. When Core publishes the tuned constants this class is the
/// only place to drop the overrides.
/// </summary>
public sealed class PagerMotion
{
    /// <summary>Number of pages (Core constant).</summary>
    public const int DefaultPageCount = PagerConstants.PageCount;

    /// <summary>Page pitch in DIP (Core constant).</summary>
    public const double PagePitch = PagerConstants.PagePitchDip;

    /// <summary>Pointer follow factor — stronger damping than the original 0.90 (user choice A).</summary>
    public const double PointerDamping = 0.80;

    /// <summary>Snap spring period in seconds (user choice A: 0.44 s).</summary>
    public const double SnapPeriodSeconds = 0.44;

    /// <summary>Snap spring damping ratio (user choice A: 0.97, softer overshoot).</summary>
    public const double SnapDampingRatio = 0.97;

    /// <summary>Wheel travel that equals one page notch (140 DIP).</summary>
    public const double WheelThresholdDip = 140;

    /// <summary>Minimum gap between two wheel-driven page changes (180 ms).</summary>
    public const int WheelCooldownMs = 180;

    /// <summary>Drag distance below which the pager snaps back without changing page (26 DIP).</summary>
    public const double SmallDragThreshold = 26;

    /// <summary>Movement above this cancels page-internal clicks (6 DIP).</summary>
    public const double ClickCancelThreshold = 6;

    private readonly Spring _snap;
    private readonly double _pageWidth;

    private bool _isDragging;
    private int _dragStartPage;
    private double _dragStartX;
    private double _dragStartOffset;
    private double _accumulatedDragDip;
    private double _dragVelocityDipPerMs;
    private long _lastDragTimestamp;
    private double _lastDragX;
    private double _wheelAccumulator;
    private long _lastWheelTimestamp = long.MinValue / 2;

    /// <summary>Creates the pager for <paramref name="pageCount"/> pages.</summary>
    public PagerMotion(int pageCount = PagerConstants.PageCount, double pageWidth = PagerConstants.PagePitchDip, int initialPage = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pageWidth, 0);
        PageCount = pageCount;
        _pageWidth = pageWidth;
        SelectedIndex = PagePagerPolicy.ClampPage(initialPage, pageCount);
        _snap = new Spring(
            new SpringParameters(SnapPeriodSeconds, SnapDampingRatio),
            PagePagerPolicy.PageToOffset(SelectedIndex, pageWidth, pageCount));
    }

    /// <summary>Number of pages.</summary>
    public int PageCount { get; }

    /// <summary>Page pitch in DIP.</summary>
    public double PageWidth => _pageWidth;

    /// <summary>Current visual offset in DIP.</summary>
    public double Offset => _snap.Value;

    /// <summary>Committed page index.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>Whether a pointer drag is in flight.</summary>
    public bool IsDragging => _isDragging;

    /// <summary>Whether the snap spring is running.</summary>
    public bool IsAnimating => !_snap.IsAtRest;

    /// <summary>Signed accumulated drag in DIP during the gesture (positive = next page).</summary>
    public double DragDistance => _accumulatedDragDip;

    /// <summary>Whether the gesture moved far enough to cancel page-internal clicks.</summary>
    public bool MovedPastClickThreshold => Math.Abs(_accumulatedDragDip) > ClickCancelThreshold;

    /// <summary>Whether wheel input is suppressed because the content owns the gesture.</summary>
    public bool IsWheelBlocked { get; set; }

    /// <summary>Maximum content offset in DIP.</summary>
    public double MaxOffset => PagePagerPolicy.PageToOffset(PageCount - 1, _pageWidth, PageCount);

    /// <summary>Current fractional page position (archipelago lag/parallax source).</summary>
    public double PagePosition => Offset / _pageWidth;

    /// <summary>Raised when the committed page changes.</summary>
    public event Action<int>? PageChanged;

    /// <summary>Starts a drag at panel-local <paramref name="x"/>; interrupts a running snap.</summary>
    public void BeginDrag(double x, long timestampMs)
    {
        _isDragging = true;
        _accumulatedDragDip = 0;
        _dragVelocityDipPerMs = 0;
        _dragStartX = x;
        _lastDragX = x;
        _lastDragTimestamp = timestampMs;
        _dragStartOffset = _snap.Value;
        _snap.SnapTo(_dragStartOffset);
        _dragStartPage = PagePagerPolicy.OffsetToPage(_dragStartOffset, _pageWidth, PageCount);
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

        var state = PagePagerPolicy.Drag(
            _dragStartPage,
            _accumulatedDragDip,
            PageCount,
            _pageWidth,
            pointerDamping: PointerDamping);
        var deltaFromPage = state.DisplayOffsetDip - PagePagerPolicy.PageToOffset(_dragStartPage, _pageWidth, PageCount);
        var visual = _dragStartOffset + deltaFromPage;
        _snap.SnapTo(visual);
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
        var decision = PagePagerPolicy.Release(
            _dragStartPage,
            _accumulatedDragDip,
            _dragVelocityDipPerMs,
            PageCount,
            _pageWidth,
            pageThresholdDip: SmallDragThreshold);
        CommitPage(decision.TargetPage);
        _snap.Retarget(decision.TargetOffsetDip, 1.0 / 120.0);
        return decision.TargetPage;
    }

    /// <summary>Resolves the target page for a completed gesture (pure Core policy delegation).</summary>
    public int ResolveTargetPage(double dragDip, double velocityDipPerMs)
        => PagePagerPolicy.Release(SelectedIndex, dragDip, velocityDipPerMs, PageCount, _pageWidth, pageThresholdDip: SmallDragThreshold).TargetPage;

    /// <summary>Applies a wheel delta (notch-sized); returns the page after the change.</summary>
    public int Wheel(double deltaX, double deltaY, long timestampMs)
    {
        if (_isDragging || IsWheelBlocked)
        {
            return SelectedIndex;
        }

        var delta = Math.Abs(deltaY) > Math.Abs(deltaX) ? deltaY : deltaX;
        _wheelAccumulator += delta * WheelThresholdDip;
        if (Math.Abs(_wheelAccumulator) < WheelThresholdDip)
        {
            return SelectedIndex;
        }

        if (timestampMs - _lastWheelTimestamp < WheelCooldownMs)
        {
            return SelectedIndex;
        }

        var step = Math.Sign(_wheelAccumulator);
        _wheelAccumulator = 0;
        _lastWheelTimestamp = timestampMs;
        var target = Math.Clamp(SelectedIndex + step, 0, PageCount - 1);
        if (target != SelectedIndex)
        {
            CommitPage(target);
            _snap.Retarget(PagePagerPolicy.PageToOffset(target, _pageWidth, PageCount), 1.0 / 120.0);
        }

        return target;
    }

    /// <summary>Animated jump to a page (archipelago click, keyboard 1–6, Home/End).</summary>
    public void SelectPage(int index)
    {
        var target = PagePagerPolicy.ClampPage(index, PageCount);
        CommitPage(target);
        _snap.Retarget(PagePagerPolicy.PageToOffset(target, _pageWidth, PageCount), 1.0 / 120.0);
    }

    /// <summary>One page left/right (keyboard arrows, wheel).</summary>
    public void MoveBy(int delta) => SelectPage(SelectedIndex + Math.Sign(delta));

    /// <summary>Steps the snap spring; returns the visual offset.</summary>
    public double Step(double dtSeconds) => _snap.Step(dtSeconds);

    /// <summary>Instantly jumps to a page (reduce motion / restore / teardown).</summary>
    public void SnapToPage(int index)
    {
        var target = PagePagerPolicy.ClampPage(index, PageCount);
        _snap.SnapTo(PagePagerPolicy.PageToOffset(target, _pageWidth, PageCount));
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
