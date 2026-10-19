using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Expanded page renderer: the approved prototype's home (clock/weather/toggles/now-playing/sliders/
/// battery) and media (artwork/metadata/lyrics/seek/transport) pages at the 400×252 content size, plus
/// a compact placeholder for the remaining pages and the shared title/collapse chrome.
/// </summary>
internal sealed partial class PageRenderer : IDisposable
{
    private readonly TextEngine _text;
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

    public PageRenderer(TextEngine text, AccentPalette accents, ArtShaderCache art, ArtworkImageCache artwork,
        NumberTextCache numbers)
    {
        _text = text;
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
        if (_opacity <= 0.01f)
        {
            return;
        }

        var page = Math.Clamp(state.PageIndex, 0, PageNumbers.Length - 1);
        _text.Draw(canvas, PageNumbers[page], IslandType.PageNumber, PageLayout.PageNumberRect(content),
            _theme.ForegroundTertiary, TextAlign.Right, _opacity);
        _text.Draw(canvas, state.Pages.Title(page), IslandType.PageTitle, PageLayout.PageTitleRect(content),
            _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        IconSet.Draw(canvas, IconKind.ChevronDown, PageLayout.CollapseRect(content).Deflate(5),
            Alpha(_theme.ForegroundSecondary, _opacity), _icon);
    }

    /// <summary>Draws one page at <paramref name="pageRect"/> (host-local DIP).</summary>
    public void DrawPage(SKCanvas canvas, in IslandRenderState state, int page, SKRect pageRect, float opacity)
    {
        _theme = state.Theme;
        _opacity = opacity;
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
        _text.Draw(canvas, home.ClockText ?? "00:00", IslandType.Clock, PageLayout.HomeClock(page),
            _theme.Foreground, TextAlign.Left, _opacity);
        _text.Draw(canvas, home.DateText, IslandType.Date, PageLayout.HomeDate(page),
            _theme.ForegroundSecondary, TextAlign.Left, _opacity);

        if (home.ShowWeather && (!string.IsNullOrEmpty(home.WeatherTemp) || !string.IsNullOrEmpty(home.WeatherCondition)))
        {
            DrawWeather(canvas, home, page);
        }

        DrawToggles(canvas, home, page);
        DrawNowPlaying(canvas, home.NowPlaying, page);
        DrawSliders(canvas, home, page);
        DrawBattery(canvas, home, page);
    }

    private void DrawWeather(SKCanvas canvas, HomePageModel home, SKRect page)
    {
        var rect = PageLayout.HomeWeather(page);
        DrawCard(canvas, rect, 10);
        IconSet.Draw(canvas, IconKind.CloudSun, new SKRect(rect.Left + 9, rect.Top + 9, rect.Left + 25, rect.Top + 25),
            Alpha(new SKColor(0xF4, 0xC4, 0x6C), _opacity), _icon);
        _text.Draw(canvas, home.WeatherTemp, IslandType.WeatherTemp,
            new SKRect(rect.Left + 30, rect.Top + 5, rect.Right - 6, rect.Top + 19), _theme.Foreground, TextAlign.Left, _opacity);
        _text.Draw(canvas, home.WeatherCondition, IslandType.WeatherCondition,
            new SKRect(rect.Left + 30, rect.Top + 18, rect.Right - 6, rect.Top + 30), _theme.ForegroundTertiary, TextAlign.Left, _opacity);
    }

    private void DrawToggles(SKCanvas canvas, HomePageModel home, SKRect page)
    {
        var toggles = home.Toggles;
        var count = toggles is null ? 0 : Math.Min(toggles.Count, 4);
        for (var i = 0; i < count; i++)
        {
            var toggle = toggles![i];
            var rect = PageLayout.HomeToggle(page, i);
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

    private void DrawNowPlaying(SKCanvas canvas, MediaSummaryModel media, SKRect page)
    {
        var rect = PageLayout.HomeMediaCard(page);
        DrawCard(canvas, rect, 11);

        var accent = _accents.Get(media.AccentHex, _theme.TrackAccent);
        DrawArt(canvas, new SKRect(rect.Left + 7, rect.Top + 8, rect.Left + 39, rect.Top + 40), 8, accent, large: false,
            media.Artwork, media.ArtworkKey);

        var textRight = rect.Left + 366 - 6;
        _text.Draw(canvas, media.Title, IslandType.CardTitle,
            new SKRect(rect.Left + 48, rect.Top + 6, textRight, rect.Top + 22), _theme.Foreground, TextAlign.Left, _opacity);
        _text.Draw(canvas, media.Subtitle, IslandType.CardSubtitle,
            new SKRect(rect.Left + 48, rect.Top + 22, textRight, rect.Top + 36), _theme.ForegroundSecondary, TextAlign.Left, _opacity);

        var play = PageLayout.HomeMediaPlay(page);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.LineStrong, _opacity);
        canvas.DrawCircle(play.MidX, play.MidY, play.Width / 2, _fill);
        IconSet.Draw(canvas, media.IsPlaying ? IconKind.Pause : IconKind.Play, play.Deflate(7),
            Alpha(_theme.Foreground, _opacity), _icon);

        if (media.HasTimeline)
        {
            DrawTrack(canvas, PageLayout.HomeMediaProgress(page), media.ProgressPercent / 100.0, accent, _theme.HudTrack);
        }
    }

    private void DrawSliders(SKCanvas canvas, HomePageModel home, SKRect page)
    {
        var rect = new SKRect(page.Left, page.Top + 176, page.Left + 400, page.Top + 214);
        DrawCard(canvas, rect, 11);

        IconSet.Draw(canvas, home.IsMuted ? IconKind.VolumeMute : IconKind.Volume,
            new SKRect(rect.Left + 10, rect.Top + 12.5f, rect.Left + 23, rect.Top + 25.5f),
            Alpha(_theme.ForegroundTertiary, _opacity), _icon);
        DrawTrack(canvas, PageLayout.HomeVolumeTrack(page), home.HasVolume ? home.Volume / 100.0 : 0,
            home.IsMuted || !home.HasVolume ? _theme.ForegroundTertiary : _theme.Foreground, _theme.LineStrong);
        _text.Draw(canvas, home.HasVolume ? _numbers.Number(home.Volume) : "—", IslandType.SliderValue,
            new SKRect(rect.Left + 145, rect.Top + 13, rect.Left + 167, rect.Top + 25), _theme.ForegroundTertiary,
            TextAlign.Right, _opacity);

        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.Line, _opacity);
        canvas.DrawRect(new SKRect(rect.Left + 200, rect.Top + 11, rect.Left + 201, rect.Top + 27), _fill);

        IconSet.Draw(canvas, IconKind.Brightness,
            new SKRect(rect.Left + 210, rect.Top + 12.5f, rect.Left + 223, rect.Top + 25.5f),
            Alpha(_theme.ForegroundTertiary, _opacity), _icon);
        DrawTrack(canvas, PageLayout.HomeBrightnessTrack(page), home.HasBrightness ? home.Brightness / 100.0 : 0,
            home.HasBrightness ? _theme.Foreground : _theme.ForegroundTertiary, _theme.LineStrong);
        _text.Draw(canvas, home.HasBrightness ? _numbers.Number(home.Brightness) : "—", IslandType.SliderValue,
            new SKRect(rect.Left + 345, rect.Top + 13, rect.Left + 367, rect.Top + 25), _theme.ForegroundTertiary,
            TextAlign.Right, _opacity);
    }

    private void DrawBattery(SKCanvas canvas, HomePageModel home, SKRect page)
    {
        var rect = PageLayout.HomeBattery(page);
        var level = Math.Clamp(home.BatteryPercent, 0, 100);
        var tone = home.IsCharging ? _theme.Success : _theme.Danger;

        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = 1;
        _stroke.Color = Alpha(_theme.LineStrong, _opacity);
        canvas.DrawRoundRect(new SKRect(rect.Left + 2, rect.Top + 4, rect.Left + 23, rect.Top + 14), 3, 3, _stroke);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.LineStrong, _opacity);
        canvas.DrawRect(new SKRect(rect.Left + 23, rect.Top + 7, rect.Left + 24.5f, rect.Top + 11), _fill);
        _fill.Color = Alpha(tone, _opacity);
        canvas.DrawRoundRect(new SKRect(rect.Left + 3.5f, rect.Top + 5.5f, rect.Left + 3.5f + (18 * level / 100f), rect.Top + 12.5f), 1, 1, _fill);

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

        var accent = _accents.Get(media.AccentHex, _theme.TrackAccent);
        DrawArt(canvas, PageLayout.MediaArt(page), 14, accent, large: true, media.Artwork, media.ArtworkKey);

        _text.Draw(canvas, media.Eyebrow, IslandType.MediaEyebrow, PageLayout.MediaEyebrow(page),
            _theme.ForegroundTertiary, TextAlign.Left, _opacity);
        _text.Draw(canvas, media.Title, IslandType.MediaTitle, PageLayout.MediaTitle(page),
            _theme.Foreground, TextAlign.Left, _opacity);
        _text.Draw(canvas, media.Artist, IslandType.MediaArtist, PageLayout.MediaArtist(page),
            _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        _text.Draw(canvas, media.Album, IslandType.MediaAlbum, PageLayout.MediaAlbum(page),
            _theme.ForegroundTertiary, TextAlign.Left, _opacity);

        // The lyric card only makes sense when a lyric line exists; an empty card with just the
        // "Next" label looked broken for video sessions without lyrics.
        if (!string.IsNullOrWhiteSpace(media.LyricLine) || !string.IsNullOrWhiteSpace(media.NextLyricLine))
        {
            var lyricCard = PageLayout.MediaLyricCard(page);
            DrawCard(canvas, lyricCard, 10);
            _text.Draw(canvas, media.LyricLine, IslandType.Lyric,
                new SKRect(lyricCard.Left + 10, lyricCard.Top + 7, lyricCard.Right - 10, lyricCard.Top + 21),
                _theme.Foreground, TextAlign.Left, _opacity);
            _text.Draw(canvas, media.NextLyricLabel, IslandType.MediaEyebrow,
                new SKRect(lyricCard.Left + 10, lyricCard.Top + 24, lyricCard.Left + 40, lyricCard.Top + 36),
                _theme.ForegroundTertiary, TextAlign.Left, _opacity);
            _text.Draw(canvas, media.NextLyricLine, IslandType.LyricNext,
                new SKRect(lyricCard.Left + 42, lyricCard.Top + 24, lyricCard.Right - 10, lyricCard.Top + 36),
                _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        }

        // Window-title fallback sources have no track length; the player still reports the position,
        // so the elapsed time is drawn without a progress track instead of hiding the row entirely.
        if (media.HasTimeline)
        {
            var seek = PageLayout.MediaSeekTrack(page);
            var progress = Math.Clamp(media.ProgressPercent / 100.0, 0, 1);
            DrawTrack(canvas, seek, progress, _theme.Foreground, _theme.LineStrong);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(_theme.Foreground, _opacity);
            canvas.DrawCircle(seek.Left + (float)(seek.Width * progress), seek.MidY, 4, _fill);

            var row = PageLayout.MediaSeekRow(page);
            _text.Draw(canvas, media.PositionText, IslandType.SeekTime,
                new SKRect(row.Left, row.Top + 3, row.Left + 30, row.Top + 19), _theme.ForegroundTertiary, TextAlign.Left, _opacity);
            _text.Draw(canvas, media.DurationText, IslandType.SeekTime,
                new SKRect(row.Right - 30, row.Top + 3, row.Right, row.Top + 19), _theme.ForegroundTertiary, TextAlign.Right, _opacity);
        }
        else if (!string.IsNullOrEmpty(media.PositionText) && media.PositionText != "—")
        {
            var row = PageLayout.MediaSeekRow(page);
            _text.Draw(canvas, media.PositionText, IslandType.SeekTime,
                new SKRect(row.Left, row.Top + 3, row.Left + 30, row.Top + 19), _theme.ForegroundTertiary, TextAlign.Left, _opacity);
            _text.Draw(canvas, "—", IslandType.SeekTime,
                new SKRect(row.Right - 30, row.Top + 3, row.Right, row.Top + 19), _theme.ForegroundTertiary, TextAlign.Right, _opacity);
        }

        DrawTransport(canvas, media, page);

        // The output label only exists for sources that expose an audio endpoint; drawing the bare
        // speaker glyph without it left an orphan icon floating in the transport row.
        if (!string.IsNullOrWhiteSpace(media.OutputName))
        {
            IconSet.Draw(canvas, IconKind.Speaker, new SKRect(page.Left + 254, page.Top + 227, page.Left + 266, page.Top + 239),
                Alpha(_theme.ForegroundTertiary, _opacity), _icon);
            _text.Draw(canvas, media.OutputName, IslandType.Output,
                new SKRect(page.Left + 270, page.Top + 220, page.Left + 320, page.Top + 246), _theme.ForegroundTertiary,
                TextAlign.Left, _opacity);
        }

        if (media.QueueCount > 0)
        {
            IconSet.Draw(canvas, IconKind.Queue, new SKRect(page.Left + 330, page.Top + 227, page.Left + 342, page.Top + 239),
                Alpha(_theme.ForegroundTertiary, _opacity), _icon);
            _text.Draw(canvas, "Queue", IslandType.Output,
                new SKRect(page.Left + 346, page.Top + 220, page.Left + 378, page.Top + 246), _theme.ForegroundTertiary,
                TextAlign.Left, _opacity);
            _text.Draw(canvas, _numbers.Number(media.QueueCount), IslandType.Output,
                new SKRect(page.Left + 378, page.Top + 220, page.Left + 400, page.Top + 246), _theme.ForegroundTertiary,
                TextAlign.Right, _opacity);
        }
    }

    private void DrawTransport(SKCanvas canvas, MediaPageModel media, SKRect page)
    {
        var previous = PageLayout.MediaPrevious(page);
        var play = PageLayout.MediaPlay(page);
        var next = PageLayout.MediaNext(page);

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