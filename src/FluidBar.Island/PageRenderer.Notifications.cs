using SkiaSharp;

namespace FluidBar.Island;

internal sealed partial class PageRenderer
{
    private string? _notificationDetailText;
    private float _notificationDetailWidth;
    private double _notificationDetailScale;
    private string[] _notificationDetailLines = [];

    private void DrawNotificationButton(SKCanvas canvas, SKRect rect, string label, IslandPageControl control, int index = -1, bool selected = false)
    {
        _fill.Color = Alpha(selected ? _theme.Info : _theme.Foreground,
            (selected ? 0.18f : IsHovered(control, index) ? 0.14f : 0.06f) * _opacity);
        canvas.DrawRoundRect(rect, 6, 6, _fill);
        _text.Draw(canvas, label, IslandType.RowMeta, rect,
            selected ? _theme.Info : _theme.ForegroundSecondary, TextAlign.Center, _opacity);
    }

    private void DrawNotifications(SKCanvas canvas, NotificationsPageModel model, SKRect page)
    {
        if (model.Detail is { } detail)
        {
            DrawNotificationDetail(canvas, model, detail, page);
            return;
        }
        for (var i = 0; i < 3; i++)
            DrawNotificationButton(canvas, PageMetricsLayout.NotificationToolbarButton(page, i),
                model.FilterLabels.ElementAtOrDefault(i) ?? string.Empty, IslandPageControl.NotificationFilter, i, i == model.Filter);
        DrawNotificationButton(canvas, PageMetricsLayout.NotificationToolbarButton(page, 3),
            model.MarkReadLabel, IslandPageControl.NotificationMarkRead);
        if (model.UndoLabel is { Length: > 0 } undo)
            DrawNotificationButton(canvas, PageMetricsLayout.NotificationToolbarButton(page, 4), undo, IslandPageControl.NotificationUndo);
        if (model.Items.Count == 0)
        {
            DrawEmpty(canvas, IconKind.Bell, model.EmptyTitle ?? "You're all caught up", model.EmptyHint ?? "New messages will show up here", page);
            return;
        }
        var body = PageMetricsLayout.NotificationBody(page);
        var maxScroll = PageMetricsLayout.NotificationMaxScroll(model.Items.Count, _metrics);
        var scroll = (float)Math.Clamp(double.IsFinite(model.ScrollOffset) ? model.ScrollOffset : 0, 0, maxScroll);
        canvas.Save();
        canvas.ClipRect(body);
        for (var i = 0; i < model.Items.Count; i++)
        {
            var row = PageMetricsLayout.NotificationRow(page, i, scroll, _metrics);
            if (row.Top >= body.Bottom) break;
            if (row.Bottom > body.Top) DrawNotificationRow(canvas, model.Items[i], row, i, model);
        }
        canvas.Restore();
    }

