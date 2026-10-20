using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Font-scale aware geometry for the expanded pages. Every function reduces to the exact prototype
/// 1.0 rectangle when <see cref="FontScaleMetrics.One"/> is passed (the golden images and hit targets
/// stay pixel-identical), and grows the text-bearing containers by the measured line-height delta at
/// larger scales. Drawing, hit-testing and UI Automation all consume these rectangles so a painted
/// control and its click target can never drift.
/// </summary>
public static class PageMetricsLayout
{
    /// <summary>Page body top inside a page rect (title gutter; 28 DIP at every scale).</summary>
    public const float BodyTop = 28;

    /// <summary>Expanded body height available below the title gutter (232 DIP).</summary>
    public const float BodyHeight = 232;

    // ---------------------------------------------------------------- home

    /// <summary>Home clock rectangle (grows with the clock line height).</summary>
    public static SKRect HomeClock(SKRect page, in FontScaleMetrics metrics) => new(
        page.Left, page.Top + BodyTop, page.Left + 172,
        page.Top + BodyTop + 34 + metrics.LineGrow(IslandType.Clock));

    /// <summary>Home date rectangle (stacked below the clock).</summary>
    public static SKRect HomeDate(SKRect page, in FontScaleMetrics metrics)
    {
        var clock = HomeClock(page, metrics);
        return new SKRect(page.Left, clock.Bottom, page.Left + 172, clock.Bottom + 12 + metrics.LineGrow(IslandType.Date));
    }

    /// <summary>Home weather chip rectangle (top-right of the first row).</summary>
    public static SKRect HomeWeather(SKRect page, in FontScaleMetrics metrics) => new(
        page.Left + 300, page.Top + BodyTop, page.Left + 400,
        page.Top + BodyTop + 34 + metrics.LineGrow(IslandType.WeatherTemp) + metrics.LineGrow(IslandType.WeatherCondition));

    /// <summary>Home quick-toggle rectangle for an index (0..3).</summary>
    public static SKRect HomeToggle(SKRect page, int index, in FontScaleMetrics metrics)
    {
        const float gap = 6f;
        var width = (400f - (gap * 3)) / 4f;
        var left = page.Left + (index * (width + gap));
        var top = HomeDate(page, metrics).Bottom;
        return new SKRect(left, top, left + width, top + 38 + metrics.LineGrow(IslandType.Toggle));
    }

    /// <summary>Home now-playing card rectangle.</summary>
    public static SKRect HomeMediaCard(SKRect page, in FontScaleMetrics metrics)
    {
        var top = HomeToggle(page, 0, metrics).Bottom + 8;
        var height = 48 + metrics.LineGrow(IslandType.CardTitle) + metrics.LineGrow(IslandType.CardSubtitle);
        return new SKRect(page.Left, top, page.Left + 400, top + height);
    }

    /// <summary>Home now-playing play/pause button (vertically centred in the card).</summary>
    public static SKRect HomeMediaPlay(SKRect page, in FontScaleMetrics metrics)
    {
        var card = HomeMediaCard(page, metrics);
        return new SKRect(card.Left + 366, card.MidY - 13, card.Left + 392, card.MidY + 13);
    }

    /// <summary>Home media progress track (bottom edge of the card).</summary>
    public static SKRect HomeMediaProgress(SKRect page, in FontScaleMetrics metrics)
    {
        var card = HomeMediaCard(page, metrics);
        return new SKRect(card.Left + 7, card.Bottom - 2, card.Right - 7, card.Bottom);
    }

    /// <summary>Home sliders card rectangle (below the media card).</summary>
    public static SKRect HomeSliders(SKRect page, in FontScaleMetrics metrics)
    {
        var top = HomeMediaCard(page, metrics).Bottom + 8;
        return new SKRect(page.Left, top, page.Left + 400, top + 38 + metrics.LineGrow(IslandType.SliderValue));
    }

    /// <summary>Home volume speaker icon (mute button) at the left of the volume track.</summary>
    public static SKRect HomeVolumeIcon(SKRect page, in FontScaleMetrics metrics)
    {
        var card = HomeSliders(page, metrics);
        return new SKRect(card.Left + 6, card.MidY - 10, card.Left + 27, card.MidY + 10);
    }

    /// <summary>Home volume track (centred in the sliders card).</summary>
    public static SKRect HomeVolumeTrack(SKRect page, in FontScaleMetrics metrics)
    {
        var card = HomeSliders(page, metrics);
        return new SKRect(card.Left + 29, card.MidY - 1.5f, card.Left + 140, card.MidY + 1.5f);
    }

