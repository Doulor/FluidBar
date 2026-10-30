using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>In-card side-rail geometry and hit contract (replaces the detached mini-island rail).</summary>
public sealed class SideRailLayoutTests
{
    private static SKRect Content => new(60, 12, 460, 272);

    [Fact]
    public void SixTabs_FillThe260DipContentHeight()
    {
        var first = SideRailLayout.TabRect(Content, 0);
        var last = SideRailLayout.TabRect(Content, 5);
        Assert.Equal(new SKRect(10, 12, 50, 52), first);
        Assert.Equal(new SKRect(10, 232, 50, 272), last);
        Assert.Equal(260, SideRailLayout.RailRect(Content, 6).Height, 0.001);
    }

    [Fact]
    public void EveryTab_Is40By40AndHitsOnlyItsOwnPage()
    {
        for (var page = 0; page < 6; page++)
        {
            var rect = SideRailLayout.TabRect(Content, page);
            Assert.Equal(40, rect.Width, 0.001);
            Assert.Equal(40, rect.Height, 0.001);
            Assert.Equal(page, SideRailLayout.HitTest(Content, 6, rect.MidX, rect.MidY));
        }

        Assert.Equal(-1, SideRailLayout.HitTest(Content, 6, 55, 30));
        Assert.Equal(-1, SideRailLayout.HitTest(Content, 6, 5, 30));
    }

    [Fact]
    public void ExpandedShell_Is472WideWith400DipPageContent()
    {
        var state = IslandRenderState.Expanded(IslandMode.Float, default, ExpandedPageContent.Empty, IslandTheme.Dark);
        Assert.Equal(472, state.ShellWidth, 0.001);
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(state));
        Assert.Equal(400, content.Width, 0.001);
        Assert.Equal(IslandMetrics.ShellRect(state).Left + 60, content.Left, 0.001);
    }

    [Fact]
    public void HitTest_ReturnsPageTab_AndNotDetachedRail()
    {
        var state = IslandRenderState.Expanded(IslandMode.Float, default, ExpandedPageContent.Empty, IslandTheme.Dark);
        using var renderer = new IslandRenderer();
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(state));
        var tab = SideRailLayout.TabRect(content, 3);
        var hit = renderer.HitTest(state, tab.MidX, tab.MidY);
        Assert.Equal(IslandHitKind.PageTab, hit.Kind);
        Assert.Equal(3, hit.PageIndex);

        var below = IslandMetrics.ShellRect(state).Bottom + 12;
        Assert.Equal(IslandHitKind.None, renderer.HitTest(state, content.MidX, below).Kind);
    }
}
