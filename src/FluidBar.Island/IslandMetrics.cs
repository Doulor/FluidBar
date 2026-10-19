using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Visual metrics for the island MVP. Shell/host/page numbers come from Core <see cref="LayoutTokens"/>;
/// the expanded page origin follows the approved prototype's 20/12 inset (content 400×252), and the
/// detached strip uses the prototype's 3-island sizes inside Core's 30 DIP strip envelope.
/// </summary>
public static class IslandMetrics
{
    /// <summary>Fixed host surface width (488 DIP).</summary>
    public const double HostWidth = LayoutTokens.HostSurfaceWidth;

    /// <summary>Fixed float/flush host surface height (384 DIP).</summary>
    public const double HostHeightFloat = LayoutTokens.HostSurfaceFloatHeight;

    /// <summary>Fixed dock host surface height (444 DIP).</summary>
    public const double HostHeightDock = LayoutTokens.HostSurfaceDockHeight;

    /// <summary>Expanded shell width (440 DIP).</summary>
    public const double ShellWidth = LayoutTokens.ExpandedWidth;

    /// <summary>Expanded shell height (284 DIP).</summary>
    public const double ShellHeight = LayoutTokens.ExpandedHeight;

    /// <summary>Expanded content left inset inside the shell (20 DIP).</summary>
    public const double ContentLeft = LayoutTokens.ContentHorizontalInset;

    /// <summary>Expanded content top inset inside the shell (12 DIP, prototype expanded layer).</summary>
    public const double ContentTop = 12;

    /// <summary>Expanded content width (400 DIP).</summary>
    public const double ContentWidth = LayoutTokens.ContentWidth;

    /// <summary>Expanded content height (252 DIP).</summary>
    public const double ContentHeight = LayoutTokens.ContentUsableHeight;

    /// <summary>Page body top inside the content rect (28 DIP; the title overlay owns the first row).</summary>
    public const double PageBodyTop = 28;

    /// <summary>Page horizontal inset (16 DIP).</summary>
    public const double PageInset = 16;

    /// <summary>Page usable width (368 DIP).</summary>
    public const double PageUsableWidth = ContentWidth - (2 * PageInset);

    /// <summary>Compact horizontal inset (12 DIP).</summary>
    public const double CompactInset = 12;

    /// <summary>Compact fixed icon slot (20 DIP).</summary>
    public const double CompactIconSlot = 20;

    /// <summary>Compact icon-to-text gap (8 DIP).</summary>
    public const double CompactIconGap = 8;

    /// <summary>Compact text-to-chip gap (8 DIP).</summary>
    public const double CompactChipGap = 8;

    /// <summary>Compact trailing chip height (20 DIP).</summary>
    public const double CompactChipHeight = 20;

    /// <summary>Detached previous/next mini-island width (26 DIP, prototype).</summary>
    public const double MiniNeighbourWidth = 26;

    /// <summary>Detached previous/next mini-island height (26 DIP, prototype).</summary>
    public const double MiniNeighbourHeight = 26;

    /// <summary>Detached current mini-island width (73 DIP, prototype label capsule).</summary>
    public const double MiniCurrentWidth = 73;

    /// <summary>Detached current mini-island height (28 DIP, prototype).</summary>
    public const double MiniCurrentHeight = 28;

    /// <summary>Gap between detached islands (8 DIP).</summary>
    public const double MiniGap = 8;

    /// <summary>Far-page dot diameter (3 DIP).</summary>
    public const double DotSize = 3;

    /// <summary>Gap between far-page dots (4 DIP, prototype).</summary>
    public const double DotGap = 4;

    /// <summary>Dot hit target size (12 DIP; dots are tiny, the hit target is not).</summary>
    public const double DotHitSize = 12;

    /// <summary>Maximum far-page dots per side (3).</summary>
    public const int MaxDotsPerSide = 3;

    /// <summary>Strip envelope height (30 DIP).</summary>
    public const double StripHeight = LayoutTokens.ArchipelagoStripHeight;