    /// <summary>Home brightness track (centred in the sliders card).</summary>
    public static SKRect HomeBrightnessTrack(SKRect page, in FontScaleMetrics metrics)
    {
        var card = HomeSliders(page, metrics);
        return new SKRect(card.Left + 229, card.MidY - 1.5f, card.Left + 340, card.MidY + 1.5f);
    }

    /// <summary>Home battery row rectangle (below the sliders card).</summary>
    public static SKRect HomeBattery(SKRect page, in FontScaleMetrics metrics)
    {
        var top = HomeSliders(page, metrics).Bottom + 8;
        return new SKRect(page.Left, top, page.Left + 400, top + 18 + metrics.LineGrow(IslandType.BatteryLine));
    }

    /// <summary>Home content height below the title gutter (0 at the approved 1.0 scale).</summary>
    public static float HomeContentExtent(SKRect page, in FontScaleMetrics metrics)
        => HomeBattery(page, metrics).Bottom - (page.Top + BodyTop);

    // ---------------------------------------------------------------- media

    /// <summary>Media artwork rectangle (92×92, fixed).</summary>
    public static SKRect MediaArt(SKRect page) => new(page.Left, page.Top + BodyTop, page.Left + 92, page.Top + 120);

    /// <summary>Media eyebrow rectangle.</summary>
    public static SKRect MediaEyebrow(SKRect page, in FontScaleMetrics metrics) => new(
        page.Left + 106, page.Top + 30, page.Left + 376,
        page.Top + 30 + 10 + metrics.LineGrow(IslandType.MediaEyebrow));

    /// <summary>Media title rectangle (below the eyebrow).</summary>
    public static SKRect MediaTitle(SKRect page, in FontScaleMetrics metrics)
    {
        var top = MediaEyebrow(page, metrics).Bottom + 2;
        return new SKRect(page.Left + 106, top, page.Left + 376, top + 26 + metrics.LineGrow(IslandType.MediaTitle));
    }

    /// <summary>Media artist rectangle.</summary>
    public static SKRect MediaArtist(SKRect page, in FontScaleMetrics metrics)
    {
        var top = MediaTitle(page, metrics).Bottom;
        return new SKRect(page.Left + 106, top, page.Left + 376, top + 14 + metrics.LineGrow(IslandType.MediaArtist));
    }

    /// <summary>Media album rectangle.</summary>
    public static SKRect MediaAlbum(SKRect page, in FontScaleMetrics metrics)
    {
        var top = MediaArtist(page, metrics).Bottom + 2;
        return new SKRect(page.Left + 106, top, page.Left + 376, top + 12 + metrics.LineGrow(IslandType.MediaAlbum));
    }

    /// <summary>Bottom of the artwork/title block: whichever of the artwork or the text stack is taller.</summary>
    public static float MediaTopBottom(SKRect page, in FontScaleMetrics metrics)
        => Math.Max(page.Top + 120, MediaAlbum(page, metrics).Bottom);

    /// <summary>Media position/duration time column width (30 DIP at 1.0, grows with the time line).</summary>
    public static float MediaSeekTimeWidth(in FontScaleMetrics metrics)
        => 30 + (metrics.LineGrow(IslandType.SeekTime) * 1.5f);

    /// <summary>Media lyric card rectangle (inner padding absorbs part of the line growth).</summary>
    public static SKRect MediaLyricCard(SKRect page, in FontScaleMetrics metrics)
        => MediaLyricCard(page, metrics, MediaSqueeze(page, metrics));

    /// <summary>Media seek row rectangle.</summary>
    public static SKRect MediaSeekRow(SKRect page, in FontScaleMetrics metrics)
        => MediaSeekRow(page, metrics, MediaSqueeze(page, metrics));

    /// <summary>
    /// Extra vertical space the media page needs at this font scale (0 when it fits).
    /// </summary>
    /// <remarks>
    /// A 1.6 font scale makes the six media text lines ~20 DIP taller than the page body, which pushed the
    /// transport row out of the shell. The page keeps every line at its user-chosen size and reclaims the
    /// deficit from the three flexible gaps instead (card, seek row, transport), so nothing overflows.
    /// </remarks>
    public static float MediaSqueeze(SKRect page, in FontScaleMetrics metrics)
    {
        var need = MediaNaturalOverflow(page, metrics, squeeze: 0);
        if (need <= 0)
        {
            return 0;
        }

        // The gaps only absorb part of the deficit, so measure again with them already shrunk and add
        // whatever is left: the second pass converges because every gap has a floor.
        for (var pass = 0; pass < 4; pass++)
        {
            var residual = MediaNaturalOverflow(page, metrics, need);
            if (residual <= 0.01f)
            {
                break;
            }

            need += residual;
        }

        return need;
    }

