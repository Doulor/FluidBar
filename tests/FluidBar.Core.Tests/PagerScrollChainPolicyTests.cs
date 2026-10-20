using FluidBar.Core.Interaction;

namespace FluidBar.Core.Tests;

/// <summary>
/// Pager v2 scroll-chain policy: decides whether a wheel event scrolls the content under the pointer
/// or flips a page. Covers content-with-room, boundary overscroll + dwell, reverse-cancel, immediate
/// paging for non-scrollable content, horizontal/Shift bypass, page-animation lock, clamping and
/// deterministic repeats.
/// </summary>
public class PagerScrollChainPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private const int TestPageCount = 6;

    private static PagerScrollChainPolicy NewPolicy() => new();

    private static PagerScrollChainRequest Request(
        double delta,
        bool scrollable = true,
        PagerScrollBoundary boundary = PagerScrollBoundary.None,
        DateTimeOffset? now = null,
        int page = 2,
        int pageCount = TestPageCount,
        bool locked = false,
        bool horizontal = false,
        bool shift = false)
        => new()
        {
            WheelDeltaDip = delta,
            ContentScrollable = scrollable,
            ContentBoundary = boundary,
            CurrentPage = page,
            PageCount = pageCount,
            Now = now ?? T0,
            PageAnimationLocked = locked,
            HorizontalWheel = horizontal,
            ShiftHeld = shift,
        };

    [Fact]
    public void Constants_DefaultScrollChainThresholds()
    {
        var policy = NewPolicy();
        Assert.Equal(40, policy.OverscrollThresholdDip, 1e-9);
        Assert.Equal(120, policy.DwellMs);
        Assert.Equal(PagerConstants.ScrollChainOverscrollDip, policy.OverscrollThresholdDip);
        Assert.Equal(PagerConstants.ScrollChainDwellMs, policy.DwellMs);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(-50)]
    public void ScrollableWithRoom_ContentOwnsTheGesture(double delta)
    {
        var policy = NewPolicy();

        var decision = policy.Handle(Request(delta));

        Assert.Equal(PagerScrollChainAction.ScrollContent, decision.Action);
        Assert.True(decision.Consumed);
        Assert.Equal(2, decision.CurrentPage);
        Assert.Equal(2, decision.TargetPage);
        Assert.Equal(0, policy.AccumulatedOverscrollDip, 1e-9);
        Assert.False(policy.HasPendingPageStep);
    }

    [Fact]
    public void BoundaryMismatch_StillScrollsContent()
    {
        var policy = NewPolicy();

        // Wheel down while the content is at its top edge: the content has room, so it scrolls.
        var decision = policy.Handle(Request(50, boundary: PagerScrollBoundary.Top));

        Assert.Equal(PagerScrollChainAction.ScrollContent, decision.Action);
    }

    [Fact]
    public void Boundary_SmallOverscroll_ReturnsNone()
    {
        var policy = NewPolicy();

        var decision = policy.Handle(Request(20, boundary: PagerScrollBoundary.Bottom));

        Assert.Equal(PagerScrollChainAction.None, decision.Action);
        Assert.False(decision.Consumed);
        Assert.Equal(20, decision.AccumulatedOverscrollDip, 1e-9);
        Assert.True(policy.HasPendingPageStep);
    }

    [Fact]
    public void Boundary_OverThresholdButShortDwell_ReturnsNone()
    {
        var policy = NewPolicy();

        var first = policy.Handle(Request(30, boundary: PagerScrollBoundary.Bottom));
        var second = policy.Handle(Request(30, boundary: PagerScrollBoundary.Bottom, now: T0.AddMilliseconds(60)));

        Assert.Equal(PagerScrollChainAction.None, first.Action);
        Assert.Equal(PagerScrollChainAction.None, second.Action);
        Assert.Equal(60, policy.AccumulatedOverscrollDip, 1e-9);
        Assert.True(policy.HasPendingPageStep);
    }

    [Fact]
    public void Boundary_OverThresholdAndDwell_PagesNext()
    {
        var policy = NewPolicy();
        _ = policy.Handle(Request(25, boundary: PagerScrollBoundary.Bottom));
        var decision = policy.Handle(Request(25, boundary: PagerScrollBoundary.Bottom, now: T0.AddMilliseconds(130)));

        Assert.Equal(PagerScrollChainAction.PageNext, decision.Action);
        Assert.Equal(2, decision.CurrentPage);
        Assert.Equal(3, decision.TargetPage);
        Assert.True(decision.Consumed);
        Assert.False(policy.HasPendingPageStep);
        Assert.Equal(0, policy.AccumulatedOverscrollDip, 1e-9);
    }

    [Fact]
    public void Boundary_AtTop_OverThresholdAndDwell_PagesPrevious()
    {
        var policy = NewPolicy();
        _ = policy.Handle(Request(-20, boundary: PagerScrollBoundary.Top));
        var decision = policy.Handle(Request(-20, boundary: PagerScrollBoundary.Top, now: T0.AddMilliseconds(120)));

        Assert.Equal(PagerScrollChainAction.PagePrevious, decision.Action);
        Assert.Equal(1, decision.TargetPage);
    }

    [Fact]
    public void ReverseDirection_CancelsPendingPageStep()
    {
        var policy = NewPolicy();
        _ = policy.Handle(Request(30, boundary: PagerScrollBoundary.Bottom, now: T0));

        var cancelled = policy.Handle(Request(-30, boundary: PagerScrollBoundary.Bottom, now: T0.AddMilliseconds(50)));

        Assert.Equal(PagerScrollChainAction.None, cancelled.Action);
        Assert.True(cancelled.ReverseCancelled);
        Assert.False(policy.HasPendingPageStep);
        Assert.Equal(0, policy.AccumulatedOverscrollDip, 1e-9);

        // After the cancel the content can scroll normally again.
        var scroll = policy.Handle(Request(-50, boundary: PagerScrollBoundary.None, now: T0.AddMilliseconds(60)));
        Assert.Equal(PagerScrollChainAction.ScrollContent, scroll.Action);
    }

    [Fact]
    public void NonScrollableContent_PagesImmediately()
    {
        var policy = NewPolicy();

        var next = policy.Handle(Request(10, scrollable: false));
        Assert.Equal(PagerScrollChainAction.PageNext, next.Action);
        Assert.Equal(3, next.TargetPage);

        var previous = policy.Handle(Request(-10, scrollable: false, page: 4));
        Assert.Equal(PagerScrollChainAction.PagePrevious, previous.Action);
        Assert.Equal(3, previous.TargetPage);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HorizontalWheelOrShift_BypassesScrollableContent(bool horizontal, bool shift)
    {
        var policy = NewPolicy();

        var decision = policy.Handle(Request(
            10,
            scrollable: true,
            boundary: PagerScrollBoundary.None,
            horizontal: horizontal,
            shift: shift));

        Assert.Equal(PagerScrollChainAction.PageNext, decision.Action);
        Assert.Equal(3, decision.TargetPage);
    }

    [Fact]
    public void PageAnimationLock_FreezesTheWholeChain()
    {
        var policy = NewPolicy();

        var scrollable = policy.Handle(Request(50, locked: true));
        var atBoundary = policy.Handle(Request(200, boundary: PagerScrollBoundary.Bottom, locked: true));
        var horizontal = policy.Handle(Request(10, scrollable: false, locked: true, horizontal: true));

        Assert.Equal(PagerScrollChainAction.None, scrollable.Action);
        Assert.True(scrollable.PageLocked);
        Assert.Equal(PagerScrollChainAction.None, atBoundary.Action);
        Assert.True(atBoundary.PageLocked);
        Assert.Equal(PagerScrollChainAction.None, horizontal.Action);
        Assert.True(horizontal.PageLocked);
        Assert.False(policy.HasPendingPageStep);
    }

    [Fact]
    public void PageBounds_ClampAtFirstAndLastPage()
    {
        var policy = NewPolicy();

        var atEnd = policy.Handle(Request(500, scrollable: false, page: 5));
        Assert.Equal(PagerScrollChainAction.None, atEnd.Action);
        Assert.Equal(5, atEnd.TargetPage);

        var atStart = policy.Handle(Request(-500, scrollable: false, page: 0));
        Assert.Equal(PagerScrollChainAction.None, atStart.Action);
        Assert.Equal(0, atStart.TargetPage);
    }

    [Fact]
    public void PageStep_RequiresFreshGestureAfterEachPage()
    {
        var policy = NewPolicy();

        _ = policy.Handle(Request(20, boundary: PagerScrollBoundary.Bottom, page: 0, now: T0));
        var pageOne = policy.Handle(Request(20, boundary: PagerScrollBoundary.Bottom, page: 0, now: T0.AddMilliseconds(120)));
        Assert.Equal(PagerScrollChainAction.PageNext, pageOne.Action);

        // A fresh boundary dwell is required for the next page: 20 DIP is below the threshold again.
        var fresh = policy.Handle(Request(20, boundary: PagerScrollBoundary.Bottom, page: 1, now: T0.AddMilliseconds(130)));
        Assert.Equal(PagerScrollChainAction.None, fresh.Action);
        Assert.False(fresh.Consumed);
    }

    [Fact]
    public void CustomPageCount_ClampsPageStep()
    {
        var policy = NewPolicy();

        var decision = policy.Handle(Request(500, scrollable: false, page: 3, pageCount: 4));

        Assert.Equal(PagerScrollChainAction.None, decision.Action);
        Assert.Equal(3, decision.TargetPage);
    }

    [Fact]
    public void ZeroOrNonFiniteDelta_ReturnsNone()
    {
        var policy = NewPolicy();

        Assert.Equal(PagerScrollChainAction.None, policy.Handle(Request(0, scrollable: false)).Action);
        Assert.Equal(PagerScrollChainAction.None, policy.Handle(Request(double.NaN, scrollable: false)).Action);
        Assert.False(policy.HasPendingPageStep);
    }

    [Fact]
    public void Reset_ClearsPendingAccumulation()
    {
        var policy = NewPolicy();
        _ = policy.Handle(Request(30, boundary: PagerScrollBoundary.Bottom));
        Assert.True(policy.HasPendingPageStep);

        policy.Reset();

        Assert.False(policy.HasPendingPageStep);
        Assert.Equal(0, policy.AccumulatedOverscrollDip, 1e-9);
        Assert.Null(policy.DwellStartedAt);
    }

    [Fact]
    public void DecisionsAreDeterministic()
    {
        static List<PagerScrollChainDecision> Run()
        {
            var policy = NewPolicy();
            return
            [
                policy.Handle(Request(30, boundary: PagerScrollBoundary.Bottom, now: T0)),
                policy.Handle(Request(30, boundary: PagerScrollBoundary.Bottom, now: T0.AddMilliseconds(130))),
                policy.Handle(Request(-20, scrollable: false, now: T0.AddMilliseconds(200))),
                policy.Handle(Request(50, now: T0.AddMilliseconds(300))),
            ];
        }

        var first = Run();
        var second = Run();
        Assert.Equal(first, second);
    }

    [Fact]
    public void DirectionToken_IsStable()
    {
        Assert.Equal("up", PagerScrollChainPolicy.DirectionToken(-10));
        Assert.Equal("down", PagerScrollChainPolicy.DirectionToken(10));
    }

    [Fact]
    public void Request_DirectionAndMagnitudeHelpers()
    {
        var down = Request(42.5);
        Assert.Equal(PagerScrollDirection.Down, down.Direction);
        Assert.Equal(42.5, down.MagnitudeDip, 1e-9);

        var up = Request(-42.5);
        Assert.Equal(PagerScrollDirection.Up, up.Direction);
        Assert.Equal(42.5, up.MagnitudeDip, 1e-9);
    }
}