    /// <summary>Gap between the expanded shell and the strip (10 DIP).</summary>
    public const double StripGap = LayoutTokens.ArchipelagoGap;

    /// <summary>Host surface size for a mode in DIP.</summary>
    public static VisualSize SurfaceSize(IslandMode mode) => HostSurface.ForMode(mode).Size;

    /// <summary>Host surface size in physical pixels (ceil).</summary>
    public static PixelRect SurfacePixels(IslandMode mode, float dpiScale)
    {
        var size = SurfaceSize(mode);
        var scale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
        return new PixelRect(
            0,
            0,
            Math.Max(1, (int)Math.Ceiling(size.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(size.Height * scale)));
    }

    /// <summary>Shell rectangle (host-local DIP) for a state, including the nudge offset.</summary>
    public static SKRect ShellRect(in IslandRenderState state) => new(
        (float)state.ShellOrigin.X,
        (float)(state.ShellOrigin.Y + state.NudgeOffsetY),
        (float)(state.ShellOrigin.X + state.ShellWidth),
        (float)(state.ShellOrigin.Y + state.NudgeOffsetY + state.ShellHeight));

    /// <summary>Expanded content rectangle (host-local DIP) for a shell rectangle.</summary>
    public static SKRect ContentRect(SKRect shell) => new(
        shell.Left + (float)ContentLeft,
        shell.Top + (float)ContentTop,
        shell.Left + (float)ContentLeft + (float)ContentWidth,
        shell.Top + (float)ContentTop + (float)ContentHeight);

    /// <summary>Page rectangle (host-local DIP) at the continuous pager offset.</summary>
    public static SKRect PageRect(in IslandRenderState state, int page)
    {
        var content = ContentRect(ShellRect(state));
        var offset = (float)(state.Pager.PagePosition * PagerConstants.PagePitchDip);
        var left = content.Left + (page * (float)PagerConstants.PagePitchDip) - offset;
        return new SKRect(left, content.Top, left + (float)PagerConstants.PagePitchDip, content.Bottom);
    }
}

/// <summary>Typography roles for the island (sizes in DIP).</summary>
public static class IslandType
{
    /// <summary>Expanded page title overlay.</summary>
    public static readonly TextStyle PageTitle = new(FontRole.UiSemiBold, 10f);

    /// <summary>Expanded page number ("01").</summary>
    public static readonly TextStyle PageNumber = new(FontRole.UiSemiBold, 8.5f);

    /// <summary>Dashboard clock.</summary>
    public static readonly TextStyle Clock = new(FontRole.DisplayBold, 30f);

    /// <summary>Dashboard date.</summary>
    public static readonly TextStyle Date = new(FontRole.Ui, 10f);

    /// <summary>Compact clock.</summary>
    public static readonly TextStyle CompactClock = new(FontRole.MonoSemiBold, 13.5f);

    /// <summary>Compact title (13.5 DIP).</summary>
    public static readonly TextStyle CompactTitle = new(FontRole.UiSemiBold, 13.5f);

    /// <summary>Compact caption (11 DIP).</summary>
    public static readonly TextStyle CompactCaption = new(FontRole.Ui, 11f);

    /// <summary>Compact small caption (10.5 DIP).</summary>
    public static readonly TextStyle CompactSmall = new(FontRole.Ui, 10.5f);

    /// <summary>Compact small semibold (10.5 DIP).</summary>
    public static readonly TextStyle CompactSmallSemiBold = new(FontRole.UiSemiBold, 10.5f);

    /// <summary>Compact mono value (12.5 DIP).</summary>
    public static readonly TextStyle CompactValue = new(FontRole.MonoSemiBold, 12.5f);

    /// <summary>Compact tiny caption (9.5 DIP).</summary>
    public static readonly TextStyle CompactTiny = new(FontRole.Ui, 9.5f);

    /// <summary>Mini-island label (9 DIP).</summary>
    public static readonly TextStyle MiniLabel = new(FontRole.UiSemiBold, 9f);

    /// <summary>Page clock (dashboard) — same as <see cref="Clock"/>.</summary>
    public static readonly TextStyle PageClock = Clock;