    /// <summary>How far the media stack runs past the page bottom at a given squeeze.</summary>
    private static float MediaNaturalOverflow(SKRect page, in FontScaleMetrics metrics, float squeeze)
    {
        var seek = MediaSeekRow(page, metrics, squeeze);
        var playBottom = seek.Bottom + MediaTransportGap(squeeze) + MediaTransportSize;
        return playBottom - page.Bottom;
    }

    /// <summary>Fixed transport button size on the media page (30 DIP).</summary>
    private const float MediaTransportSize = 30f;

    /// <summary>Gap between the seek row and the transport row, shrunk first when the page overflows.</summary>
    private static float MediaTransportGap(float squeeze) => Math.Max(4, 16 - (squeeze * 0.4f));

    /// <summary>Gap above the lyric card, shrunk when the page overflows.</summary>
    private static float MediaCardGap(float squeeze) => Math.Max(2, 8 - (squeeze * 0.15f));

    /// <summary>Gap above the seek row, shrunk when the page overflows.</summary>
    private static float MediaSeekGap(float squeeze) => Math.Max(2, 10 - (squeeze * 0.25f));

    private static SKRect MediaLyricCard(SKRect page, in FontScaleMetrics metrics, float squeeze)
    {
        var top = MediaTopBottom(page, metrics) + MediaCardGap(squeeze);
        // 13 DIP lyric + 10.5 DIP next line (readability-first): 7 top + 17 lyric + 3 gap +
        // 14 next + 4 bottom = 45; the 50 DIP floor keeps the two lines from touching the card edge.
        var content = 7 + 17 + 3 + 14 + 4
            + metrics.LineGrow(IslandType.Lyric) + metrics.LineGrow(IslandType.LyricNext);
        var height = Math.Max(46, content);
        return new SKRect(page.Left, top, page.Left + 400, top + height);
    }

    private static SKRect MediaSeekRow(SKRect page, in FontScaleMetrics metrics, float squeeze)
    {
        var top = MediaLyricCard(page, metrics, squeeze).Bottom + MediaSeekGap(squeeze);
        // The seek row also gives up height when the page is squeezed: its labels sit well inside the row.
        var height = Math.Max(18, 22 - (squeeze * 0.2f)) + metrics.LineGrow(IslandType.SeekTime);
        return new SKRect(page.Left, top, page.Left + 400, top + height);
    }

    /// <summary>Media seek track (centred between the two time labels).</summary>
    public static SKRect MediaSeekTrack(SKRect page, in FontScaleMetrics metrics)
    {
        var row = MediaSeekRow(page, metrics);
        var time = MediaSeekTimeWidth(metrics);
        return new SKRect(page.Left + 9 + time, row.MidY - 1.5f, page.Right - 9 - time, row.MidY + 1.5f);
    }

    /// <summary>Vertical offset applied to the transport row (0 at the approved scale).</summary>
    public static float TransportOffset(SKRect page, in FontScaleMetrics metrics)
    {
        var squeeze = MediaSqueeze(page, metrics);
        var seek = MediaSeekRow(page, metrics, squeeze);
        return (seek.Bottom + MediaTransportGap(squeeze)) - (page.Top + 222);
    }

    // Media footer split: volume + output on the left, the three transport buttons exactly centred,
    // queue on the right. Group width = 28 + 3 + 30 + 3 + 28 = 92, so its left edge is 154 (400 DIP).
    /// <summary>Media previous-track hit rectangle (left button of the centred transport group).</summary>
    public static SKRect MediaPrevious(SKRect page, in FontScaleMetrics metrics)
    {
        var dy = TransportOffset(page, metrics);
        return new SKRect(page.Left + 154, page.Top + 219 + dy, page.Left + 182, page.Top + 247 + dy);
    }

    /// <summary>Media play/pause hit rectangle (centre of the transport group).</summary>
    public static SKRect MediaPlay(SKRect page, in FontScaleMetrics metrics)
    {
        var dy = TransportOffset(page, metrics);
        return new SKRect(page.Left + 185, page.Top + 218 + dy, page.Left + 215, page.Top + 248 + dy);
    }

    /// <summary>Media next-track hit rectangle (right button of the centred transport group).</summary>
    public static SKRect MediaNext(SKRect page, in FontScaleMetrics metrics)
    {
        var dy = TransportOffset(page, metrics);
        return new SKRect(page.Left + 218, page.Top + 219 + dy, page.Left + 246, page.Top + 247 + dy);
    }

    /// <summary>Left volume readout (speaker + percentage) before the output label.</summary>
    public static SKRect MediaVolume(SKRect page, in FontScaleMetrics metrics)
    {
        var dy = TransportOffset(page, metrics);
        return new SKRect(page.Left + 10, page.Top + 219 + dy, page.Left + 58, page.Top + 247 + dy);
    }

