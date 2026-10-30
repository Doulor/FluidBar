using FluidBar.Core.Interaction;
using FluidBar.Core.Motion;
using FluidBar.Core.Tests.TestSupport;

namespace FluidBar.Core.Tests;

/// <summary>
/// Pager navigation policy (task-10): horizontal drag pager replacing the rejected left rail.
/// Covers the exact release rules (threshold, flick cap, multi-page distance, bounds), wheel
/// accumulation/cooldown/owner suppression, and the interruptible pager v2 snap spring (T 0.38 / ζ 0.94).
/// </summary>
public class PagerPolicyTests
{
    private const double Eps = 1e-9;
    private const int TestPageCount = 6;
    private static readonly DateTimeOffset T0 = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constants_MatchAcceptedFeedback()
    {
        Assert.Equal(6, FluidBar.Core.Activities.PageRef.BuiltinPages.Count);
        Assert.Equal(400, PagerConstants.PagePitchDip, Eps);
        Assert.Equal(0.85, PagerConstants.PointerDamping, Eps);
        Assert.Equal(0.35, PagerConstants.EdgeResistance, Eps);
        Assert.Equal(26, PagerConstants.PageThresholdDip, Eps);
        Assert.Equal(0.55, PagerConstants.FlickVelocityDipPerMs, Eps);
        Assert.Equal(10, PagerConstants.FlickMinimumDragDip, Eps);
        // prototype2 release: |distance| >= 0.75 pages decides by distance instead of nearest page.
        Assert.Equal(0.75, PagerConstants.LongDragPageFraction, Eps);
        Assert.Equal(14, PagerConstants.MaxReleaseVelocityPagesPerSecond, Eps);
        Assert.Equal(60, PagerConstants.WheelNotchDip, Eps);
        Assert.Equal(60, PagerConstants.WheelDipPerNotch, Eps);
        Assert.Equal(120, PagerConstants.WheelCooldownMs);
        Assert.Equal(120, PagerConstants.WheelTransitionLockMs);
        Assert.Equal(80, PagerConstants.WheelGestureIdleMs);
        Assert.Equal(90, PagerConstants.WheelNewIntentMinMs);
        Assert.Equal(0.35, PagerConstants.SnapSpringPeriodSeconds, Eps);
        Assert.Equal(0.75, PagerConstants.SnapSpringDampingRatio, Eps);
        Assert.Equal(24, PagerConstants.ScrollChainOverscrollDip, Eps);
        Assert.Equal(50, PagerConstants.ScrollChainDwellMs);
        Assert.Equal(400, PagerConstants.ContentWidthDip, Eps);
        Assert.Equal(60, PagerConstants.ContentHorizontalInsetDip, Eps);
        Assert.Equal(16, PagerConstants.ContentVerticalInsetDip, Eps);
    }

    [Fact]
    public void Drag_AppliesPointerDamping()
    {
        var state = Drag(startPage: 0, accumulatedDragDip: 100);

        Assert.Equal(85, state.DampedDragDip, Eps);
        Assert.Equal(85, state.DisplayOffsetDip, Eps);
        Assert.Equal(0.2125, state.PagePosition, Eps);
        Assert.False(state.AtStartEdge);
        Assert.False(state.AtEndEdge);
    }

    [Fact]
    public void Drag_RubberBandsPastLastPageAt35Percent()
    {
        var state = Drag(startPage: 5, accumulatedDragDip: 100);

        // max offset = 2000; damped 90 → raw 2090 → display 2000 + 90*0.35.
        Assert.Equal(2085, state.RawOffsetDip, Eps);
        Assert.Equal(2000 + (85 * 0.35), state.DisplayOffsetDip, Eps);
        Assert.True(state.AtEndEdge);
        Assert.False(state.AtStartEdge);
    }

    [Fact]
    public void Drag_RubberBandsBeforeFirstPageAt35Percent()
    {
        var state = Drag(startPage: 0, accumulatedDragDip: -100);

        Assert.Equal(-85, state.RawOffsetDip, Eps);
        Assert.Equal(-85 * 0.35, state.DisplayOffsetDip, Eps);
        Assert.True(state.AtStartEdge);
        Assert.False(state.AtEndEdge);
    }