    /// <summary>Media page title.</summary>
    public static readonly TextStyle MediaTitle = new(FontRole.DisplaySemiBold, 20f);

    /// <summary>Media artist.</summary>
    public static readonly TextStyle MediaArtist = new(FontRole.Ui, 11f);

    /// <summary>Media album.</summary>
    public static readonly TextStyle MediaAlbum = new(FontRole.Ui, 9.5f);

    /// <summary>Media eyebrow label.</summary>
    public static readonly TextStyle MediaEyebrow = new(FontRole.UiSemiBold, 8f);

    /// <summary>Media lyric line.</summary>
    public static readonly TextStyle Lyric = new(FontRole.Ui, 11f);

    /// <summary>Media next lyric.</summary>
    public static readonly TextStyle LyricNext = new(FontRole.Ui, 9f);

    /// <summary>Media seek time.</summary>
    public static readonly TextStyle SeekTime = new(FontRole.Mono, 8.5f);

    /// <summary>Home quick toggle label.</summary>
    public static readonly TextStyle Toggle = new(FontRole.Ui, 9.5f);

    /// <summary>Home media card title.</summary>
    public static readonly TextStyle CardTitle = new(FontRole.UiSemiBold, 11.5f);

    /// <summary>Home media card subtitle.</summary>
    public static readonly TextStyle CardSubtitle = new(FontRole.Ui, 9f);

    /// <summary>Home weather temperature.</summary>
    public static readonly TextStyle WeatherTemp = new(FontRole.UiSemiBold, 12.5f);

    /// <summary>Home weather condition.</summary>
    public static readonly TextStyle WeatherCondition = new(FontRole.Ui, 8.5f);

    /// <summary>Slider percentage.</summary>
    public static readonly TextStyle SliderValue = new(FontRole.Ui, 9f);

    /// <summary>Battery line.</summary>
    public static readonly TextStyle BatteryLine = new(FontRole.Ui, 9f);

    /// <summary>Battery percentage (strong).</summary>
    public static readonly TextStyle BatteryValue = new(FontRole.UiSemiBold, 10f);

    /// <summary>Transport output/queue labels.</summary>
    public static readonly TextStyle Output = new(FontRole.Ui, 8.5f);

    /// <summary>Empty-state title.</summary>
    public static readonly TextStyle EmptyTitle = new(FontRole.UiSemiBold, 11f);

    /// <summary>Empty-state hint.</summary>
    public static readonly TextStyle EmptyHint = new(FontRole.Ui, 9f);

    // ---- expanded list/widget pages -------------------------------------------------------

    /// <summary>List row title (notifications/agent/clipboard preview).</summary>
    public static readonly TextStyle RowTitle = new(FontRole.UiSemiBold, 10.5f);

    /// <summary>Uppercase app/tool label in a row head.</summary>
    public static readonly TextStyle RowApp = new(FontRole.UiSemiBold, 8f);

    /// <summary>Row body/secondary line.</summary>
    public static readonly TextStyle RowBody = new(FontRole.Ui, 9.5f);

    /// <summary>Row meta line (time, project, type).</summary>
    public static readonly TextStyle RowMeta = new(FontRole.Ui, 8f);

    /// <summary>Row meta line, semibold (status label).</summary>
    public static readonly TextStyle RowMetaStrong = new(FontRole.UiSemiBold, 8f);

    /// <summary>Small accent chip (verification code).</summary>
    public static readonly TextStyle Chip = new(FontRole.UiSemiBold, 8f);

    /// <summary>Clipboard monospace preview.</summary>
    public static readonly TextStyle RowMono = new(FontRole.Mono, 9.5f);

    /// <summary>Calendar month title.</summary>
    public static readonly TextStyle CalendarMonth = new(FontRole.UiSemiBold, 10f);

    /// <summary>Calendar weekday header.</summary>
    public static readonly TextStyle CalendarWeek = new(FontRole.Ui, 7f);

