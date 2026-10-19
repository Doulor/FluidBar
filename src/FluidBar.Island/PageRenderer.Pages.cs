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
    private static readonly string[] ClipboardTypeLabels = ["Text", "Code", "Link", "Image"];

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

        var body = PageLayout.ListBody(page);
        var scroll = PageLayout.ClampScroll(
            model.ScrollOffset,
            count,
            PageLayout.NotificationRowHeight,
            PageLayout.NotificationRowGap);
        var first = PageLayout.FirstVisibleRow(scroll, PageLayout.NotificationRowHeight, PageLayout.NotificationRowGap);

        canvas.Save();
        canvas.ClipRect(body);
        for (var i = first; i < count; i++)
        {
            var row = PageLayout.NotificationRow(page, i, scroll);
            if (row.Top >= body.Bottom)
            {
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
        var hovered = index == model.HoverIndex;
        DrawListRowBackground(canvas, row, hovered, index == model.SelectedIndex);

        var accent = _accents.Get(item.AccentHex, _theme.Info);
        var glyph = new SKRect(row.Left + 10, row.Top + 7, row.Left + 36, row.Top + 33);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(accent, 0.16f * _opacity);
        canvas.DrawRoundRect(glyph, 7, 7, _fill);
        IconSet.Draw(canvas, item.Icon ?? IconKind.Bell, glyph.Deflate(6), Alpha(accent, _opacity), _icon);

        var dismiss = new SKRect(row.Right - 30, row.Top + 10, row.Right - 10, row.Top + 30);
        if (hovered)
        {
            _fill.Color = Alpha(SKColors.White, 0.06f * _opacity);
            canvas.DrawRoundRect(dismiss, 6, 6, _fill);
        }

        IconSet.Draw(
            canvas,
            IconKind.Close,
            dismiss.Deflate(5),
            Alpha(hovered ? _theme.ForegroundSecondary : _theme.ForegroundTertiary, _opacity),
            _icon);

        var hasCode = item.VerificationCode is { Length: > 0 };
        var chip = hasCode
            ? new SKRect(row.Right - 98, row.Top + 21, row.Right - 38, row.Top + 37)
            : SKRect.Empty;

        var lineTop = row.Top + 5;
        var lineBottom = row.Top + 21;
        var app = item.AppName;
        var appWidth = string.IsNullOrEmpty(app) ? 0 : Math.Min(70, _text.Measure(app, IslandType.RowApp));
        if (appWidth > 0)
        {
            _text.Draw(
                canvas,
                app,
                IslandType.RowApp,
                new SKRect(row.Left + 44, lineTop, row.Left + 44 + appWidth, lineBottom),
                _theme.ForegroundTertiary,
                TextAlign.Left,
                _opacity);
        }

        var titleLeft = row.Left + 44 + (appWidth > 0 ? appWidth + 6 : 0);
        _text.Draw(
            canvas,
            item.Title,
            IslandType.RowTitle,
            new SKRect(titleLeft, lineTop, row.Right - 86, lineBottom),
            _theme.Foreground,
            TextAlign.Left,
            _opacity);
        _text.Draw(
            canvas,
            item.TimeText,
            IslandType.RowMeta,
            new SKRect(row.Right - 82, lineTop, row.Right - 36, lineBottom),
            _theme.ForegroundTertiary,
            TextAlign.Right,
            _opacity);

        var bodyRight = hasCode ? chip.Left - 6 : row.Right - 36;
        _text.Draw(
            canvas,
            item.Body,
            IslandType.RowBody,
            new SKRect(row.Left + 44, row.Top + 21, bodyRight, row.Top + 37),
            _theme.ForegroundSecondary,
            TextAlign.Left,
            _opacity);

        if (hasCode)
        {
            _fill.Color = Alpha(_theme.Info, 0.16f * _opacity);
            canvas.DrawRoundRect(chip, 8, 8, _fill);
            _text.Draw(canvas, item.VerificationCode, IslandType.Chip, chip, _theme.Info, TextAlign.Center, _opacity);
        }
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

        var body = PageLayout.ListBody(page);
        var scroll = PageLayout.ClampScroll(
            model.ScrollOffset,
            count,
            PageLayout.ClipboardRowHeight,
            PageLayout.ClipboardRowGap);
        var first = PageLayout.FirstVisibleRow(scroll, PageLayout.ClipboardRowHeight, PageLayout.ClipboardRowGap);

        canvas.Save();
        canvas.ClipRect(body);
        for (var i = first; i < count; i++)
        {
            var row = PageLayout.ClipboardRow(page, i, scroll);
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

        var tone = ClipboardTone(item.Kind);
        var badge = new SKRect(row.Left + 10, row.Top + 12, row.Left + 34, row.Top + 36);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(tone, 0.14f * _opacity);
        canvas.DrawRoundRect(badge, 6, 6, _fill);
        IconSet.Draw(canvas, ClipboardIcon(item.Kind), badge.Deflate(6), Alpha(tone, _opacity), _icon);

        _text.Draw(
            canvas,
            item.Preview,
            item.IsMonospace ? IslandType.RowMono : IslandType.RowTitle,
            new SKRect(row.Left + 44, row.Top + 7, row.Right - 64, row.Top + 25),
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

        var typeLabel = item.TypeLabel ?? ClipboardTypeLabels[Math.Clamp((int)item.Kind, 0, 3)];
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
            new SKRect(x, subTop, row.Right - 66, subBottom),
            _theme.ForegroundTertiary,
            TextAlign.Left,
            _opacity);

        var pin = new SKRect(row.Right - 58, row.Top + 13, row.Right - 36, row.Top + 35);
        var copy = new SKRect(row.Right - 34, row.Top + 13, row.Right - 12, row.Top + 35);
        if (hovered)
        {
            _fill.Color = Alpha(SKColors.White, 0.06f * _opacity);
            canvas.DrawRoundRect(pin, 6, 6, _fill);
            canvas.DrawRoundRect(copy, 6, 6, _fill);
        }

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
    }

    private SKColor ClipboardTone(ClipboardContentKind kind) => kind switch
    {
        ClipboardContentKind.Code => _theme.Accent,
        ClipboardContentKind.Link => _theme.Info,
        ClipboardContentKind.Image => _theme.Warning,
        _ => _theme.Success,
    };

    private static IconKind ClipboardIcon(ClipboardContentKind kind) => kind switch
    {
        ClipboardContentKind.Code => IconKind.Code,
        ClipboardContentKind.Link => IconKind.Link,
        ClipboardContentKind.Image => IconKind.Image,
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

        var body = PageLayout.ListBody(page);
        var scroll = PageLayout.ClampScroll(model.ScrollOffset, count, PageLayout.AgentRowHeight, PageLayout.AgentRowGap);
        var first = PageLayout.FirstVisibleRow(scroll, PageLayout.AgentRowHeight, PageLayout.AgentRowGap);

        canvas.Save();
        canvas.ClipRect(body);
        for (var i = first; i < count; i++)
        {
            var row = PageLayout.AgentRow(page, i, scroll);
            if (row.Top >= body.Bottom)
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
        var stateCenter = new SKPoint(row.Left + 20, row.Top + 24);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(tone, 0.12f * _opacity);
        canvas.DrawCircle(stateCenter.X, stateCenter.Y, 10, _fill);

        if (item.Tone == CompactStatus.Info)
        {
            _stroke.Style = SKPaintStyle.Stroke;
            _stroke.StrokeWidth = 1.5f;
            _stroke.Color = Alpha(tone, 0.25f * _opacity);
            canvas.DrawCircle(stateCenter.X, stateCenter.Y, 5.5f, _stroke);
            _stroke.Color = Alpha(tone, 0.9f * _opacity);
            canvas.DrawArc(
                new SKRect(stateCenter.X - 5.5f, stateCenter.Y - 5.5f, stateCenter.X + 5.5f, stateCenter.Y + 5.5f),
                -90,
                130,
                false,
                _stroke);
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

        var mainLeft = row.Left + 38;
        var mainRight = row.Right - 80;
        var tool = item.Tool;
        var toolWidth = string.IsNullOrEmpty(tool) ? 0 : Math.Min(80, _text.Measure(tool, IslandType.RowApp));
        if (toolWidth > 0)
        {
            _text.Draw(
                canvas,
                tool,
                IslandType.RowApp,
                new SKRect(mainLeft, row.Top + 7, mainLeft + toolWidth, row.Top + 20),
                _theme.ForegroundSecondary,
                TextAlign.Left,
                _opacity);
        }

        var projectLeft = mainLeft + (toolWidth > 0 ? toolWidth + 6 : 0);
        _text.Draw(
            canvas,
            item.Project,
            IslandType.RowMeta,
            new SKRect(projectLeft, row.Top + 8, mainRight, row.Top + 20),
            _theme.ForegroundTertiary,
            TextAlign.Left,
            _opacity);
        _text.Draw(
            canvas,
            item.Task,
            IslandType.RowTitle,
            new SKRect(mainLeft, row.Top + 21, mainRight, row.Top + 39),
            _theme.Foreground,
            TextAlign.Left,
            _opacity);
        _text.Draw(
            canvas,
            item.StatusText,
            IslandType.RowMetaStrong,
            new SKRect(row.Right - 78, row.Top + 8, row.Right - 10, row.Top + 20),
            tone,
            TextAlign.Right,
            _opacity);
        _text.Draw(
            canvas,
            item.DurationText,
            IslandType.RowMeta,
            new SKRect(row.Right - 78, row.Top + 22, row.Right - 10, row.Top + 34),
            _theme.ForegroundTertiary,
            TextAlign.Right,
            _opacity);

        if (item.Tone == CompactStatus.Info && item.ProgressPercent >= 0)
        {
            var track = new SKRect(row.Left + 10, row.Bottom - 3.5f, row.Right - 10, row.Bottom - 2);
            _fill.Color = Alpha(SKColors.White, 0.06f * _opacity);
            canvas.DrawRoundRect(track, 0.75f, 0.75f, _fill);
            var fraction = (float)Math.Clamp(item.ProgressPercent / 100.0, 0, 1);
            if (fraction > 0)
            {
                _fill.Color = Alpha(_theme.Accent, 0.75f * _opacity);
                canvas.DrawRoundRect(
                    new SKRect(track.Left, track.Top, track.Left + (track.Width * fraction), track.Bottom),
                    0.75f,
                    0.75f,
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
        var card = PageLayout.WidgetsWeather(page);
        DrawCard(canvas, card, 11);
        IconSet.Draw(
            canvas,
            IconKind.CloudSun,
            new SKRect(card.Left + 10, card.MidY - 9, card.Left + 28, card.MidY + 9),
            Alpha(new SKColor(0xF4, 0xC4, 0x6C), _opacity),
            _icon);

        // No weather source exists yet; an icon-only card looked broken, so the card states that the
        // data is unavailable instead of painting an empty temperature column.
        if (string.IsNullOrEmpty(model.WeatherTemp) && string.IsNullOrEmpty(model.WeatherCondition))
        {
            DrawCardCaption(canvas, card, model.WeatherUnavailableText, card.Left + 36);
            return;
        }

        _text.Draw(
            canvas,
            model.WeatherTemp,
            IslandType.WidgetTemp,
            new SKRect(card.Left + 36, card.Top + 6, card.Left + 96, card.Top + 28),
            _theme.Foreground,
            TextAlign.Left,
            _opacity);
        _text.Draw(
            canvas,
            model.WeatherCondition,
            IslandType.WidgetDesc,
            new SKRect(card.Left + 36, card.Top + 29, card.Right - 64, card.Top + 43),
            _theme.ForegroundTertiary,
            TextAlign.Left,
            _opacity);
        _text.Draw(
            canvas,
            model.WeatherHighLow,
            IslandType.WidgetMetaStrong,
            new SKRect(card.Right - 60, card.Top + 9, card.Right - 10, card.Top + 23),
            _theme.ForegroundSecondary,
            TextAlign.Right,
            _opacity);
        _text.Draw(
            canvas,
            model.WeatherWind,
            IslandType.WidgetMeta,
            new SKRect(card.Right - 60, card.Top + 25, card.Right - 10, card.Top + 39),
            _theme.ForegroundTertiary,
            TextAlign.Right,
            _opacity);
    }

    private void DrawWidgetStats(SKCanvas canvas, WidgetsPageModel model, SKRect page)
    {
        var card = PageLayout.WidgetsStats(page);
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
            var top = card.Top + 10 + (i * 20);
            _text.Draw(
                canvas,
                stat.Label,
                IslandType.StatLabel,
                new SKRect(card.Left + 10, top, card.Left + 44, top + 14),
                _theme.ForegroundTertiary,
                TextAlign.Left,
                _opacity);

            var track = new SKRect(card.Left + 48, top + 5, card.Right - 42, top + 8);
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
                new SKRect(card.Right - 38, top, card.Right - 10, top + 14),
                _theme.ForegroundSecondary,
                TextAlign.Right,
                _opacity);
        }
    }

    private void DrawWidgetTasks(SKCanvas canvas, WidgetsPageModel model, SKRect page)
    {
        var card = PageLayout.WidgetsNotes(page);
        DrawCard(canvas, card, 11);
        _text.Draw(
            canvas,
            model.TasksTitle,
            IslandType.NotesHead,
            new SKRect(card.Left + 10, card.Top + 7, card.Right - 80, card.Top + 21),
            _theme.ForegroundSecondary,
            TextAlign.Left,
            _opacity);
        _text.Draw(
            canvas,
            model.TasksProgressText,
            IslandType.WidgetMeta,
            new SKRect(card.Right - 78, card.Top + 8, card.Right - 10, card.Top + 20),
            _theme.ForegroundTertiary,
            TextAlign.Right,
            _opacity);

        var tasks = model.Tasks;
        var count = tasks is null ? 0 : Math.Min(3, tasks.Count);
        if (count == 0)
        {
            // The task card is the tallest body area; without rows only the header was painted, so the
            // card looked like an empty placeholder.
            DrawCardCaption(canvas, new SKRect(card.Left, card.Top + 22, card.Right, card.Bottom - 6),
                model.TasksEmptyText, card.Left);
        }

        for (var i = 0; i < count; i++)
        {
            var task = tasks![i];
            var row = PageLayout.WidgetsTaskRow(page, i);
            if (i == model.SelectedIndex || i == model.HoverIndex)
            {
                _fill.Style = SKPaintStyle.Fill;
                _fill.Color = Alpha(
                    i == model.SelectedIndex ? _theme.Accent : SKColors.White,
                    (i == model.SelectedIndex ? 0.10f : 0.04f) * _opacity);
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
