using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class ClipboardCardLayoutTests
{
    [Theory]
    [InlineData(0.8)]
    [InlineData(1.0)]
    [InlineData(1.6)]
    public void Files_filter_uses_two_columns_with_distinct_actions_and_scrolls_to_last_file(double scale)
    {
        var metrics = FontScaleMetrics.From(scale);
        var page = new SKRect(0, 0, 400, 260);
        var model = new ClipboardPageModel { FilesOnly = true,
            Items = Enumerable.Range(0, 5).Select(i => new ClipboardItemModel
                { Kind = ClipboardContentKind.Files, Preview = $"文件 {i}.pdf" }).ToArray() };
        var left = ContentCardLayout.ClipboardRow(page, model, 0, metrics);
        var right = ContentCardLayout.ClipboardRow(page, model, 1, metrics);
        Assert.Equal(left.Top, right.Top);
        Assert.True(left.Right < right.Left);
        var preview = ContentCardLayout.ClipboardPreview(right, metrics);
        Assert.True(right.Contains(preview));
        Assert.True(preview.Right < ContentCardLayout.ClipboardAction(right, 2, metrics).Left);
        var copy = ContentCardLayout.ClipboardAction(right, 0, metrics);
        Assert.Equal(IslandPageControl.Copy, ContentCardLayout.HitTestClipboard(page, model, copy.MidX, copy.MidY, metrics, out var index));
        Assert.Equal(1, index);
        model = model with { ScrollOffset = ContentCardLayout.ClipboardMaxScroll(model, metrics) };
        var last = ContentCardLayout.ClipboardRow(page, model, 4, metrics);
        Assert.Equal(PageLayout.ListBody(page).Bottom, last.Bottom, 3);
        var lastCopy = ContentCardLayout.ClipboardAction(last, 0, metrics);
        Assert.Equal(IslandPageControl.Copy, ContentCardLayout.HitTestClipboard(page, model, lastCopy.MidX, lastCopy.MidY, metrics, out index));
        Assert.Equal(4, index);
        Assert.False(ContentCardLayout.ClipboardGrid(model with { FilesOnly = false }));
    }

    [Fact]
    public void Mixed_cards_preserve_image_aspect_ratio_and_partial_card_remains_interactive()
    {
        var page = new SKRect(0, 0, 400, 260);
        var model = new ClipboardPageModel { Items = [new() { Kind = ClipboardContentKind.Image }, new() { Kind = ClipboardContentKind.Text }] };
        var second = ContentCardLayout.ClipboardRow(page, model, 1, FontScaleMetrics.One);
        Assert.True(second.Bottom > PageLayout.ListBody(page).Bottom);
        Assert.Equal(IslandPageControl.Row, ContentCardLayout.HitTestClipboard(page, model, second.MidX, second.Top + 5, FontScaleMetrics.One, out var index));
        Assert.Equal(1, index);
        var fitted = ContentCardLayout.FitImage(new SKRect(0, 0, 100, 100), 400, 200);
        Assert.Equal(100, fitted.Width);
        Assert.Equal(50, fitted.Height);
        Assert.Equal(25, fitted.Top);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.6)]
    public void File_grid_renders_at_supported_font_sizes(double scale)
    {
        var state = IslandTestFixtures.WithFontScale(IslandTestFixtures.Expanded(3), scale);
        state = state with { Pages = state.Pages with { Clipboard = new ClipboardPageModel
        {
            FilesOnly = true, PreviewLabel = "预览", Items = [
                new() { Kind = ClipboardContentKind.Files, Preview = "灵动岛文件排版设计_最终确认版.pdf", FileDetail = "PDF · 2.4 MB" },
                new() { Kind = ClipboardContentKind.Files, Preview = "窗口布局与通知卡片.cs", FileDetail = "CS · 6.8 KB" },
                new() { Kind = ClipboardContentKind.Files, Preview = "Archive.zip", FileDetail = "ZIP · 28 MB" },
            ],
        } } };
        using var renderer = new IslandRenderer();
        using var bitmap = renderer.RenderToBitmap(state, 1.5f);
        var shell = IslandMetrics.ShellRect(state);
        using var card = new SKBitmap();
        Assert.True(bitmap.ExtractSubset(card, new SKRectI((int)(shell.Left * 1.5f), (int)(shell.Top * 1.5f), (int)(shell.Right * 1.5f), (int)(shell.Bottom * 1.5f))));
        var directory = Path.Combine(IslandTestFixtures.RepoRoot, "build", "reports", "content-cards-preview");
        Directory.CreateDirectory(directory);
        IslandTestFixtures.SavePng(card, Path.Combine(directory, $"clipboard-grid-fs{scale:0.0}.png"));
    }
}