    /// <summary>Media output button rectangle, on the left of the footer.</summary>
    public static SKRect MediaOutput(SKRect page, in FontScaleMetrics metrics)
    {
        var dy = TransportOffset(page, metrics);
        return new SKRect(page.Left + 60, page.Top + 220 + dy, page.Left + 148, page.Top + 246 + dy);
    }

    /// <summary>Media queue button rectangle, on the right of the footer.</summary>
    public static SKRect MediaQueue(SKRect page, in FontScaleMetrics metrics)
    {
        var dy = TransportOffset(page, metrics);
        return new SKRect(page.Left + 300, page.Top + 220 + dy, page.Left + 390, page.Top + 246 + dy);
    }

    /// <summary>Media content height below the title gutter.</summary>
    public static float MediaContentExtent(SKRect page, in FontScaleMetrics metrics)
        => MediaSeekRow(page, metrics).Bottom + 16 + 38 - (page.Top + BodyTop);

    // ---------------------------------------------------------------- lists

    /// <summary>Notification row height (40 at 1.0; grows with the two text lines).</summary>
    public static float NotificationRowHeight(in FontScaleMetrics metrics)
        => 40 + metrics.LineGrow(IslandType.RowTitle) + metrics.LineGrow(IslandType.RowBody);

    /// <summary>Clipboard row height (48 at 1.0).</summary>
    public static float ClipboardRowHeight(in FontScaleMetrics metrics)
        => 48 + metrics.LineGrow(IslandType.RowTitle) + metrics.LineGrow(IslandType.RowMeta);

    /// <summary>Agent row height (44.6 at 1.0 — prototype2 row: 2×9 padding + head 12 + 2 + task 12.6).</summary>
    public static float AgentRowHeight(in FontScaleMetrics metrics)
        => 44.6f + metrics.LineGrow(IslandType.AgentTask) + metrics.LineGrow(IslandType.RowMeta);

    /// <summary>Notification row rectangle for an index at a scroll offset.</summary>
    public static SKRect NotificationRow(SKRect page, int index, float scrollOffset, in FontScaleMetrics metrics)
    {
        var top = page.Top + BodyTop + (index * (NotificationRowHeight(metrics) + 4)) - scrollOffset;
        return new SKRect(page.Left + 16, top, page.Right - 16, top + NotificationRowHeight(metrics));
    }

    /// <summary>Notification dismiss button (vertically centred in the row).</summary>
    public static SKRect NotificationDismiss(SKRect page, int index, float scrollOffset, in FontScaleMetrics metrics)
    {
        var row = NotificationRow(page, index, scrollOffset, metrics);
        return new SKRect(row.Right - 30, row.MidY - 10, row.Right - 10, row.MidY + 10);
    }

    /// <summary>Notification verification-code chip.</summary>
    public static SKRect NotificationCodeChip(SKRect page, int index, float scrollOffset, in FontScaleMetrics metrics)
    {
        var row = NotificationRow(page, index, scrollOffset, metrics);
        return new SKRect(row.Right - 98, row.Bottom - 19, row.Right - 38, row.Bottom - 3);
    }

    /// <summary>Clipboard row rectangle for an index at a scroll offset.</summary>
    public static SKRect ClipboardRow(SKRect page, int index, float scrollOffset, in FontScaleMetrics metrics)
    {
        var top = page.Top + BodyTop + (index * (ClipboardRowHeight(metrics) + 6)) - scrollOffset;
        return new SKRect(page.Left + 16, top, page.Right - 16, top + ClipboardRowHeight(metrics));
    }

    /// <summary>Clipboard pin button (vertically centred in the row).</summary>
    public static SKRect ClipboardPin(SKRect page, int index, float scrollOffset, in FontScaleMetrics metrics)
    {
        var row = ClipboardRow(page, index, scrollOffset, metrics);
        return new SKRect(row.Right - 58, row.MidY - 11, row.Right - 36, row.MidY + 11);
    }

    /// <summary>Clipboard copy button (vertically centred in the row).</summary>
    public static SKRect ClipboardCopy(SKRect page, int index, float scrollOffset, in FontScaleMetrics metrics)
    {
        var row = ClipboardRow(page, index, scrollOffset, metrics);
        return new SKRect(row.Right - 34, row.MidY - 11, row.Right - 12, row.MidY + 11);
    }

    /// <summary>Agent row rectangle for an index at a scroll offset (full page width, prototype2 .agent-row).</summary>
    public static SKRect AgentRow(SKRect page, int index, float scrollOffset, in FontScaleMetrics metrics)
    {
        var top = page.Top + BodyTop + (index * (AgentRowHeight(metrics) + 6)) - scrollOffset;
        return new SKRect(page.Left, top, page.Right, top + AgentRowHeight(metrics));
    }