    /// <summary>Calendar day number.</summary>
    public static readonly TextStyle CalendarDay = new(FontRole.Ui, 8f);

    /// <summary>Calendar day number, semibold (today).</summary>
    public static readonly TextStyle CalendarDayStrong = new(FontRole.UiSemiBold, 8f);

    /// <summary>Calendar footer event label.</summary>
    public static readonly TextStyle CalendarFoot = new(FontRole.Ui, 7.5f);

    /// <summary>Widget temperature.</summary>
    public static readonly TextStyle WidgetTemp = new(FontRole.DisplaySemiBold, 17f);

    /// <summary>Widget condition/description.</summary>
    public static readonly TextStyle WidgetDesc = new(FontRole.Ui, 8f);

    /// <summary>Widget secondary meta (high/low).</summary>
    public static readonly TextStyle WidgetMetaStrong = new(FontRole.UiSemiBold, 7.5f);

    /// <summary>Widget tertiary meta (wind).</summary>
    public static readonly TextStyle WidgetMeta = new(FontRole.Ui, 7.5f);

    /// <summary>System stat label.</summary>
    public static readonly TextStyle StatLabel = new(FontRole.Ui, 7.5f);

    /// <summary>System stat value.</summary>
    public static readonly TextStyle StatValue = new(FontRole.UiSemiBold, 7.5f);

    /// <summary>Task card title.</summary>
    public static readonly TextStyle NotesHead = new(FontRole.UiSemiBold, 8.5f);

    /// <summary>Task row label.</summary>
    public static readonly TextStyle TaskLabel = new(FontRole.Ui, 8.5f);
}

/// <summary>
/// Shared page control geometry (page-local DIP). Draw and hit-test both consume these rectangles so
/// the click target can never drift from the painted control.
/// </summary>
public static class PageLayout
{
    // ---- chrome ---------------------------------------------------------------------------

    /// <summary>Collapse chevron hit rectangle (content-local DIP).</summary>
    public static SKRect CollapseRect(SKRect content) => new(content.Right - 24, content.Top - 4, content.Right, content.Top + 20);

    /// <summary>Page number rectangle (content-local DIP).</summary>
    public static SKRect PageNumberRect(SKRect content) => new(content.Left, content.Top - 4, content.Left + 18, content.Top + 12);

    /// <summary>Page title rectangle (content-local DIP).</summary>
    public static SKRect PageTitleRect(SKRect content) => new(content.Left + 22, content.Top - 4, content.Right - 30, content.Top + 12);

    // ---- home -----------------------------------------------------------------------------

    /// <summary>Home clock rectangle.</summary>
    public static SKRect HomeClock(SKRect page) => new(page.Left, page.Top + 28, page.Left + 172, page.Top + 62);

    /// <summary>Home date rectangle.</summary>
    public static SKRect HomeDate(SKRect page) => new(page.Left, page.Top + 62, page.Left + 172, page.Top + 74);

    /// <summary>Home weather chip rectangle.</summary>
    public static SKRect HomeWeather(SKRect page) => new(page.Left + 300, page.Top + 28, page.Left + 400, page.Top + 62);

    /// <summary>Home quick-toggle rectangle for an index (0..3).</summary>
    public static SKRect HomeToggle(SKRect page, int index)
    {
        const float gap = 6f;
        var width = (400f - (gap * 3)) / 4f;
        var left = page.Left + (index * (width + gap));
        return new SKRect(left, page.Top + 74, left + width, page.Top + 112);
    }

    /// <summary>Home now-playing card rectangle.</summary>
    public static SKRect HomeMediaCard(SKRect page) => new(page.Left, page.Top + 120, page.Left + 400, page.Top + 168);

    /// <summary>Home now-playing card play/pause button.</summary>
    public static SKRect HomeMediaPlay(SKRect page) => new(page.Left + 366, page.Top + 131, page.Left + 392, page.Top + 157);

    /// <summary>Home media progress track.</summary>
    public static SKRect HomeMediaProgress(SKRect page) => new(page.Left + 7, page.Top + 166, page.Left + 393, page.Top + 168);

