using SkiaSharp;

namespace FluidBar.Island;

internal sealed partial class PageRenderer
{
    private static readonly string[] ClipboardTypeLabels = ["Text", "Code", "Link", "Image", "File"];
    private readonly ClipboardLines?[] _clipboardLines = new ClipboardLines?[64];
    private int _nextClipboardLines;
    private sealed record ClipboardLines(string Source, TextStyle Style, float Width, double Scale, int Count, string[] Lines);

    private void DrawClipboardLines(SKCanvas canvas, string? source, TextStyle style, SKRect bounds, int count)
    {
        if (string.IsNullOrEmpty(source)) return;
        string[]? lines = null;
        foreach (var entry in _clipboardLines)
            if (entry is not null && ReferenceEquals(entry.Source, source) && entry.Style == style
                && entry.Width == bounds.Width && entry.Scale == _text.Scale && entry.Count == count)
            { lines = entry.Lines; break; }
        if (lines is null)
        {
            var length = Math.Min(source.Length, 1024);
            if (length < source.Length && char.IsLowSurrogate(source[length])) length--;
            var remaining = length == source.Length ? source : source[..length] + "…";
            lines = new string[count];
            for (var i = 0; i < count; i++)
            {
                var newline = remaining.IndexOfAny(['\r', '\n']);
                var paragraph = newline >= 0 ? remaining[..newline] : remaining;
                if (i == count - 1) { lines[i] = remaining.ReplaceLineEndings(" "); break; }
                var (line, rest) = SplitTwoLines(paragraph, style, bounds.Width);
                lines[i] = line;
                remaining = rest.Length > 0 ? rest + (newline >= 0 ? remaining[newline..] : string.Empty)
                    : newline >= 0 ? remaining[(newline + (remaining[newline] == '\r' && newline + 1 < remaining.Length && remaining[newline + 1] == '\n' ? 2 : 1))..] : string.Empty;
            }
            _clipboardLines[_nextClipboardLines] = new ClipboardLines(source, style, bounds.Width, _text.Scale, count, lines);
            _nextClipboardLines = (_nextClipboardLines + 1) % _clipboardLines.Length;
        }
        var height = _text.LineHeight(style);
        for (var i = 0; i < lines.Length && bounds.Top + (i + 1) * height <= bounds.Bottom + 1; i++)
            _text.Draw(canvas, lines[i], style, new SKRect(bounds.Left, bounds.Top + i * height, bounds.Right, bounds.Top + (i + 1) * height),
                _theme.Foreground, opacity: _opacity);
    }

    // ---------------------------------------------------------------- clipboard

    private void DrawClipboard(SKCanvas canvas, ClipboardPageModel model, SKRect page)
    {
        var items = model.Items;
        var count = items is null ? 0 : items.Count;
        if (count == 0)
        {
            DrawEmpty(
                canvas,
                IconKind.Clipboard,
                model.EmptyTitle ?? "Nothing copied yet",
                model.EmptyHint ?? "Clipboard history shows up here",
                page);
            return;
        }

        var m = _metrics;

        var body = PageLayout.ListBody(page);
        var maxScroll = ContentCardLayout.ClipboardMaxScroll(model, m);
        var scroll = (float)Math.Clamp(double.IsFinite(model.ScrollOffset) ? model.ScrollOffset : 0, 0, maxScroll);


        canvas.Save();
        canvas.ClipRect(body);
        for (var i = 0; i < count; i++)
        {
            var row = ContentCardLayout.ClipboardRow(page, model, i, m);
            if (row.Top >= body.Bottom)
            {
                break;
            }

            if (row.Bottom > body.Top)
            {
                DrawClipboardRow(canvas, items![i], row, i, model);
            }
        }

        canvas.Restore();
    }