    // ---------------------------------------------------------------- widgets

    /// <summary>Widgets weather card (right column, grows with the temperature/condition lines).</summary>
    public static SKRect WidgetsWeather(SKRect page, in FontScaleMetrics metrics)
    {
        var height = Math.Max(54, 8 + 18 + metrics.LineGrow(IslandType.WidgetTemp)
            + 14 + metrics.LineGrow(IslandType.WidgetDesc) + 6);
        return new SKRect(page.Left + 224, page.Top + 28, page.Right - 16, page.Top + 28 + (float)height);
    }

    /// <summary>Widgets stats card (three rows; grows with the stat label pitch).</summary>
    public static SKRect WidgetsStats(SKRect page, in FontScaleMetrics metrics)
    {
        var top = WidgetsWeather(page, metrics).Bottom + 6;
        var pitch = Math.Min(20 + metrics.LineGrow(IslandType.StatLabel), 24f);
        var height = 10 + (2 * pitch) + 14 + metrics.LineGrow(IslandType.StatLabel) + 10;
        return new SKRect(page.Left + 224, top, page.Right - 16, top + Math.Max(74, height));
    }

    /// <summary>Task rows the widgets card paints; longer files get the localized "N more" line.</summary>
    public const int MaxPaintedTaskRows = 8;

    /// <summary>Widgets task row pitch (label line height plus the fixed gap).</summary>
    public static float WidgetsTaskPitch(in FontScaleMetrics metrics) => 15 + metrics.LineGrow(IslandType.TaskLabel);

    /// <summary>Widgets tasks card sized for three rows (historical height; fixtures/goldens unchanged).</summary>
    public static SKRect WidgetsNotes(SKRect page, in FontScaleMetrics metrics) => WidgetsNotes(page, metrics, 0);

    /// <summary>
    /// Widgets tasks card grown to the painted row count (at least three rows keep the historical height,
    /// at most <see cref="MaxPaintedTaskRows"/>); the page scroll offset then reveals the taller card.
    /// </summary>
    /// <param name="page">Page rect.</param>
    /// <param name="metrics">Font-scale metrics.</param>
    /// <param name="taskCount">Tasks the card will paint.</param>
    public static SKRect WidgetsNotes(SKRect page, in FontScaleMetrics metrics, int taskCount)
    {
        var top = WidgetsStats(page, metrics).Bottom + 6;
        var pitch = WidgetsTaskPitch(metrics);
        // Painted rows plus one optional "N more" line may extend the card.
        var rows = Math.Max(3, Math.Clamp(taskCount, 0, MaxPaintedTaskRows + 1));
        var content = 7 + 14 + metrics.LineGrow(IslandType.NotesHead) + 10
            + ((rows - 1) * pitch) + (15 + metrics.LineGrow(IslandType.TaskLabel)) + 4;
        return new SKRect(page.Left + 224, top, page.Right - 16, top + Math.Max(92, content));
    }

    /// <summary>Widgets task row rectangle; the pitch follows the task line height and the card top.</summary>
    public static SKRect WidgetsTaskRow(SKRect page, int index, in FontScaleMetrics metrics)
    {
        var card = WidgetsNotes(page, metrics);
        var pitch = WidgetsTaskPitch(metrics);
        var top = card.Top + 24 + (index * pitch);
        return new SKRect(card.Left + 10, top, card.Right - 10, top + pitch);
    }

    /// <summary>Height of the taller widgets column (the calendar stays 232 DIP; the right column grows).</summary>
    /// <param name="page">Page rect.</param>
    /// <param name="metrics">Font-scale metrics.</param>
    /// <param name="taskCount">Tasks the card will paint (0 keeps the three-row height).</param>
    public static float WidgetsContentExtent(SKRect page, in FontScaleMetrics metrics, int taskCount = 0)
        => Math.Max(BodyHeight, WidgetsNotes(page, metrics, taskCount).Bottom - (page.Top + BodyTop));

    // ---------------------------------------------------------------- extents

    /// <summary>Scroll extent of a page's body content (DIP below the title gutter).</summary>
    public static float ContentExtent(SKRect page, PageSlotKind kind, in FontScaleMetrics metrics, int itemCount = 0)
    {
        switch (kind)
        {
            case PageSlotKind.Home:
                return HomeContentExtent(page, metrics);
            case PageSlotKind.Media:
                return MediaContentExtent(page, metrics);
            case PageSlotKind.Notifications:
            case PageSlotKind.Clipboard:
                return BodyHeight; // list pages clamp their own model scroll offset
            case PageSlotKind.Widgets:
                return WidgetsContentExtent(page, metrics, itemCount);
            default:
                return BodyHeight;
        }
    }

