using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Pixel/layout contract for the prototype2 transient preview rail (`.mini-rail`). The expected
/// numbers are the live measurements from
/// <c>docs/rewrite/prototype2/index.html?state=expanded&amp;tab=…&amp;arch=1</c> in a 1440×900 Edge
/// viewport: shell (500,12) 440×284, rail y 304 height 32, prev/next 26×26 centred on y 320,
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
    [InlineData("主页", 64f)]
    [InlineData("媒体", 64f)]
    [InlineData("通知", 64f)]
    [InlineData("剪贴板", 73f)]
    [InlineData("小组件", 73f)]
    [InlineData("Agent", 70f)]
    public void CapsuleWidth_MatchesThePrototypeMeasuredWidths(string label, float expected)
        => Assert.Equal(expected, MiniRailLayout.CapsuleWidth(label), Eps);

    [Theory]
    [InlineData("主页", 18.188f)]
    [InlineData("通知", 18.188f)]
    [InlineData("剪贴板", 27.271f)]
    [InlineData("Agent", 24.406f)]
    public void LabelWidth_MatchesTheBrowserMeasurement(string label, float expected)
        => Assert.Equal(expected, MiniRailLayout.LabelWidth(label), 0.05f);


    [Fact]
    public void CapsuleWidth_ClampsToThePrototypeRange()
    {
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
        Assert.Equal(32f, layout.Strip.Height, 0.001f);
        Assert.Equal(shell.MidX, layout.Current.MidX, 0.001f);
        Assert.Equal(CentreX + (shell.Left - 500f), layout.Current.MidX, 0.001f);
        Assert.Equal(28f, layout.Current.Height, 0.001f);
        Assert.Equal(26f, layout.Previous.Width, 0.001f);
        Assert.Equal(26f, layout.Previous.Height, 0.001f);
        Assert.Equal(26f, layout.Next.Width, 0.001f);
        Assert.Equal(26f, layout.Next.Height, 0.001f);
    }

    [Fact]
    public void ClipboardPage_MatchesThePrototypeRects()
    {
        // Prototype (page index 3, 剪贴板): prev centre 662.5, current 73×28 at 683.5/306,
        // next centre 777.5; left dots 632.5 and 639.5, right dot 797.5, all 3×3 at y 318.5.
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = State(3);
        var shell = IslandMetrics.ShellRect(state);
        var layout = renderer.MeasureStrip(state);
        var offsetX = shell.Left - 500f;
        var offsetY = shell.Top - 12f;

        Assert.Equal(73f, layout.Current.Width, 0.001f);
        Assert.Equal(683.5f + offsetX, layout.Current.Left, 0.001f);
        Assert.Equal(306f + offsetY, layout.Current.Top, 0.001f);
        Assert.Equal(662.5f + offsetX, layout.Previous.MidX, 0.001f);
        Assert.Equal(777.5f + offsetX, layout.Next.MidX, 0.001f);
        Assert.Equal(320f + offsetY, layout.Current.MidY, 0.001f);

        Assert.Equal(2, layout.DotsLeft.Length);
        Assert.Equal(1, layout.DotsRight.Length);
        Assert.Equal(632.5f + offsetX, layout.DotsLeft[0].Left, 0.001f);
        Assert.Equal(639.5f + offsetX, layout.DotsLeft[1].Left, 0.001f);
        Assert.Equal(797.5f + offsetX, layout.DotsRight[0].Left, 0.001f);
        Assert.Equal(3f, layout.DotsLeft[0].Width, 0.001f);
        Assert.Equal(318.5f + offsetY, layout.DotsLeft[0].Top, 0.001f);

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
        Assert.Equal(793f + offsetX, layout.DotsRight[0].Left, 0.001f);
        Assert.Equal(800f + offsetX, layout.DotsRight[1].Left, 0.001f);
        Assert.Equal(807f + offsetX, layout.DotsRight[2].Left, 0.001f);
        Assert.Equal(3f, layout.DotsRight[1].Width, 0.001f);
        Assert.Equal(7f, layout.DotsRight[2].Width, 0.001f);
        var offsetY = IslandMetrics.ShellRect(State(0)).Top - 12f;
        Assert.Equal(320f + offsetY, layout.DotsRight[2].MidY, 0.001f);

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
        Assert.Equal(621.5f + offsetX, layout.DotsLeft[0].Left, 0.001f);
        Assert.Equal(628.5f + offsetX, layout.DotsLeft[1].Left, 0.001f);
        Assert.Equal(635.5f + offsetX, layout.DotsLeft[2].Left, 0.001f);
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
    public void EveryDotJumpsToItsOwnPage_AndNeighboursNeverStealTheClick()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var pageCount = FluidBar.Core.Activities.PageRef.BuiltinPages.Count;
        for (var page = 0; page < pageCount; page++)
        {
            var state = State(page);
            var layout = renderer.MeasureStrip(state);

            // Nearest dot first: the page just before the previous pill / just after the next one, so a
            // cluster of three dots stands for three DIFFERENT pages (they used to share one far target).
            for (var i = 0; i < layout.DotsLeft.Length; i++)
            {
                var hit = renderer.HitTest(state, layout.DotsLeft[i].MidX, layout.DotsLeft[i].MidY);
                Assert.Equal(IslandHitKind.DotLeft, hit.Kind);
                Assert.Equal(Math.Max(0, page - 2 - i), hit.PageIndex);
                Assert.Equal(hit.PageIndex, layout.LeftDotPage(i));
            }

            for (var i = 0; i < layout.DotsRight.Length; i++)
            {
                var hit = renderer.HitTest(state, layout.DotsRight[i].MidX, layout.DotsRight[i].MidY);
                Assert.Equal(IslandHitKind.DotRight, hit.Kind);
                Assert.Equal(Math.Min(pageCount - 1, page + 2 + i), hit.PageIndex);
                Assert.Equal(hit.PageIndex, layout.RightDotPage(i));
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
    public void DotHitArea_IsSevenWideByTwentyTwoTall_WithoutOverlap()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = State(4);
        var layout = renderer.MeasureStrip(state);
        var dot = layout.DotsLeft[1];

        // 22 DIP tall: ±10 DIP around the 3 DIP dot still hits, ±12 DIP does not.
        Assert.Equal(IslandHitKind.DotLeft, renderer.HitTest(state, dot.MidX, dot.MidY + 10).Kind);
        Assert.Equal(IslandHitKind.None, renderer.HitTest(state, dot.MidX, dot.MidY + 12).Kind);
        Assert.Equal(IslandHitKind.DotLeft, renderer.HitTest(state, dot.Left + 0.5f, dot.MidY).Kind);

        // Pitch is 7 DIP, the hit area 7 DIP wide: neighbouring targets touch but never overlap.
        var neighbour = layout.DotsLeft[0];
        Assert.Equal(IslandHitKind.DotLeft, renderer.HitTest(state, neighbour.MidX, neighbour.MidY).Kind);
        var between = (dot.Left + neighbour.Right) / 2f;
        var betweenHit = renderer.HitTest(state, between, dot.MidY);
        Assert.Equal(IslandHitKind.DotLeft, betweenHit.Kind);
        Assert.Equal(Math.Max(0, layout.Page - 2 - 1), betweenHit.PageIndex);
    }


    [Theory]
    [InlineData(0, 64f, 667f, 773f, 0, 3)]
    [InlineData(1, 64f, 667f, 773f, 0, 3)]
    [InlineData(2, 64f, 667f, 773f, 1, 2)]
    [InlineData(3, 73f, 662.5f, 777.5f, 2, 1)]
    [InlineData(4, 70f, 664f, 776f, 3, 0)]
    [InlineData(5, 73f, 662.5f, 777.5f, 3, 0)]
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
        Assert.Equal(320f + dy, layout.Current.MidY, 0.001f);

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
    [InlineData(0, new[] { 793f, 800f, 807f })]
    [InlineData(1, new[] { 793f, 800f, 807f })]
    [InlineData(2, new[] { 793f, 800f })]
    [InlineData(3, new[] { 797.5f })]
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
        var width64 = MiniRailLayout.CapsuleWidth("通知");
        var width73 = MiniRailLayout.CapsuleWidth("剪贴板");
        Assert.Equal(64f, width64, Eps);
        Assert.Equal(73f, width73, Eps);

        var content = IslandTestFixtures.Content() with { ShortTitles = ZhLabels };
        var previous = MiniRailLayout.CapsuleWidthAt(2, content);
        var settled = MiniRailLayout.CapsuleWidthAt(3, content);
        Assert.Equal(64f, previous, 0.001f);
        Assert.Equal(73f, settled, 0.001f);

        var last = previous;
        var maxStep = 0f;
        for (var i = 1; i <= 120; i++)
        {
            var width = MiniRailLayout.CapsuleWidthAt(2 + (i / 120.0), content);
            Assert.InRange(width, 64f - Eps, 73f + Eps);
            Assert.True(width >= last - 0.001f, "the capsule only grows while the pager advances");
            maxStep = Math.Max(maxStep, width - last);
            last = width;
        }

        // 64 → 73 over a 400 DIP page; one 16.6 ms frame of the pager spring stays well below 2 DIP.
        Assert.True(maxStep < 2f, $"max frame-to-frame capsule jump {maxStep:0.000} DIP");
        Assert.Equal(73f, MiniRailLayout.CapsuleWidthAt(3.0, content), 0.001f);
        Assert.Equal(64f, MiniRailLayout.CapsuleWidthAt(2.0, content), 0.001f);

        // Leaving page 3 towards 代理 (shorter label) shrinks the capsule again, still inside 64..73.
        var leaving = MiniRailLayout.CapsuleWidthAt(3.4, content);
        Assert.InRange(leaving, 64f, 73f);
        Assert.True(leaving < 73f && leaving > 64f, $"mid-morph width {leaving:0.00} should sit between the two labels");
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
