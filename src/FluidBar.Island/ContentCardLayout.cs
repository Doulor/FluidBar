using SkiaSharp;

namespace FluidBar.Island;

/// <summary>Shared content-first card bands: summary above metadata and separately hit-tested actions.</summary>
public static class ContentCardLayout
{
    public const float ClipboardGap = 8;

    public static float ClipboardHeight(ClipboardContentKind kind, in FontScaleMetrics metrics) => kind switch
    {
        ClipboardContentKind.Image => 176 + metrics.LineGrow(IslandType.RowMeta),
        ClipboardContentKind.Files => 158 + 2 * metrics.LineGrow(IslandType.ContentTitle) + 2 * metrics.LineGrow(IslandType.RowMeta),
        _ => 116 + 4 * metrics.LineGrow(IslandType.ContentTitle) + metrics.LineGrow(IslandType.RowMeta),
    };

    public static bool ClipboardGrid(ClipboardPageModel model)
        => model.FilesOnly && model.Items.All(static item => item.Kind == ClipboardContentKind.Files);

    public static SKRect ClipboardRow(SKRect page, ClipboardPageModel model, int index, in FontScaleMetrics metrics)
    {
        var top = page.Top + PageMetricsLayout.BodyTop - ClipboardScroll(model, metrics);
        var left = page.Left + 16;
        var right = page.Right - 16;
        if (ClipboardGrid(model))
        {
            top += index / 2 * (ClipboardHeight(ClipboardContentKind.Files, metrics) + ClipboardGap);
            var width = (right - left - ClipboardGap) / 2;
            left += index % 2 * (width + ClipboardGap);
            right = left + width;
        }
        else
            for (var i = 0; i < index; i++) top += ClipboardHeight(model.Items[i].Kind, metrics) + ClipboardGap;
        return new SKRect(left, top, right, top + ClipboardHeight(model.Items[index].Kind, metrics));
    }

    public static float ClipboardMaxScroll(ClipboardPageModel model, in FontScaleMetrics metrics)
    {
        float height = 0;
        if (ClipboardGrid(model)) height = (model.Items.Count + 1) / 2 * (ClipboardHeight(ClipboardContentKind.Files, metrics) + ClipboardGap);
        else for (var i = 0; i < model.Items.Count; i++) height += ClipboardHeight(model.Items[i].Kind, metrics) + ClipboardGap;
        return Math.Max(0, height - ClipboardGap - PageMetricsLayout.BodyHeight);
    }

    public static float ClipboardScroll(ClipboardPageModel model, in FontScaleMetrics metrics)
        => (float)Math.Clamp(double.IsFinite(model.ScrollOffset) ? model.ScrollOffset : 0, 0, ClipboardMaxScroll(model, metrics));

    public static SKRect ClipboardSummary(SKRect row, in FontScaleMetrics metrics)
        => new(row.Left + 12, row.Top + 12, row.Right - 12, ClipboardAction(row, 0, metrics).Top - 10);

    public static SKRect ClipboardImage(SKRect row, in FontScaleMetrics metrics)
        => ClipboardSummary(row, metrics);

    public static SKRect FitImage(SKRect bounds, float width, float height)
    {
        if (width <= 0 || height <= 0) return SKRect.Empty;
        var ratio = Math.Min(bounds.Width / width, bounds.Height / height);
        var w = width * ratio;
        var h = height * ratio;
        return new SKRect(bounds.MidX - w / 2, bounds.MidY - h / 2, bounds.MidX + w / 2, bounds.MidY + h / 2);
    }

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

    public static IslandPageControl HitTestClipboard(SKRect page, ClipboardPageModel model, float x, float y,
        in FontScaleMetrics metrics, out int index)
    {
        index = -1;
        if (!PageLayout.ListBody(page).Contains(x, y)) return IslandPageControl.None;
        for (var i = 0; i < model.Items.Count; i++)
        {
            var row = ClipboardRow(page, model, i, metrics);
            if (!row.Contains(x, y)) continue;
            index = i;
            if (ClipboardPreview(row, metrics).Contains(x, y)) return IslandPageControl.ClipboardPreview;
            if (ClipboardAction(row, 2, metrics).Contains(x, y)) return IslandPageControl.ClipboardDelete;
            if (ClipboardAction(row, 1, metrics).Contains(x, y)) return IslandPageControl.Pin;
            if (ClipboardAction(row, 0, metrics).Contains(x, y)) return IslandPageControl.Copy;
            return IslandPageControl.Row;
        }
        return IslandPageControl.Scroll;
    }
}

/// <summary>One geometry contract for declarative plugin painting, scrolling, hit testing and UIA.</summary>
public static class PluginListLayout
{
    public static bool IsFileShelf(PluginPageModel model) => model.OwnerPluginId == "fluidbar-file-shelf";
    public static bool IsFile(PluginPageRowModel row) => row.DragFiles is { Count: > 0 };
    public static bool IsToolbar(PluginPageModel model, int index)
        => IsFileShelf(model) && model.Rows[index].RowId is "actions" or "navigation";