    private void DrawNotificationRow(SKCanvas canvas, NotificationItemModel item, SKRect row, int index, NotificationsPageModel model)
    {
        DrawListRowBackground(canvas, row, IsHovered(IslandPageControl.Row, index), index == model.SelectedIndex);
        var accent = _accents.Get(item.AccentHex, _theme.Info);
        var icon = PageMetricsLayout.NotificationIcon(row);
        DrawNotificationGlyph(canvas, icon, accent, item);
        if (item.IsUnread)
        {
            _fill.Color = Alpha(accent, _opacity);
            canvas.DrawCircle(icon.Right - 1, icon.Top + 1, 2.5f, _fill);
        }
        _text.Draw(canvas, item.AppName, IslandType.RowApp,
            new SKRect(icon.Right + 7, row.Top + 7, row.Right - 36, row.Top + 29), _theme.ForegroundTertiary, TextAlign.Left, _opacity);
        var left = row.Left + 12;
        var right = row.Right - 12;
        var titleTop = row.Top + 31;
        var titleHeight = _text.LineHeight(PageMetricsLayout.NotificationTitleStyle);
        _text.Draw(canvas, item.Title, PageMetricsLayout.NotificationTitleStyle, new SKRect(left, titleTop, right, titleTop + titleHeight), _theme.Foreground, TextAlign.Left, _opacity);
        var bodyTop = titleTop + titleHeight + 3;
        var bodyHeight = _text.LineHeight(PageMetricsLayout.NotificationBodyStyle);
        var (first, second) = SplitContentLines(item.Body, PageMetricsLayout.NotificationBodyStyle, right - left);
        _text.Draw(canvas, first, PageMetricsLayout.NotificationBodyStyle, new SKRect(left, bodyTop, right, bodyTop + bodyHeight), _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        _text.Draw(canvas, second, PageMetricsLayout.NotificationBodyStyle, new SKRect(left, bodyTop + bodyHeight, right, bodyTop + 2 * bodyHeight), _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        DrawNotificationButton(canvas, PageMetricsLayout.NotificationDismiss(row), "×", IslandPageControl.Dismiss, index);
        if (item.VerificationCode is { Length: > 0 })
            DrawNotificationButton(canvas, PageMetricsLayout.NotificationCopyCode(row), item.CodeChipLabel ?? item.VerificationCode, IslandPageControl.Copy, index, true);
        _text.Draw(canvas, item.TimeText, IslandType.RowMeta, PageMetricsLayout.NotificationTime(row), _theme.ForegroundTertiary, TextAlign.Right, _opacity);
    }

    private void DrawNotificationDetail(SKCanvas canvas, NotificationsPageModel model, NotificationItemModel item, SKRect page)
    {
        DrawNotificationButton(canvas, PageMetricsLayout.NotificationToolbarButton(page, 0), "‹ " + model.BackLabel, IslandPageControl.NotificationBack);
        var top = page.Top + PageMetricsLayout.BodyTop + 30;
        _text.Draw(canvas, item.AppName, IslandType.RowApp, new SKRect(page.Left + 24, top, page.Right - 100, top + 19), _theme.ForegroundTertiary, TextAlign.Left, _opacity);
        _text.Draw(canvas, item.TimeText, IslandType.RowMeta, new SKRect(page.Right - 100, top, page.Right - 24, top + 19), _theme.ForegroundTertiary, TextAlign.Right, _opacity);
        var body = PageMetricsLayout.NotificationDetailBody(page);
        var fullText = item.FullBody ?? item.Body ?? string.Empty;
        if (!ReferenceEquals(fullText, _notificationDetailText) || body.Width != _notificationDetailWidth || _text.Scale != _notificationDetailScale)
        {
            _notificationDetailText = fullText;
            _notificationDetailWidth = body.Width;
            _notificationDetailScale = _text.Scale;
            _notificationDetailLines = PageMetricsLayout.NotificationDetailLines(fullText, body.Width, _metrics);
        }
        var lineHeight = PageMetricsLayout.NotificationDetailLineHeight(_metrics);
        var titleHeight = PageMetricsLayout.NotificationDetailTitleHeight(body, item, _metrics);
        var codeHeight = PageMetricsLayout.NotificationCodeHeight(item, _metrics);
        var maxScroll = Math.Max(0, _notificationDetailLines.Length * lineHeight + titleHeight + codeHeight - body.Height);
        var scroll = (float)Math.Clamp(double.IsFinite(model.ScrollOffset) ? model.ScrollOffset : 0, 0, maxScroll);
        canvas.Save();
        canvas.ClipRect(body);
        if (titleHeight > 0)
        {
            var titleLines = PageMetricsLayout.NotificationDetailLines(item.Title, body.Width, _metrics, title: true);
            var titleLineHeight = PageMetricsLayout.NotificationDetailTitleLineHeight(_metrics);
            for (var i = Math.Max(0, (int)(scroll / titleLineHeight)); i < titleLines.Length; i++)
            {
                var y = body.Top + i * titleLineHeight - scroll;
                if (y >= body.Bottom) break;
                _text.Draw(canvas, titleLines[i], PageMetricsLayout.NotificationTitleStyle, new SKRect(body.Left, y, body.Right, y + titleLineHeight), _theme.Foreground, TextAlign.Left, _opacity);
            }
        }
        if (codeHeight > 0)
        {
            var codeRect = new SKRect(body.Left, body.Top + titleHeight - scroll, body.Right, body.Top + titleHeight + codeHeight - 8 - scroll);
            _fill.Color = Alpha(_theme.Info, 0.10f * _opacity);
            canvas.DrawRoundRect(codeRect, 8, 8, _fill);
            _text.Draw(canvas, item.VerificationCode, PageMetricsLayout.NotificationCodeStyle, codeRect, _theme.Info, TextAlign.Center, _opacity);
        }
        for (var i = Math.Max(0, (int)((scroll - titleHeight - codeHeight) / lineHeight)); i < _notificationDetailLines.Length; i++)
        {
            var y = body.Top + titleHeight + codeHeight + i * lineHeight - scroll;
            if (y >= body.Bottom) break;
            _text.Draw(canvas, _notificationDetailLines[i], PageMetricsLayout.NotificationBodyStyle, new SKRect(body.Left, y, body.Right, y + lineHeight), _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        }
        canvas.Restore();
        DrawNotificationButton(canvas, PageMetricsLayout.NotificationDetailAction(page, 0), model.CopyBodyLabel, IslandPageControl.NotificationCopyBody);
        if (!string.IsNullOrWhiteSpace(item.VerificationCode))
            DrawNotificationButton(canvas, PageMetricsLayout.NotificationDetailAction(page, 1), model.CopyCodeLabel ?? "Copy code", IslandPageControl.Copy, selected: true);
        DrawNotificationButton(canvas, PageMetricsLayout.NotificationDetailAction(page, 2), model.OpenSourceLabel, IslandPageControl.NotificationOpenSource);
    }

    /// <summary>
    /// Leading notification glyph: the real toast app icon when Windows exposed one, otherwise the
    /// existing tinted bell. The image is clipped to the same rounded square as the fallback.
    /// </summary>
    private void DrawNotificationGlyph(SKCanvas canvas, SKRect rect, SKColor accent, NotificationItemModel item)
    {
        var image = item.AppIconPng is { Length: > 0 }
            ? _artwork.Get(item.AppIconKey, item.AppIconPng)
            : null;
        if (image is not null && image.Width > 0 && image.Height > 0)
        {
            canvas.Save();
            using var clip = new SKRoundRect(rect, 5, 5);
            canvas.ClipRoundRect(clip, SKClipOperation.Intersect, antialias: true);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            _fill.Color = SKColors.White.WithAlpha((byte)Math.Clamp(_opacity * 255, 0, 255));
            canvas.DrawImage(image, rect, _fill);
            canvas.Restore();
            return;
        }

        _fill.Style = SKPaintStyle.Fill;
        _fill.Shader = null;
        _fill.Color = Alpha(accent, 0.16f * _opacity);
        canvas.DrawRoundRect(rect, 10, 10, _fill);
        IconSet.Draw(canvas, item.Icon ?? IconKind.Bell, rect.Deflate(3), Alpha(accent, _opacity), _icon);
    }

}
