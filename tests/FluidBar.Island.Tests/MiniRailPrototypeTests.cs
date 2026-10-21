using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Pixel/layout contract for the prototype2 transient preview rail (mini-rail).
/// <para>
/// <b>Baseline note (deliberate deviation, 2026-10-21).</b> Every number in this file was measured
/// from prototype2 with a <b>9 DIP</b> rail label (mini-label, 9 pt). The shipped rail renders the
/// label at <b>11 DIP</b> - the readability floor agreed with the iOS study - so the expectations
/// below were migrated to the 11 pt baseline and the prototype values are kept inline in comments
/// (capsule 64 -> 67, 70 -> 75, 73 -> 79; label width 18.188 -> 22.22, 24.444 -> 29.83,
/// 27.271 -> 33.33; far-dot centres 317/321.5 -> 318.5/324.5). This is a documented product
/// decision recorded in docs/ios-design-study.md (rail label row), not a silent re-baseline.
/// </para>
/// <para>
/// The expected numbers are the live measurements from
/// <c>docs/rewrite/prototype2/index.html?state=expanded&amp;tab=…&amp;arch=1</c> in a 1440×900 Edge
/// viewport: shell (500,12) 440×284, rail y 264 height 32, prev/next 26×26 centred on y 280,
/// current capsule 28 DIP tall with label-grown width, 8 DIP island gaps, 3 DIP dots on a 7 DIP pitch
/// 7 DIP from the nearest island.
/// </summary>
public sealed class MiniRailPrototypeTests
{
    private const float Eps = 0.01f;

    /// <summary>Shell midpoint of the prototype viewport (500 + 440 / 2); the rail is centred on it.</summary>
    private const float CentreX = 720f;

    /// <summary>Short labels exactly as the prototype2 rail paints them (zh-Hans).</summary>
    private static readonly string[] ZhLabels = ["主页", "媒体", "通知", "剪贴板", "Agent", "小组件"];

    [Theory]
    // 11 pt labels: 64 -> 67, 73 -> 79, 70 -> 75 (prototype 9 pt values in the comments).
    [InlineData("主页", 67f)]   // prototype 64f
    [InlineData("媒体", 67f)]   // prototype 64f
    [InlineData("通知", 67f)]   // prototype 64f
    [InlineData("剪贴板", 79f)] // prototype 73f
    [InlineData("小组件", 79f)] // prototype 73f
    [InlineData("Agent", 75f)]  // prototype 70f
    public void CapsuleWidth_MatchesThePrototypeMeasuredWidths(string label, float expected)
        => Assert.Equal(expected, MiniRailLayout.CapsuleWidth(label), Eps);

    [Theory]
    // 11 pt label advances (prototype 9 pt: 18.188 / 18.188 / 27.271 / 24.444).
    [InlineData("主页", 22.22f)]
    [InlineData("通知", 22.22f)]
    [InlineData("剪贴板", 33.33f)]
    [InlineData("Agent", 29.83f)]
    public void LabelWidth_MatchesTheBrowserMeasurement(string label, float expected)
        => Assert.Equal(expected, MiniRailLayout.LabelWidth(label), 0.05f);


    [Fact]
    public void CapsuleWidth_ClampsToThePrototypeRange()
    {
        // The 64 DIP floor is a token, not label-dependent: an empty/absent label keeps it at any
        // label size (only the label-grown part above the floor scales with the 11 pt label).
        Assert.Equal(64f, MiniRailLayout.CapsuleWidth(""), Eps);
        Assert.Equal(64f, MiniRailLayout.CapsuleWidth(null), Eps);
        Assert.Equal(104f, MiniRailLayout.CapsuleWidth(new string('W', 40)), Eps);
    }

    [Fact]
    public void Rail_SitsEightDipBelowTheShell_AndIs32DipTall()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = State(3);
        var shell = IslandMetrics.ShellRect(state);
        var layout = renderer.MeasureStrip(state);