    /// <summary>Maximum scroll offset for a page (0 when the content fits the body).</summary>
    public static float MaxScroll(SKRect page, PageSlotKind kind, in FontScaleMetrics metrics, int itemCount = 0)
        => Math.Max(0, ContentExtent(page, kind, metrics, itemCount) - BodyHeight);

    /// <summary>Clamps a page scroll offset.</summary>
    public static float ClampScroll(SKRect page, PageSlotKind kind, double offset, in FontScaleMetrics metrics, int itemCount = 0)
    {
        var max = MaxScroll(page, kind, metrics, itemCount);
        var value = double.IsFinite(offset) ? (float)offset : 0;
        return Math.Clamp(value, 0, max);
    }

    /// <summary>Clamps a list scroll offset against the metric row height.</summary>
    private static float ClampListScroll(int count, float rowHeight, float gap, float scrollOffset)
    {
        var max = Math.Max(0, (count * (rowHeight + gap)) - gap - BodyHeight);
        var value = float.IsFinite(scrollOffset) ? scrollOffset : 0;
        return Math.Clamp(value, 0, max);
    }

    /// <summary>Body rectangle of a page (title gutter excluded) at any font scale.</summary>
    public static SKRect Body(SKRect page) => new(page.Left, page.Top + BodyTop, page.Right, page.Bottom);

    /// <summary>
    /// Hit-tests an expanded page's controls with the font-scale aware rectangles, the per-page scroll
    /// offset and the row count, so painted rows and click targets never drift.
    /// </summary>
    public static IslandPageControl HitTest(
        PageSlotKind kind, SKRect pageRect, float x, float y, in FontScaleMetrics metrics, float scrollOffset, int itemCount, out int index)
    {
        index = -1;
        var scrolled = kind is PageSlotKind.Home or PageSlotKind.Media or PageSlotKind.Widgets
            ? new SKRect(pageRect.Left, pageRect.Top - scrollOffset, pageRect.Right, pageRect.Bottom - scrollOffset)
            : pageRect;

        switch (kind)
        {
            case PageSlotKind.Home:
                if (HomeMediaPlay(scrolled, metrics).Contains(x, y))
                {
                    return IslandPageControl.PlayPause;
                }

                if (HomeMediaCard(scrolled, metrics).Contains(x, y))
                {
                    return IslandPageControl.MediaCard;
                }

                for (var i = 0; i < 4; i++)
                {
                    if (HomeToggle(scrolled, i, metrics).Contains(x, y))
                    {
                        index = i;
                        return IslandPageControl.QuickToggle;
                    }
                }

                if (HomeVolumeIcon(scrolled, metrics).Contains(x, y))
                {
                    return IslandPageControl.VolumeMute;
                }

                if (HomeVolumeTrack(scrolled, metrics).InflateY(8).Contains(x, y))
                {
                    return IslandPageControl.VolumeSlider;
                }

                if (HomeBrightnessTrack(scrolled, metrics).InflateY(8).Contains(x, y))
                {
                    return IslandPageControl.BrightnessSlider;
                }

                return IslandPageControl.None;
            case PageSlotKind.Media:
                if (MediaPlay(scrolled, metrics).Contains(x, y))
                {
                    return IslandPageControl.PlayPause;
                }

                if (MediaPrevious(scrolled, metrics).Contains(x, y))
                {
                    return IslandPageControl.Previous;
                }

                if (MediaNext(scrolled, metrics).Contains(x, y))
                {
                    return IslandPageControl.Next;
                }

                if (MediaVolume(scrolled, metrics).Contains(x, y))
                {
                    return IslandPageControl.VolumeMute;
                }

                if (MediaSeekRow(scrolled, metrics).Contains(x, y))
                {
                    return IslandPageControl.Seek;
                }

                if (MediaOutput(scrolled, metrics).Contains(x, y))
                {
                    return IslandPageControl.Output;
                }

                if (MediaQueue(scrolled, metrics).Contains(x, y))
                {
                    return IslandPageControl.Queue;
                }

                return IslandPageControl.None;
            case PageSlotKind.Notifications:
                {
                    var body = Body(pageRect);
                    if (!body.Contains(x, y))
                    {
                        return IslandPageControl.None;
                    }

                    var count = Math.Clamp(itemCount, 0, 64);
                    var scroll = ClampListScroll(count, NotificationRowHeight(metrics), 4, scrollOffset);
                    for (var i = 0; i < count; i++)
                    {
                        if (NotificationDismiss(pageRect, i, scroll, metrics).Contains(x, y))
                        {
                            index = i;
                            return IslandPageControl.Dismiss;
                        }
                    }

                    for (var i = 0; i < count; i++)
                    {
                        if (NotificationRow(pageRect, i, scroll, metrics).Contains(x, y))
                        {
                            index = i;
                            return IslandPageControl.Row;
                        }
                    }

                    return IslandPageControl.Scroll;
                }

            case PageSlotKind.Clipboard:
                {
                    var body = Body(pageRect);
                    if (!body.Contains(x, y))
                    {
                        return IslandPageControl.None;
                    }

                    var count = Math.Clamp(itemCount, 0, 64);
                    var scroll = ClampListScroll(count, ClipboardRowHeight(metrics), 6, scrollOffset);
                    for (var i = 0; i < count; i++)
                    {
                        if (ClipboardPin(pageRect, i, scroll, metrics).Contains(x, y))
                        {
                            index = i;
                            return IslandPageControl.Pin;
                        }

                        if (ClipboardCopy(pageRect, i, scroll, metrics).Contains(x, y))
                        {
                            index = i;
                            return IslandPageControl.Copy;
                        }
                    }

                    for (var i = 0; i < count; i++)
                    {
                        if (ClipboardRow(pageRect, i, scroll, metrics).Contains(x, y))
                        {
                            index = i;
                            return IslandPageControl.Row;
                        }
                    }

                    return IslandPageControl.Scroll;
                }

            case PageSlotKind.Widgets:
                if (PageLayout.WidgetsCalendarNav(scrolled, previous: true).Contains(x, y))
                {
                    return IslandPageControl.CalendarPrevious;
                }

                if (PageLayout.WidgetsCalendarNav(scrolled, previous: false).Contains(x, y))
                {
                    return IslandPageControl.CalendarNext;
                }

                for (var i = 0; i < Math.Clamp(itemCount, 0, MaxPaintedTaskRows); i++)
                {
                    if (WidgetsTaskRow(scrolled, i, metrics).Contains(x, y))
                    {
                        index = i;
                        return IslandPageControl.TaskToggle;
                    }
                }

                return IslandPageControl.None;
            default:
                return IslandPageControl.None;
        }
    }
}