    /// <summary>Home volume track.</summary>
    public static SKRect HomeVolumeTrack(SKRect page) => new(page.Left + 29, page.Top + 193.5f, page.Left + 140, page.Top + 196.5f);

    /// <summary>Home brightness track.</summary>
    public static SKRect HomeBrightnessTrack(SKRect page) => new(page.Left + 229, page.Top + 193.5f, page.Left + 340, page.Top + 196.5f);

    /// <summary>Home battery row rectangle.</summary>
    public static SKRect HomeBattery(SKRect page) => new(page.Left, page.Top + 222, page.Left + 400, page.Top + 240);

    // ---- media ----------------------------------------------------------------------------

    /// <summary>Media artwork rectangle (92×92).</summary>
    public static SKRect MediaArt(SKRect page) => new(page.Left, page.Top + 28, page.Left + 92, page.Top + 120);

    /// <summary>Media eyebrow rectangle.</summary>
    public static SKRect MediaEyebrow(SKRect page) => new(page.Left + 106, page.Top + 30, page.Left + 376, page.Top + 40);

    /// <summary>Media title rectangle.</summary>
    public static SKRect MediaTitle(SKRect page) => new(page.Left + 106, page.Top + 42, page.Left + 376, page.Top + 68);

    /// <summary>Media artist rectangle.</summary>
    public static SKRect MediaArtist(SKRect page) => new(page.Left + 106, page.Top + 68, page.Left + 376, page.Top + 82);

    /// <summary>Media album rectangle.</summary>
    public static SKRect MediaAlbum(SKRect page) => new(page.Left + 106, page.Top + 84, page.Left + 376, page.Top + 96);

    /// <summary>Media lyric card rectangle.</summary>
    public static SKRect MediaLyricCard(SKRect page) => new(page.Left, page.Top + 128, page.Left + 400, page.Top + 174);

    /// <summary>Media seek row rectangle.</summary>
    public static SKRect MediaSeekRow(SKRect page) => new(page.Left, page.Top + 184, page.Left + 400, page.Top + 206);

    /// <summary>Media seek track (the interactive slider).</summary>
    public static SKRect MediaSeekTrack(SKRect page) => new(page.Left + 39, page.Top + 193.5f, page.Left + 361, page.Top + 196.5f);

    /// <summary>Media previous-track hit rectangle.</summary>
    public static SKRect MediaPrevious(SKRect page) => new(page.Left, page.Top + 219, page.Left + 28, page.Top + 247);

    /// <summary>Media play/pause hit rectangle.</summary>
    public static SKRect MediaPlay(SKRect page) => new(page.Left + 31, page.Top + 218, page.Left + 61, page.Top + 248);

    /// <summary>Media next-track hit rectangle.</summary>
    public static SKRect MediaNext(SKRect page) => new(page.Left + 64, page.Top + 219, page.Left + 92, page.Top + 247);

    /// <summary>Media output button rectangle.</summary>
    public static SKRect MediaOutput(SKRect page) => new(page.Left + 250, page.Top + 220, page.Left + 320, page.Top + 246);

    /// <summary>Media queue button rectangle.</summary>
    public static SKRect MediaQueue(SKRect page) => new(page.Left + 326, page.Top + 220, page.Left + 400, page.Top + 246);

    // ---- shared empty state ---------------------------------------------------------------

    /// <summary>Empty-state icon rectangle.</summary>
    public static SKRect EmptyIcon(SKRect page) => new(page.Left + 186, page.Top + 92, page.Left + 214, page.Top + 120);

    /// <summary>Empty-state title rectangle.</summary>
    public static SKRect EmptyTitle(SKRect page) => new(page.Left + 16, page.Top + 128, page.Right - 16, page.Top + 146);

    /// <summary>Empty-state hint rectangle.</summary>
    public static SKRect EmptyHint(SKRect page) => new(page.Left + 16, page.Top + 150, page.Right - 16, page.Top + 166);

    // ---- expanded list pages (notifications / clipboard / agent) --------------------------

