using SkiaSharp;

namespace FluidBar.Island;

internal sealed partial class PageRenderer
{
    // ------------------------------------------------------------ plugin list (M0)

    private void DrawPluginPage(SKCanvas canvas, PluginPageModel model, SKRect page)
    {
        if (model.Rows.Count == 0)
        {
            DrawEmpty(
                canvas,
                IconKind.Bell,
                model.EmptyTitle ?? "Nothing here yet",
                model.EmptyHint ?? "This page is provided by a plugin",
                page);
            return;
        }

        var m = _metrics;
        var body = PluginListLayout.Body(page, model, m);
        for (var i = 0; i < model.Rows.Count; i++)
        {
            var row = PluginListLayout.Row(page, model, i, m);
            var clip = PluginListLayout.Clip(page, model, i, m);
            if (row.Top >= clip.Bottom || row.Bottom <= clip.Top) continue;
            canvas.Save();
            canvas.ClipRect(clip);
            if (PluginListLayout.IsToolbar(model, i)) DrawFileShelfToolbar(canvas, model.Rows[i], row, i);
            else if (PluginListLayout.IsFileShelf(model) && PluginListLayout.IsFile(model.Rows[i]))
                DrawFileShelfRow(canvas, model.Rows[i], row, i);
            else DrawPluginRow(canvas, model.Rows[i], row, i);
            canvas.Restore();
        }

    }

    private void DrawFileShelfToolbar(SKCanvas canvas, PluginPageRowModel item, SKRect row, int index)
    {
        var trailing = PluginListLayout.TrailingButton(row, item, compactFile: false);
        var main = new SKRect(row.Left, row.Top, trailing.Left - 8, row.Bottom);
        _fill.Color = Alpha(_theme.Foreground, (IsHovered(IslandPageControl.PluginRow, index) ? 0.09f : 0.035f) * _opacity);
        canvas.DrawRoundRect(main, 6, 6, _fill);
        var titleWidth = Math.Min(main.Width - 16, _text.Measure(item.Title, IslandType.RowMeta) + 8);
        _text.Draw(canvas, item.Title, IslandType.RowMeta,
            new SKRect(main.Left + 8, main.Top, main.Left + 8 + titleWidth, main.Bottom),
            _theme.ForegroundSecondary, opacity: _opacity);
        if (main.Width - titleWidth > 90)
            _text.Draw(canvas, item.Value ?? item.Subtitle, IslandType.RowMeta,
                new SKRect(main.Left + titleWidth + 20, main.Top, main.Right - 8, main.Bottom),
                _theme.ForegroundSecondary, TextAlign.Right, _opacity);
        if (!string.IsNullOrWhiteSpace(item.TrailingCommandId))
        {
            _fill.Color = Alpha(_theme.Foreground, (IsHovered(IslandPageControl.PluginRowButton, index) ? 0.12f : 0.055f) * _opacity);
            canvas.DrawRoundRect(trailing, 6, 6, _fill);
            _text.Draw(canvas, item.TrailingLabel, IslandType.RowMeta, trailing, _theme.ForegroundSecondary, TextAlign.Center, _opacity);
        }
    }

    private void DrawPluginRow(SKCanvas canvas, PluginPageRowModel item, SKRect row, int index)
    {
        DrawListRowBackground(canvas, row, IsHovered(IslandPageControl.PluginRow, index) || IsHovered(IslandPageControl.PluginRowButton, index), false);
        var hasTrailing = !string.IsNullOrWhiteSpace(item.TrailingLabel) && !string.IsNullOrWhiteSpace(item.TrailingCommandId);
        var trailingWidth = hasTrailing ? 76f : 0f;
        var glyph = new SKRect(row.Left + 10, row.MidY - 10, row.Left + 30, row.MidY + 10);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.Accent, 0.16f * _opacity);
        canvas.DrawRoundRect(glyph, 6, 6, _fill);
        IconSet.Draw(canvas, item.IconToken == "file" ? IconKind.Document : IconKind.Check,
            glyph.Deflate(4), Alpha(_theme.Accent, _opacity), _icon);

