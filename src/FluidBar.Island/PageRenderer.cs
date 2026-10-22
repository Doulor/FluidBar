using System.Diagnostics;
using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Expanded page renderer: the approved prototype's home (clock/weather/toggles/now-playing/sliders/
/// battery) and media (artwork/metadata/lyrics/seek/transport) pages at the 400×260 content size, plus
/// a compact placeholder for the remaining pages and the shared title/collapse chrome.
/// </summary>
/// <summary>
/// Lyric line-change motion. The timings are a design contract (asserted by MediaLyricTransitionTests):
/// the incoming line is visible on the exact frame its start time is reached, and the outgoing line fades
/// in parallel instead of holding the stage.
/// </summary>
public static class LyricTransition
{
    /// <summary>How long the outgoing line takes to slide up and fade out.</summary>
    public const float OutMs = 180;

    /// <summary>How long the incoming line takes to reach full opacity.</summary>
    public const float InMs = 180;

    /// <summary>Delay before the incoming line starts (0 = it appears on the switch frame).</summary>
    public const float StaggerMs = 0;

    /// <summary>Opacity the incoming line starts at, so its first frame is never empty.</summary>
    public const float StartOpacity = 0.3f;

    /// <summary>Vertical travel of the outgoing line (DIP).</summary>
    public const float SlideDip = 6;

    /// <summary>Test/evidence override for the four timing values (null = the shipped contract).</summary>
    public static (float OutMs, float InMs, float StaggerMs, float StartOpacity)? Override { get; set; }

    /// <summary>Clock in milliseconds; tests inject a synthetic clock.</summary>
    public static Func<double>? Clock { get; set; }

