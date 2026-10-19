using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Expanded page renderer: the approved prototype's home (clock/weather/toggles/now-playing/sliders/
/// battery) and media (artwork/metadata/lyrics/seek/transport) pages at the 400×260 content size, plus
/// a compact placeholder for the remaining pages and the shared title/collapse chrome.
/// </summary>
internal sealed partial class PageRenderer : IDisposable
{
    private readonly ScaledText _text;
    private readonly AccentPalette _accents;
    private readonly ArtShaderCache _art;
    private readonly ArtworkImageCache _artwork;
    private readonly NumberTextCache _numbers;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true };
    private readonly SKPaint _icon = new() { IsAntialias = true };

    private static readonly string[] PageNumbers = ["01", "02", "03", "04", "05", "06"];

    private IslandTheme _theme = IslandTheme.Dark;
    private float _opacity = 1;
    private FontScaleMetrics _metrics = FontScaleMetrics.One;

    public PageRenderer(TextEngine text, AccentPalette accents, ArtShaderCache art, ArtworkImageCache artwork,
        NumberTextCache numbers)
    {
        _text = new ScaledText(text);
        _accents = accents;
        _art = art;
        _artwork = artwork;
        _numbers = numbers;
    }

    /// <summary>Draws the title/collapse chrome for the committed page.</summary>
    public void DrawChrome(SKCanvas canvas, in IslandRenderState state, SKRect content, float opacity)
    {
        _theme = state.Theme;
        _opacity = opacity;
        _text.SetScale(state.FontScale);
        _metrics = _text.Metrics;
        if (_opacity <= 0.01f)
        {
            return;
        }

        // Prototype2 `.pager-title`: page number 8.5 DIP @ 32 % white, title 10 DIP @ 72 % white, both
        // left aligned at the content inset (left 20 / top 11).
        var page = Math.Clamp(state.PageIndex, 0, PageNumbers.Length - 1);
        _text.Draw(canvas, PageNumbers[page], IslandType.PageNumber, PageLayout.PageNumberRect(content),
            _theme.Foreground.WithAlpha(82), TextAlign.Left, _opacity);
        _text.Draw(canvas, state.Pages.Title(page), IslandType.PageTitle, PageLayout.PageTitleRect(content),
            _theme.Foreground.WithAlpha(184), TextAlign.Left, _opacity);

        // The island had no in-island route to the settings window (only the global hotkey, which
        // synthetic/keyboard-injection scenarios never deliver). The header now carries the standard
        // gear next to the collapse chevron on every page; the same rectangle is the click target and
        // the UI Automation button, so pointer and screen-reader users find one control, not two.
        IconSet.Draw(canvas, IconKind.Settings, PageLayout.SettingsRect(content).Deflate(5.5f),
            Alpha(_theme.Foreground.WithAlpha(128), _opacity), _icon);
        IconSet.Draw(canvas, IconKind.ChevronDown, PageLayout.CollapseRect(content).Deflate(5),
            Alpha(_theme.Foreground.WithAlpha(128), _opacity), _icon);
    }

    /// <summary>Draws one page at <paramref name="pageRect"/> (host-local DIP).</summary>
    public void DrawPage(SKCanvas canvas, in IslandRenderState state, int page, SKRect pageRect, float opacity)
    {
        _theme = state.Theme;
        _opacity = opacity;
        _text.SetScale(state.FontScale);
        _metrics = _text.Metrics;
        if (_opacity <= 0.01f)
        {
            return;
        }

        switch (page)
        {
            case 0:
                DrawHome(canvas, state.Pages.Home, pageRect);
                break;
            case 1:
                DrawMedia(canvas, state.Pages.Media, pageRect);
                break;
            case 2:
                DrawNotifications(canvas, state.Pages.Notifications, pageRect);
                break;
            case 3:
                DrawClipboard(canvas, state.Pages.Clipboard, pageRect);
                break;
            case 4:
                DrawAgent(canvas, state.Pages.Agent, pageRect);
                break;
            case 5:
                DrawWidgets(canvas, state.Pages.Widgets, pageRect);
                break;
            default:
                DrawPlaceholder(canvas, state.Pages.Title(page), pageRect);
                break;
        }
    }

    // ------------------------------------------------------------------- home

    private void DrawHome(SKCanvas canvas, HomePageModel home, SKRect page)
    {
        var m = _metrics;
        _text.Draw(canvas, home.ClockText ?? "00:00", IslandType.Clock, PageMetricsLayout.HomeClock(page, m),
            _theme.Foreground, TextAlign.Left, _opacity);
        _text.Draw(canvas, home.DateText, IslandType.Date, PageMetricsLayout.HomeDate(page, m),
            _theme.ForegroundSecondary, TextAlign.Left, _opacity);

        if (home.ShowWeather && (!string.IsNullOrEmpty(home.WeatherTemp) || !string.IsNullOrEmpty(home.WeatherCondition)))
        {
            DrawWeather(canvas, home, page, m);
        }

        DrawToggles(canvas, home, page, m);
        DrawNowPlaying(canvas, home.NowPlaying, page, m);
        DrawSliders(canvas, home, page, m);
        DrawBattery(canvas, home, page, m);
    }

    private void DrawWeather(SKCanvas canvas, HomePageModel home, SKRect page, in FontScaleMetrics m)
    {
        var rect = PageMetricsLayout.HomeWeather(page, m);
        DrawCard(canvas, rect, 10);
        var iconTop = rect.Top + 9;
        IconSet.Draw(canvas, IconKind.CloudSun, new SKRect(rect.Left + 9, iconTop, rect.Left + 25, iconTop + 16),
            Alpha(new SKColor(0xF4, 0xC4, 0x6C), _opacity), _icon);
        var tempTop = rect.Top + 5;
        var tempBottom = tempTop + 14 + m.LineGrow(IslandType.WeatherTemp);
        _text.Draw(canvas, home.WeatherTemp, IslandType.WeatherTemp,
            new SKRect(rect.Left + 30, tempTop, rect.Right - 6, tempBottom), _theme.Foreground, TextAlign.Left, _opacity);
        var conditionBottom = tempBottom + 11 + m.LineGrow(IslandType.WeatherCondition);
        _text.Draw(canvas, home.WeatherCondition, IslandType.WeatherCondition,
            new SKRect(rect.Left + 30, tempBottom - 1, rect.Right - 6, conditionBottom),
            _theme.ForegroundTertiary, TextAlign.Left, _opacity);
    }

    private void DrawToggles(SKCanvas canvas, HomePageModel home, SKRect page, in FontScaleMetrics m)
    {
        var toggles = home.Toggles;
        var count = toggles is null ? 0 : Math.Min(toggles.Count, 4);
        for (var i = 0; i < count; i++)
        {
            var toggle = toggles![i];
            var rect = PageMetricsLayout.HomeToggle(page, i, m);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(toggle.IsOn ? _theme.Accent : _theme.BackgroundRaised, toggle.IsOn ? 0.10f * _opacity : _opacity);
            canvas.DrawRoundRect(rect, 10, 10, _fill);

            var labelWidth = Math.Min(rect.Width - 30, _text.Measure(toggle.Label, IslandType.Toggle));
            var contentWidth = 14 + 5 + labelWidth;
            var startX = rect.Left + ((rect.Width - contentWidth) / 2);
            IconSet.Draw(canvas, toggle.Icon, new SKRect(startX, rect.MidY - 7, startX + 14, rect.MidY + 7),
                Alpha(toggle.IsOn ? _theme.Accent : _theme.ForegroundTertiary, _opacity), _icon);
            _text.Draw(canvas, toggle.Label, IslandType.Toggle,
                new SKRect(startX + 19, rect.Top, rect.Right - 4, rect.Bottom),
                toggle.IsOn ? _theme.Foreground : _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        }
    }

    private void DrawNowPlaying(SKCanvas canvas, MediaSummaryModel media, SKRect page, in FontScaleMetrics m)
    {
        var rect = PageMetricsLayout.HomeMediaCard(page, m);
        DrawCard(canvas, rect, 11);

        var accent = _accents.Get(media.AccentHex, _theme.TrackAccent);
        DrawArt(canvas, new SKRect(rect.Left + 7, rect.Top + 8, rect.Left + 39, rect.Top + 40), 8, accent, large: false,
            media.Artwork, media.ArtworkKey);

        var textRight = rect.Left + 366 - 6;
        var titleTop = rect.Top + 6;
        var titleBottom = titleTop + 16 + m.LineGrow(IslandType.CardTitle);
        _text.Draw(canvas, media.Title, IslandType.CardTitle,
            new SKRect(rect.Left + 48, titleTop, textRight, titleBottom), _theme.Foreground, TextAlign.Left, _opacity);
        _text.Draw(canvas, media.Subtitle, IslandType.CardSubtitle,
            new SKRect(rect.Left + 48, titleBottom, textRight, titleBottom + 14 + m.LineGrow(IslandType.CardSubtitle)),
            _theme.ForegroundSecondary, TextAlign.Left, _opacity);

        var play = PageMetricsLayout.HomeMediaPlay(page, m);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.LineStrong, _opacity);
        canvas.DrawCircle(play.MidX, play.MidY, play.Width / 2, _fill);
        IconSet.Draw(canvas, media.IsPlaying ? IconKind.Pause : IconKind.Play, play.Deflate(7),
            Alpha(_theme.Foreground, _opacity), _icon);

        if (media.HasTimeline)
        {
            DrawTrack(canvas, PageMetricsLayout.HomeMediaProgress(page, m), media.ProgressPercent / 100.0, accent, _theme.HudTrack);
        }
    }

    private void DrawSliders(SKCanvas canvas, HomePageModel home, SKRect page, in FontScaleMetrics m)
    {
        var rect = PageMetricsLayout.HomeSliders(page, m);
        DrawCard(canvas, rect, 11);
        var iconTop = rect.MidY - 6.5f;

        IconSet.Draw(canvas, home.IsMuted ? IconKind.VolumeMute : IconKind.Volume,
            new SKRect(rect.Left + 10, iconTop, rect.Left + 23, iconTop + 13),
            Alpha(_theme.ForegroundTertiary, _opacity), _icon);
        DrawTrack(canvas, PageMetricsLayout.HomeVolumeTrack(page, m), home.HasVolume ? home.Volume / 100.0 : 0,
            home.IsMuted || !home.HasVolume ? _theme.ForegroundTertiary : _theme.Foreground, _theme.LineStrong);
        _text.Draw(canvas, home.HasVolume ? _numbers.Number(home.Volume) : "—", IslandType.SliderValue,
            new SKRect(rect.Left + 145, rect.MidY - 6, rect.Left + 167, rect.MidY + 6), _theme.ForegroundTertiary,
            TextAlign.Right, _opacity);

        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.Line, _opacity);
        canvas.DrawRect(new SKRect(rect.Left + 200, rect.MidY - 8, rect.Left + 201, rect.MidY + 8), _fill);

        IconSet.Draw(canvas, IconKind.Brightness,
            new SKRect(rect.Left + 210, iconTop, rect.Left + 223, iconTop + 13),
            Alpha(_theme.ForegroundTertiary, _opacity), _icon);
        DrawTrack(canvas, PageMetricsLayout.HomeBrightnessTrack(page, m), home.HasBrightness ? home.Brightness / 100.0 : 0,
            home.HasBrightness ? _theme.Foreground : _theme.ForegroundTertiary, _theme.LineStrong);
        _text.Draw(canvas, home.HasBrightness ? _numbers.Number(home.Brightness) : "—", IslandType.SliderValue,
            new SKRect(rect.Left + 345, rect.MidY - 6, rect.Left + 367, rect.MidY + 6), _theme.ForegroundTertiary,
            TextAlign.Right, _opacity);
    }

    private void DrawBattery(SKCanvas canvas, HomePageModel home, SKRect page, in FontScaleMetrics m)
    {
        var rect = PageMetricsLayout.HomeBattery(page, m);
        var level = Math.Clamp(home.BatteryPercent, 0, 100);
        var tone = home.IsCharging ? _theme.Success : _theme.Danger;
        var iconTop = rect.MidY - 5;

        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = 1;
        _stroke.Color = Alpha(_theme.LineStrong, _opacity);
        canvas.DrawRoundRect(new SKRect(rect.Left + 2, iconTop, rect.Left + 23, iconTop + 10), 3, 3, _stroke);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.LineStrong, _opacity);
        canvas.DrawRect(new SKRect(rect.Left + 23, iconTop + 3, rect.Left + 24.5f, iconTop + 7), _fill);
        _fill.Color = Alpha(tone, _opacity);
        canvas.DrawRoundRect(
            new SKRect(rect.Left + 3.5f, iconTop + 1.5f, rect.Left + 3.5f + (18 * level / 100f), iconTop + 8.5f), 1, 1, _fill);

        _text.Draw(canvas, _numbers.Percent(level), IslandType.BatteryValue,
            new SKRect(rect.Left + 27, rect.Top, rect.Left + 57, rect.Bottom), _theme.Foreground, TextAlign.Left, _opacity);
        _text.Draw(canvas, "·", IslandType.BatteryLine,
            new SKRect(rect.Left + 60, rect.Top, rect.Left + 66, rect.Bottom), _theme.ForegroundTertiary, TextAlign.Left, _opacity);
        _text.Draw(canvas, home.BatteryStatus, IslandType.BatteryLine,
            new SKRect(rect.Left + 68, rect.Top, rect.Left + 260, rect.Bottom), _theme.ForegroundTertiary, TextAlign.Left, _opacity);
        _text.Draw(canvas, home.BatteryRemaining, IslandType.BatteryLine,
            new SKRect(rect.Left + 300, rect.Top, rect.Right, rect.Bottom), _theme.ForegroundTertiary, TextAlign.Right, _opacity);
    }

    // ------------------------------------------------------------------ media

    private void DrawMedia(SKCanvas canvas, MediaPageModel media, SKRect page)
    {
        if (!media.HasSession)
        {
            DrawEmpty(canvas, IconKind.Media, media.EmptyTitle ?? "Nothing playing", media.EmptyHint ?? "Start a track to see it here", page);
            return;
        }

        var m = _metrics;
        var accent = _accents.Get(media.AccentHex, _theme.TrackAccent);
        DrawArt(canvas, PageMetricsLayout.MediaArt(page), 14, accent, large: true, media.Artwork, media.ArtworkKey);

        _text.Draw(canvas, media.Eyebrow, IslandType.MediaEyebrow, PageMetricsLayout.MediaEyebrow(page, m),
            _theme.ForegroundTertiary, TextAlign.Left, _opacity);
        _text.Draw(canvas, media.Title, IslandType.MediaTitle, PageMetricsLayout.MediaTitle(page, m),
            _theme.Foreground, TextAlign.Left, _opacity);
        _text.Draw(canvas, media.Artist, IslandType.MediaArtist, PageMetricsLayout.MediaArtist(page, m),
            _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        _text.Draw(canvas, media.Album, IslandType.MediaAlbum, PageMetricsLayout.MediaAlbum(page, m),
            _theme.ForegroundTertiary, TextAlign.Left, _opacity);

        // The lyric card only makes sense when a lyric line exists; an empty card with just the
        // "Next" label looked broken for video sessions without lyrics.
        if (!string.IsNullOrWhiteSpace(media.LyricLine) || !string.IsNullOrWhiteSpace(media.NextLyricLine))
        {
            var lyricCard = PageMetricsLayout.MediaLyricCard(page, m);
            DrawCard(canvas, lyricCard, 10);
            var lyricTop = lyricCard.Top + 7;
            var lyricBottom = lyricTop + 14 + m.LineGrow(IslandType.Lyric);
            _text.Draw(canvas, media.LyricLine, IslandType.Lyric,
                new SKRect(lyricCard.Left + 10, lyricTop, lyricCard.Right - 10, lyricBottom),
                _theme.Foreground, TextAlign.Left, _opacity);
            var nextTop = lyricBottom + 3;
            var nextBottom = nextTop + 12 + m.LineGrow(IslandType.LyricNext);
            _text.Draw(canvas, media.NextLyricLabel, IslandType.MediaEyebrow,
                new SKRect(lyricCard.Left + 10, nextTop, lyricCard.Left + 40, nextBottom),
                _theme.ForegroundTertiary, TextAlign.Left, _opacity);
            _text.Draw(canvas, media.NextLyricLine, IslandType.LyricNext,
                new SKRect(lyricCard.Left + 42, nextTop, lyricCard.Right - 10, nextBottom),
                _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        }

        // Window-title fallback sources have no track length; the player still reports the position,
        // so the elapsed time is drawn without a progress track instead of hiding the row entirely.
        var seekRow = PageMetricsLayout.MediaSeekRow(page, m);
        var seekTextHeight = 16 + m.LineGrow(IslandType.SeekTime);
        var seekTextTop = seekRow.MidY - (seekTextHeight / 2);
        var seekTimeWidth = PageMetricsLayout.MediaSeekTimeWidth(m);
        if (media.HasTimeline)
        {
            var seek = PageMetricsLayout.MediaSeekTrack(page, m);
            var progress = Math.Clamp(media.ProgressPercent / 100.0, 0, 1);
            DrawTrack(canvas, seek, progress, _theme.Foreground, _theme.LineStrong);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(_theme.Foreground, _opacity);
            canvas.DrawCircle(seek.Left + (float)(seek.Width * progress), seek.MidY, 4, _fill);

            _text.Draw(canvas, media.PositionText, IslandType.SeekTime,
                new SKRect(seekRow.Left, seekTextTop, seekRow.Left + seekTimeWidth, seekTextTop + seekTextHeight),
                _theme.ForegroundTertiary, TextAlign.Left, _opacity);
            _text.Draw(canvas, media.DurationText, IslandType.SeekTime,
                new SKRect(seekRow.Right - seekTimeWidth, seekTextTop, seekRow.Right, seekTextTop + seekTextHeight),
                _theme.ForegroundTertiary, TextAlign.Right, _opacity);
        }
        else if (!string.IsNullOrEmpty(media.PositionText) && media.PositionText != "—")
        {
            _text.Draw(canvas, media.PositionText, IslandType.SeekTime,
                new SKRect(seekRow.Left, seekTextTop, seekRow.Left + seekTimeWidth, seekTextTop + seekTextHeight),
                _theme.ForegroundTertiary, TextAlign.Left, _opacity);
            _text.Draw(canvas, "—", IslandType.SeekTime,
                new SKRect(seekRow.Right - seekTimeWidth, seekTextTop, seekRow.Right, seekTextTop + seekTextHeight),
                _theme.ForegroundTertiary, TextAlign.Right, _opacity);
        }

        DrawTransport(canvas, media, page);

        // The output label only exists for sources that expose an audio endpoint; drawing the bare
        // speaker glyph without it left an orphan icon floating in the transport row.
        if (!string.IsNullOrWhiteSpace(media.OutputName))
        {
            var output = PageMetricsLayout.MediaOutput(page, m);
            IconSet.Draw(canvas, IconKind.Speaker,
                new SKRect(output.Left + 4, output.MidY - 6, output.Left + 16, output.MidY + 6),
                Alpha(_theme.ForegroundTertiary, _opacity), _icon);
            _text.Draw(canvas, media.OutputName, IslandType.Output,
                new SKRect(output.Left + 20, output.Top, output.Left + 70, output.Bottom), _theme.ForegroundTertiary,
                TextAlign.Left, _opacity);
        }

        if (media.QueueCount > 0)
        {
            var queue = PageMetricsLayout.MediaQueue(page, m);
            IconSet.Draw(canvas, IconKind.Queue,
                new SKRect(queue.Left + 4, queue.MidY - 6, queue.Left + 16, queue.MidY + 6),
                Alpha(_theme.ForegroundTertiary, _opacity), _icon);
            _text.Draw(canvas, "Queue", IslandType.Output,
                new SKRect(queue.Left + 20, queue.Top, queue.Left + 52, queue.Bottom), _theme.ForegroundTertiary,
                TextAlign.Left, _opacity);
            _text.Draw(canvas, _numbers.Number(media.QueueCount), IslandType.Output,
                new SKRect(queue.Left + 52, queue.Top, queue.Right, queue.Bottom), _theme.ForegroundTertiary,
                TextAlign.Right, _opacity);
        }
    }

    private void DrawTransport(SKCanvas canvas, MediaPageModel media, SKRect page)
    {
        var previous = PageMetricsLayout.MediaPrevious(page, _metrics);
        var play = PageMetricsLayout.MediaPlay(page, _metrics);
        var next = PageMetricsLayout.MediaNext(page, _metrics);

        IconSet.Draw(canvas, IconKind.Previous, previous.Deflate(7), Alpha(_theme.ForegroundSecondary, _opacity), _icon);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.Foreground, 0.92f * _opacity);
        canvas.DrawCircle(play.MidX, play.MidY, 15, _fill);
        IconSet.Draw(canvas, media.IsPlaying ? IconKind.Pause : IconKind.Play, play.Deflate(8),
            Alpha(_theme.Background, _opacity), _icon);
        IconSet.Draw(canvas, IconKind.Next, next.Deflate(7), Alpha(_theme.ForegroundSecondary, _opacity), _icon);
    }

    // ------------------------------------------------------------- placeholder

    private void DrawPlaceholder(SKCanvas canvas, string title, SKRect page)
    {
        IconSet.Draw(canvas, IconKind.Grid, PageLayout.EmptyIcon(page), Alpha(_theme.ForegroundTertiary, _opacity), _icon);
        _text.Draw(canvas, title, IslandType.EmptyTitle, PageLayout.EmptyTitle(page),
            _theme.ForegroundSecondary, TextAlign.Center, _opacity);
        _text.Draw(canvas, "Page content arrives in a later milestone", IslandType.EmptyHint,
            PageLayout.EmptyHint(page), _theme.ForegroundTertiary, TextAlign.Center, _opacity);
    }

    private void DrawEmpty(SKCanvas canvas, IconKind icon, string title, string hint, SKRect page)
    {
        IconSet.Draw(canvas, icon, PageLayout.EmptyIcon(page), Alpha(_theme.ForegroundTertiary, _opacity), _icon);
        _text.Draw(canvas, title, IslandType.EmptyTitle, PageLayout.EmptyTitle(page),
            _theme.ForegroundSecondary, TextAlign.Center, _opacity);
        _text.Draw(canvas, hint, IslandType.EmptyHint, PageLayout.EmptyHint(page),
            _theme.ForegroundTertiary, TextAlign.Center, _opacity);
    }

    // ---------------------------------------------------------------- helpers

    private void DrawCard(SKCanvas canvas, SKRect rect, float radius)
    {
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.BackgroundRaised, _opacity);
        canvas.DrawRoundRect(rect, radius, radius, _fill);
    }

    private void DrawArt(SKCanvas canvas, SKRect rect, float radius, SKColor accent, bool large,
        byte[]? artwork = null, string? artworkKey = null)
    {
        // Real cover art when the platform supplied bytes; otherwise the accent gradient placeholder.
        var image = _artwork.Get(artworkKey, artwork);
        if (image is not null && image.Width > 0 && image.Height > 0)
        {
            var coverScale = Math.Max(rect.Width / image.Width, rect.Height / image.Height);
            var width = image.Width * coverScale;
            var height = image.Height * coverScale;
            var destination = new SKRect(
                rect.MidX - (width / 2),
                rect.MidY - (height / 2),
                rect.MidX + (width / 2),
                rect.MidY + (height / 2));
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(rect, radius, radius), SKClipOperation.Intersect, antialias: true);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            _fill.Color = SKColors.White.WithAlpha((byte)Math.Clamp(_opacity * 255, 0, 255));
            canvas.DrawImage(image, destination, _fill);
            canvas.Restore();
            return;
        }

        canvas.Save();
        canvas.Translate(rect.Left, rect.Top);
        var shader = _art.Get(accent, _theme.ArtEnd, large);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Shader = shader;
        _fill.Color = SKColors.White.WithAlpha((byte)Math.Clamp(_opacity * 255, 0, 255));
        canvas.DrawRoundRect(new SKRect(0, 0, rect.Width, rect.Height), radius, radius, _fill);
        _fill.Shader = null;
        IconSet.Draw(canvas, IconKind.Media,
            new SKRect(rect.Width * 0.28f, rect.Height * 0.28f, rect.Width * 0.72f, rect.Height * 0.72f),
            SKColors.White.WithAlpha((byte)Math.Clamp(204 * _opacity, 0, 255)), _icon);
        canvas.Restore();
    }

    private void DrawTrack(SKCanvas canvas, SKRect track, double fraction, SKColor accent, SKColor background)
    {
        var radius = track.Height / 2;
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(background, _opacity);
        canvas.DrawRoundRect(track, radius, radius, _fill);
        var clamped = Math.Clamp(fraction, 0, 1);
        if (clamped > 0)
        {
            var width = Math.Max(track.Height, (float)(track.Width * clamped));
            _fill.Color = Alpha(accent, _opacity);
            canvas.DrawRoundRect(new SKRect(track.Left, track.Top, track.Left + width, track.Bottom), radius, radius, _fill);
        }
    }

    private static SKColor Alpha(SKColor color, float opacity)
        => opacity >= 0.999f
            ? color
            : color.WithAlpha((byte)Math.Clamp(color.Alpha * opacity, 0, 255));

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _icon.Dispose();
    }
}