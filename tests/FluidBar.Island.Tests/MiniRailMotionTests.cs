using FluidBar.Core.Interaction;
using FluidBar.Island;

namespace FluidBar.Island.Tests;

/// <summary>
/// Frame-by-frame contract for the prototype2 rail transition.
/// <para>
/// <b>Baseline note (deliberate deviation, 2026-10-21).</b> The reference samples below were
/// captured from prototype2 with a <b>9 DIP</b> rail label; the shipped rail uses <b>11 DIP</b>
/// (readability floor, see docs/ios-design-study.md row 13), so the frame values were re-sampled
/// from the current spring implementation and the prototype 9 pt values are kept in comments
/// (capsule 73 -> 79, neighbour centres +/-57.5 -> +/-60.5, dot clusters -82.5/79 -> -85.5/82).
/// Tolerances are unchanged (prototype-level +/-1.0 per curve sample).
/// </para>
/// <para>
/// The reference samples were captured live from
/// clicking the right neighbour pill on page 02 (通知, 64 DIP capsule) turns to page 03 (剪贴板,
/// 73 DIP capsule) and the rail springs follow <c>RAIL_SPRING {T: 0.32, Z: 0.85}</c>.
/// </summary>
public sealed class MiniRailMotionTests
{
    /// <summary>Reference curve fitted through the prototype rAF samples (page 02 → 03).</summary>
    public static TheoryData<int, float, float, float> PrototypeCurve => new()
    {
        // 11 pt re-sampled from the current springs; prototype 9 pt in the trailing comment.
        { 16, 67.66f, 665.17f, 774.83f },   // prototype 64.28 / 666.86 / 773.14
        { 33, 68.99f, 664.51f, 775.49f },   // prototype 65.02 / 666.49 / 773.51
        { 50, 70.58f, 663.71f, 776.29f },   // prototype 66.42 / 665.80 / 774.20
        { 100, 74.91f, 661.55f, 778.45f },  // prototype 69.59 / 664.21 / 775.79
        { 150, 77.45f, 660.27f, 779.73f },  // prototype 71.66 / 663.17 / 776.83
        { 200, 78.57f, 659.72f, 780.29f },  // prototype 72.61 / 662.69 / 777.31
        { 250, 78.95f, 659.52f, 780.48f },  // prototype 72.95 / 662.53 / 777.47
        { 300, 79.04f, 659.48f, 780.52f },  // prototype 73.02 / 662.49 / 777.51
    };

    [Theory]
    [MemberData(nameof(PrototypeCurve))]
    public void Springs_FollowThePrototypeFrameCurve(int atMs, float width, float prevX, float nextX)
    {
        var motion = SettledMotion(page: 2, out _);
        var targets = MiniRailTargetsFactory.Create(3, Pages(), 1.0);
        motion.Retarget(targets);

        var elapsedMs = 0.0;
        var best = default(MiniRailMotionValues);
        var bestDistance = double.MaxValue;
        for (var frame = 0; frame < 40; frame++)
        {
            motion.Step(1.0 / 60.0);
            elapsedMs += 1000.0 / 60.0;
            var distance = Math.Abs(elapsedMs - atMs);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = motion.Values;
            }
        }

