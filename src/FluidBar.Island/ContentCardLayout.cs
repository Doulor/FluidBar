using SkiaSharp;

namespace FluidBar.Island;

/// <summary>Shared content-first card bands: summary above metadata and separately hit-tested actions.</summary>
public static class ContentCardLayout
{
    public static SKRect ClipboardSummary(SKRect row, in FontScaleMetrics metrics)
        => new(row.Left + 46, row.Top + 9, row.Right - 12,
            row.Top + 9 + 32 + 2 * metrics.LineGrow(IslandType.ContentTitle));

    public static SKRect ClipboardAction(SKRect row, int slot, in FontScaleMetrics metrics)
    {
        var height = 22 + Math.Max(0, metrics.LineGrow(IslandType.RowMeta));
        var right = row.Right - 10 - slot * 28;
        return new SKRect(right - 24, row.Bottom - 8 - height, right, row.Bottom - 8);
    }

    public static SKRect ClipboardPreview(SKRect row, in FontScaleMetrics metrics)
    {
        var last = ClipboardAction(row, 2, metrics);
        var width = Math.Max(40, metrics.Length(34));
        return new SKRect(last.Left - 4 - width, last.Top, last.Left - 4, last.Bottom);
    }
}

/// <summary>One geometry contract for declarative plugin painting, scrolling, hit testing and UIA.</summary>
public static class PluginListLayout
{
    public static bool IsFileShelf(PluginPageModel model) => model.OwnerPluginId == "fluidbar-file-shelf";
    public static bool IsFile(PluginPageRowModel row) => row.DragFiles is { Count: > 0 };

    public static float RowHeight(PluginPageModel model, int index, in FontScaleMetrics metrics)
        => !IsFileShelf(model) ? PageMetricsLayout.NotificationRowHeight(metrics)
            : IsFile(model.Rows[index]) ? 72 + 2 * metrics.LineGrow(IslandType.ContentTitle) + metrics.LineGrow(IslandType.RowBody)
            : 44 + metrics.LineGrow(IslandType.RowTitle) + metrics.LineGrow(IslandType.RowBody);

    public static SKRect Row(SKRect page, PluginPageModel model, int index, in FontScaleMetrics metrics)
    {
        var top = page.Top + PageMetricsLayout.BodyTop - (float)model.ScrollOffset;
        for (var i = 0; i < index; i++) top += RowHeight(model, i, metrics) + 4;
        return new SKRect(page.Left + 16, top, page.Right - 16, top + RowHeight(model, index, metrics));
    }

    public static float MaxScroll(PluginPageModel model, in FontScaleMetrics metrics)
    {
        float height = 0;
        for (var i = 0; i < model.Rows.Count; i++) height += RowHeight(model, i, metrics) + 4;
        return Math.Max(0, height - 4 - PageMetricsLayout.BodyHeight);
    }

    public static SKRect TrailingButton(SKRect row, PluginPageRowModel item, bool compactFile = true)
        => compactFile && IsFile(item) ? new SKRect(row.Right - 34, row.Top + 8, row.Right - 10, row.Top + 32)
            : new SKRect(row.Right - 96, row.MidY - 12, row.Right - 8, row.MidY + 12);
}