        var titleRight = row.Right - 14 - trailingWidth - 8;
        var titleHeight = _text.LineHeight(IslandType.RowTitle);
        var detailHeight = _text.LineHeight(IslandType.RowBody);
        var lineTop = row.MidY - (titleHeight + detailHeight + 2) / 2;
        var lineBottom = lineTop + titleHeight;
        _text.Draw(canvas, item.Title, IslandType.RowTitle,
            new SKRect(row.Left + 38, lineTop, titleRight, lineBottom),
            _theme.Foreground, TextAlign.Left, _opacity);
        if (!string.IsNullOrWhiteSpace(item.Subtitle))
        {
            _text.Draw(canvas, item.Subtitle, IslandType.RowBody,
                new SKRect(row.Left + 38, lineBottom + 2, titleRight, lineBottom + 2 + detailHeight),
                _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        }
        else if (!string.IsNullOrWhiteSpace(item.Value))
        {
            _text.Draw(canvas, item.Value, IslandType.RowMeta,
                new SKRect(row.Left + 38, lineBottom + 2, titleRight, lineBottom + 2 + detailHeight),
                _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        }

        if (hasTrailing)
        {
            var button = PluginListLayout.TrailingButton(row, item, compactFile: false);
            _fill.Color = Alpha(_theme.Accent, 0.16f * _opacity);
            canvas.DrawRoundRect(button, 7, 7, _fill);
            _text.Draw(canvas, item.TrailingLabel!, IslandType.Chip, button,
                _theme.Accent, TextAlign.Center, _opacity);
        }
    }

    private void DrawFileShelfRow(SKCanvas canvas, PluginPageRowModel item, SKRect row, int index)
    {
        var hovered = IsHovered(IslandPageControl.PluginRow, index) || IsHovered(IslandPageControl.PluginRowButton, index);
        DrawListRowBackground(canvas, row, hovered, false);
        var visual = item.FileVisual;
        var kind = visual?.Kind ?? FileVisualKind.Document;
        var tone = kind switch
        {
            FileVisualKind.Image => _theme.Info,
            FileVisualKind.Pdf => new SKColor(218, 135, 112),
            FileVisualKind.Archive => new SKColor(187, 162, 104),
            FileVisualKind.Audio or FileVisualKind.Video => new SKColor(161, 147, 208),
            FileVisualKind.Code => _theme.Accent,
            _ => _theme.ForegroundSecondary,
        };
        var glyph = new SKRect(row.Left + 10, row.Top + 9, row.Left + 46, row.Top + 39);
        var thumbnail = _artwork.Get(visual?.ImageKey, visual?.ThumbnailPng);
        if (thumbnail is not null)
        {
            canvas.Save();
            using var clip = new SKRoundRect(glyph, 6, 6);
            canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
            var factor = Math.Max(glyph.Width / thumbnail.Width, glyph.Height / thumbnail.Height);
            var destination = SKRect.Create(glyph.MidX - thumbnail.Width * factor / 2,
                glyph.MidY - thumbnail.Height * factor / 2, thumbnail.Width * factor, thumbnail.Height * factor);
            _fill.Color = Alpha(SKColors.White, _opacity);
            canvas.DrawImage(thumbnail, destination, _fill);
            canvas.Restore();
        }
        else
        {
            _fill.Color = Alpha(tone, 0.12f * _opacity);
            canvas.DrawRoundRect(glyph, 6, 6, _fill);
            var icon = kind switch
            {
                FileVisualKind.Image => IconKind.Image,
                FileVisualKind.Audio => IconKind.Media,
                FileVisualKind.Video => IconKind.Play,
                FileVisualKind.Code => IconKind.Code,
                _ => IconKind.Document,
            };
            IconSet.Draw(canvas, icon,
                new SKRect(glyph.Left + 8, glyph.Top + 5, glyph.Right - 8, glyph.Bottom - 5), Alpha(tone, _opacity), _icon);
        }
        var titleLeft = row.Left + 10;
        var titleRight = row.Right - 10;
        var titleTop = row.Top + 44;
        var lineHeight = _text.LineHeight(IslandType.ContentTitle);
        var (first, second) = SplitFileName(item.Title, titleRight - titleLeft);
        _text.Draw(canvas, first, IslandType.ContentTitle,
            new SKRect(titleLeft, titleTop, titleRight, titleTop + lineHeight), _theme.Foreground, opacity: _opacity);
        if (second.Length > 0)
            _text.Draw(canvas, second, IslandType.ContentTitle,
                new SKRect(titleLeft, titleTop + lineHeight, titleRight, titleTop + 2 * lineHeight), _theme.Foreground, opacity: _opacity);
        var detailHeight = _text.LineHeight(IslandType.RowMeta);
        var metadataTop = row.Bottom - 8 - 2 * detailHeight;
        _text.Draw(canvas, visual?.TypeLabel ?? "FILE", IslandType.RowMeta,
            new SKRect(titleLeft, metadataTop, row.MidX, metadataTop + detailHeight), tone, opacity: _opacity);
        _text.Draw(canvas, visual?.SizeText, IslandType.RowMeta,
            new SKRect(row.MidX, metadataTop, titleRight, metadataTop + detailHeight),
            _theme.ForegroundSecondary, TextAlign.Right, _opacity);
        _text.Draw(canvas, visual?.Directory ?? item.FileDirectory ?? item.Subtitle, IslandType.RowMeta,
            new SKRect(titleLeft, row.Bottom - 8 - detailHeight, titleRight, row.Bottom - 8),
            _theme.ForegroundSecondary, opacity: _opacity);
        if (!string.IsNullOrWhiteSpace(item.TrailingCommandId))
        {
            var remove = PluginListLayout.TrailingButton(row, item);
            _fill.Color = Alpha(_theme.Foreground, (IsHovered(IslandPageControl.PluginRowButton, index) ? 0.18f : 0.09f) * _opacity);
            canvas.DrawRoundRect(remove, 7, 7, _fill);
            _text.Draw(canvas, item.TrailingLabel ?? "Remove", IslandType.RowMeta, remove,
                _theme.Foreground, TextAlign.Center, _opacity);
        }
    }

    private readonly Dictionary<(string Name, float Width, double Scale), (string First, string Second)> _fileNames = new();

    private (string First, string Second) SplitFileName(string name, float width)
    {
        var key = (name, width, _text.Scale);
        if (_fileNames.TryGetValue(key, out var cached)) return cached;
        var lines = SplitContentLines(name, IslandType.ContentTitle, width);
        if (lines.Second.Length > 0)
        {
            var extension = Path.GetExtension(name);
            // Always reserve the extension on line two, even when the original tail is ellipsized.
            var tail = lines.Second;
            if (extension.Length > 0 && !tail.EndsWith(extension, StringComparison.Ordinal)) tail = extension;
            if (_text.Measure(tail, IslandType.ContentTitle) > width)
            {
                var stem = tail[..Math.Max(0, tail.Length - extension.Length)];
                var low = 0;
                var high = stem.Length;
                while (low < high)
                {
                    var mid = (low + high + 1) / 2;
                    if (_text.Measure(stem[..mid] + "…" + extension, IslandType.ContentTitle) <= width) low = mid;
                    else high = mid - 1;
                }
                if (low > 0 && low < stem.Length && char.IsLowSurrogate(stem[low])) low--;
                tail = stem[..low] + "…" + extension;
            }
            lines = (lines.First, tail);
        }
        if (_fileNames.Count >= 128) _fileNames.Clear();
        _fileNames[key] = lines;
        return lines;
    }

}