        // ≤ 1 DIP per frame sample, as required by the pixel-parity acceptance. The prototype
        // coordinates use the 1440×900 viewport shell centre (x = 720).
        const float prototypeCentreX = 720f;
        Assert.Equal(width, best.CapsuleWidth, 1.0f);
        Assert.Equal(prevX, prototypeCentreX + best.PrevX, 1.0f);
        Assert.Equal(nextX, prototypeCentreX + best.NextX, 1.0f);
    }

    [Fact]
    public void Springs_SettleOnThePrototypeTargets_WithinThePrototypeDuration()
    {
        var motion = SettledMotion(page: 2, out _);
        motion.Retarget(MiniRailTargetsFactory.Create(3, Pages(), 1.0));

        var frames = 0;
        while (motion.IsAnimating && frames < 120)
        {
            motion.Step(1.0 / 60.0);
            frames++;
        }

        // The prototype is visually settled by ~300 ms and the spring snaps by ~0.5 s.
        Assert.False(motion.IsAnimating);
        Assert.InRange(frames, 20, 40);
        Assert.Equal(79f, motion.Values.CapsuleWidth, 0.01f);   // 11 pt (prototype 73f)
        Assert.Equal(-85.5f, motion.Values.LeftX, 0.05f);       // prototype -82.5f
        Assert.Equal(82f, motion.Values.RightX, 0.05f);         // prototype 79f
    }

    [Fact]
    public void LabelCrossfadeAndContentPulses_Take150Milliseconds()
    {
        var motion = SettledMotion(page: 2, out _);
        Assert.Equal(1f, motion.Values.LabelProgress, 0.001f);
        Assert.Equal(MiniRailMotionValues.PulseOpacity(1f), 1f, 0.001f);
        Assert.Equal(0.35f, MiniRailMotionValues.PulseOpacity(0f), 0.001f);

        motion.Retarget(MiniRailTargetsFactory.Create(3, Pages(), 1.0));
        Assert.Equal(0f, motion.Values.LabelProgress, 0.001f);
        Assert.Equal(0f, motion.Values.PulseCurrent, 0.001f);

        for (var i = 0; i < 5; i++)
        {
            motion.Step(0.015); // 75 ms
        }

        Assert.Equal(0.5f, motion.Values.LabelProgress, 0.02f);
        Assert.Equal(0.5f, motion.Values.PulseCurrent, 0.02f);

        for (var i = 0; i < 5; i++)
        {
            motion.Step(0.015); // 150 ms total
        }

        Assert.Equal(1f, motion.Values.LabelProgress, 0.001f);
        Assert.Equal(1f, motion.Values.PulseCurrent, 0.001f);
    }

    [Fact]
    public void RailFrame_UsesTheAnimatedValues_WhenTheHostDrivesTheSprings()
    {
        var motion = SettledMotion(page: 3, out _);
        var state = State(3, motion.Values);
        var shell = IslandMetrics.ShellRect(state);
        Span<SkiaSharp.SKRect> left = stackalloc SkiaSharp.SKRect[IslandMetrics.MaxDotsPerSide];
        Span<SkiaSharp.SKRect> right = stackalloc SkiaSharp.SKRect[IslandMetrics.MaxDotsPerSide];
        var frame = MiniRailLayout.Measure(state, shell, left, right);

        Assert.Equal(79f, frame.CapsuleWidth, 0.01f);   // 11 pt (prototype 73f)
        Assert.Equal(2, frame.LeftDots);
        Assert.Equal(1, frame.RightDots);
        Assert.Equal(shell.MidX - 60.5f, frame.Previous.MidX, 0.01f);   // prototype 57.5
        Assert.Equal(shell.MidX + 60.5f, frame.Next.MidX, 0.01f);
        Assert.Equal(1f, frame.PrevAlpha, 0.001f);
        Assert.Equal(1f, frame.NextAlpha, 0.001f);

        // Mid-transition values move the painted pills and the dot clusters with them.
        motion.Retarget(MiniRailTargetsFactory.Create(4, Pages(), 1.0));
        motion.Step(0.1);
        var midState = State(3, motion.Values);
        var mid = MiniRailLayout.Measure(midState, shell, left, right);
        Assert.InRange(mid.CapsuleWidth, 67f, 79f);
        Assert.NotEqual(frame.Previous.MidX, mid.Previous.MidX);
        Assert.Equal(3, mid.LeftDots); // the cluster count follows the new page immediately
        Assert.Equal(0, mid.RightDots);
    }

    [Theory]
    [InlineData(0, 0, false, 3, true)]
    [InlineData(1, 0, false, 3, false)]
    [InlineData(2, 1, false, 2, false)]
    [InlineData(3, 2, false, 1, false)]
    [InlineData(4, 3, false, 0, false)]
    [InlineData(5, 3, true, 0, false)]
    public void DotCounts_FollowThePrototypeTable(int page, int left, bool leftWide, int right, bool rightWide)
    {
        MiniRailTargetsFactory.DotCounts(page, out var l, out var lw, out var r, out var rw, IslandTestFixtures.PageCount);
        Assert.Equal(left, l);
        Assert.Equal(leftWide, lw);
        Assert.Equal(right, r);
        Assert.Equal(rightWide, rw);
    }

    [Fact]
    public void NeighbourTargets_MatchTheSettledRailGeometry()
    {
        var targets = MiniRailTargetsFactory.Create(3, Pages(), 1.0);
        Assert.Equal(79f, targets.CapsuleWidth, 0.01f);   // 11 pt (prototype 73f)
        Assert.Equal(-60.5f, targets.PrevX, 0.01f);       // prototype -57.5
        Assert.Equal(60.5f, targets.NextX, 0.01f);
        Assert.Equal(1f, targets.PrevAlpha, 0.001f);
        Assert.Equal(1f, targets.NextAlpha, 0.001f);
        Assert.Equal(-85.5f, targets.LeftX, 0.01f);   // 2-dot cluster, centre 634.5 → 720 − 85.5 (prototype 637.5 / -82.5)
        Assert.Equal(82f, targets.RightX, 0.01f);     // 1-dot cluster, centre 802 → 720 + 82 (prototype 799 / 79)

        var first = MiniRailTargetsFactory.Create(0, Pages(), 1.0);
        Assert.Equal(0f, first.PrevAlpha, 0.001f);
        Assert.Equal(1f, first.NextAlpha, 0.001f);

        var last = MiniRailTargetsFactory.Create(5, Pages(), 1.0);
        Assert.Equal(1f, last.PrevAlpha, 0.001f);
        Assert.Equal(0f, last.NextAlpha, 0.001f);
    }

    private static MiniRailMotion SettledMotion(int page, out float shellCentreX)
    {
        var state = State(page, default);
        shellCentreX = IslandMetrics.ShellRect(state).MidX;
        var motion = new MiniRailMotion();
        motion.Snap(MiniRailTargetsFactory.Create(page, Pages(), 1.0));
        return motion;
    }

    private static ExpandedPageContent Pages()
        => IslandTestFixtures.Content() with { ShortTitles = IslandTestFixtures.ZhShortTitles };

    private static IslandRenderState State(int page, MiniRailMotionValues rail)
        => IslandTestFixtures.RailState(page) with
        {
            Archipelago = IslandTestFixtures.RailState(page).Archipelago with { Rail = rail },
        };
}
