using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class ContentCardLayoutTests
{
    [Fact]
    public void Long_clipboard_summaries_do_not_reallocate_the_original_text_every_frame()
    {
        var original = new string('文', 16_384);
        var state = IslandTestFixtures.Expanded(3);
        state = state with { Pages = state.Pages with { Clipboard = new ClipboardPageModel
        { Items = [new ClipboardItemModel { Preview = original, TypeLabel = "文本", TimeText = "刚刚" }] } } };
        using var renderer = new IslandRenderer();
        using var surface = renderer.CreateSurface(state, 1.5f);
        for (var i = 0; i < 12; i++) renderer.Render(surface.Canvas, state, 1.5f);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++) renderer.Render(surface.Canvas, state, 1.5f);
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 20;
        Assert.True(perFrame < 2048, $"Long clipboard summary allocated {perFrame} bytes per warm frame.");
        Assert.Same(original, state.Pages.Clipboard.Items[0].Preview);
    }

    [Theory]
    [InlineData(false, false, 1.0)]
    [InlineData(false, true, 1.0)]
    [InlineData(true, false, 1.0)]
    [InlineData(true, true, 1.0)]
    [InlineData(false, false, 1.6)]
    [InlineData(true, true, 1.6)]
    public void Render_content_and_drag_previews_for_visual_review(bool english, bool light, double scale)
    {
        var theme = light ? IslandTheme.Light : IslandTheme.Dark;
        var name = english ? "FluidBar file shelf and clipboard layout review 2026.pdf" : "灵动岛文件暂存与剪贴板排版优化方案_最终确认版.pdf";
        var folder = english ? @"C:\Work\FluidBar\Design reviews" : @"C:\工作资料\灵动岛\设计评审";
        var file = new PluginPageModel { PageId = "fluidbar-file-shelf.page", OwnerPluginId = "fluidbar-file-shelf",
            Title = english ? "File shelf" : "文件暂存", Rows = [
                new PluginPageRowModel { RowId = "actions", Title = english ? "Copy all files" : "全部复制",
                    Subtitle = english ? "Hold a file name and drag to a folder" : "按住文件名，拖到文件夹松手复制",
                    TrailingLabel = english ? "Clear" : "清空", TrailingCommandId = "clear" },
                new PluginPageRowModel { RowId = "file-1", Title = name, Subtitle = folder + "\\" + name, FileDirectory = folder,
                    DragFiles = [folder + "\\" + name], TrailingCommandId = "remove" },
                new PluginPageRowModel { RowId = "file-2", Title = "Video Project 1.mp3", FileDirectory = @"D:\Music\Projects",
                    DragFiles = [@"D:\Music\Projects\Video Project 1.mp3"], TrailingCommandId = "remove" }] };
        var clipboard = new ClipboardPageModel { PreviewLabel = english ? "View" : "预览", Items = [
            new ClipboardItemModel { Preview = english ? "Drag files between folders with a visible preview. Content summaries now have room for a complete thought." :
                "拖动文件时显示跟随鼠标的预览。文件名和剪贴板内容有更充分的展示空间，长文本也更容易辨认。",
                TypeLabel = english ? "Text" : "文本", TimeText = english ? "2 min ago" : "2 分钟前", IsPinned = true },
            new ClipboardItemModel { Preview = "var files = shelf.Where(file => file.IsAvailable).ToArray();", IsMonospace = true,
                Kind = ClipboardContentKind.Code, TypeLabel = english ? "Code" : "代码", TimeText = english ? "5 min ago" : "5 分钟前" }] };
        var state = IslandTestFixtures.WithFontScale(IslandTestFixtures.Expanded(0, theme: theme), scale);
        var directory = Path.Combine(IslandTestFixtures.RepoRoot, "build", "reports", "content-cards-preview");
        Directory.CreateDirectory(directory);
        var tag = $"{(english ? "en" : "zh")}-{(light ? "light" : "dark")}-fs{scale:0.0}";
        using var renderer = new IslandRenderer(theme);
        var slot = new PageSlot { PageId = file.PageId, Kind = PageSlotKind.Plugin, TemplateId = "list", Title = file.Title,
            PluginPage = file, IconToken = "file" };
        using var fileBitmap = renderer.RenderToBitmap(state with { Pages = state.Pages with { PageSlots = [slot] }, Pager = PagerRenderState.At(0, 1) }, 1.5f);
        SaveCardPreview(fileBitmap, state, Path.Combine(directory, "files-" + tag + ".png"));
        slot = new PageSlot { PageId = "builtin.clipboard", Kind = PageSlotKind.Clipboard, TemplateId = "list",
            Title = english ? "Clipboard" : "剪贴板", IconToken = "clipboard" };
        using var clipboardBitmap = renderer.RenderToBitmap(state with { Pages = state.Pages with { PageSlots = [slot], Clipboard = clipboard }, Pager = PagerRenderState.At(0, 1) }, 1.5f);
        SaveCardPreview(clipboardBitmap, state, Path.Combine(directory, "clipboard-" + tag + ".png"));
        using var drag = FileDragPreviewRenderer.Render(theme, name, english ? "Copy · 1 file · Esc to cancel" : "复制 · 1 个文件 · Esc 取消", 1.5f);
        Assert.Equal(SKAlphaType.Unpremul, drag.AlphaType);
        Assert.Equal(0, drag.GetPixel(0, 0).Alpha);
        Assert.True(drag.GetPixel(20, 40).Alpha > 200);
        IslandTestFixtures.SavePng(drag, Path.Combine(directory, "drag-" + tag + ".png"));
    }

    private static void SaveCardPreview(SKBitmap bitmap, IslandRenderState state, string path)
    {
        var shell = IslandMetrics.ShellRect(state);
        var bounds = new SKRectI(Math.Max(0, (int)(shell.Left * 1.5f) - 10), Math.Max(0, (int)(shell.Top * 1.5f) - 10),
            Math.Min(bitmap.Width, (int)(shell.Right * 1.5f) + 10), Math.Min(bitmap.Height, (int)(shell.Bottom * 1.5f) + 10));
        using var card = new SKBitmap();
        Assert.True(bitmap.ExtractSubset(card, bounds));
        IslandTestFixtures.SavePng(card, path);
    }

    [Theory]
    [InlineData(0.8)]
    [InlineData(1.0)]
    [InlineData(1.6)]
    public void Clipboard_actions_are_below_the_two_line_summary_and_have_distinct_targets(double scale)
    {
        var metrics = FontScaleMetrics.From(scale);
        var page = new SKRect(0, 0, 400, 252);
        var row = PageMetricsLayout.ClipboardRow(page, 0, 0, metrics);
        var text = ContentCardLayout.ClipboardSummary(row, metrics);
        var preview = PageMetricsLayout.ClipboardPreview(page, 0, 0, metrics);
        var delete = PageMetricsLayout.ClipboardDelete(page, 0, 0, metrics);
        var pin = PageMetricsLayout.ClipboardPin(page, 0, 0, metrics);
        var copy = PageMetricsLayout.ClipboardCopy(page, 0, 0, metrics);
        Assert.True(text.Width >= 280);
        Assert.True(text.Bottom <= preview.Top);
        Assert.True(preview.Right < delete.Left && delete.Right < pin.Left && pin.Right < copy.Left);
        Assert.True(row.Contains(copy) && row.Contains(text));
        Assert.Equal(IslandPageControl.Copy, PageMetricsLayout.HitTest(PageSlotKind.Clipboard, page, copy.MidX, copy.MidY,
            metrics, 0, 3, out var index));
        Assert.Equal(0, index);
    }

    [Fact]
    public void File_shelf_toolbar_does_not_consume_the_space_of_a_file_card()
    {
        var model = new PluginPageModel { PageId = "fluidbar-file-shelf.page", OwnerPluginId = "fluidbar-file-shelf", Title = "文件暂存",
            Rows = [new PluginPageRowModel { RowId = "actions", Title = "全部复制" },
                new PluginPageRowModel { RowId = "file-1", Title = "一份很长的文件名.pdf", DragFiles = ["C:\\work\\file.pdf"] }] };
        var page = new SKRect(0, 0, 400, 252);
        var toolbar = PluginListLayout.Row(page, model, 0, FontScaleMetrics.One);
        var file = PluginListLayout.Row(page, model, 1, FontScaleMetrics.One);
        Assert.True(toolbar.Height < file.Height);
        Assert.True(toolbar.Bottom < file.Top);
        Assert.True(PluginListLayout.TrailingButton(file, model.Rows[1]).Width <= 30);
    }
}
