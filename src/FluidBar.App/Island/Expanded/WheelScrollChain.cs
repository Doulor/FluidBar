namespace FluidBar.App.Island.Expanded;

/// <summary>Decision of the single-owner wheel scroll-chain gate.</summary>
public enum WheelChainAction
{
    /// <summary>No effect (still accumulating overscroll).</summary>
    None,

    /// <summary>Turn one page (boundary crossed or forced).</summary>
    Page,

    /// <summary>The content owns this wheel event (scrollable with room).</summary>
    ContentScroll,

    /// <summary>Wheel is ignored because a page animation/lock owns the gesture.</summary>
    Locked,
}

/// <summary>
/// Single-owner wheel scroll chaining while Core's <c>PagerScrollChainPolicy</c> lands (Lead hand-off
/// 2026-10-18): horizontal wheel and Shift+wheel always page; otherwise the scrollable content keeps
/// the wheel until it hits its boundary, then 40 DIP of overscroll + a 120 ms dwell turns the page;
/// reversing direction cancels the pending overscroll; a running page animation locks content scroll.
/// Pure and deterministic; the panel feeds it pointer/wheel state.
/// </summary>
public sealed class WheelScrollChain
{
    /// <summary>Overscroll distance that arms a page turn (DIP).</summary>
    public const double OverscrollThreshold = 40;

    /// <summary>Dwell time at the boundary before the page turns (ms).</summary>
    public const int DwellMs = 120;

    private double _overscroll;
    private long _dwellStart = -1;
    private int _direction;

    /// <summary>Current accumulated overscroll (DIP, signed).</summary>
    public double CurrentOverscroll => _overscroll;

    /// <summary>Evaluates one wheel event.</summary>
    /// <param name="deltaX">Horizontal wheel delta (notches).</param>
    /// <param name="deltaY">Vertical wheel delta (notches).</param>
    /// <param name="shiftForcesPage">Shift is held (always page).</param>
    /// <param name="contentCanScrollFurther">Whether the focused content can still scroll this direction.</param>
    /// <param name="pagerAnimating">Whether a page animation currently owns the gesture.</param>
    /// <param name="nowMs">Monotonic timestamp in milliseconds.</param>
    public WheelChainAction Accumulate(
        double deltaX,
        double deltaY,
        bool shiftForcesPage,
        bool contentCanScrollFurther,
        bool pagerAnimating,
        long nowMs)
    {
        var horizontal = Math.Abs(deltaX) > Math.Abs(deltaY);
        var delta = horizontal ? deltaX : deltaY;
        if (Math.Abs(delta) < 1e-9)
        {
            return WheelChainAction.None;
        }

        if (pagerAnimating)
        {
            Reset();
            return WheelChainAction.Locked;
        }

        if (horizontal || shiftForcesPage)
        {
            Reset();
            return WheelChainAction.Page;
        }

        if (contentCanScrollFurther)
        {
            Reset();
            return WheelChainAction.ContentScroll;
        }

        var direction = Math.Sign(delta);
        if (_direction != 0 && direction != _direction)
        {
            // Reverse cancels the pending overscroll and hands the gesture back to the content.
            Reset();
            return WheelChainAction.ContentScroll;
        }

        _direction = direction;
        if (_dwellStart < 0)
        {
            _dwellStart = nowMs;
        }

        _overscroll += delta * 120.0;
        if (Math.Abs(_overscroll) < OverscrollThreshold)
        {
            return WheelChainAction.None;
        }

        if (nowMs - _dwellStart < DwellMs)
        {
            return WheelChainAction.None;
        }

        Reset();
        return WheelChainAction.Page;
    }

    /// <summary>Releases the pending overscroll after the host performed the page turn.</summary>
    public void Reset()
    {
        _overscroll = 0;
        _dwellStart = -1;
        _direction = 0;
    }
}