    /// <summary>Scrollable list body height (page body 28..244 DIP).</summary>
    public const float ListBodyHeight = 216;

    /// <summary>Notification row height (40 DIP).</summary>
    public const float NotificationRowHeight = 40;

    /// <summary>Notification row gap (4 DIP).</summary>
    public const float NotificationRowGap = 4;

    /// <summary>Clipboard row height (48 DIP).</summary>
    public const float ClipboardRowHeight = 48;

    /// <summary>Clipboard row gap (6 DIP).</summary>
    public const float ClipboardRowGap = 6;

    /// <summary>Agent row height (48 DIP).</summary>
    public const float AgentRowHeight = 48;

    /// <summary>Agent row gap (6 DIP).</summary>
    public const float AgentRowGap = 6;

    /// <summary>List row corner radius (11 DIP).</summary>
    public const float ListRowRadius = 11;

    /// <summary>Clipboard row action button size (22 DIP).</summary>
    public const float ListActionSize = 22;

    /// <summary>Scrollable list body rectangle (page-local DIP).</summary>
    public static SKRect ListBody(SKRect page) => new(page.Left + 16, page.Top + 28, page.Right - 16, page.Top + 244);

    /// <summary>Notification row rectangle for an index at a scroll offset.</summary>
    public static SKRect NotificationRow(SKRect page, int index, float scrollOffset = 0)
    {
        var top = page.Top + 28 + (index * (NotificationRowHeight + NotificationRowGap)) - scrollOffset;
        return new SKRect(page.Left + 16, top, page.Right - 16, top + NotificationRowHeight);
    }

    /// <summary>Notification dismiss button rectangle.</summary>
    public static SKRect NotificationDismiss(SKRect page, int index, float scrollOffset = 0)
    {
        var row = NotificationRow(page, index, scrollOffset);
        return new SKRect(row.Right - 30, row.Top + 10, row.Right - 10, row.Top + 30);
    }

    /// <summary>Notification verification-code chip rectangle.</summary>
    public static SKRect NotificationCodeChip(SKRect page, int index, float scrollOffset = 0)
    {
        var row = NotificationRow(page, index, scrollOffset);
        return new SKRect(row.Right - 98, row.Top + 21, row.Right - 38, row.Top + 37);
    }

    /// <summary>Clipboard row rectangle for an index at a scroll offset.</summary>
    public static SKRect ClipboardRow(SKRect page, int index, float scrollOffset = 0)
    {
        var top = page.Top + 28 + (index * (ClipboardRowHeight + ClipboardRowGap)) - scrollOffset;
        return new SKRect(page.Left + 16, top, page.Right - 16, top + ClipboardRowHeight);
    }

    /// <summary>Clipboard pin button rectangle.</summary>
    public static SKRect ClipboardPin(SKRect page, int index, float scrollOffset = 0)
    {
        var row = ClipboardRow(page, index, scrollOffset);
        return new SKRect(row.Right - 58, row.Top + 13, row.Right - 36, row.Top + 35);
    }

    /// <summary>Clipboard copy button rectangle.</summary>
    public static SKRect ClipboardCopy(SKRect page, int index, float scrollOffset = 0)
    {
        var row = ClipboardRow(page, index, scrollOffset);
        return new SKRect(row.Right - 34, row.Top + 13, row.Right - 12, row.Top + 35);
    }

    /// <summary>Agent row rectangle for an index at a scroll offset.</summary>
    public static SKRect AgentRow(SKRect page, int index, float scrollOffset = 0)
    {
        var top = page.Top + 28 + (index * (AgentRowHeight + AgentRowGap)) - scrollOffset;
        return new SKRect(page.Left + 16, top, page.Right - 16, top + AgentRowHeight);
    }

    /// <summary>Maximum scroll offset for a list (0 when every row fits).</summary>
    public static float MaxScroll(int itemCount, float rowHeight, float rowGap)
    {
        if (itemCount <= 0)
        {
            return 0;
        }

        var content = (itemCount * (rowHeight + rowGap)) - rowGap;
        return Math.Max(0, content - ListBodyHeight);
    }

