using FluidBar.Core.Interaction;
using FluidBar.Island;

namespace FluidBar.Island.Tests;

/// <summary>
/// Frame-by-frame contract for the prototype2 rail transition. The reference samples were captured
/// live from <c>docs/rewrite/prototype2</c> (Edge 1440×900, <c>?state=expanded&amp;tab=notifications&amp;arch=1</c>):
/// clicking the right neighbour pill on page 02 (通知, 64 DIP capsule) turns to page 03 (剪贴板,
/// 73 DIP capsule) and the rail springs follow <c>RAIL_SPRING {T: 0.32, Z: 0.85}</c>.
/// </summary>
public sealed class MiniRailMotionTests
{
    /// <summary>Reference curve fitted through the prototype rAF samples (page 02 → 03).</summary>
    public static TheoryData<int, float, float, float> PrototypeCurve => new()
    {
        { 16, 64.28f, 666.86f, 773.14f },
        { 33, 65.02f, 666.49f, 773.51f },
        { 50, 66.42f, 665.80f, 774.20f },
        { 100, 69.59f, 664.21f, 775.79f },
        { 150, 71.66f, 663.17f, 776.83f },
        { 200, 72.61f, 662.69f, 777.31f },
        { 250, 72.95f, 662.53f, 777.47f },
        { 300, 73.02f, 662.49f, 777.51f },
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
        Assert.Equal(73f, motion.Values.CapsuleWidth, 0.01f);
        Assert.Equal(-82.5f, motion.Values.LeftX, 0.05f);
        Assert.Equal(79f, motion.Values.RightX, 0.05f);
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

        Assert.Equal(73f, frame.CapsuleWidth, 0.01f);
        Assert.Equal(2, frame.LeftDots);
        Assert.Equal(1, frame.RightDots);
        Assert.Equal(shell.MidX - 57.5f, frame.Previous.MidX, 0.01f);
        Assert.Equal(shell.MidX + 57.5f, frame.Next.MidX, 0.01f);
        Assert.Equal(1f, frame.PrevAlpha, 0.001f);
        Assert.Equal(1f, frame.NextAlpha, 0.001f);

        // Mid-transition values move the painted pills and the dot clusters with them.
        motion.Retarget(MiniRailTargetsFactory.Create(4, Pages(), 1.0));
        motion.Step(0.1);
        var midState = State(3, motion.Values);
        var mid = MiniRailLayout.Measure(midState, shell, left, right);
        Assert.InRange(mid.CapsuleWidth, 64f, 73f);
        Assert.NotEqual(frame.Previous.MidX, mid.Previous.MidX);
        // The dot clusters stay on the COMMITTED page (3) so the capsule label and the dots can never
        // disagree; a stale rail page used to paint three unclickable dots on the Home page.
        Assert.Equal(2, mid.LeftDots);
        Assert.Equal(1, mid.RightDots);
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
        Assert.Equal(73f, targets.CapsuleWidth, 0.01f);
        Assert.Equal(-57.5f, targets.PrevX, 0.01f);
        Assert.Equal(57.5f, targets.NextX, 0.01f);
        Assert.Equal(1f, targets.PrevAlpha, 0.001f);
        Assert.Equal(1f, targets.NextAlpha, 0.001f);
        Assert.Equal(-82.5f, targets.LeftX, 0.01f);   // 2-dot cluster, centre 637.5 → 720 − 82.5
        Assert.Equal(79f, targets.RightX, 0.01f);     // 1-dot cluster, centre 799 → 720 + 79

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