/// <summary>
/// The 8 DIP page grid used by the visual upgrade. These rectangles are deliberately separate from
/// the legacy prototype geometry in <see cref="PageLayout"/>: existing hit-test and font-scale contracts
/// stay stable while the renderer paints the calmer iOS spacing.
/// </summary>
public static class PageVisual
{
    /// <summary>Horizontal page inset (16 DIP).</summary>
    public const float PageInset = 16;

    /// <summary>Header top padding (16 DIP).</summary>
    public const float HeaderTop = 16;

    /// <summary>Header/title line box (16 DIP).</summary>
    public const float HeaderHeight = 16;

    /// <summary>Body starts below the title plus one 8 DIP gap (40 DIP).</summary>
    public const float BodyTop = 40;

    /// <summary>Bottom safe area (16 DIP).</summary>
    public const float FooterSafe = 16;

    /// <summary>Card/list corner radius (12 DIP).</summary>
    public const float CardRadius = 12;

    /// <summary>Unified list row height (44 DIP).</summary>
    public const float RowHeight = 44;

    /// <summary>Unified list row gap (4 DIP).</summary>
    public const float RowGap = 4;

    /// <summary>Header action icon box (20 DIP).</summary>
    public const float ActionIcon = 20;

    /// <summary>Page body after the title and before the bottom safe area.</summary>
    public static SKRect Body(SKRect page) => new(
        page.Left + PageInset, page.Top + BodyTop, page.Right - PageInset, page.Bottom - FooterSafe);

    /// <summary>Page number rectangle (left of the title).</summary>
    public static SKRect PageNumber(SKRect content) => new(
        content.Left, content.Top + HeaderTop + 1, content.Left + 16, content.Top + HeaderTop + HeaderHeight + 1);

    /// <summary>Page title rectangle; reserves the right operation icons.</summary>
    public static SKRect PageTitle(SKRect content) => new(
        content.Left + 20, content.Top + HeaderTop, content.Right - 72, content.Top + HeaderTop + HeaderHeight);

    /// <summary>Settings gear hit/paint box (20 DIP).</summary>
    public static SKRect Settings(SKRect content) => new(
        content.Right - 64, content.Top + HeaderTop, content.Right - 44, content.Top + HeaderTop + ActionIcon);

    /// <summary>Collapse chevron hit/paint box (20 DIP).</summary>
    public static SKRect Collapse(SKRect content) => new(
        content.Right - 40, content.Top + HeaderTop, content.Right - 20, content.Top + HeaderTop + ActionIcon);