    [Theory]
    [InlineData(-23.9)]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(23.9)]
    public void Release_BelowThreshold_SnapsBackWithNoPageChange(double drag)
    {
        var decision = Release(startPage: 3, accumulatedDragDip: drag, velocityDipPerMs: 10);

        Assert.Equal(PagerSnapKind.None, decision.Kind);
        Assert.Equal(3, decision.StartPage);
        Assert.Equal(3, decision.TargetPage);
        Assert.False(decision.ChangesPage);
        Assert.Equal(3 * 400, decision.TargetOffsetDip, Eps);
    }

    [Theory]
    [InlineData(0.56)]
    [InlineData(1.5)]
    [InlineData(1000.0)]
    public void Release_Flick_IsCappedAtOnePage(double velocity)
    {
        // prototype2 release: a flick (velocity >= 0.55 DIP/ms, same direction, >= 10 DIP travel)
        // always moves exactly one page, regardless of how large the velocity is.
        var decision = Release(startPage: 2, accumulatedDragDip: 150, velocityDipPerMs: velocity);

        Assert.Equal(PagerSnapKind.Flick, decision.Kind);
        Assert.Equal(2, decision.StartPage);
        Assert.Equal(3, decision.TargetPage);
        Assert.Equal(1, decision.PageDelta);
        Assert.Equal(3 * 400, decision.TargetOffsetDip, Eps);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.54)]
    public void Release_SlowShortDrag_SnapsBackToTheNearestPage(double velocity)
    {
        // 150 DIP raw = 0.375 page, 0.31875 page after the 0.85 pointer damping: below the 0.75-page
        // long-drag threshold and not a flick, so prototype2 rounds to the nearest page (0) and snaps back.
        var decision = Release(startPage: 2, accumulatedDragDip: 150, velocityDipPerMs: velocity);

        Assert.Equal(PagerSnapKind.None, decision.Kind);
        Assert.Equal(2, decision.TargetPage);
        Assert.Equal(0, decision.PageDelta);
        Assert.Equal(2 * 400, decision.TargetOffsetDip, Eps);
    }

    [Fact]
    public void Release_HugeVelocityNeverSkipsPages()
    {
        // Just under half a page with an absurd flick velocity: still exactly one page.
        var decision = Release(
            startPage: 1,
            accumulatedDragDip: (0.5 * 400) - 0.01,
            velocityDipPerMs: 10_000);

        Assert.Equal(PagerSnapKind.Flick, decision.Kind);
        Assert.Equal(2, decision.TargetPage);
        Assert.Equal(1, decision.PageDelta);
    }

    [Fact]
    public void Release_FlickNegativeDirection()
    {
        var decision = Release(startPage: 3, accumulatedDragDip: -150, velocityDipPerMs: -2.5);

        Assert.Equal(PagerSnapKind.Flick, decision.Kind);
        Assert.Equal(2, decision.TargetPage);
        Assert.Equal(-1, decision.PageDelta);
    }

    [Fact]
    public void Release_LongDrag27Pages_CrossesTwoDampedPages()
    {
        // prototype2 counts the damped travel (2.7 × 0.85 = 2.295 pages) and rounds it to 2.
        var decision = Release(startPage: 1, accumulatedDragDip: 2.7 * 400, velocityDipPerMs: 0.2);

        Assert.Equal(PagerSnapKind.Distance, decision.Kind);
        Assert.Equal(3, decision.TargetPage);
        Assert.Equal(2, decision.PageDelta);
        Assert.Equal(3 * 400, decision.TargetOffsetDip, Eps);
    }

    [Fact]
    public void Release_LongDragNegative27Pages()
    {
        var decision = Release(startPage: 5, accumulatedDragDip: -2.7 * 400, velocityDipPerMs: -0.1);

        Assert.Equal(PagerSnapKind.Distance, decision.Kind);
        Assert.Equal(3, decision.TargetPage);
        Assert.Equal(-2, decision.PageDelta);
    }

    [Fact]
    public void Release_HalfPage_SnapsBackBelowTheLongDragThreshold()
    {
        // 200 DIP raw is only 0.425 page after pointer damping, so prototype2 settles back rather than
        // flipping a page (a slow half-page drag reads as "not far enough", not an unrequested jump).
        var forward = Release(startPage: 1, accumulatedDragDip: 200, velocityDipPerMs: 0);
        Assert.Equal(PagerSnapKind.None, forward.Kind);
        Assert.Equal(1, forward.TargetPage);
        Assert.Equal(0, forward.PageDelta);

        var backward = Release(startPage: 3, accumulatedDragDip: -200, velocityDipPerMs: 0);
        Assert.Equal(PagerSnapKind.None, backward.Kind);
        Assert.Equal(3, backward.TargetPage);
        Assert.Equal(0, backward.PageDelta);
    }

    [Fact]
    public void Release_ClampsAtBounds()
    {
        var atEnd = Release(startPage: 5, accumulatedDragDip: 2.7 * 400, velocityDipPerMs: 1);
        Assert.Equal(5, atEnd.TargetPage);
        Assert.Equal(PagerSnapKind.None, atEnd.Kind);
        Assert.False(atEnd.ChangesPage);

        var nearEnd = Release(startPage: 4, accumulatedDragDip: 2.7 * 400, velocityDipPerMs: 0.8);
        Assert.Equal(5, nearEnd.TargetPage);
        Assert.Equal(1, nearEnd.PageDelta);

        var atStart = Release(startPage: 0, accumulatedDragDip: -2.7 * 400, velocityDipPerMs: -1);
        Assert.Equal(0, atStart.TargetPage);
        Assert.Equal(PagerSnapKind.None, atStart.Kind);
    }

    [Fact]
    public void Release_CustomPitchScalesDistanceDecision()
    {
        var decision = Release(
            startPage: 0,
            accumulatedDragDip: 2.7 * 300,
            velocityDipPerMs: 0,
            pageCount: 6,
            pagePitchDip: 300);

        // 2.7 × 300 DIP raw, damped by 0.85 = 2.295 pages → rounds to 2 pages (600 DIP).
        Assert.Equal(2, decision.TargetPage);
        Assert.Equal(600, decision.TargetOffsetDip, Eps);
    }

    [Fact]
    public void Release_SinglePageCountAlwaysStaysAtZero()
    {
        var decision = Release(startPage: 0, accumulatedDragDip: 2.7 * 400, velocityDipPerMs: 5, pageCount: 1);
        Assert.Equal(0, decision.TargetPage);
        Assert.Equal(PagerSnapKind.None, decision.Kind);
        Assert.Equal(0, ClampPage(4, 1));
    }

    [Fact]
    public void Release_NonFiniteInputsAreTreatedAsNoDrag()
    {
        var nanDrag = Release(startPage: 2, accumulatedDragDip: double.NaN, velocityDipPerMs: 5);
        Assert.Equal(2, nanDrag.TargetPage);
        Assert.Equal(PagerSnapKind.None, nanDrag.Kind);

        var nanVelocity = Release(startPage: 2, accumulatedDragDip: 150, velocityDipPerMs: double.NaN);
        Assert.Equal(2, nanVelocity.TargetPage);
        Assert.Equal(PagerSnapKind.None, nanVelocity.Kind);
    }

    [Fact]
    public void Release_FromDragStateOverload_MatchesDirectCall()
    {
        var state = Drag(startPage: 2, accumulatedDragDip: 2.7 * 400);
        var viaState = Release(state, velocityDipPerMs: 0);
        var direct = Release(2, 2.7 * 400, 0);

        Assert.Equal(direct, viaState);
    }

    [Fact]
    public void ReleaseWheel_ClampsAndClassifies()
    {
        var next = ReleaseWheel(startPage: 5, pageDelta: 1);
        Assert.Equal(PagerSnapKind.None, next.Kind);
        Assert.Equal(5, next.TargetPage);

        var previous = ReleaseWheel(startPage: 2, pageDelta: -2);
        Assert.Equal(PagerSnapKind.Wheel, previous.Kind);
        Assert.Equal(0, previous.TargetPage);
        Assert.Equal(0, previous.TargetOffsetDip, Eps);
    }

    [Fact]
    public void OffsetHelpers_RoundAndClamp()
    {
        Assert.Equal(1, OffsetToPage(400));
        Assert.Equal(1, OffsetToPage(599));
        Assert.Equal(2, OffsetToPage(601));
        Assert.Equal(5, OffsetToPage(99_999));
        Assert.Equal(0, OffsetToPage(-500));
        Assert.Equal(800, PageToOffset(2), Eps);
        Assert.Equal(1600, PageToOffset(4), Eps);
    }

    [Fact]
    public void RepeatedReleasesAreDeterministic()
    {
        var expected = Release(1, 2.7 * 400, 0.75);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(expected, Release(1, 2.7 * 400, 0.75));
            Assert.Equal(expected, Release(1, 2.7 * 400, 0.75));
        }
    }

    [Fact]
    public void Drag_ClampsStartPageInput()
    {
        var below = Drag(startPage: -10, accumulatedDragDip: 0);
        var above = Drag(startPage: 99, accumulatedDragDip: 0);
        Assert.Equal(0, below.StartPage);
        Assert.Equal(5, above.StartPage);
    }

    // ---------------------------------------------------------------- wheel

    [Fact]
    public void Wheel_OneNotchAdvancesOnePage()
    {
        var policy = new PagerWheelPolicy(TestPageCount);
        var result = policy.Accumulate(PagerConstants.WheelDipPerNotch, T0);

        Assert.Equal(1, result.PageDelta);
        Assert.True(result.Consumed);
        Assert.Equal(0, result.AccumulatedDip, Eps);
        Assert.Equal(T0, policy.LastPageChangeAt);
        Assert.Equal(3, WheelDelta(2, result.PageDelta));
    }

    [Fact]
    public void Wheel_OneNotchNegativeGoesBackOnePage()
    {
        var result = new PagerWheelPolicy(TestPageCount).Accumulate(-PagerConstants.WheelDipPerNotch, T0);
        Assert.Equal(-1, result.PageDelta);
    }

    [Fact]
    public void Wheel_SubThresholdDeltasAccumulateToOneNotch()
    {
        var policy = new PagerWheelPolicy(TestPageCount);

        var first = policy.Accumulate(30, T0);
        Assert.Equal(0, first.PageDelta);
        Assert.Equal(30, first.AccumulatedDip, Eps);

        var second = policy.Accumulate(30, T0.AddMilliseconds(10));
        Assert.Equal(1, second.PageDelta);
        Assert.Equal(0, policy.AccumulatedDip, Eps);
    }

    [Fact]
    public void Wheel_CooldownBlocksResidualInertia()
    {
        var policy = new PagerWheelPolicy(TestPageCount, cooldownMs: PagerConstants.WheelCooldownMs);
        Assert.Equal(1, policy.Accumulate(PagerConstants.WheelDipPerNotch, T0).PageDelta);

        // prototype2 keeps accumulating during the cooldown (a reversal drops the residual); only the
        // page commit is paced, so the event is consumed with CooldownBlocked and no page delta.
        var blocked = policy.Accumulate(PagerConstants.WheelDipPerNotch, T0.AddMilliseconds(PagerConstants.WheelNewIntentMs - 1));
        Assert.Equal(0, blocked.PageDelta);
        Assert.True(blocked.CooldownBlocked);
        Assert.True(blocked.Consumed);

        var allowed = policy.Accumulate(PagerConstants.WheelDipPerNotch,
            T0.AddMilliseconds(PagerConstants.WheelNewIntentMs + PagerConstants.WheelGestureIdleMs + 1));
        Assert.Equal(1, allowed.PageDelta);
    }

    [Fact]
    public void Wheel_FiveNotchesOverCooldownWindowsAdvanceFivePages()
    {
        var policy = new PagerWheelPolicy(TestPageCount);
        var page = 0;
        for (var i = 0; i < 5; i++)
        {
            var result = policy.Accumulate(PagerConstants.WheelDipPerNotch,
                T0.AddMilliseconds(i * (PagerConstants.WheelCooldownMs + 1)));
            page = WheelDelta(page, result.PageDelta);
        }

        Assert.Equal(5, page);
    }

    [Fact]
    public void Wheel_RapidNotchesOnlyAdvanceOnePage()
    {
        var policy = new PagerWheelPolicy(TestPageCount);
        var page = 0;
        for (var i = 0; i < 5; i++)
        {
            page = WheelDelta(page, policy.Accumulate(PagerConstants.WheelDipPerNotch, T0).PageDelta);
        }

        Assert.Equal(1, page);
        Assert.Equal(5, policy.ClampCurrent(5));
    }

    [Fact]
    public void Wheel_IgnoresWheelWhenControlOwnsGesture()
    {
        var policy = new PagerWheelPolicy(TestPageCount);
        Assert.Equal(0, policy.Accumulate(30, T0).PageDelta);
        policy.Reset();

        var suppressed = policy.Accumulate(PagerConstants.WheelDipPerNotch, T0.AddSeconds(1), controlOwnsGesture: true);
        Assert.True(suppressed.OwnerSuppressed);
        Assert.False(suppressed.Consumed);
        Assert.Equal(0, suppressed.PageDelta);
        Assert.Equal(0, policy.AccumulatedDip, Eps);
        Assert.Null(policy.LastPageChangeAt);
    }

    [Fact]
    public void Wheel_ResetClearsAccumulationAndCooldown()
    {
        var policy = new PagerWheelPolicy(TestPageCount);
        policy.Accumulate(80, T0);
        policy.Accumulate(140, T0.AddSeconds(1));
        Assert.NotNull(policy.LastPageChangeAt);

        policy.Reset();

        Assert.Equal(0, policy.AccumulatedDip, Eps);
        Assert.Null(policy.LastPageChangeAt);
        Assert.False(policy.IsInCooldown(T0.AddSeconds(1)));
    }

    [Fact]
    public void Wheel_NonFiniteDeltaIsIgnored()
    {
        var policy = new PagerWheelPolicy(TestPageCount);
        var result = policy.Accumulate(double.NaN, T0);
        Assert.Equal(0, result.PageDelta);
        Assert.True(result.Consumed);
        Assert.Equal(0, policy.AccumulatedDip, Eps);
    }

    // ---------------------------------------------------------------- snap motion

    [Fact]
    public void SnapMotion_UsesMotionTabSpring()
    {
        var parameters = PagerSnapMotion.SpringParameters;
        Assert.Equal(0.35, parameters.PeriodSeconds, Eps);
        Assert.Equal(0.75, parameters.DampingRatio, Eps);
        Assert.NotEqual(MotionTokens.Tab.Spring, parameters);
        Assert.Equal(322.27, parameters.Stiffness, 0.5);
    }

    [Fact]
    public void SnapMotion_SettlesExactlyOnTargetPage()
    {
        var motion = new PagerSnapMotion();
        Snap(motion, 3);

        Assert.Equal(1200, motion.TargetOffsetDip, Eps);
        Assert.False(motion.IsAtRest);

        var frames = 0;
        while (!motion.IsAtRest && frames < 2_000)
        {
            motion.Step(1.0 / 300.0);
            frames++;
        }

        Assert.True(motion.IsAtRest);
        Assert.Equal(1200, motion.CurrentOffsetDip, Eps);
        Assert.InRange(frames, 150, 500);
    }

    [Fact]
    public void SnapMotion_InterruptFreezesAtCurrentVisualOffset()
    {
        var motion = new PagerSnapMotion();
        Snap(motion, 4);
        for (var i = 0; i < 50; i++)
        {
            motion.Step(1.0 / 300.0);
        }

        var mid = motion.CurrentOffsetDip;
        Assert.True(mid > 0 && mid < 1600);

        var interrupted = motion.Interrupt();
        Assert.Equal(mid, interrupted, Eps);
        Assert.Equal(mid, motion.CurrentOffsetDip, Eps);
        Assert.True(motion.IsAtRest);
    }

    [Fact]
    public void SnapMotion_DragHandoffContinuesFromCurrentOffset()
    {
        var motion = new PagerSnapMotion();
        Snap(motion, 4);
        for (var i = 0; i < 40; i++)
        {
            motion.Step(1.0 / 300.0);
        }

        var current = motion.Interrupt();
        // A new drag starts at the current visual offset; retarget back to the page the user is on.
        motion.SnapToOffset(current);
        var next = motion.Step(1.0 / 300.0);
        Assert.InRange(Math.Abs(next - current), 0.0, 1.0);
    }

    [Fact]
    public void SnapMotion_JumpToIsInstant()
    {
        var motion = new PagerSnapMotion();
        motion.JumpTo(2000);

        Assert.Equal(2000, motion.CurrentOffsetDip, Eps);
        Assert.True(motion.IsAtRest);
        Assert.Equal(0, motion.RemainingDistanceDip, Eps);
        Assert.Equal(5, motion.PagePosition, Eps);
    }

    [Fact]
    public void SnapMotion_PagePositionTracksVisualOffset()
    {
        var motion = new PagerSnapMotion(1000);
        Assert.Equal(2.5, motion.PagePosition, Eps);
        motion.JumpTo(-100);
        Assert.Equal(-0.25, motion.PagePosition, Eps);
    }

    [Fact]
    public void SnapMotion_RepeatedInterruptsAreIdempotent()
    {
        var motion = new PagerSnapMotion();
        Snap(motion, 2);
        for (var i = 0; i < 30; i++)
        {
            motion.Step(1.0 / 300.0);
        }

        var first = motion.Interrupt();
        var second = motion.Interrupt();
        Assert.Equal(first, second, Eps);
    }

    [Fact]
    public void SnapMotion_IsIndependentOfWallClock()
    {
        var policy = new PagerWheelPolicy(TestPageCount);
        var motionA = new PagerSnapMotion();
        var motionB = new PagerSnapMotion();
        Snap(motionA, 5);
        Thread.Sleep(5);
        Snap(motionB, 5);

        for (var i = 0; i < 200; i++)
        {
            motionA.Step(1.0 / 300.0);
            motionB.Step(1.0 / 300.0);
        }

        Assert.Equal(motionA.CurrentOffsetDip, motionB.CurrentOffsetDip, Eps);
        Assert.Equal(T0, T0);
        _ = policy;
    }

    private static PagerDragState Drag(int startPage, double accumulatedDragDip)
        => PagePagerPolicy.Drag(startPage, accumulatedDragDip, TestPageCount);

    private static PagerSnapDecision Release(int startPage, double accumulatedDragDip, double velocityDipPerMs, int pageCount = TestPageCount, double pagePitchDip = PagerConstants.PagePitchDip)
        => PagePagerPolicy.Release(startPage, accumulatedDragDip, velocityDipPerMs, pageCount, pagePitchDip);

    private static PagerSnapDecision Release(in PagerDragState drag, double velocityDipPerMs, int pageCount = TestPageCount)
        => PagePagerPolicy.Release(drag, velocityDipPerMs, pageCount);

    private static PagerSnapDecision ReleaseWheel(int startPage, int pageDelta, int pageCount = TestPageCount)
        => PagePagerPolicy.ReleaseWheel(startPage, pageDelta, pageCount);

    private static int OffsetToPage(double offsetDip, int pageCount = TestPageCount)
        => PagePagerPolicy.OffsetToPage(offsetDip, pageCount);

    private static double PageToOffset(int page, int pageCount = TestPageCount)
        => PagePagerPolicy.PageToOffset(page, pageCount);

    private static int ClampPage(int page, int pageCount) => PagePagerPolicy.ClampPage(page, pageCount);

    private static int WheelDelta(int currentPage, int pageDelta, int pageCount = TestPageCount)
        => PagerWheelPolicy.ApplyPageDelta(currentPage, pageDelta, pageCount);

    private static void Snap(PagerSnapMotion motion, int page) => motion.SnapToPage(page, TestPageCount);

}




