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
    private readonly ContentLineEntry?[] _contentLines = new ContentLineEntry?[64];
    private int _nextContentLine;
    private sealed record ContentLineEntry(string Source, TextStyle Style, float Width, double Scale, string First, string Second);

    // Bounded display-only text and identity lookup avoid reshaping/copying a 16 KiB clipboard
    // entry on every frame. Original text and copy payloads are never shortened.
    private (string First, string Second) SplitContentLines(string? source, TextStyle style, float width)
    {
        if (string.IsNullOrWhiteSpace(source)) return (string.Empty, string.Empty);
        foreach (var cached in _contentLines)
            if (cached is not null && ReferenceEquals(cached.Source, source) && cached.Style == style
                && cached.Width == width && cached.Scale == _text.Scale)
                return (cached.First, cached.Second);
        var count = Math.Min(source.Length, 512);
        if (count < source.Length && char.IsLowSurrogate(source[count])) count--;
        var value = count == source.Length ? source : source[..count] + "…";
        var lines = SplitTwoLines(value, style, width);
        _contentLines[_nextContentLine] = new ContentLineEntry(source, style, width, _text.Scale, lines.First, lines.Second);
        _nextContentLine = (_nextContentLine + 1) % _contentLines.Length;
        return lines;
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

    private void DrawContentScrollIndicator(SKCanvas canvas, SKRect body, float scroll, float maxScroll)
    {
        if (maxScroll <= 0) return;
        var height = Math.Max(24, body.Height * body.Height / (body.Height + maxScroll));
        var top = body.Top + (body.Height - height) * Math.Clamp(scroll / maxScroll, 0, 1);
        _fill.Color = Alpha(_theme.ForegroundSecondary, 0.35f * _opacity);
        canvas.DrawRoundRect(new SKRect(body.Right + 5, top, body.Right + 7, top + height), 1, 1, _fill);
    }

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
            _fill.Color = Alpha(_theme.Foreground, 0.13f * _opacity);
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
            new SKRect(card.Right - 78, card.Top + 8, card.Right - 29,
                card.Top + 8 + 12 + _metrics.LineGrow(IslandType.WidgetMeta)),
            _theme.ForegroundTertiary,
            TextAlign.Right,
            _opacity);

        _text.Draw(canvas, "+", IslandType.NotesHead,
            new SKRect(card.Right - 25, card.Top + 5, card.Right - 7, card.Top + 23),
            IsHovered(IslandPageControl.TaskManage) ? _theme.Foreground : _theme.ForegroundSecondary,
            TextAlign.Center, _opacity);

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