    /// <summary>Scrollable list body rectangle.</summary>
    public static SKRect ListBody(SKRect page) => Body(page);

    /// <summary>Unified list row at an index and scroll offset.</summary>
    public static SKRect ListRow(SKRect page, int index, float scrollOffset = 0)
    {
        var body = Body(page);
        var top = body.Top + (index * (RowHeight + RowGap)) - scrollOffset;
        return new SKRect(body.Left, top, body.Right, top + RowHeight);
    }

    /// <summary>Empty-state icon: 28×28, vertically centred with the two text lines.</summary>
    public static SKRect EmptyIcon(SKRect page)
    {
        var body = Body(page);
        var top = body.MidY - 50;
        return new SKRect(page.MidX - 14, top, page.MidX + 14, top + 28);
    }

    /// <summary>Empty-state title: 13 DIP, centred.</summary>
    public static SKRect EmptyTitle(SKRect page)
    {
        var icon = EmptyIcon(page);
        return new SKRect(page.Left + PageInset, icon.Bottom + 8, page.Right - PageInset, icon.Bottom + 28);
    }

    /// <summary>Empty-state hint: 11.5 DIP, centred.</summary>
    public static SKRect EmptyHint(SKRect page)
    {
        var title = EmptyTitle(page);
        return new SKRect(page.Left + PageInset, title.Bottom + 4, page.Right - PageInset, title.Bottom + 20);
    }

    /// <summary>Media artwork on the 8 DIP grid.</summary>
    public static SKRect MediaArt(SKRect page) => new(page.Left + PageInset, page.Top + BodyTop, page.Left + PageInset + 72, page.Top + BodyTop + 72);

    /// <summary>Media eyebrow.</summary>
    public static SKRect MediaEyebrow(SKRect page) => new(page.Left + 112, page.Top + BodyTop, page.Right - PageInset, page.Top + BodyTop + 12);

    /// <summary>Media title.</summary>
    public static SKRect MediaTitle(SKRect page) => new(page.Left + 112, page.Top + BodyTop + 16, page.Right - PageInset, page.Top + BodyTop + 40);

    /// <summary>Media artist.</summary>
    public static SKRect MediaArtist(SKRect page) => new(page.Left + 112, page.Top + BodyTop + 40, page.Right - PageInset, page.Top + BodyTop + 52);

    /// <summary>Media album.</summary>
    public static SKRect MediaAlbum(SKRect page) => new(page.Left + 112, page.Top + BodyTop + 56, page.Right - PageInset, page.Top + BodyTop + 68);

    /// <summary>Media lyric card.</summary>
    public static SKRect MediaLyricCard(SKRect page) => new(page.Left + PageInset, page.Top + BodyTop + 80, page.Right - PageInset, page.Top + BodyTop + 126);

    /// <summary>Media seek row.</summary>
    public static SKRect MediaSeekRow(SKRect page) => new(page.Left + PageInset, page.Top + BodyTop + 134, page.Right - PageInset, page.Top + BodyTop + 154);

    /// <summary>Media seek track.</summary>
    public static SKRect MediaSeekTrack(SKRect page) => new(page.Left + PageInset + 36, page.Top + BodyTop + 143, page.Right - PageInset - 36, page.Top + BodyTop + 146);

    /// <summary>Media previous-track button.</summary>
    public static SKRect MediaPrevious(SKRect page) => new(page.Left + 154, page.Top + BodyTop + 162, page.Left + 182, page.Top + BodyTop + 190);

    /// <summary>Media play/pause button.</summary>
    public static SKRect MediaPlay(SKRect page) => new(page.Left + 185, page.Top + BodyTop + 161, page.Left + 215, page.Top + BodyTop + 191);

    /// <summary>Media next-track button.</summary>
    public static SKRect MediaNext(SKRect page) => new(page.Left + 218, page.Top + BodyTop + 162, page.Left + 246, page.Top + BodyTop + 190);

    /// <summary>Media volume/mute control.</summary>
    public static SKRect MediaVolume(SKRect page) => new(page.Left + PageInset, page.Top + BodyTop + 162, page.Left + PageInset + 66, page.Top + BodyTop + 190);

    /// <summary>Media output control.</summary>
    public static SKRect MediaOutput(SKRect page) => new(page.Left + 88, page.Top + BodyTop + 162, page.Left + 150, page.Top + BodyTop + 190);

    /// <summary>Media queue control.</summary>
    public static SKRect MediaQueue(SKRect page) => new(page.Right - PageInset - 74, page.Top + BodyTop + 162, page.Right - PageInset, page.Top + BodyTop + 190);
}