    public static float ToolbarHeight(in FontScaleMetrics metrics)
        => 26 + Math.Max(0, metrics.LineGrow(IslandType.RowMeta));

    public static float FileCardHeight(in FontScaleMetrics metrics)
        => 116 + 2 * Math.Max(0, metrics.LineGrow(IslandType.ContentTitle))
            + 2 * Math.Max(0, metrics.LineGrow(IslandType.RowMeta));

    public static float RowHeight(PluginPageModel model, int index, in FontScaleMetrics metrics)
        => IsToolbar(model, index) ? ToolbarHeight(metrics)
            : IsFileShelf(model) && IsFile(model.Rows[index]) ? FileCardHeight(metrics)
            : 72 + metrics.LineGrow(IslandType.RowTitle) + 2 * metrics.LineGrow(IslandType.RowBody)
                + metrics.LineGrow(IslandType.RowMeta);

    private static float ToolbarExtent(PluginPageModel model, in FontScaleMetrics metrics)
    {
        float height = 0;
        for (var i = 0; i < model.Rows.Count; i++)
            if (IsToolbar(model, i)) height += ToolbarHeight(metrics) + 6;
        return height;
    }

    public static SKRect Body(SKRect page, PluginPageModel model, in FontScaleMetrics metrics)
    {
        var body = PageLayout.ListBody(page);
        body.Top += ToolbarExtent(model, metrics);
        return body;
    }

    public static SKRect Clip(SKRect page, PluginPageModel model, int index, in FontScaleMetrics metrics)
        => IsToolbar(model, index) ? PageLayout.ListBody(page) : Body(page, model, metrics);

    public static SKRect Row(SKRect page, PluginPageModel model, int index, in FontScaleMetrics metrics)
    {
        var scroll = (float)Math.Clamp(double.IsFinite(model.ScrollOffset) ? model.ScrollOffset : 0, 0, MaxScroll(model, metrics));
        var top = page.Top + PageMetricsLayout.BodyTop;
        if (IsToolbar(model, index))
        {
            for (var i = 0; i < index; i++) if (IsToolbar(model, i)) top += ToolbarHeight(metrics) + 6;
            return new SKRect(page.Left + 16, top, page.Right - 16, top + ToolbarHeight(metrics));
        }
        top += ToolbarExtent(model, metrics) - scroll;
        var files = 0;
        for (var i = 0; i < index; i++)
        {
            if (IsToolbar(model, i)) continue;
            if (IsFileShelf(model) && IsFile(model.Rows[i])) files++;
            else top += RowHeight(model, i, metrics) + 4;
        }
        if (IsFileShelf(model) && IsFile(model.Rows[index]))
        {
            top += files / 2 * (FileCardHeight(metrics) + 8);
            var width = (page.Width - 32 - 8) / 2;
            var left = page.Left + 16 + files % 2 * (width + 8);
            return new SKRect(left, top, left + width, top + FileCardHeight(metrics));
        }
        top += (files + 1) / 2 * (FileCardHeight(metrics) + 8);
        return new SKRect(page.Left + 16, top, page.Right - 16, top + RowHeight(model, index, metrics));
    }

    public static float MaxScroll(PluginPageModel model, in FontScaleMetrics metrics)
    {
        float height = 0;
        var files = 0;
        for (var i = 0; i < model.Rows.Count; i++)
        {
            if (IsToolbar(model, i)) continue;
            if (IsFileShelf(model) && IsFile(model.Rows[i])) files++;
            else height += RowHeight(model, i, metrics) + 4;
        }
        height += (files + 1) / 2 * (FileCardHeight(metrics) + 8);
        height -= files > 0 ? 8 : height > 0 ? 4 : 0;
        return Math.Max(0, height - PageMetricsLayout.BodyHeight + ToolbarExtent(model, metrics));
    }

    public static SKRect TrailingButton(SKRect row, PluginPageRowModel item, bool compactFile = true)
        => compactFile && IsFile(item) ? new SKRect(row.Right - 78, row.Top + 8, row.Right - 10, row.Top + 36)
            : new SKRect(row.Right - 96, row.MidY - 12, row.Right - 8, row.MidY + 12);

    public static IslandPageControl HitTest(SKRect page, PluginPageModel model, float x, float y,
        in FontScaleMetrics metrics, out int index)
    {
        for (var i = 0; i < model.Rows.Count; i++)
        {
            var row = Row(page, model, i, metrics);
            if (!row.Contains(x, y) || !Clip(page, model, i, metrics).Contains(x, y)) continue;
            var item = model.Rows[i];
            index = i;
            if (!string.IsNullOrWhiteSpace(item.TrailingCommandId)
                && TrailingButton(row, item, IsFileShelf(model)).Contains(x, y))
                return IslandPageControl.PluginRowButton;
            return !string.IsNullOrWhiteSpace(item.RowCommandId) || IsFile(item)
                || !IsFileShelf(model) && !string.IsNullOrWhiteSpace(item.TrailingCommandId)
                ? IslandPageControl.PluginRow : IslandPageControl.None;
        }
        index = -1;
        return IslandPageControl.None;
    }
}