    /// <summary>Resolves the timings in effect.</summary>
    internal static (float OutMs, float InMs, float StaggerMs, float StartOpacity) Resolve()
        => Override ?? (OutMs, InMs, StaggerMs, StartOpacity);
}

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
    private double _agentAnimationSeconds;
    private bool _reduced;
    private FontScaleMetrics _metrics = FontScaleMetrics.One;
    private double _mediaNowMs;
    private double _mediaDtMs = 16.7;
    private bool _mediaClockStarted;
    private string? _lyricShown;
    private string? _lyricOutgoing;
    private double _lyricChangedAt = double.NegativeInfinity;

    private IslandPageControl _pressedControl;
    private int _pressedPage = -1;
    private PageSlotKind _pressedKind = PageSlotKind.Plugin;
    private PageSlotKind _hoverKind = PageSlotKind.Plugin;
    private IslandPageControl _hoverControl;
    private int _hoverPage = -1;
    private double _pointerX = double.NaN;
    private double _pointerY = double.NaN;
    private bool _hasPointer;

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
        var page = Math.Max(0, state.PageIndex);
        var number = page < PageNumbers.Length ? PageNumbers[page] : (page + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        _text.Draw(canvas, number, IslandType.PageNumber, PageLayout.PageNumberRect(content),
            _theme.Foreground.WithAlpha(82), TextAlign.Left, _opacity);
        _text.Draw(canvas, state.Pages.Title(page), IslandType.PageTitle, PageLayout.PageTitleRect(content),
            _theme.Foreground.WithAlpha(184), TextAlign.Left, _opacity);

        if (state.Pages.PageSlotAt(page)?.Kind == PageSlotKind.Notifications
            && state.Pages.Notifications.ClearLabel is { Length: > 0 } clearLabel)
        {
            var clear = PageLayout.NotificationClearRect(content);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            _fill.Color = Alpha(_theme.Foreground, 0.08f * _opacity);
            canvas.DrawRoundRect(clear, 6, 6, _fill);
            _text.Draw(canvas, clearLabel, IslandType.RowMeta, clear,
                _theme.Foreground.WithAlpha(190), TextAlign.Center, _opacity);
        }

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
        _agentAnimationSeconds = state.AgentAnimationSeconds;
        _reduced = state.ReducedMotion;
        _pressedControl = state.PressedControl;
        _pressedPage = state.PressedPage;
        _pressedKind = state.Pages.PageSlotAt(_pressedPage)?.Kind ?? PageSlotKind.Plugin;
        _hoverControl = state.HoverControl;
        _hoverPage = state.HoverPage;
        _hoverKind = state.Pages.PageSlotAt(_hoverPage)?.Kind ?? PageSlotKind.Plugin;
        _pointerX = state.PointerX;
        _pointerY = state.PointerY;
        _hasPointer = state.HasPointer;
        if (_opacity <= 0.01f)
        {
            return;
        }

        var slot = state.Pages.PageSlotAt(page);
        var template = slot?.TemplateId ?? "list";
        if (slot is { Kind: PageSlotKind.Plugin } && slot.PluginPage is { } pluginPage)
        {
            DrawPluginPage(canvas, pluginPage, pageRect);
            return;
        }

        switch (template)
        {
            case "builtin.home":
                DrawHome(canvas, state.Pages.Home, pageRect);
                return;
            case "builtin.media":
                DrawMedia(canvas, state.Pages.Media, pageRect);
                return;
            case "list":
                switch (slot?.Kind)
                {
                    case PageSlotKind.Notifications:
                        DrawNotifications(canvas, state.Pages.Notifications, pageRect);
                        return;
                    case PageSlotKind.Clipboard:
                        DrawClipboard(canvas, state.Pages.Clipboard, pageRect);
                        return;
                    case PageSlotKind.Agent:
                        DrawAgent(canvas, state.Pages.Agent, pageRect);
                        return;
                    default:
                        DrawPlaceholder(canvas, state.Pages.Title(page), pageRect);
                        return;
                }
            case "calendar":
                DrawWidgets(canvas, state.Pages.Widgets, pageRect);
                return;
            case "stat-grid":
                DrawWidgets(canvas, state.Pages.Widgets, pageRect);
                return;
            default:
                DrawPlaceholder(canvas, state.Pages.Title(page), pageRect);
                return;
        }
    }

    // ------------------------------------------------------------------- home

    private void DrawHome(SKCanvas canvas, HomePageModel home, SKRect page)
    {
        var m = _metrics;
        TickMediaClock();
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
            var enabled = toggle.IsEnabled;
            var alpha = (enabled ? 1f : 0.45f) * _opacity;
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = !enabled
                ? Alpha(_theme.BackgroundRaised, 0.55f * _opacity)
                : Alpha(toggle.IsOn ? _theme.Accent : _theme.BackgroundRaised, toggle.IsOn ? 0.10f * _opacity : _opacity);
            canvas.DrawRoundRect(rect, 10, 10, _fill);

            var labelWidth = Math.Min(rect.Width - 30, _text.Measure(toggle.Label, IslandType.Toggle));
            var contentWidth = 14 + 5 + labelWidth;
            var startX = rect.Left + ((rect.Width - contentWidth) / 2);
            IconSet.Draw(canvas, toggle.Icon, new SKRect(startX, rect.MidY - 7, startX + 14, rect.MidY + 7),
                Alpha(enabled && toggle.IsOn ? _theme.Accent : _theme.ForegroundTertiary, alpha), _icon);
            _text.Draw(canvas, toggle.Label, IslandType.Toggle,
                new SKRect(startX + 19, rect.Top, rect.Right - 4, rect.Bottom),
                enabled && toggle.IsOn ? _theme.Foreground : _theme.ForegroundSecondary, TextAlign.Left, alpha);
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
        var volumePressed = _pressedControl == IslandPageControl.VolumeMute && _pressedKind == PageSlotKind.Home;
        var volumeHovered = _hoverControl == IslandPageControl.VolumeMute && _hoverKind == PageSlotKind.Home;
        if (home.HasVolume && (volumePressed || volumeHovered))
        {
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(_theme.Foreground, (volumePressed ? 0.12f : 0.07f) * _opacity);
            canvas.DrawRoundRect(PageMetricsLayout.HomeVolumeIcon(page, m), 6, 6, _fill);
        }

        IconSet.Draw(canvas, home.IsMuted ? IconKind.VolumeMute : IconKind.Volume,
            new SKRect(rect.Left + (volumePressed ? 11f : 10f), iconTop, rect.Left + (volumePressed ? 22f : 23f), iconTop + 13),
            Alpha(home.IsMuted ? _theme.Accent : _theme.ForegroundTertiary, _opacity), _icon);
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

    /// <summary>Advances the media motion clock by one frame and returns the delta in milliseconds.</summary>
    private double TickMediaClock()
    {
        var now = LyricTransition.Clock?.Invoke() ?? (Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
        _mediaDtMs = _mediaClockStarted ? Math.Clamp(now - _mediaNowMs, 0, 100) : 1000.0 / 60.0;
        _mediaClockStarted = true;
        _mediaNowMs = now;
        return _mediaDtMs;
    }

    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - (2 * t));
    }

    /// <summary>
    /// Paints the current lyric line with the line-change motion: both lines run in parallel, the incoming
    /// one starting at <see cref="LyricTransition.StartOpacity"/> on the switch frame (never an empty line).
    /// </summary>
    private void DrawLyricLineTransition(SKCanvas canvas, MediaPageModel media, SKRect lineBox,
        SKColor baseColor, SKColor sungColor)
    {
        if (!string.Equals(media.LyricLine, _lyricShown, StringComparison.Ordinal))
        {
            _lyricOutgoing = _lyricShown;
            _lyricShown = media.LyricLine;
            _lyricChangedAt = _mediaNowMs;
        }

        var timing = LyricTransition.Resolve();
        var elapsed = _mediaNowMs - _lyricChangedAt;
        var window = Math.Max(timing.OutMs, timing.StaggerMs + timing.InMs);
        if (_lyricOutgoing is not null && elapsed < window)
        {
            var outT = Math.Clamp(elapsed / timing.OutMs, 0, 1);
            if (outT < 1)
            {
                var moved = new SKRect(lineBox.Left, lineBox.Top - (LyricTransition.SlideDip * (float)SmoothStep(outT)),
                    lineBox.Right, lineBox.Bottom - (LyricTransition.SlideDip * (float)SmoothStep(outT)));
                KaraokeText.Draw(_text, canvas, _lyricOutgoing, -1, IslandType.Lyric, moved, baseColor, sungColor,
                    TextAlign.Left, _opacity * (float)(1 - outT));
            }

            var inT = Math.Clamp((elapsed - timing.StaggerMs) / timing.InMs, 0, 1);
            var alpha = timing.StartOpacity + ((1 - timing.StartOpacity) * (float)SmoothStep(inT));
            var incoming = new SKRect(lineBox.Left,
                lineBox.Top + (LyricTransition.SlideDip * (float)(1 - SmoothStep(inT))), lineBox.Right,
                lineBox.Bottom + (LyricTransition.SlideDip * (float)(1 - SmoothStep(inT))));
            KaraokeText.Draw(_text, canvas, media.LyricLine, media.LyricHighlight, IslandType.Lyric, incoming,
                baseColor, sungColor, TextAlign.Left, _opacity * alpha);
            return;
        }

        KaraokeText.Draw(_text, canvas, media.LyricLine, media.LyricHighlight, IslandType.Lyric, lineBox,
            baseColor, sungColor, TextAlign.Left, _opacity);
    }

    private void DrawMedia(SKCanvas canvas, MediaPageModel media, SKRect page)
    {
        if (!media.HasSession)
        {
            DrawEmpty(canvas, IconKind.Media, media.EmptyTitle ?? "Nothing playing", media.EmptyHint ?? "Start a track to see it here", page);
            return;
        }

        var m = _metrics;
        // Accent from the album cover (extracted upstream) drives the art glow, the progress track and
        // the karaoke sweep; without a cover the theme fallback rules stay in charge.
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
        // "Next" label looked broken for video sessions without lyrics. Without any lyric the band
        // still occupies the approved geometry, so a light caption keeps it intentional instead of a
        // blank gap between the title block and the seek row.
        var hasLyrics = !string.IsNullOrWhiteSpace(media.LyricLine) || !string.IsNullOrWhiteSpace(media.NextLyricLine);
        var lyricCard = PageMetricsLayout.MediaLyricCard(page, m);
        if (hasLyrics)
        {
            DrawCard(canvas, lyricCard, 10);
            var lyricTop = lyricCard.Top + 7;
            var lyricBottom = lyricTop + 17 + m.LineGrow(IslandType.Lyric);
            // Karaoke sweep from the KRC word timeline; a line without word timing paints as a plain line
            // in the primary colour, exactly like before.
            // The split style is a property of the TRACK (does it carry word timing), not of the frame:
            // keying it on the instantaneous highlight painted the whole line in the plain foreground
            // colour whenever a single frame arrived without a sweep ("整句突然变白一下").
            var hasSweep = media.HasKaraokeSweep || media.LyricHighlight >= 0;
            var baseColor = hasSweep
                ? KaraokeText.UnsungColor(_theme.Foreground)
                : _theme.Foreground;
            var sungColor = hasSweep
                ? KaraokeText.SungColor(KaraokeText.SaturatedAccent(accent, _theme.TrackAccent))
                : accent;
            KaraokeText.Draw(_text, canvas, media.LyricLine, media.LyricHighlight, IslandType.Lyric,
                new SKRect(lyricCard.Left + 10, lyricTop, lyricCard.Right - 10, lyricBottom),
                baseColor, sungColor, TextAlign.Left, _opacity);
            var nextTop = lyricBottom + 3;
            var nextBottom = nextTop + 14 + m.LineGrow(IslandType.LyricNext);
            // The tag is measured instead of parked in a fixed 30 DIP column: at a 1.6 font scale the old
            // column clipped it to "NE…" (prototype2 keeps it whole and spaces the text 6 DIP after it).
            var tagLeft = lyricCard.Left + 10;
            var tagWidth = Math.Max(0, _text.Measure(media.NextLyricLabel, IslandType.MediaEyebrow));
            _text.Draw(canvas, media.NextLyricLabel, IslandType.MediaEyebrow,
                new SKRect(tagLeft, nextTop, Math.Min(lyricCard.Right - 10, tagLeft + tagWidth + 1), nextBottom),
                _theme.ForegroundTertiary, TextAlign.Left, _opacity);
            _text.Draw(canvas, media.NextLyricLine, IslandType.LyricNext,
                new SKRect(tagLeft + tagWidth + 6, nextTop, lyricCard.Right - 10, nextBottom),
                _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        }
        else if (!string.IsNullOrWhiteSpace(media.NoLyricsText))
        {
            DrawCard(canvas, lyricCard, 10);
            var captionHeight = 12 + m.LineGrow(IslandType.MediaEyebrow);
            var captionTop = lyricCard.MidY - (captionHeight / 2);
            // No lyric to follow: centring the caption inside the full-width band reads as a deliberate
            // empty state; a left-hugging caption looked like a clipped lyric line (visual audit, task-38).
            _text.Draw(canvas, media.NoLyricsText, IslandType.MediaEyebrow,
                new SKRect(lyricCard.Left + 10, captionTop, lyricCard.Right - 10, captionTop + captionHeight),
                _theme.ForegroundTertiary, TextAlign.Center, 0.8f * _opacity);
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
            // A source that cannot seek (KuGou: no SMTC session) gets a read-only row: the position is
            // still informative, but a full-strength track with a knob invited dragging that could never
            // work (user report 2026-10-21).
            var seekAlpha = media.CanSeek ? _opacity : _opacity * 0.45f;
            DrawTrack(canvas, seek, progress, Alpha(accent, seekAlpha), Alpha(_theme.LineStrong, seekAlpha));
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(accent, seekAlpha);
            canvas.DrawCircle(seek.Left + (float)(seek.Width * progress), seek.MidY, media.CanSeek ? 4 : 2.5f, _fill);

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

        // Footer follows the requested split: volume + output on the left, transport exactly centred,
        // queue on the right. Unsupported slots are greyed with an honest placeholder; their hit
        // targets are refused by SkiaIslandHitTest (OutputAvailable/QueueAvailable stay false).
        var volume = PageMetricsLayout.MediaVolume(page, m);
        var volumeAlpha = (media.HasVolume ? 1f : 0.45f) * _opacity;
        // The speaker is a real mute button, not a decorative glyph: paint the same hover/press
        // affordance as the transport buttons so the hit target is discoverable.
        var volumeHovered = media.HasVolume && IsHovered(IslandPageControl.VolumeMute);
        var volumePressed = media.HasVolume && IsPressed(IslandPageControl.VolumeMute);
        if (volumeHovered || volumePressed)
        {
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(_theme.Foreground, (volumePressed ? 0.12f : 0.07f) * _opacity);
            canvas.DrawRoundRect(volume, 7, 7, _fill);
        }

        var volumeColor = media.IsMuted ? _theme.Accent : _theme.ForegroundSecondary;
        IconSet.Draw(canvas, media.IsMuted ? IconKind.VolumeMute : IconKind.Speaker,
            new SKRect(volume.Left + (volumePressed ? 4.5f : 4f), volume.MidY - 7,
                volume.Left + (volumePressed ? 15.5f : 15f), volume.MidY + 7),
            Alpha(volumeColor, volumeAlpha), _icon);
        _text.Draw(canvas, media.HasVolume ? _numbers.Number(media.VolumePercent ?? 0) + "%" : "—",
            IslandType.Output,
            new SKRect(volume.Left + 19, volume.Top, volume.Right, volume.Bottom),
            _theme.ForegroundSecondary, TextAlign.Left, volumeAlpha);

        var output = PageMetricsLayout.MediaOutput(page, m);
        var queue = PageMetricsLayout.MediaQueue(page, m);
        var hasOutput = media.OutputAvailable && !string.IsNullOrWhiteSpace(media.OutputName);
        var hasQueue = media.QueueAvailable && media.QueueCount > 0;
        var outputText = hasOutput ? media.OutputName : media.OutputPlaceholder;
        if (!string.IsNullOrWhiteSpace(outputText))
        {
            var outputAlpha = (hasOutput ? 1f : 0.5f) * _opacity;
            IconSet.Draw(canvas, IconKind.Speaker,
                new SKRect(output.Left, output.MidY - 6, output.Left + 12, output.MidY + 6),
                Alpha(_theme.ForegroundTertiary, outputAlpha), _icon);
            _text.Draw(canvas, outputText, IslandType.Output,
                new SKRect(output.Left + 16, output.Top, output.Right, output.Bottom),
                _theme.ForegroundTertiary, TextAlign.Left, outputAlpha);
        }

        if (hasQueue)
        {
            IconSet.Draw(canvas, IconKind.Queue,
                new SKRect(queue.Left + 4, queue.MidY - 6, queue.Left + 16, queue.MidY + 6),
                Alpha(_theme.ForegroundTertiary, _opacity), _icon);
            _text.Draw(canvas, media.QueueLabel ?? "Queue", IslandType.Output,
                new SKRect(queue.Left + 20, queue.Top, queue.Left + 56, queue.Bottom), _theme.ForegroundTertiary,
                TextAlign.Left, _opacity);
            _text.Draw(canvas, _numbers.Number(media.QueueCount), IslandType.Output,
                new SKRect(queue.Left + 56, queue.Top, queue.Right, queue.Bottom), _theme.ForegroundTertiary,
                TextAlign.Right, _opacity);
        }
        else if (!string.IsNullOrWhiteSpace(media.QueuePlaceholder))
        {
            var queueAlpha = 0.5f * _opacity;
            IconSet.Draw(canvas, IconKind.Queue,
                new SKRect(queue.Left + 4, queue.MidY - 6, queue.Left + 16, queue.MidY + 6),
                Alpha(_theme.ForegroundTertiary, queueAlpha), _icon);
            _text.Draw(canvas, media.QueuePlaceholder, IslandType.Output,
                new SKRect(queue.Left + 20, queue.Top, queue.Right, queue.Bottom), _theme.ForegroundTertiary,
                TextAlign.Left, queueAlpha);
        }
    }

    private void DrawTransport(SKCanvas canvas, MediaPageModel media, SKRect page)
    {
        var previous = PageMetricsLayout.MediaPrevious(page, _metrics);
        var play = PageMetricsLayout.MediaPlay(page, _metrics);
        var next = PageMetricsLayout.MediaNext(page, _metrics);

        // Press feedback: the held button shrinks slightly and brightens; hovering lifts it a touch.
        // (The old code painted three static glyphs, so a tap gave no visual answer at all.)
        DrawSkipIcon(canvas, IconKind.Previous, previous, IslandPageControl.Previous);
        DrawSkipIcon(canvas, IconKind.Next, next, IslandPageControl.Next);

        var playPressed = IsPressed(IslandPageControl.PlayPause);
        var playHovered = IsHovered(IslandPageControl.PlayPause);
        var radius = 15f * (playPressed ? 0.9f : playHovered ? 1.04f : 1f);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.Foreground, (playPressed ? 1f : 0.92f) * _opacity);
        canvas.DrawCircle(play.MidX, play.MidY, radius, _fill);
        IconSet.Draw(canvas, media.IsPlaying ? IconKind.Pause : IconKind.Play,
            play.Deflate(playPressed ? 9f : 8f), Alpha(_theme.Background, _opacity), _icon);
    }

    private void DrawSkipIcon(SKCanvas canvas, IconKind icon, SKRect rect, IslandPageControl control)
    {
        var pressed = IsPressed(control);
        var hovered = IsHovered(control);
        var inset = pressed ? 8.5f : 7f;
        var color = pressed || hovered ? _theme.Foreground : _theme.ForegroundSecondary;
        IconSet.Draw(canvas, icon, rect.Deflate(inset), Alpha(color, (pressed ? 1f : hovered ? 0.92f : 1f) * _opacity),
            _icon);
    }

    private bool IsPressed(IslandPageControl control)
        => _pressedControl == control && (_pressedPage < 0 || _pressedKind == PageSlotKind.Media);

    /// <summary>Whether the given control is hovered (the controller resolves it from the same hit test
    /// the clicks use, so the highlight can never sit on a different rectangle than the click target).</summary>
    private bool IsHovered(IslandPageControl control)
        => _hasPointer && _hoverControl == control && (_hoverPage < 0 || _hoverKind == PageSlotKind.Media);

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