    /// <summary>Clamps a requested scroll offset to the list range.</summary>
    public static float ClampScroll(double offset, int itemCount, float rowHeight, float rowGap)
    {
        var max = MaxScroll(itemCount, rowHeight, rowGap);
        var value = double.IsFinite(offset) ? (float)offset : 0;
        return Math.Clamp(value, 0, max);
    }

    /// <summary>Index of the first row that intersects the list body at a scroll offset.</summary>
    public static int FirstVisibleRow(float scrollOffset, float rowHeight, float rowGap)
        => scrollOffset <= 0 ? 0 : (int)Math.Floor(scrollOffset / (rowHeight + rowGap));

    // ---- widgets page ---------------------------------------------------------------------

    /// <summary>Calendar card rectangle (200 DIP wide, fills the body band).</summary>
    public static SKRect WidgetsCalendar(SKRect page) => new(page.Left + 16, page.Top + 28, page.Left + 216, page.Top + 244);

    /// <summary>Right widget column rectangle (160 DIP wide).</summary>
    public static SKRect WidgetsSide(SKRect page) => new(page.Left + 224, page.Top + 28, page.Right - 16, page.Top + 244);

    /// <summary>Weather card rectangle (54 DIP high).</summary>
    public static SKRect WidgetsWeather(SKRect page) => new(page.Left + 224, page.Top + 28, page.Right - 16, page.Top + 82);

    /// <summary>System stats card rectangle (74 DIP high).</summary>
    public static SKRect WidgetsStats(SKRect page) => new(page.Left + 224, page.Top + 88, page.Right - 16, page.Top + 162);

    /// <summary>Task card rectangle (76 DIP high).</summary>
    public static SKRect WidgetsNotes(SKRect page) => new(page.Left + 224, page.Top + 168, page.Right - 16, page.Top + 244);

    /// <summary>Calendar month navigation button rectangle (18 DIP square).</summary>
    public static SKRect WidgetsCalendarNav(SKRect page, bool previous)
        => new(previous ? page.Left + 170 : page.Left + 190, page.Top + 37, previous ? page.Left + 188 : page.Left + 208, page.Top + 55);

    /// <summary>Calendar grid cell rectangle for a Monday-first cell index (0..41).</summary>
    public static SKRect WidgetsCalendarCell(SKRect page, int index)
    {
        const float cellWidth = 180f / 7f;
        var column = index % 7;
        var row = index / 7;
        var left = page.Left + 26 + (column * cellWidth);
        var top = page.Top + 74 + (row * 22);
        return new SKRect(left, top, left + cellWidth, top + 22);
    }

    /// <summary>Task row rectangle for an index (up to three rows).</summary>
    public static SKRect WidgetsTaskRow(SKRect page, int index)
        => new(page.Left + 234, page.Top + 192 + (index * 15), page.Right - 26, page.Top + 207 + (index * 15));

    /// <summary>Hit-tests an expanded page's controls; returns the control and page-relative index.</summary>
    public static IslandPageControl HitTest(int page, SKRect pageRect, float x, float y, out int index)
        => HitTest(page, pageRect, x, y, out index, 0, int.MaxValue);