        Assert.Equal(shell.Bottom + 8f, layout.Strip.Top, 0.001f);
        Assert.Equal(34f, layout.Strip.Height, 0.001f);   // v3: strip 32 -> 34
        Assert.Equal(shell.MidX, layout.Current.MidX, 0.001f);
        Assert.Equal(CentreX + (shell.Left - 500f), layout.Current.MidX, 0.001f);
        Assert.Equal(34f, layout.Current.Height, 0.001f);   // v3: height 32 -> 34 (matches the 34 DIP strip)
        Assert.Equal(28f, layout.Previous.Width, 0.001f);   // v3: 26 -> 28
        Assert.Equal(28f, layout.Previous.Height, 0.001f);   // v3: 26 -> 28
        Assert.Equal(28f, layout.Next.Width, 0.001f);
        Assert.Equal(28f, layout.Next.Height, 0.001f);
    }

    [Fact]
    public void ClipboardPage_MatchesThePrototypeRects()
    {
        // Prototype (page index 3, 剪贴板): prev centre 662.5, current 73×28 at 683.5/306,
        // next centre 777.5; left dots 632.5 and 639.5, right dot 797.5, all 3×3 at y 278.5.
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = State(3);
        var shell = IslandMetrics.ShellRect(state);
        var layout = renderer.MeasureStrip(state);
        var offsetX = shell.Left - 500f;
        var offsetY = shell.Top - 12f;

        Assert.Equal(79f, layout.Current.Width, 0.001f);
        Assert.Equal(680.5f + offsetX, layout.Current.Left, 0.001f);
        Assert.Equal(264f + offsetY, layout.Current.Top, 0.001f);   // sampled absolute 316 (offsetY is top-12)
        Assert.Equal(658.5f + offsetX, layout.Previous.MidX, 0.001f);
        Assert.Equal(781.5f + offsetX, layout.Next.MidX, 0.001f);
        Assert.Equal(281f + offsetY, layout.Current.MidY, 0.001f);   // sampled 293 at offsetY 12

        Assert.Equal(2, layout.DotsLeft.Length);
        Assert.Equal(1, layout.DotsRight.Length);
        Assert.Equal(627.5f + offsetX, layout.DotsLeft[0].Left, 0.001f);
        Assert.Equal(634.5f + offsetX, layout.DotsLeft[1].Left, 0.001f);
        Assert.Equal(802.5f + offsetX, layout.DotsRight[0].Left, 0.001f);
        Assert.Equal(3f, layout.DotsLeft[0].Width, 0.001f);
        Assert.Equal(279.5f + offsetY, layout.DotsLeft[0].Top, 0.001f);

        // Island gaps are 8 DIP and the dot cluster keeps 7 DIP from the nearest island edge.
        Assert.Equal(8f, layout.Current.Left - layout.Previous.Right, 0.001f);
        Assert.Equal(8f, layout.Next.Left - layout.Current.Right, 0.001f);
        Assert.Equal(7f, layout.Previous.Left - layout.DotsLeft[1].Right, 0.001f);
        Assert.Equal(7f, layout.DotsRight[0].Left - layout.Next.Right, 0.001f);
    }

    [Fact]
    public void HomePage_HasThreeRightDots_WithTheLastOneWide()
    {
        // Prototype page 0: right total 4, shown 3, last dot 7×3 at x 807.
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var layout = renderer.MeasureStrip(State(0));
        var offsetX = IslandMetrics.ShellRect(State(0)).Left - 500f;

        Assert.Empty(layout.DotsLeft);
        Assert.Equal(3, layout.DotsRight.Length);
        // v3 sampled: 796.5 / 803.5 / 810.5 (prototype 9 pt: 793 / 800 / 807).
        Assert.Equal(796.5f + offsetX, layout.DotsRight[0].Left, 0.001f);
        Assert.Equal(803.5f + offsetX, layout.DotsRight[1].Left, 0.001f);
        Assert.Equal(810.5f + offsetX, layout.DotsRight[2].Left, 0.001f);
        Assert.Equal(3f, layout.DotsRight[1].Width, 0.001f);
        Assert.Equal(7f, layout.DotsRight[2].Width, 0.001f);
        var offsetY = IslandMetrics.ShellRect(State(0)).Top - 12f;
        Assert.Equal(281f + offsetY, layout.DotsRight[2].MidY, 0.001f);

        Assert.True(layout.Previous.IsEmpty, "page 0 has no previous island");
        Assert.False(layout.Next.IsEmpty);
    }

    [Fact]
    public void LastPage_HasThreeLeftDots_WithTheLastOneWide()
    {
        // Prototype page 5: left total 4, shown 3, last dot 7×3 at x 635.5.
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = State(5);
        var layout = renderer.MeasureStrip(state);
        var offsetX = IslandMetrics.ShellRect(state).Left - 500f;

        Assert.Empty(layout.DotsRight);
        Assert.Equal(3, layout.DotsLeft.Length);
        Assert.Equal(616.5f + offsetX, layout.DotsLeft[0].Left, 0.001f);
        Assert.Equal(623.5f + offsetX, layout.DotsLeft[1].Left, 0.001f);
        Assert.Equal(592.5f + offsetX, layout.DotsLeft[2].Left, 0.001f);
        Assert.Equal(7f, layout.DotsLeft[2].Width, 0.001f);
        Assert.True(layout.Next.IsEmpty, "the last page has no next island");
    }

    [Theory]
    [InlineData(0, 0, 3, false)]
    [InlineData(1, 0, 3, false)]
    [InlineData(2, 1, 2, false)]
    [InlineData(3, 2, 1, false)]
    [InlineData(4, 3, 0, false)]
    [InlineData(5, 3, 0, true)]
    public void FarDotCounts_FollowThePrototypeTable(int page, int left, int right, bool leftWide)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var layout = renderer.MeasureStrip(State(page));

        Assert.Equal(left, layout.DotsLeft.Length);
        Assert.Equal(right, layout.DotsRight.Length);
        if (left > 0)
        {
            var wide = layout.DotsLeft[^1].Width > 3.5f;
            Assert.Equal(leftWide, wide);
        }
    }

    [Fact]
    public void EveryDotJumpsToTheFarthestPage_AndNeighboursNeverStealTheClick()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        for (var page = 0; page < FluidBar.Core.Activities.PageRef.BuiltinPages.Count; page++)
        {
            var state = State(page);
            var layout = renderer.MeasureStrip(state);

            foreach (var dot in layout.DotsLeft)
            {
                var hit = renderer.HitTest(state, dot.MidX, dot.MidY);
                Assert.Equal(IslandHitKind.DotLeft, hit.Kind);
                Assert.Equal(0, hit.PageIndex);
            }

            foreach (var dot in layout.DotsRight)
            {
                var hit = renderer.HitTest(state, dot.MidX, dot.MidY);
                Assert.Equal(IslandHitKind.DotRight, hit.Kind);
                Assert.Equal(FluidBar.Core.Activities.PageRef.BuiltinPages.Count - 1, hit.PageIndex);
            }

            // The last dot (the prototype's far/wide dot) must jump to 01/06, never to its neighbour.
            if (layout.DotsLeft.Length > 0)
            {
                var far = layout.DotsLeft[^1];
                Assert.Equal(0, renderer.HitTest(state, far.MidX, far.MidY).PageIndex);
                Assert.Equal(0, layout.LeftDotPage(layout.DotsLeft.Length - 1));
            }

            if (layout.DotsRight.Length > 0)
            {
                var far = layout.DotsRight[^1];
                Assert.Equal(FluidBar.Core.Activities.PageRef.BuiltinPages.Count - 1, renderer.HitTest(state, far.MidX, far.MidY).PageIndex);
                Assert.Equal(FluidBar.Core.Activities.PageRef.BuiltinPages.Count - 1, layout.RightDotPage(layout.DotsRight.Length - 1));
            }

            // The 8 DIP island gaps and the dot gaps are transparent.
            Assert.Equal(IslandHitKind.None, renderer.HitTest(state, layout.Current.Right + 4, layout.Current.MidY).Kind);
            if (layout.DotsLeft.Length > 0)
            {
                Assert.Equal(IslandHitKind.None,
                    renderer.HitTest(state, layout.Previous.Left - 3, layout.Previous.MidY).Kind);
            }
        }
    }

    [Fact]
    public void DotHitArea_IsSevenByFifteen_WithoutOverlap()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = State(4);
        var layout = renderer.MeasureStrip(state);
        var dot = layout.DotsLeft[1];

        Assert.Equal(IslandHitKind.DotLeft, renderer.HitTest(state, dot.MidX, dot.MidY + 7).Kind);
        Assert.Equal(IslandHitKind.None, renderer.HitTest(state, dot.MidX, dot.MidY + 8).Kind);
        Assert.Equal(IslandHitKind.DotLeft, renderer.HitTest(state, dot.Left + 0.5f, dot.MidY).Kind);

        // Pitch is 7 DIP, the hit area 7 DIP wide: neighbouring targets touch but never overlap.
        var neighbour = layout.DotsLeft[0];
        Assert.Equal(IslandHitKind.DotLeft, renderer.HitTest(state, neighbour.MidX, neighbour.MidY).Kind);
        var between = (dot.Left + neighbour.Right) / 2f;
        var betweenHit = renderer.HitTest(state, between, dot.MidY);
        Assert.Equal(IslandHitKind.DotLeft, betweenHit.Kind);
        Assert.Equal(0, betweenHit.PageIndex);
    }


    [Theory]
    // 11 pt: capsule 64->67 / 70->75 / 73->79, neighbour centres move by half the width delta.
    [InlineData(0, 67f, 664.5f, 775.5f, 0, 3)]   // v3 sampled (28 DIP neighbours)
    [InlineData(1, 67f, 664.5f, 775.5f, 0, 3)]
    [InlineData(2, 67f, 664.5f, 775.5f, 1, 2)]
    [InlineData(3, 79f, 658.5f, 781.5f, 2, 1)]
    [InlineData(4, 75f, 660.5f, 779.5f, 3, 0)]
    [InlineData(5, 79f, 658.5f, 781.5f, 3, 0)]
    public void PrototypePageTable_IsReproduced(
        int page, float capsule, float prevCentre, float nextCentre, int leftDots, int rightDots)
    {
        // Live prototype2 measurements (1440×900 viewport, shell centre x 720). Page 4 uses the
        // localized 代理 label (2 CJK glyphs = 64 DIP) instead of the prototype's Latin "Agent".
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = State(page);
        var shell = IslandMetrics.ShellRect(state);
        var layout = renderer.MeasureStrip(state);
        var dx = shell.MidX - CentreX;
        var dy = shell.Top - 12f;

        Assert.Equal(capsule, layout.Current.Width, 0.001f);
        Assert.Equal(capsule, layout.CapsuleWidth, 0.001f);
        Assert.Equal(281f + dy, layout.Current.MidY, 0.001f);   // sampled 293

        if (page > 0)
        {
            Assert.Equal(prevCentre + dx, layout.Previous.MidX, 0.001f);
        }

        if (page < FluidBar.Core.Activities.PageRef.BuiltinPages.Count - 1)
        {
            Assert.Equal(nextCentre + dx, layout.Next.MidX, 0.001f);
        }

        Assert.Equal(leftDots, layout.DotsLeft.Length);
        Assert.Equal(rightDots, layout.DotsRight.Length);

        // Island gaps are 8 DIP and every dot cluster sits 7 DIP from its nearest island. The
        // neighbour slot stays reserved (never a placeholder pill), so the gap always measures 8.
        if (page > 0)
        {
            Assert.Equal(8f, layout.Current.Left - layout.Previous.Right, 0.001f);
        }

        if (page < FluidBar.Core.Activities.PageRef.BuiltinPages.Count - 1)
        {
            Assert.Equal(8f, layout.Next.Left - layout.Current.Right, 0.001f);
        }

        if (leftDots > 0)
        {
            var neighbourLeft = page > 0 ? layout.Previous.Left : layout.Current.Left - 8f - 26f;
            Assert.Equal(7f, neighbourLeft - layout.DotsLeft[^1].Right, 0.001f);
        }

        if (rightDots > 0)
        {
            var neighbourRight = page < FluidBar.Core.Activities.PageRef.BuiltinPages.Count - 1
                ? layout.Next.Right
                : layout.Current.Right + 8f + 26f;
            Assert.Equal(7f, layout.DotsRight[0].Left - neighbourRight, 0.001f);
        }

        // Dot pitch is 7 DIP; neighbouring dots in a cluster never overlap.
        for (var i = 1; i < layout.DotsLeft.Length; i++)
        {
            Assert.Equal(7f, layout.DotsLeft[i].Left - layout.DotsLeft[i - 1].Left, 0.001f);
        }

        for (var i = 1; i < layout.DotsRight.Length; i++)
        {
            Assert.Equal(7f, layout.DotsRight[i].Left - layout.DotsRight[i - 1].Left, 0.001f);
        }

        foreach (var dot in layout.DotsLeft)
        {
            Assert.Equal(3f, dot.Height, 0.001f);
            Assert.True(dot.Width is 3f or 7f);
        }

        foreach (var dot in layout.DotsRight)
        {
            Assert.Equal(3f, dot.Height, 0.001f);
            Assert.True(dot.Width is 3f or 7f);
        }
    }

    [Theory]
    // v3 sampled with 28 DIP neighbours and the 34 DIP strip.
    [InlineData(0, new[] { 796.5f, 803.5f, 810.5f })]
    [InlineData(1, new[] { 796.5f, 803.5f, 810.5f })]
    [InlineData(2, new[] { 796.5f, 803.5f })]
    [InlineData(3, new[] { 802.5f })]
    [InlineData(4, new float[0])]
    [InlineData(5, new float[0])]
    public void RightDotCluster_MatchesThePrototypeRects(int page, float[] expected)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = State(page);
        var layout = renderer.MeasureStrip(state);
        var dx = IslandMetrics.ShellRect(state).MidX - CentreX;

        Assert.Equal(expected.Length, layout.DotsRight.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i] + dx, layout.DotsRight[i].Left, 0.001f);
        }
    }

    [Fact]
    public void CapsuleWidth_MorphsBetweenNeighbouringLabels_WithoutJumps()
    {
        var widthNarrow = MiniRailLayout.CapsuleWidth("通知");
        var widthWide = MiniRailLayout.CapsuleWidth("剪贴板");
        Assert.Equal(67f, widthNarrow, Eps);   // 11 pt (prototype 64f)
        Assert.Equal(79f, widthWide, Eps);     // 11 pt (prototype 73f)

        var content = IslandTestFixtures.Content() with { ShortTitles = ZhLabels };
        var previous = MiniRailLayout.CapsuleWidthAt(2, content);
        var settled = MiniRailLayout.CapsuleWidthAt(3, content);
        Assert.Equal(67f, previous, 0.001f);
        Assert.Equal(79f, settled, 0.001f);

        var last = previous;
        var maxStep = 0f;
        for (var i = 1; i <= 120; i++)
        {
            var width = MiniRailLayout.CapsuleWidthAt(2 + (i / 120.0), content);
            Assert.InRange(width, 67f - Eps, 79f + Eps);
            Assert.True(width >= last - 0.001f, "the capsule only grows while the pager advances");
            maxStep = Math.Max(maxStep, width - last);
            last = width;
        }

        // 64 → 73 over a 400 DIP page; one 16.6 ms frame of the pager spring stays well below 2 DIP.
        Assert.True(maxStep < 2f, $"max frame-to-frame capsule jump {maxStep:0.000} DIP");
        Assert.Equal(79f, MiniRailLayout.CapsuleWidthAt(3.0, content), 0.001f);
        Assert.Equal(67f, MiniRailLayout.CapsuleWidthAt(2.0, content), 0.001f);

        // Leaving page 3 towards 代理 (shorter label) shrinks the capsule again, still inside 64..73.
        var leaving = MiniRailLayout.CapsuleWidthAt(3.4, content);
        Assert.InRange(leaving, 67f, 79f);
        Assert.True(leaving < 79f && leaving > 67f, $"mid-morph width {leaving:0.00} should sit between the two labels");
    }

    [Fact]
    public void ExpandedRest_PaintsNoIdlePreviewRow_EvenWithTheLegacyFlag()
    {
        // Prototype2 shows nothing below the shell at rest. The legacy `ShowIdlePreview` flag must
        // not resurrect the old six-dot row / capsule preview: both frames are byte-identical.
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var expanded = State(3);
        var hidden = expanded with { Archipelago = expanded.Archipelago with { Visible = false, Opacity = 0 } };
        var withLegacyFlag = hidden with { Archipelago = hidden.Archipelago with { ShowIdlePreview = true } };

        using var plain = renderer.RenderToBitmap(hidden, IslandTestFixtures.Scale);
        using var flagged = renderer.RenderToBitmap(withLegacyFlag, IslandTestFixtures.Scale);
        Assert.Equal(0, MeanChannelDifference(plain, flagged), 0.001);

        var shell = IslandMetrics.ShellRect(expanded);
        var railBand = new SKRect(0, shell.Bottom + 8, 488, shell.Bottom + 40);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueInside(plain, railBand, IslandTestFixtures.Scale, 60));
    }

    [Fact]
    public void RevealedStrip_PaintsGlassAtThePillCentres()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = State(3);
        var layout = renderer.MeasureStrip(state);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        var glass = bitmap.GetPixel(
            (int)((layout.Current.Left + 6) * IslandTestFixtures.Scale),
            (int)(layout.Current.MidY * IslandTestFixtures.Scale));
        Assert.True(glass.Alpha > 120, $"capsule glass should be painted, got alpha {glass.Alpha}");
        Assert.InRange(glass.Red, 10, 45);
        Assert.InRange(glass.Green, 10, 45);
        Assert.InRange(glass.Blue, 12, 50);

        var dot = layout.DotsRight[0];
        var dotPixel = bitmap.GetPixel(
            (int)(dot.MidX * IslandTestFixtures.Scale),
            (int)(dot.MidY * IslandTestFixtures.Scale));
        Assert.True(dotPixel.Alpha > 40, $"far dot should be painted, got alpha {dotPixel.Alpha}");
    }

    private static double MeanChannelDifference(SKBitmap a, SKBitmap b)
    {
        var pa = a.Pixels;
        var pb = b.Pixels;
        long total = 0;
        for (var i = 0; i < pa.Length; i++)
        {
            total += Math.Abs(pa[i].Alpha - pb[i].Alpha);
            total += Math.Abs(pa[i].Red - pb[i].Red);
            total += Math.Abs(pa[i].Green - pb[i].Green);
            total += Math.Abs(pa[i].Blue - pb[i].Blue);
        }

        return total / (double)(pa.Length * 4);
    }

    /// <summary>Expanded prototype rail state (zh short labels, rail revealed at a page).</summary>
    public static IslandRenderState State(int page, double position = double.NaN)
        => IslandTestFixtures.RailState(page, position);
}


