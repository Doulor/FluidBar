using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class ContentScrollbarRenderTests
{
    [Fact]
    public void Painted_thumb_matches_hit_coordinates_and_moves_with_the_content()
    {
        var state = IslandTestFixtures.Expanded(2);
        state = state with { Pages = state.Pages with { Notifications = state.Pages.Notifications with
        {
            Detail = new NotificationItemModel { Title = "Long notification", FullBody = string.Concat(Enumerable.Repeat("Scrollable detail text. ", 100)) },
        } } };
        var geometry = ContentScrollLayout.Measure(state.Pages, 2, IslandMetrics.PageRect(state, 2), FontScaleMetrics.One);
        Assert.True(geometry.CanScroll);
        using var renderer = new IslandRenderer();
        using var top = renderer.RenderToBitmap(state, 1);
        using var bottom = renderer.RenderToBitmap(state with { Pages = state.Pages with
        { Notifications = state.Pages.Notifications with { ScrollOffset = geometry.Maximum } } }, 1);
        var x = (int)geometry.Thumb.MidX;
        var topY = (int)geometry.Thumb.MidY;
        var bottomY = (int)(geometry.Track.Bottom - geometry.Thumb.Height / 2);
        Assert.NotEqual(top.GetPixel(x, topY), bottom.GetPixel(x, topY));
        Assert.NotEqual(top.GetPixel(x, bottomY), bottom.GetPixel(x, bottomY));
    }
}