    /// <summary>
    /// Hit-tests an expanded page's controls with the list scroll offset and row count used by the
    /// renderer, so click targets can never drift from the painted rows.
    /// </summary>
    public static IslandPageControl HitTest(
        int page,
        SKRect pageRect,
        float x,
        float y,
        out int index,
        float scrollOffset,
        int itemCount)
    {
        index = -1;
        switch (page)
        {
            case 0:
                if (HomeMediaPlay(pageRect).Contains(x, y))
                {
                    return IslandPageControl.PlayPause;
                }

                if (HomeMediaCard(pageRect).Contains(x, y))
                {
                    return IslandPageControl.MediaCard;
                }

                for (var i = 0; i < 4; i++)
                {
                    if (HomeToggle(pageRect, i).Contains(x, y))
                    {
                        index = i;
                        return IslandPageControl.QuickToggle;
                    }
                }

                if (HomeVolumeTrack(pageRect).InflateY(8).Contains(x, y))
                {
                    return IslandPageControl.VolumeSlider;
                }

                if (HomeBrightnessTrack(pageRect).InflateY(8).Contains(x, y))
                {
                    return IslandPageControl.BrightnessSlider;
                }

                return IslandPageControl.None;
            case 1:
                if (MediaPlay(pageRect).Contains(x, y))
                {
                    return IslandPageControl.PlayPause;
                }

                if (MediaPrevious(pageRect).Contains(x, y))
                {
                    return IslandPageControl.Previous;
                }

                if (MediaNext(pageRect).Contains(x, y))
                {
                    return IslandPageControl.Next;
                }

                if (MediaSeekRow(pageRect).Contains(x, y))
                {
                    return IslandPageControl.Seek;
                }

                if (MediaOutput(pageRect).Contains(x, y))
                {
                    return IslandPageControl.Output;
                }

                if (MediaQueue(pageRect).Contains(x, y))
                {
                    return IslandPageControl.Queue;
                }

                return IslandPageControl.None;
            case 2:
                {
                    var body = ListBody(pageRect);
                    if (!body.Contains(x, y))
                    {
                        return IslandPageControl.None;
                    }

                    var count = Math.Clamp(itemCount, 0, 64);
                    var scroll = ClampScroll(scrollOffset, count, NotificationRowHeight, NotificationRowGap);
                    for (var i = 0; i < count; i++)
                    {
                        if (NotificationDismiss(pageRect, i, scroll).Contains(x, y))
                        {
                            index = i;
                            return IslandPageControl.Dismiss;
                        }
                    }

                    for (var i = 0; i < count; i++)
                    {
                        if (NotificationRow(pageRect, i, scroll).Contains(x, y))
                        {
                            index = i;
                            return IslandPageControl.Row;
                        }
                    }

                    return IslandPageControl.Scroll;
                }

            case 3:
                {
                    var body = ListBody(pageRect);
                    if (!body.Contains(x, y))
                    {
                        return IslandPageControl.None;
                    }

                    var count = Math.Clamp(itemCount, 0, 64);
                    var scroll = ClampScroll(scrollOffset, count, ClipboardRowHeight, ClipboardRowGap);
                    for (var i = 0; i < count; i++)
                    {
                        if (ClipboardPin(pageRect, i, scroll).Contains(x, y))
                        {
                            index = i;
                            return IslandPageControl.Pin;
                        }

                        if (ClipboardCopy(pageRect, i, scroll).Contains(x, y))
                        {
                            index = i;
                            return IslandPageControl.Copy;
                        }
                    }

                    for (var i = 0; i < count; i++)
                    {
                        if (ClipboardRow(pageRect, i, scroll).Contains(x, y))
                        {
                            index = i;
                            return IslandPageControl.Row;
                        }
                    }

                    return IslandPageControl.Scroll;
                }

            case 4:
                {
                    var body = ListBody(pageRect);
                    if (!body.Contains(x, y))
                    {
                        return IslandPageControl.None;
                    }

                    var count = Math.Clamp(itemCount, 0, 64);
                    var scroll = ClampScroll(scrollOffset, count, AgentRowHeight, AgentRowGap);
                    for (var i = 0; i < count; i++)
                    {
                        if (AgentRow(pageRect, i, scroll).Contains(x, y))
                        {
                            index = i;
                            return IslandPageControl.Row;
                        }
                    }

                    return IslandPageControl.Scroll;
                }

            case 5:
                if (WidgetsCalendarNav(pageRect, previous: true).Contains(x, y))
                {
                    return IslandPageControl.CalendarPrevious;
                }

                if (WidgetsCalendarNav(pageRect, previous: false).Contains(x, y))
                {
                    return IslandPageControl.CalendarNext;
                }

                for (var i = 0; i < Math.Clamp(itemCount, 0, 3); i++)
                {
                    if (WidgetsTaskRow(pageRect, i).Contains(x, y))
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