    private void DrawClipboardRow(
        SKCanvas canvas, ClipboardItemModel item, SKRect row, int index, ClipboardPageModel model)
    {
        var hovered = index == model.HoverIndex;
        var selected = index == model.SelectedIndex;
        var copied = index == model.CopiedIndex;
        DrawListRowBackground(canvas, row, hovered, selected);
        if (copied && !selected && !hovered)
        {
            _fill.Color = Alpha(_theme.Accent, 0.07f * _opacity);
            canvas.DrawRoundRect(row, PageLayout.ListRowRadius, PageLayout.ListRowRadius, _fill);
        }

        var tone = ClipboardTone(item.Kind);
        var summary = ContentCardLayout.ClipboardSummary(row, _metrics);
        if (item.Kind == ClipboardContentKind.Image)
        {
            var area = ContentCardLayout.ClipboardImage(row, _metrics);
            _fill.Color = Alpha(_theme.Foreground, 0.035f * _opacity);
            canvas.DrawRoundRect(area, 8, 8, _fill);
            var image = item.ImagePng is { Length: > 0 } ? _artwork.Get(item.ImageKey, item.ImagePng) : null;
            if (image is not null)
            {
                var destination = ContentCardLayout.FitImage(area, image.Width, image.Height);
                canvas.Save();
                using var clip = new SKRoundRect(area, 8, 8);
                canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
                _fill.Color = Alpha(SKColors.White, _opacity);
                canvas.DrawImage(image, destination, _fill);
                canvas.Restore();
            }
            else
            {
                IconSet.Draw(canvas, IconKind.Image, new SKRect(area.MidX - 18, area.MidY - 28, area.MidX + 18, area.MidY + 8), Alpha(tone, _opacity), _icon);
                _text.Draw(canvas, item.Preview, IslandType.RowMeta, new SKRect(area.Left, area.MidY + 12, area.Right, area.Bottom), _theme.ForegroundSecondary, TextAlign.Center, _opacity);
            }
        }
        else if (item.Kind == ClipboardContentKind.Files)
        {
            var badge = new SKRect(summary.Left, summary.Top, summary.Left + 30, summary.Top + 30);
            var thumbnail = item.ImagePng is { Length: > 0 } ? _artwork.Get(item.ImageKey, item.ImagePng) : null;
            _fill.Color = Alpha(tone, 0.12f * _opacity);
            canvas.DrawRoundRect(badge, 7, 7, _fill);
            if (thumbnail is not null)
            {
                _fill.Color = Alpha(SKColors.White, _opacity);
                canvas.DrawImage(thumbnail, ContentCardLayout.FitImage(badge, thumbnail.Width, thumbnail.Height), _fill);
            }
            else IconSet.Draw(canvas, IconKind.Document, badge.Deflate(6), Alpha(tone, _opacity), _icon);
            var title = new SKRect(summary.Left, badge.Bottom + 8, summary.Right, summary.Bottom - 24 - _metrics.LineGrow(IslandType.RowMeta));
            var (first, second) = SplitFileName(item.Preview ?? string.Empty, title.Width);
            var lineHeight = _text.LineHeight(IslandType.ContentTitle);
            _text.Draw(canvas, first, IslandType.ContentTitle, new SKRect(title.Left, title.Top, title.Right, title.Top + lineHeight), _theme.Foreground, opacity: _opacity);
            _text.Draw(canvas, second, IslandType.ContentTitle, new SKRect(title.Left, title.Top + lineHeight, title.Right, title.Bottom), _theme.Foreground, opacity: _opacity);
            _text.Draw(canvas, item.FileDetail ?? item.TypeLabel, IslandType.RowMeta,
                new SKRect(summary.Left, title.Bottom + 4, summary.Right, summary.Bottom), _theme.ForegroundTertiary, opacity: _opacity);
        }
        else DrawClipboardLines(canvas, item.Preview, item.IsMonospace ? IslandType.RowMono : IslandType.ContentTitle, summary, 4);

        var preview = ContentCardLayout.ClipboardPreview(row, _metrics);
        var delete = ContentCardLayout.ClipboardAction(row, 2, _metrics);
        var pin = ContentCardLayout.ClipboardAction(row, 1, _metrics);
        var copy = ContentCardLayout.ClipboardAction(row, 0, _metrics);
        var typeLabel = item.TypeLabel ?? ClipboardTypeLabels[Math.Clamp((int)item.Kind, 0, 4)];
        var metaLeft = row.Left + 12;
        var fileCard = item.Kind == ClipboardContentKind.Files;
        // File cards use the whole footer for actions; the type/size is already above it.
        if (!fileCard)
        {
            var meta = new SKRect(metaLeft, preview.Top, preview.Left - 8, preview.Bottom);
            var typeWidth = Math.Min(_text.Measure(typeLabel, IslandType.RowMeta), 56 * (float)_metrics.Value);
            _text.Draw(canvas, typeLabel, IslandType.RowMeta,
                new SKRect(meta.Left, meta.Top, Math.Min(meta.Right, meta.Left + typeWidth), meta.Bottom), tone, opacity: _opacity);
            var timeLeft = metaLeft + typeWidth + 8;
            if (timeLeft < meta.Right)
                _text.Draw(canvas, item.TimeText, IslandType.RowMeta,
                    new SKRect(timeLeft, meta.Top, meta.Right, meta.Bottom), _theme.ForegroundTertiary, opacity: _opacity);
        }

        _fill.Color = Alpha(_theme.Accent, (IsHovered(IslandPageControl.ClipboardPreview, index) ? 0.17f : 0.08f) * _opacity);
        canvas.DrawRoundRect(preview, 6, 6, _fill);
        _text.Draw(canvas, model.PreviewLabel ?? "View", IslandType.RowMeta, preview,
            _theme.Accent, TextAlign.Center, _opacity);
        DrawClipboardAction(canvas, delete, IconKind.Close, IslandPageControl.ClipboardDelete, index, false);
        DrawClipboardAction(canvas, pin, IconKind.Pin, IslandPageControl.Pin, index, item.IsPinned);
        DrawClipboardAction(canvas, copy, copied ? IconKind.Check : IconKind.Copy, IslandPageControl.Copy, index, copied);
    }

    private void DrawClipboardAction(SKCanvas canvas, SKRect rect, IconKind glyph, IslandPageControl control, int index, bool active)
    {
        _fill.Color = Alpha(active ? _theme.Accent : _theme.Foreground,
            (IsHovered(control, index) ? 0.15f : active ? 0.10f : 0.045f) * _opacity);
        canvas.DrawRoundRect(rect, 6, 6, _fill);
        IconSet.Draw(canvas, glyph, rect.Deflate(6), Alpha(active ? _theme.Accent : _theme.ForegroundSecondary, _opacity), _icon);
    }

    private SKColor ClipboardTone(ClipboardContentKind kind) => kind switch
    {
        ClipboardContentKind.Code => _theme.Accent,
        ClipboardContentKind.Link => _theme.Info,
        ClipboardContentKind.Image => _theme.Warning,
        ClipboardContentKind.Files => _theme.Info,
        _ => _theme.Success,
    };

    private static IconKind ClipboardIcon(ClipboardContentKind kind) => kind switch
    {
        ClipboardContentKind.Code => IconKind.Code,
        ClipboardContentKind.Link => IconKind.Link,
        ClipboardContentKind.Image => IconKind.Image,
        ClipboardContentKind.Files => IconKind.Document,
        _ => IconKind.Document,
    };

}
