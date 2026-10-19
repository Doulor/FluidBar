using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>Rendering coverage for the expanded notifications/clipboard/agent/widgets pages.</summary>
public sealed class ExpandedPageTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Expanded_pages_2_to_5_render_inside_the_shell_without_bands(int pageIndex)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(pageIndex);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        Assert.Equal(732, bitmap.Width);
        Assert.Equal(576, bitmap.Height);

        var shell = IslandMetrics.ShellRect(state);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(bitmap, shell, IslandTestFixtures.Scale));

        var content = IslandMetrics.ContentRect(shell);
        Assert.True(
            CountPaintedPixels(bitmap, content) > 5000,
            $"page {pageIndex}: expected cards/rows/text inside the content rect");

        // No black/transparent bands: the title row, the body and the bottom margin are all shell fill.
        foreach (var dipY in new[] { content.Top + 1, content.MidY, content.Bottom - 1 })
        {
            var y = (int)Math.Round(dipY * IslandTestFixtures.Scale);
            for (var dipX = content.Left + 1; dipX < content.Right - 1; dipX += 4)
            {
                var pixel = bitmap.GetPixel((int)Math.Round(dipX * IslandTestFixtures.Scale), y);
                Assert.True(
                    pixel.Alpha > 200,
                    $"page {pageIndex}: transparent band at ({dipX:0},{dipY:0}) with alpha {pixel.Alpha}");
            }
        }
    }

    [Fact]
    public void MediaPage_DrawsTheRealCoverArt_WhenBytesAreProvided()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var content = IslandTestFixtures.Content() with
        {
            Media = IslandTestFixtures.Content().Media with
            {
                Artwork = SolidPng(new SKColor(220, 30, 40)),
                ArtworkKey = "cover-test",
            },
        };
        var state = IslandRenderState.Expanded(IslandMode.Float, PagerRenderState.At(1), content,
            IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        var art = PageLayout.MediaArt(IslandMetrics.PageRect(state, 1));
        var pixel = bitmap.GetPixel(
            (int)Math.Round(art.MidX * IslandTestFixtures.Scale),
            (int)Math.Round(art.MidY * IslandTestFixtures.Scale));
        Assert.True(pixel.Red > 180 && pixel.Green < 90 && pixel.Blue < 90,
            $"expected the red cover art, got R={pixel.Red} G={pixel.Green} B={pixel.Blue}");
    }

    private static byte[] SolidPng(SKColor color)
    {
        using var bitmap = new SKBitmap(8, 8);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 5)]
    public void Adjacent_expanded_pages_2_to_5_paint_different_content(int leftPage, int rightPage)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var left = renderer.RenderToBitmap(IslandTestFixtures.Expanded(leftPage), IslandTestFixtures.Scale);
        using var right = renderer.RenderToBitmap(IslandTestFixtures.Expanded(rightPage), IslandTestFixtures.Scale);

        Assert.NotEqual(Encode(left), Encode(right));
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(left, IslandMetrics.ShellRect(IslandTestFixtures.Expanded(leftPage)), IslandTestFixtures.Scale));
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(right, IslandMetrics.ShellRect(IslandTestFixtures.Expanded(rightPage)), IslandTestFixtures.Scale));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Page_strip_positions_2_to_5_render_settled_and_mid_transition(int pageIndex)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var settled = IslandTestFixtures.Expanded(pageIndex);
        var transition = IslandTestFixtures.Expanded(pageIndex, pagePosition: pageIndex + 0.4);
        var shell = IslandMetrics.ShellRect(settled);

        using var settledBitmap = renderer.RenderToBitmap(settled, IslandTestFixtures.Scale);
        using var transitionBitmap = renderer.RenderToBitmap(transition, IslandTestFixtures.Scale);

        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(settledBitmap, shell, IslandTestFixtures.Scale));
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(transitionBitmap, shell, IslandTestFixtures.Scale));
        Assert.True(CountPaintedPixels(settledBitmap, IslandMetrics.ContentRect(shell)) > 5000);
        Assert.True(CountPaintedPixels(transitionBitmap, IslandMetrics.ContentRect(shell)) > 5000);
    }

    [Fact]
    public void Notification_scroll_offset_and_selection_change_the_painted_page()
    {
        var baseState = IslandTestFixtures.Expanded(2);
        var items = new List<NotificationItemModel>(baseState.Pages.Notifications.Items)
        {
            new() { AppName = "Store", Title = "Update available", Body = "FluidBar 2.0.1", TimeText = "1 hr" },
            new() { AppName = "System", Title = "Backup complete", Body = "Saved to OneDrive", TimeText = "2 hr" },
        };
        var pages = baseState.Pages with
        {
            Notifications = baseState.Pages.Notifications with { Items = items },
        };
        var unscrolled = baseState with { Pages = pages };
        var scrolled = baseState with
        {
            Pages = pages with { Notifications = pages.Notifications with { ScrollOffset = 30 } },
        };
        var clamped = baseState with
        {
            Pages = pages with { Notifications = pages.Notifications with { ScrollOffset = 10000 } },
        };
        var maxScrolled = baseState with
        {
            Pages = pages with
            {
                Notifications = pages.Notifications with
                {
                    ScrollOffset = PageLayout.MaxScroll(items.Count, PageLayout.NotificationRowHeight, PageLayout.NotificationRowGap),
                },
            },
        };
        var selected = baseState with
        {
            Pages = pages with { Notifications = pages.Notifications with { SelectedIndex = 2 } },
        };
        var hovered = baseState with
        {
            Pages = pages with { Notifications = pages.Notifications with { HoverIndex = 3 } },
        };

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var unscrolledBitmap = renderer.RenderToBitmap(unscrolled, IslandTestFixtures.Scale);
        using var scrolledBitmap = renderer.RenderToBitmap(scrolled, IslandTestFixtures.Scale);
        using var clampedBitmap = renderer.RenderToBitmap(clamped, IslandTestFixtures.Scale);
        using var maxScrolledBitmap = renderer.RenderToBitmap(maxScrolled, IslandTestFixtures.Scale);
        using var selectedBitmap = renderer.RenderToBitmap(selected, IslandTestFixtures.Scale);
        using var hoveredBitmap = renderer.RenderToBitmap(hovered, IslandTestFixtures.Scale);

        Assert.NotEqual(Encode(unscrolledBitmap), Encode(scrolledBitmap));
        Assert.Equal(Encode(maxScrolledBitmap), Encode(clampedBitmap));
        Assert.NotEqual(Encode(unscrolledBitmap), Encode(selectedBitmap));
        Assert.NotEqual(Encode(unscrolledBitmap), Encode(hoveredBitmap));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Empty_list_pages_render_a_centered_empty_state(int pageIndex)
    {
        var content = IslandTestFixtures.Content();
        var state = pageIndex switch
        {
            2 => IslandTestFixtures.Expanded(2) with
            {
                Pages = content with { Notifications = new NotificationsPageModel() },
            },
            3 => IslandTestFixtures.Expanded(3) with
            {
                Pages = content with { Clipboard = new ClipboardPageModel() },
            },
            _ => IslandTestFixtures.Expanded(4) with
            {
                Pages = content with { Agent = new AgentPageModel() },
            },
        };

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var shell = IslandMetrics.ShellRect(state);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(bitmap, shell, IslandTestFixtures.Scale));

        var emptyBand = new SKRect(shell.Left + 100, shell.Top + 100, shell.Right - 100, shell.Top + 200);
        Assert.True(
            CountPaintedPixels(bitmap, emptyBand) > 200,
            $"page {pageIndex}: the empty-state icon/title/hint should paint");
    }

    private static int CountPaintedPixels(SKBitmap bitmap, SKRect dipRect)
    {
        var left = Math.Max(0, (int)Math.Floor(dipRect.Left * IslandTestFixtures.Scale));
        var top = Math.Max(0, (int)Math.Floor(dipRect.Top * IslandTestFixtures.Scale));
        var right = Math.Min(bitmap.Width, (int)Math.Ceiling(dipRect.Right * IslandTestFixtures.Scale));
        var bottom = Math.Min(bitmap.Height, (int)Math.Ceiling(dipRect.Bottom * IslandTestFixtures.Scale));
        var pixels = bitmap.Pixels;
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            var row = y * bitmap.Width;
            for (var x = left; x < right; x++)
            {
                var pixel = pixels[row + x];
                if (pixel.Alpha > 200
                    && (Math.Abs(pixel.Red - 8) + Math.Abs(pixel.Green - 8) + Math.Abs(pixel.Blue - 10)) > 24)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
