using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Expanded pages 2–5: notifications, clipboard history, agent activity and widgets. Every page draws
/// inside the 400×252 content rect (20/12 shell insets) and the 368×216 body band, uses the shared
/// list geometry in <see cref="PageLayout"/> for both painting and hit-testing, and keeps the warm
/// frame allocation-free (no LINQ, no string composition, indexed list access, cached number strings).
/// </summary>
internal sealed partial class PageRenderer
{
    private static readonly string[] ClipboardTypeLabels = ["Text", "Code", "Link", "Image", "File"];

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
        var body = PageLayout.ListBody(page);
        canvas.Save();
        canvas.ClipRect(body);
        for (var i = 0; i < model.Rows.Count; i++)
        {
            var row = PageMetricsLayout.NotificationRow(page, i, 0, m);
            if (row.Top >= body.Bottom)
            {
                break;
            }

            DrawPluginRow(canvas, model.Rows[i], row, i);
        }

        canvas.Restore();
    }

    private void DrawPluginRow(SKCanvas canvas, PluginPageRowModel item, SKRect row, int index)
    {
        DrawListRowBackground(canvas, row, IsHovered(IslandPageControl.PluginRow, index) || IsHovered(IslandPageControl.PluginRowButton, index), false);
        var hasTrailing = !string.IsNullOrWhiteSpace(item.TrailingLabel);
        var trailingWidth = hasTrailing ? 76f : 0f;
        var glyph = new SKRect(row.Left + 10, row.Top + 10, row.Left + 30, row.Top + 30);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.Accent, 0.16f * _opacity);
        canvas.DrawRoundRect(glyph, 6, 6, _fill);
        IconSet.Draw(canvas, IconKind.Check, glyph.Deflate(6), Alpha(_theme.Accent, _opacity), _icon);

        var titleRight = row.Right - 14 - trailingWidth - 8;
        var lineTop = row.Top + 5;
        var lineBottom = row.Top + 21;
        _text.Draw(canvas, item.Title, IslandType.RowTitle,
            new SKRect(row.Left + 38, lineTop, titleRight, lineBottom),
            _theme.Foreground, TextAlign.Left, _opacity);
        if (!string.IsNullOrWhiteSpace(item.Subtitle))
        {
            _text.Draw(canvas, item.Subtitle, IslandType.RowBody,
                new SKRect(row.Left + 38, row.Top + 21, titleRight, row.Top + 37),
                _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        }
        else if (!string.IsNullOrWhiteSpace(item.Value))
        {
            _text.Draw(canvas, item.Value, IslandType.RowMeta,
                new SKRect(row.Left + 38, row.Top + 21, titleRight, row.Top + 37),
                _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        }

        if (hasTrailing)
        {
            var button = new SKRect(row.Right - 96, row.MidY - 12, row.Right - 8, row.MidY + 12);
            _fill.Color = Alpha(_theme.Accent, 0.16f * _opacity);
            canvas.DrawRoundRect(button, 7, 7, _fill);
            _text.Draw(canvas, item.TrailingLabel!, IslandType.Chip, button,
                _theme.Accent, TextAlign.Center, _opacity);
        }
    }

    // ------------------------------------------------------------ notifications

    private void DrawNotifications(SKCanvas canvas, NotificationsPageModel model, SKRect page)
    {
        var items = model.Items;
        var count = items is null ? 0 : items.Count;
        if (count == 0)
        {
            DrawEmpty(
                canvas,
                IconKind.Bell,
                model.EmptyTitle ?? "You're all caught up",
                model.EmptyHint ?? "New messages will show up here",
                page);
            return;
        }

        var m = _metrics;
        var rowHeight = PageMetricsLayout.NotificationRowHeight(m);
        var gap = PageMetricsLayout.NotificationGap;
        var body = PageMetricsLayout.NotificationBody(page);
        var maxScroll = PageMetricsLayout.MaxListScroll(count, rowHeight, gap);
        var scroll = (float)Math.Clamp(double.IsFinite(model.ScrollOffset) ? model.ScrollOffset : 0, 0, maxScroll);
        var first = rowHeight + gap <= 0 ? 0 : (int)Math.Floor(scroll / (rowHeight + gap));

        canvas.Save();
        canvas.ClipRect(body);
        for (var i = first; i < count; i++)
        {
            var row = PageMetricsLayout.NotificationRow(page, i, scroll, m);
            if (row.Top >= body.Bottom || row.Bottom > body.Bottom)
            {
                // A card that only half fits the body would be painted as a clipped sliver; the wheel
                // scrolls the list, so stop at the last fully visible card.
                break;
            }

            if (row.Bottom > body.Top)
            {
                DrawNotificationRow(canvas, items![i], row, i, model);
            }
        }

        canvas.Restore();
    }

    private void DrawNotificationRow(
        SKCanvas canvas,
        NotificationItemModel item,
        SKRect row,
        int index,
        NotificationsPageModel model)
    {
        var hovered = IsHovered(IslandPageControl.Row, index);
        DrawListRowBackground(canvas, row, hovered, index == model.SelectedIndex);

        var accent = _accents.Get(item.AccentHex, _theme.Info);
        var icon = PageMetricsLayout.NotificationIcon(row);
        DrawNotificationGlyph(canvas, icon, accent, item);

        // Unread cue: a coloured dot on the app glyph (phone-card read), never a separate hit target.
        if (item.IsUnread)
        {
            _fill.Color = Alpha(accent, _opacity);
            canvas.DrawCircle(icon.Right - 3, icon.Top + 3, 3f, _fill);
        }

        var contentLeft = icon.Right + 12;
        var contentRight = row.Right - 16;
        var titleHeight = _text.LineHeight(IslandType.RowTitle);
        var bodyHeight = _text.LineHeight(IslandType.RowBody);
        var metaHeight = _text.LineHeight(IslandType.RowMeta);
        var titleTop = row.Top + 6;
        var titleBottom = titleTop + titleHeight;

        // App name as a compact pill, then the bold title. A measured pill (not a fixed column) keeps
        // the title wide; +10 absorbs the rounded ends and the shaper's fractional advance.
        var app = item.AppName;
        var appWidth = string.IsNullOrEmpty(app)
            ? 0
            : Math.Min(84, _text.Measure(app, IslandType.RowApp) + 10f);
        if (appWidth > 0)
        {
            var appHeight = _text.LineHeight(IslandType.RowApp) + 4;
            var pill = new SKRect(
                contentLeft,
                titleTop + ((titleHeight - appHeight) / 2),
                contentLeft + appWidth,
                titleTop + ((titleHeight + appHeight) / 2));
            _fill.Color = Alpha(_theme.Foreground, 0.07f * _opacity);
            canvas.DrawRoundRect(pill, 5, 5, _fill);
            _text.Draw(canvas, app, IslandType.RowApp, pill, _theme.ForegroundSecondary, TextAlign.Center, _opacity);
        }

        var titleLeft = contentLeft + (appWidth > 0 ? appWidth + 6 : 0);
        _text.Draw(
            canvas,
            item.Title,
            IslandType.RowTitle,
            new SKRect(titleLeft, titleTop, contentRight, titleBottom),
            _theme.Foreground,
            TextAlign.Left,
            _opacity);

        // Two body lines: the first break is measured at word boundaries; the shaper ellipsises the
        // remainder of line two when the body is longer than both lines.
        var bodyTop = titleBottom + 2;
        var (line1, line2) = SplitTwoLines(item.Body, IslandType.RowBody, contentRight - contentLeft);
        _text.Draw(
            canvas,
            line1,
            IslandType.RowBody,
            new SKRect(contentLeft, bodyTop, contentRight, bodyTop + bodyHeight),
            _theme.ForegroundSecondary,
            TextAlign.Left,
            _opacity);
        if (line2.Length > 0)
        {
            _text.Draw(
                canvas,
                line2,
                IslandType.RowBody,
                new SKRect(contentLeft, bodyTop + bodyHeight, contentRight, bodyTop + (bodyHeight * 2)),
                _theme.ForegroundSecondary,
                TextAlign.Left,
                _opacity);
        }

        // Bottom row: action chips on the left, timestamp on the right (user request: "右下时间").
        var dismiss = PageMetricsLayout.NotificationDismiss(row);
        var dismissLabel = model.DismissLabel ?? "Close";
        var dismissHovered = IsHovered(IslandPageControl.Dismiss, index);
        _fill.Color = Alpha(_theme.Foreground, (dismissHovered ? 0.15f : hovered ? 0.10f : 0.06f) * _opacity);
        canvas.DrawRoundRect(dismiss, 6, 6, _fill);
        _text.Draw(
            canvas,
            dismissLabel,
            IslandType.Chip,
            dismiss,
            dismissHovered ? _theme.Foreground : _theme.ForegroundSecondary,
            TextAlign.Center,
            _opacity);

        if (item.VerificationCode is { Length: > 0 })
        {
            var copy = PageMetricsLayout.NotificationCopyCode(row);
            _fill.Color = Alpha(_theme.Info, 0.16f * _opacity);
            canvas.DrawRoundRect(copy, 6, 6, _fill);
            var codeLabel = item.CodeChipLabel ?? item.VerificationCode;
            _text.Draw(canvas, codeLabel, IslandType.Chip, copy, _theme.Info, TextAlign.Center, _opacity);
        }

        if (!string.IsNullOrEmpty(item.TimeText))
        {
            _text.Draw(
                canvas,
                item.TimeText,
                IslandType.RowMeta,
                PageMetricsLayout.NotificationTime(row),
                _theme.ForegroundTertiary,
                TextAlign.Right,
                _opacity);
        }
    }

    /// <summary>
    /// Splits a body into the two lines a notification card paints. The first line breaks at the last
    /// whitespace that still fits (CJK breaks between characters); the second is left to the shaper's
    /// ellipsis. Pure text measurement: no allocation when the whole body already fits one line.
    /// </summary>
    private (string First, string Second) SplitTwoLines(string? text, TextStyle style, float maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (string.Empty, string.Empty);
        }

        var value = text.Trim();
        if (maxWidth <= 1 || _text.Measure(value, style) <= maxWidth)
        {
            return (value, string.Empty);
        }

        var low = 1;
        var high = value.Length;
        var best = 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_text.Measure(value[..mid], style) <= maxWidth)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        var cut = best;
        for (var i = best - 1; i >= Math.Max(0, best - 16); i--)
        {
            if (char.IsWhiteSpace(value[i]))
            {
                cut = i;
                break;
            }
        }

        if (cut <= 0)
        {
            cut = best;
        }

        // Never split a surrogate pair (emoji) down the middle.
        if (cut < value.Length && char.IsLowSurrogate(value[cut]))
        {
            cut--;
        }

        if (cut <= 0 || cut >= value.Length)
        {
            return (value, string.Empty);
        }

        return (value[..cut].TrimEnd(), value[cut..].TrimStart());
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
            canvas.ClipRoundRect(new SKRoundRect(rect, 10, 10), SKClipOperation.Intersect, antialias: true);
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
        IconSet.Draw(canvas, item.Icon ?? IconKind.Bell, rect.Deflate(8), Alpha(accent, _opacity), _icon);
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
        var rowHeight = PageMetricsLayout.ClipboardRowHeight(m);
        var body = PageLayout.ListBody(page);
        var maxScroll = Math.Max(0, (count * (rowHeight + 6)) - 6 - PageMetricsLayout.BodyHeight);
        var scroll = (float)Math.Clamp(double.IsFinite(model.ScrollOffset) ? model.ScrollOffset : 0, 0, maxScroll);
        var first = rowHeight + 6 <= 0 ? 0 : (int)Math.Floor(scroll / (rowHeight + 6));

        canvas.Save();
        canvas.ClipRect(body);
        for (var i = first; i < count; i++)
        {
            var row = PageMetricsLayout.ClipboardRow(page, i, scroll, m);
            if (row.Top >= body.Bottom || row.Bottom > body.Bottom)
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
        SKCanvas canvas,
        ClipboardItemModel item,
        SKRect row,
        int index,
        ClipboardPageModel model)
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

        var dy = (row.Height - 48f) / 2;
        canvas.Save();
        canvas.Translate(0, dy);
        var tone = ClipboardTone(item.Kind);
        var badge = new SKRect(row.Left + 10, row.Top + 12, row.Left + 34, row.Top + 36);
        var thumbnail = item.ImagePng is { Length: > 0 }
            ? _artwork.Get(item.ImageKey, item.ImagePng)
            : null;
        if (thumbnail is not null && thumbnail.Width > 0 && thumbnail.Height > 0)
        {
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(badge, 6, 6), SKClipOperation.Intersect, antialias: true);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            _fill.Color = SKColors.White.WithAlpha((byte)Math.Clamp(_opacity * 255, 0, 255));
            canvas.DrawImage(thumbnail, badge, _fill);
            canvas.Restore();
        }
        else
        {
            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            _fill.Color = Alpha(tone, 0.14f * _opacity);
            canvas.DrawRoundRect(badge, 6, 6, _fill);
            IconSet.Draw(canvas, ClipboardIcon(item.Kind), badge.Deflate(6), Alpha(tone, _opacity), _icon);
        }

        _text.Draw(
            canvas,
            item.Preview,
            item.IsMonospace ? IslandType.RowMono : IslandType.RowTitle,
            new SKRect(row.Left + 44, row.Top + 7, row.Right - 88, row.Top + 25),
            _theme.Foreground,
            TextAlign.Left,
            _opacity);

        var subTop = row.Top + 28;
        var subBottom = row.Top + 42;
        var x = row.Left + 44;
        if (item.IsPinned)
        {
            IconSet.Draw(
                canvas,
                IconKind.Pin,
                new SKRect(x, subTop + 1, x + 8, subTop + 9),
                Alpha(_theme.Accent, _opacity),
                _icon);
            x += 10;
            var pinnedLabel = model.PinnedLabel ?? "Pinned";
            var pinnedWidth = Math.Min(42, _text.Measure(pinnedLabel, IslandType.RowMeta));
            _text.Draw(
                canvas,
                pinnedLabel,
                IslandType.RowMeta,
                new SKRect(x, subTop, x + pinnedWidth, subBottom),
                _theme.Accent,
                TextAlign.Left,
                _opacity);
            x += pinnedWidth + 4;
            _text.Draw(canvas, "·", IslandType.RowMeta, new SKRect(x, subTop, x + 6, subBottom),
                _theme.ForegroundTertiary, TextAlign.Left, _opacity);
            x += 8;
        }

        var typeLabel = item.TypeLabel ?? ClipboardTypeLabels[Math.Clamp((int)item.Kind, 0, 4)];
        var typeWidth = Math.Min(40, _text.Measure(typeLabel, IslandType.RowMeta));
        _text.Draw(
            canvas,
            typeLabel,
            IslandType.RowMeta,
            new SKRect(x, subTop, x + typeWidth, subBottom),
            _theme.ForegroundTertiary,
            TextAlign.Left,
            _opacity);
        x += typeWidth + 4;
        _text.Draw(canvas, "·", IslandType.RowMeta, new SKRect(x, subTop, x + 6, subBottom),
            _theme.ForegroundTertiary, TextAlign.Left, _opacity);
        x += 8;
        _text.Draw(
            canvas,
            item.TimeText,
            IslandType.RowMeta,
            new SKRect(x, subTop, row.Right - 90, subBottom),
            _theme.ForegroundTertiary,
            TextAlign.Left,
            _opacity);

        var delete = new SKRect(row.Right - 82, row.Top + 13, row.Right - 60, row.Top + 35);
        var pin = new SKRect(row.Right - 58, row.Top + 13, row.Right - 36, row.Top + 35);
        var copy = new SKRect(row.Right - 34, row.Top + 13, row.Right - 12, row.Top + 35);
        if (hovered || IsHovered(IslandPageControl.ClipboardDelete, index)
            || IsHovered(IslandPageControl.Pin, index) || IsHovered(IslandPageControl.Copy, index))
        {
            _fill.Color = Alpha(SKColors.White, 0.06f * _opacity);
            if (hovered && !IsHovered(IslandPageControl.Pin, index) && !IsHovered(IslandPageControl.Copy, index)
                && !IsHovered(IslandPageControl.ClipboardDelete, index))
            {
                canvas.DrawRoundRect(delete, 6, 6, _fill);
                canvas.DrawRoundRect(pin, 6, 6, _fill);
                canvas.DrawRoundRect(copy, 6, 6, _fill);
            }
            else
            {
                if (IsHovered(IslandPageControl.ClipboardDelete, index)) canvas.DrawRoundRect(delete, 6, 6, _fill);
                if (IsHovered(IslandPageControl.Pin, index)) canvas.DrawRoundRect(pin, 6, 6, _fill);
                if (IsHovered(IslandPageControl.Copy, index)) canvas.DrawRoundRect(copy, 6, 6, _fill);
            }
        }

        IconSet.Draw(
            canvas,
            IconKind.Close,
            delete.Deflate(6),
            Alpha(_theme.ForegroundTertiary, _opacity),
            _icon);
        IconSet.Draw(
            canvas,
            IconKind.Pin,
            pin.Deflate(5.5f),
            Alpha(item.IsPinned ? _theme.Accent : _theme.ForegroundTertiary, _opacity),
            _icon);
        IconSet.Draw(
            canvas,
            copied ? IconKind.Check : IconKind.Copy,
            copy.Deflate(5.5f),
            Alpha(copied ? _theme.Accent : _theme.ForegroundTertiary, _opacity),
            _icon);

        canvas.Restore();
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

    // -------------------------------------------------------------------- agent

    private void DrawAgent(SKCanvas canvas, AgentPageModel model, SKRect page)
    {
        var items = model.Items;
        var count = items is null ? 0 : items.Count;
        if (count == 0)
        {
            DrawEmpty(
                canvas,
                IconKind.Agent,
                model.EmptyTitle ?? "No agent sessions",
                model.EmptyHint ?? "Running agents will show up here",
                page);
            return;
        }

        var m = _metrics;
        var rowHeight = PageMetricsLayout.AgentRowHeight(m);
        // prototype2 .agent-list: the rows span the full 400 DIP page width (no 16 DIP list inset).
        var body = new SKRect(page.Left, page.Top + PageMetricsLayout.BodyTop,
            page.Right, page.Top + PageMetricsLayout.BodyTop + PageMetricsLayout.BodyHeight);
        var maxScroll = Math.Max(0, (count * (rowHeight + 6)) - 6 - PageMetricsLayout.BodyHeight);
        var scroll = (float)Math.Clamp(double.IsFinite(model.ScrollOffset) ? model.ScrollOffset : 0, 0, maxScroll);
        var first = rowHeight + 6 <= 0 ? 0 : (int)Math.Floor(scroll / (rowHeight + 6));

        canvas.Save();
        canvas.ClipRect(body);
        for (var i = first; i < count; i++)
        {
            var row = PageMetricsLayout.AgentRow(page, i, scroll, m);
            if (row.Top >= body.Bottom || row.Bottom > body.Bottom)
            {
                break;
            }

            if (row.Bottom > body.Top)
            {
                DrawAgentRow(canvas, items![i], row, i, model);
            }
        }

        canvas.Restore();
    }

    private void DrawAgentRow(SKCanvas canvas, AgentItemModel item, SKRect row, int index, AgentPageModel model)
    {
        var hovered = index == model.HoverIndex;
        DrawListRowBackground(canvas, row, hovered, index == model.SelectedIndex);

        var tone = Tone(item.Tone);
        // prototype2 .agent-state: 20×20 circle whose centre sits 10 DIP in from the row edge.
        var stateCenter = new SKPoint(row.Left + 20, row.MidY);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(tone, 0.10f * _opacity);
        canvas.DrawCircle(stateCenter.X, stateCenter.Y, 10, _fill);

        if (item.Tone is CompactStatus.Info or CompactStatus.Warning)
        {
            // prototype2 .agent-state .spinner-ring: an 11×11 border ring (1.5px) whose top+right
            // seams spin 900ms linear, exactly like the compact capsule ring.
            const float spinnerRadius = (11f - AgentMotion.RingStrokeWidth) / 2f;
            // Track ring (the 25 % base border) then the arc, both through the shared primitive.
            _stroke.Style = SKPaintStyle.Stroke;
            _stroke.StrokeCap = AgentSpinner.Cap;
            _stroke.StrokeWidth = AgentSpinner.StrokeForRadius(spinnerRadius);
            _stroke.Color = Alpha(_theme.Accent, AgentMotion.SpinnerBaseAlpha * _opacity);
            canvas.DrawCircle(stateCenter.X, stateCenter.Y, spinnerRadius, _stroke);
            var rotation = _reduced ? 0 : AgentSpinner.RotationDegrees(_agentAnimationSeconds);
            AgentSpinner.Draw(canvas, _stroke, stateCenter, spinnerRadius, rotation, Alpha(tone, _opacity));
        }
        else if (item.Tone == CompactStatus.Success)
        {
            IconSet.Draw(
                canvas,
                IconKind.Check,
                new SKRect(stateCenter.X - 5.5f, stateCenter.Y - 5.5f, stateCenter.X + 5.5f, stateCenter.Y + 5.5f),
                Alpha(tone, _opacity),
                _icon);
        }
        else if (item.Tone == CompactStatus.Danger)
        {
            IconSet.Draw(
                canvas,
                IconKind.Alert,
                new SKRect(stateCenter.X - 5.5f, stateCenter.Y - 5.5f, stateCenter.X + 5.5f, stateCenter.Y + 5.5f),
                Alpha(tone, _opacity),
                _icon);
        }
        else
        {
            _fill.Color = Alpha(tone, _opacity);
            canvas.DrawCircle(stateCenter.X, stateCenter.Y, 2.5f, _fill);
        }

        // prototype2 .agent-head: tool/brand 9px/600 fg-2, project 8px fg-3, 6 DIP gap, baseline-aligned;
        // the right column is 8px/600 status over 8px fg-3 duration, right-aligned 10 DIP in.
        // The duration column is reserved for the widest "00:00" with tabular (mono) digits: the
        // per-second text change must not move the left column. Status width still follows its text
        // (a real status change is a content change), but the duration always sits in this fixed slot.
        var statusWidth = string.IsNullOrEmpty(item.StatusText)
            ? 0
            : _text.Measure(item.StatusText, IslandType.RowMetaStrong) + 2f;
        var durationColumnWidth = _text.Measure("00:00", IslandType.RowMetaMono) + 2f;
        var durationWidth = string.IsNullOrEmpty(item.DurationText) ? 0 : durationColumnWidth;
        var rightRight = row.Right - 10;
        var rightLeft = rightRight - Math.Max(statusWidth, durationWidth);
        var mainLeft = row.Left + 39;
        var mainRight = Math.Max(mainLeft, rightLeft - 9);
        var headTop = row.Top + 9;

        var tool = item.Tool;
        var toolWidth = string.IsNullOrEmpty(tool)
            ? 0
            : Math.Min((mainRight - mainLeft) * 0.6f, _text.Measure(tool, IslandType.AgentTool) + 2f);
        if (toolWidth > 0)
        {
            _text.Draw(
                canvas,
                tool,
                IslandType.AgentTool,
                new SKRect(mainLeft, headTop, mainLeft + toolWidth, headTop + 12),
                _theme.ForegroundSecondary,
                TextAlign.Left,
                _opacity);
        }

        var projectLeft = mainLeft + (toolWidth > 0 ? toolWidth + 6 : 0);
        if (projectLeft < mainRight)
        {
            _text.Draw(
                canvas,
                item.Project ?? string.Empty,
                IslandType.RowMeta,
                new SKRect(projectLeft, headTop, mainRight, headTop + 12),
                _theme.ForegroundTertiary,
                TextAlign.Left,
                _opacity);
        }

        _text.Draw(
            canvas,
            item.Task,
            IslandType.AgentTask,
            new SKRect(mainLeft, row.Top + 23, mainRight, row.Top + 23 + 14),
            _theme.Foreground,
            TextAlign.Left,
            _opacity);

        if (statusWidth > 0)
        {
            _text.Draw(
                canvas,
                item.StatusText,
                IslandType.RowMetaStrong,
                new SKRect(rightLeft, headTop, rightRight, headTop + 12),
                tone,
                TextAlign.Right,
                _opacity);
        }

        if (durationWidth > 0)
        {
            _text.Draw(
                canvas,
                item.DurationText,
                IslandType.RowMetaMono,
                new SKRect(rightRight - durationColumnWidth, row.Top + 23, rightRight, row.Top + 23 + 12),
                _theme.ForegroundTertiary,
                TextAlign.Right,
                _opacity);
        }

        if (item.Tone is CompactStatus.Info or CompactStatus.Warning)
        {
            // prototype2 .agent-progress: a 1.5 DIP full-row track; the span pulses its width
            // 48% → 72% over 2.4s ease-in-out alternate while the agent is active. A real provider
            // progress (ProgressPercent >= 0) pins the width instead of pulsing.
            var track = new SKRect(row.Left, row.Bottom - AgentMotion.ProgressHeight, row.Right, row.Bottom);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(SKColors.White, 0.06f * _opacity);
            canvas.DrawRect(track, _fill);
            var fraction = item.ProgressPercent >= 0
                ? (float)Math.Clamp(item.ProgressPercent / 100.0, 0, 1)
                : _reduced
                    ? (float)AgentMotion.ProgressMinFraction
                    : (float)(AgentMotion.IndeterminateProgressPercent(_agentAnimationSeconds) / 100.0);
            if (fraction > 0)
            {
                var fillColor = item.Tone == CompactStatus.Info ? _theme.Accent : tone;
                _fill.Color = Alpha(fillColor, AgentMotion.ProgressOpacity * _opacity);
                canvas.DrawRoundRect(
                    new SKRect(track.Left, track.Top, track.Left + (track.Width * fraction), track.Bottom),
                    AgentMotion.ProgressRadius,
                    AgentMotion.ProgressRadius,
                    _fill);
            }
        }
    }

    // ------------------------------------------------------------------ widgets

    private void DrawWidgets(SKCanvas canvas, WidgetsPageModel model, SKRect page)
    {
        DrawCalendar(canvas, model, page);
        DrawWidgetWeather(canvas, model, page);
        DrawWidgetStats(canvas, model, page);
        DrawWidgetTasks(canvas, model, page);
    }

    private void DrawCalendar(SKCanvas canvas, WidgetsPageModel model, SKRect page)
    {
        var card = PageLayout.WidgetsCalendar(page);
        DrawCard(canvas, card, 12);
        _text.Draw(
            canvas,
            model.CalendarMonthTitle,
            IslandType.CalendarMonth,
            new SKRect(card.Left + 10, card.Top + 8, card.Right - 56, card.Top + 26),
            _theme.Foreground,
            TextAlign.Left,
            _opacity);

        if (IsHovered(IslandPageControl.CalendarPrevious) || IsPressed(IslandPageControl.CalendarPrevious))
        {
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(_theme.Foreground, 0.09f * _opacity);
            canvas.DrawRoundRect(PageLayout.WidgetsCalendarNav(page, previous: true), 5, 5, _fill);
        }
        if (IsHovered(IslandPageControl.CalendarNext) || IsPressed(IslandPageControl.CalendarNext))
        {
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(_theme.Foreground, 0.09f * _opacity);
            canvas.DrawRoundRect(PageLayout.WidgetsCalendarNav(page, previous: false), 5, 5, _fill);
        }
        IconSet.Draw(
            canvas,
            IconKind.ChevronLeft,
            PageLayout.WidgetsCalendarNav(page, previous: true).Deflate(4),
            Alpha(_theme.ForegroundSecondary, _opacity),
            _icon);
        IconSet.Draw(
            canvas,
            IconKind.ChevronRight,
            PageLayout.WidgetsCalendarNav(page, previous: false).Deflate(4),
            Alpha(_theme.ForegroundSecondary, _opacity),
            _icon);

        const float cellWidth = 180f / 7f;
        var headers = model.WeekdayHeaders;
        var headerCount = headers is null ? 0 : Math.Min(7, headers.Count);
        for (var i = 0; i < headerCount; i++)
        {
            var left = page.Left + 26 + (i * cellWidth);
            _text.Draw(
                canvas,
                headers![i],
                IslandType.CalendarWeek,
                new SKRect(left, card.Top + 33, left + cellWidth, card.Top + 45),
                _theme.ForegroundTertiary,
                TextAlign.Center,
                _opacity);
        }

        var days = model.CalendarDays;
        var dayCount = days is null ? 0 : Math.Min(42, days.Count);
        for (var i = 0; i < dayCount; i++)
        {
            var day = days![i];
            var cell = PageLayout.WidgetsCalendarCell(page, i);
            if (day.IsToday)
            {
                _fill.Style = SKPaintStyle.Fill;
                _fill.Color = Alpha(_theme.Foreground, 0.92f * _opacity);
                canvas.DrawCircle(cell.MidX, cell.MidY, 9, _fill);
            }
            else if (day.HasEvents)
            {
                _fill.Style = SKPaintStyle.Fill;
                _fill.Color = Alpha(_theme.Accent, 0.85f * _opacity);
                canvas.DrawCircle(cell.MidX, cell.Bottom - 3, 1.4f, _fill);
            }

            var color = day.IsToday
                ? _theme.Background
                : day.InMonth
                    ? _theme.ForegroundSecondary
                    : _theme.ForegroundTertiary;
            _text.Draw(
                canvas,
                _numbers.Number(day.Day),
                day.IsToday ? IslandType.CalendarDayStrong : IslandType.CalendarDay,
                cell,
                color,
                TextAlign.Center,
                _opacity);
        }

        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = 1;
        _stroke.Color = Alpha(_theme.Line, _opacity);
        canvas.DrawLine(card.Left + 10, card.Top + 184, card.Right - 10, card.Top + 184, _stroke);

        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.Accent, _opacity);
        canvas.DrawCircle(card.Left + 12, card.Top + 194, 2, _fill);
        _text.Draw(
            canvas,
            model.CalendarEventText,
            IslandType.CalendarFoot,
            new SKRect(card.Left + 18, card.Top + 186, card.Right - 48, card.Top + 202),
            _theme.ForegroundTertiary,
            TextAlign.Left,
            _opacity);
        _text.Draw(
            canvas,
            model.TodayLabel,
            IslandType.CalendarFoot,
            new SKRect(card.Right - 44, card.Top + 186, card.Right - 10, card.Top + 202),
            _theme.ForegroundTertiary,
            TextAlign.Right,
            _opacity);
    }

    private void DrawWidgetWeather(SKCanvas canvas, WidgetsPageModel model, SKRect page)
    {
        var m = _metrics;
        var card = PageMetricsLayout.WidgetsWeather(page, m);
        DrawCard(canvas, card, 11);

        // The App maps the WMO weather code onto the three weather glyphs this icon set contains; a
        // model without a glyph keeps the historical cloud-sun so old fixtures/previews are unchanged.
        var glyph = model.WeatherIcon ?? IconKind.CloudSun;
        var glyphColor = glyph switch
        {
            IconKind.Sun => new SKColor(0xF4, 0xC4, 0x6C),
            IconKind.Moon => new SKColor(0xA9, 0xB8, 0xE8),
            _ => new SKColor(0xF4, 0xC4, 0x6C),
        };
        IconSet.Draw(
            canvas,
            glyph,
            new SKRect(card.Left + 10, card.MidY - 9, card.Left + 28, card.MidY + 9),
            Alpha(glyphColor, _opacity),
            _icon);

        // Without a real reading (offline and no cache yet) the card states that the data is
        // unavailable instead of painting an empty temperature column.
        if (string.IsNullOrEmpty(model.WeatherTemp) && string.IsNullOrEmpty(model.WeatherCondition))
        {
            DrawCardCaption(canvas, card, model.WeatherUnavailableText, card.Left + 36);
            return;
        }

        var tempTop = card.Top + 6;
        var tempHeight = 22 + m.LineGrow(IslandType.WidgetTemp);
        _text.Draw(
            canvas,
            model.WeatherTemp,
            IslandType.WidgetTemp,
            new SKRect(card.Left + 36, tempTop, card.Left + 96, tempTop + tempHeight),
            _theme.Foreground,
            TextAlign.Left,
            _opacity);
        var conditionTop = card.Top + 29 + m.LineGrow(IslandType.WidgetTemp);
        _text.Draw(
            canvas,
            model.WeatherCondition,
            IslandType.WidgetDesc,
            // The high/low pair sits on the line above, so the condition may use the full card width;
            // the old 64 DIP right inset clipped "Partly cloudy" to "Partly cl…" at 1.6.
            new SKRect(card.Left + 36, conditionTop, card.Right - 10,
                conditionTop + 14 + m.LineGrow(IslandType.WidgetDesc)),
            _theme.ForegroundTertiary,
            TextAlign.Left,
            _opacity);
        _text.Draw(
            canvas,
            model.WeatherHighLow,
            IslandType.WidgetMetaStrong,
            new SKRect(card.Right - 60, card.Top + 9, card.Right - 10,
                card.Top + 9 + 14 + m.LineGrow(IslandType.WidgetMetaStrong)),
            _theme.ForegroundSecondary,
            TextAlign.Right,
            _opacity);
        _text.Draw(
            canvas,
            model.WeatherWind,
            IslandType.WidgetMeta,
            new SKRect(card.Right - 60, card.Top + 25 + m.LineGrow(IslandType.WidgetMetaStrong), card.Right - 10,
                card.Top + 25 + m.LineGrow(IslandType.WidgetMetaStrong) + 14 + m.LineGrow(IslandType.WidgetMeta)),
            _theme.ForegroundTertiary,
            TextAlign.Right,
            _opacity);
    }

    private void DrawWidgetStats(SKCanvas canvas, WidgetsPageModel model, SKRect page)
    {
        var m = _metrics;
        var card = PageMetricsLayout.WidgetsStats(page, m);
        DrawCard(canvas, card, 11);
        var stats = model.Stats;
        var count = stats is null ? 0 : Math.Min(3, stats.Count);
        if (count == 0)
        {
            DrawCardCaption(canvas, card, model.StatsEmptyText, card.Left);
        }

        for (var i = 0; i < count; i++)
        {
            var stat = stats![i];
            // Three stat rows must stay inside the fixed 74 DIP card at every font scale.
            var pitch = Math.Min(20 + _metrics.LineGrow(IslandType.StatLabel), 24f);
            var rowHeight = 14 + m.LineGrow(IslandType.StatLabel);
            var top = card.Top + 10 + (i * pitch);
            var labelWidth = 34 + (2 * m.LineGrow(IslandType.StatLabel));
            _text.Draw(
                canvas,
                stat.Label,
                IslandType.StatLabel,
                new SKRect(card.Left + 10, top, card.Left + 10 + labelWidth, top + rowHeight),
                _theme.ForegroundTertiary,
                TextAlign.Left,
                _opacity);

            var track = new SKRect(card.Left + 14 + labelWidth, top + 5, card.Right - 42, top + 8);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(SKColors.White, 0.09f * _opacity);
            canvas.DrawRoundRect(track, 1.5f, 1.5f, _fill);
            var fraction = (float)Math.Clamp(stat.Value / 100.0, 0, 1);
            if (fraction > 0)
            {
                _fill.Color = Alpha(StatTone(stat.Tone), _opacity);
                canvas.DrawRoundRect(
                    new SKRect(track.Left, track.Top, track.Left + (track.Width * fraction), track.Bottom),
                    1.5f,
                    1.5f,
                    _fill);
            }

            _text.Draw(
                canvas,
                _numbers.Percent(stat.Value),
                IslandType.StatValue,
                new SKRect(card.Right - 38, top, card.Right - 10, top + rowHeight),
                _theme.ForegroundSecondary,
                TextAlign.Right,
                _opacity);
        }
    }

    private void DrawWidgetTasks(SKCanvas canvas, WidgetsPageModel model, SKRect page)
    {
        var tasks = model.Tasks;
        var total = tasks?.Count ?? 0;
        // Paint every task up to the cap; a longer file gets a localized "N more" line instead of silently
        // hiding rows. The card grows with the painted rows and the page scroll offset reveals it.
        var painted = Math.Min(PageMetricsLayout.MaxPaintedTaskRows, total);
        var hasOverflow = painted > 0 && !string.IsNullOrEmpty(model.TasksOverflowText);
        var card = PageMetricsLayout.WidgetsNotes(page, _metrics, painted + (hasOverflow ? 1 : 0));
        DrawCard(canvas, card, 11);
        var headHeight = 14 + _metrics.LineGrow(IslandType.NotesHead);
        _text.Draw(
            canvas,
            model.TasksTitle,
            IslandType.NotesHead,
            new SKRect(card.Left + 10, card.Top + 7, card.Right - 80, card.Top + 7 + headHeight),
            _theme.ForegroundSecondary,
            TextAlign.Left,
            _opacity);
        _text.Draw(
            canvas,
            model.TasksProgressText,
            IslandType.WidgetMeta,
            new SKRect(card.Right - 78, card.Top + 8, card.Right - 10,
                card.Top + 8 + 12 + _metrics.LineGrow(IslandType.WidgetMeta)),
            _theme.ForegroundTertiary,
            TextAlign.Right,
            _opacity);

        if (painted == 0)
        {
            // The task card is the tallest body area; without rows only the header was painted, so the
            // card looked like an empty placeholder.
            DrawCardCaption(canvas, new SKRect(card.Left, card.Top + 22, card.Right, card.Bottom - 6),
                model.TasksEmptyText, card.Left);
            return;
        }

        for (var i = 0; i < painted; i++)
        {
            var task = tasks![i];
            var row = PageMetricsLayout.WidgetsTaskRow(page, i, _metrics);
            var taskHovered = IsHovered(IslandPageControl.TaskToggle, i);
            if (i == model.SelectedIndex || taskHovered)
            {
                _fill.Style = SKPaintStyle.Fill;
                _fill.Color = Alpha(
                    i == model.SelectedIndex ? _theme.Accent : SKColors.White,
                    (i == model.SelectedIndex ? 0.10f : 0.075f) * _opacity);
                canvas.DrawRoundRect(row, 4, 4, _fill);
            }

            var box = new SKRect(row.Left, row.MidY - 5.5f, row.Left + 11, row.MidY + 5.5f);
            if (task.Done)
            {
                _fill.Style = SKPaintStyle.Fill;
                _fill.Color = Alpha(_theme.Success, _opacity);
                canvas.DrawRoundRect(box, 3, 3, _fill);
                IconSet.Draw(
                    canvas,
                    IconKind.Check,
                    box.Deflate(2),
                    Alpha(_theme.Background, _opacity),
                    _icon);
            }
            else
            {
                _stroke.Style = SKPaintStyle.Stroke;
                _stroke.StrokeWidth = 1.2f;
                _stroke.Color = Alpha(_theme.LineStrong, _opacity);
                canvas.DrawRoundRect(box, 3, 3, _stroke);
            }

            _text.Draw(
                canvas,
                task.Title,
                IslandType.TaskLabel,
                new SKRect(row.Left + 17, row.Top, row.Right, row.Bottom),
                task.Done ? _theme.ForegroundTertiary : _theme.ForegroundSecondary,
                TextAlign.Left,
                _opacity);
        }

        if (hasOverflow)
        {
            // Pinned to the last fully visible row slot while the card is taller than the body, so the
            // "N more" line is visible without scrolling; once the page scrolls it settles into its
            // natural row (the canvas is translated by the scroll offset by the renderer).
            var hint = PageMetricsLayout.WidgetsTaskRow(page, painted, _metrics);
            var pitch = PageMetricsLayout.WidgetsTaskPitch(_metrics);
            var bodyBottom = PageMetricsLayout.Body(page).Bottom;
            var maxScroll = PageMetricsLayout.MaxScroll(page, PageSlotKind.Widgets, _metrics, total);
            var scroll = (float)Math.Clamp(
                double.IsFinite(model.ScrollOffset) ? model.ScrollOffset : 0, 0, maxScroll);
            // Page coordinates that render at the bottom of the visible band for the current scroll.
            var pinnedTop = bodyBottom - pitch + scroll;
            var top = hint.Top - scroll <= bodyBottom ? hint.Top : pinnedTop;
            var rect = new SKRect(hint.Left, top, hint.Right, top + pitch);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(_theme.Background, 0.86f * _opacity);
            canvas.DrawRoundRect(new SKRect(rect.Left, rect.MidY - 9, rect.Right, rect.MidY + 9), 4, 4, _fill);
            _text.Draw(
                canvas,
                model.TasksOverflowText,
                IslandType.TaskLabel,
                new SKRect(rect.Left + 17, rect.Top, rect.Right, rect.Bottom),
                _theme.ForegroundTertiary,
                TextAlign.Left,
                _opacity);
        }
    }

    private SKColor StatTone(WidgetStatTone tone) => tone switch
    {
        WidgetStatTone.Accent => _theme.Accent,
        WidgetStatTone.Info => _theme.Info,
        WidgetStatTone.Success => _theme.Success,
        _ => _theme.ForegroundSecondary,
    };

    // ------------------------------------------------------------------ helpers

    /// <summary>Centres a small localized caption inside a widget card (empty/unavailable state).</summary>
    private void DrawCardCaption(SKCanvas canvas, SKRect area, string? text, float leftInset)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var rect = new SKRect(leftInset + 8, area.Top, Math.Max(leftInset + 9, area.Right - 8), area.Bottom);
        _text.Draw(canvas, text, IslandType.EmptyHint, rect, _theme.ForegroundTertiary, TextAlign.Left, _opacity);
    }

    private void DrawListRowBackground(SKCanvas canvas, SKRect row, bool hovered, bool selected)
    {
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = selected
            ? Alpha(_theme.Accent, 0.10f * _opacity)
            : hovered
                ? Alpha(Mix(_theme.BackgroundRaised, SKColors.White, 0.06f), _opacity)
                : Alpha(_theme.BackgroundRaised, _opacity);
        canvas.DrawRoundRect(row, PageLayout.ListRowRadius, PageLayout.ListRowRadius, _fill);
    }

    private SKColor Tone(CompactStatus status) => status switch
    {
        CompactStatus.Info => _theme.Info,
        CompactStatus.Success => _theme.Success,
        CompactStatus.Warning => _theme.Warning,
        CompactStatus.Danger => _theme.Danger,
        _ => _theme.ForegroundSecondary,
    };

    private static SKColor Mix(SKColor from, SKColor to, float amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return new SKColor(
            (byte)(from.Red + ((to.Red - from.Red) * t)),
            (byte)(from.Green + ((to.Green - from.Green) * t)),
            (byte)(from.Blue + ((to.Blue - from.Blue) * t)),
            (byte)(from.Alpha + ((to.Alpha - from.Alpha) * t)));
    }
}
