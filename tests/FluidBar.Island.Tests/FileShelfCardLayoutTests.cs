using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class FileShelfCardLayoutTests
{
    [Theory]
    [InlineData(0.8)]
    [InlineData(1.0)]
    [InlineData(1.6)]
    public void Files_form_two_columns_and_toolbar_remains_visible_while_scrolling(double scale)
    {
        var metrics = FontScaleMetrics.From(scale);
        var model = Shelf();
        var page = new SKRect(0, 0, 400, 260);
        var toolbar = PluginListLayout.Row(page, model, 0, metrics);
        var first = PluginListLayout.Row(page, model, 1, metrics);
        var second = PluginListLayout.Row(page, model, 2, metrics);
        var third = PluginListLayout.Row(page, model, 3, metrics);
        Assert.Equal(first.Top, second.Top);
        Assert.True(first.Right < second.Left);
        Assert.True(third.Top > first.Bottom);
        Assert.True(toolbar.Height < 40);
        model = model with { ScrollOffset = 45 };
        Assert.Equal(toolbar, PluginListLayout.Row(page, model, 0, metrics));
        Assert.Equal(first.Top - 45, PluginListLayout.Row(page, model, 1, metrics).Top);
    }

    [Fact]
    public void Generic_plugin_rows_keep_full_width_and_individual_scroll_positions()
    {
        var model = Shelf() with { OwnerPluginId = "other-plugin" };
        var page = new SKRect(0, 0, 400, 260);
        var first = PluginListLayout.Row(page, model, 1, FontScaleMetrics.One);
        var second = PluginListLayout.Row(page, model, 2, FontScaleMetrics.One);
        Assert.Equal(first.Left, second.Left);
        Assert.Equal(368, first.Width);
        Assert.True(second.Top > first.Bottom);
    }

    private static PluginPageModel Shelf() => new()
    {
        PageId = "fluidbar-file-shelf.page", OwnerPluginId = "fluidbar-file-shelf", Title = "文件暂存",
        Rows = new[] { new PluginPageRowModel { RowId = "actions", Title = "全部复制" } }
            .Concat(Enumerable.Range(1, 7).Select(i => new PluginPageRowModel
            { RowId = "file-" + i, Title = "file-" + i + ".pdf", DragFiles = ["C:\\files\\file-" + i + ".pdf"] })).ToArray(),
    };
}
