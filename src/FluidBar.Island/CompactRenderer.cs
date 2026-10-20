using FluidBar.Core.Activities;
using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Compact pill renderer: idle, media, HUD, notification, clipboard, agent, timer and battery at their
/// exact Core size tokens, plus a generic surface for the remaining activity kinds. Every surface uses
/// fixed icon slots, a shrinkable single-line text column and a hard clip to the pill, so an overlong
/// string can never push the icon or a chip outside the shell.
/// </summary>
internal sealed class CompactRenderer : IDisposable
{
    private readonly ScaledText _text;
    private readonly AccentPalette _accents;
    private readonly ArtShaderCache _art;
    private readonly ArtworkImageCache _artwork;
    private readonly NumberTextCache _numbers;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true };
    private readonly SKPaint _icon = new() { IsAntialias = true };

    private IslandTheme _theme = IslandTheme.Dark;
    private float _opacity = 1;
    private double _phase;
    private double _agentPhase;
    private bool _reduced;
    private bool _pressed;

    private static readonly CompactModel EmptyModel = new();

    public CompactRenderer(TextEngine text, AccentPalette accents, ArtShaderCache art,
        ArtworkImageCache artwork, NumberTextCache numbers)
    {
        _text = new ScaledText(text);
        _accents = accents;
        _art = art;
        _artwork = artwork;
        _numbers = numbers;
    }

    /// <summary>Draws the compact layer at <paramref name="opacity"/> (host motion drives the fade).</summary>
    /// <param name="canvas">Host-local DIP canvas.</param>
    /// <param name="state">Render state.</param>
    /// <param name="opacity">Layer opacity 0..1.</param>
    public void Draw(SKCanvas canvas, in IslandRenderState state, float opacity)
    {
        _theme = state.Theme;
        _opacity = Math.Clamp(opacity, 0, 1);
        _text.SetScale(state.FontScale);
        _phase = state.MotionPhase;
        _agentPhase = state.AgentAnimationSeconds;
        _reduced = state.ReducedMotion;
        // Compact pill = one big button: the shell spring scales it on press, and the renderer adds a
        // light wash so the pressed frame is readable in a still capture too.
        _pressed = state.PressedControl != IslandPageControl.None;
        if (_opacity <= 0.01f)
        {
            return;
        }

        var activity = state.Compact;
        var kind = activity?.Kind ?? ActivityKind.Idle;
        var model = activity?.Model ?? EmptyModel;
        var width = (float)state.ShellWidth;
        var height = (float)state.ShellHeight;

        switch (kind)
        {
            case ActivityKind.Media:
                DrawMedia(canvas, model, width, height);
                break;
            case ActivityKind.Volume:
            case ActivityKind.Brightness:
                DrawHud(canvas, model, kind, width, height);
                break;
            case ActivityKind.Notification:
                DrawNotification(canvas, model, width, height);
                break;
            case ActivityKind.Clipboard:
                DrawClipboard(canvas, model, width, height);
                break;
            case ActivityKind.Agent:
                DrawAgent(canvas, model, width, height);
                break;
            case ActivityKind.Timer:
                DrawTimer(canvas, model, width, height);
                break;
            case ActivityKind.BatteryLow:
                DrawBattery(canvas, model, width, height);
                break;
            case ActivityKind.Idle:
                DrawIdle(canvas, model, width, height);
                break;
            default:
                DrawGeneric(canvas, model, kind, width, height);
                break;
        }
    }

    // ------------------------------------------------------------------ idle

    private void DrawIdle(SKCanvas canvas, CompactModel model, float width, float height)
    {
        // prototype2 .pill-idle: padding 0 12, gap 7; 13.5 DIP display clock, 5 DIP green dot, then a
        // 10.5 DIP date. The layout is measured, not hard-coded, so a localized clock keeps the gaps.
        var clock = model.ClockText ?? "00:00";
        var clockWidth = Math.Min(64f, _text.Measure(clock, IslandType.CompactClock));
        _text.Draw(canvas, clock, IslandType.CompactClock,
            new SKRect(12, 0, 12 + clockWidth, height), _theme.Foreground, TextAlign.Left, _opacity);

        var dotX = 12 + clockWidth + 7 + 2.5f;
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.Success, _opacity);
        canvas.DrawCircle(dotX, height / 2, 2.5f, _fill);

        var dateLeft = dotX + 2.5f + 7;
        _text.Draw(canvas, model.DateText, IslandType.CompactSmall,
            new SKRect(dateLeft, 0, width - 10, height), _theme.ForegroundSecondary, TextAlign.Left, _opacity);
    }

    // ----------------------------------------------------------------- media

    private void DrawMedia(SKCanvas canvas, CompactModel model, float width, float height)
    {
        var accent = _accents.Get(model.AccentHex, _theme.TrackAccent);
        var art = CompactMediaLayout.ArtworkRect(height);
        DrawArt(canvas, art, 8, accent, model.Artwork, model.ArtworkKey);

        // prototype2 .pill-media without the compact play button (removed on request; transport stays on
        // the expanded media page): padding 0 10 0 8, gap 9; 28 DIP artwork, 13/600 title, 10/400
        // subtitle, 20×15 wave, full-width 1.5 DIP progress bar. The text column reaches the wave.
        var wave = CompactMediaLayout.WaveRect(width, height);
        var textRight = CompactMediaLayout.TextRight(width);
        if (model.ShowLyrics && !string.IsNullOrEmpty(model.LyricLine))
        {
            // Lyric layout (media setting "lyrics in the pill"): cover + live lyric + wave, no title/artist.
            // The single line is vertically centred in the two-row band and ellipsised by the text engine,
            // so an overlong lyric can never push the wave or the play button.
            DrawLyricLine(canvas, model, accent,
                new SKRect(CompactMediaLayout.TextLeft, 5.5f, textRight, height - 5.5f));
        }
        else
        {
            _text.Draw(canvas, model.Title, IslandType.CompactMediaTitle,
                new SKRect(CompactMediaLayout.TextLeft, 5.5f, textRight, 21), _theme.Foreground,
                TextAlign.Left, _opacity);
            _text.Draw(canvas, model.Subtitle, IslandType.CompactMediaSub,
                new SKRect(CompactMediaLayout.TextLeft, 21, textRight, 35), _theme.ForegroundSecondary,
                TextAlign.Left, _opacity);
        }

        DrawWave(canvas, wave, accent, model.IsActive);

        if (_pressed)
        {
            // 6 % white wash: visible press feedback without changing the pill's own colours.
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(SKColors.White.WithAlpha(16), _opacity);
            canvas.DrawRoundRect(new SKRect(0, 0, width, height), height / 2, height / 2, _fill);
        }

        // No timeline = no bar at all (an always-empty 0 % bar read as a stuck progress).
        if (model.ProgressPercent >= 0)
        {
            var track = new SKRect(0, height - 1.5f, width, height);
            DrawTrack(canvas, track, Math.Clamp(model.ProgressPercent / 100.0, 0, 1), accent,
                SKColors.White.WithAlpha(20));
        }
    }

    /// <summary>Draws the live lyric line with its fade-in and karaoke sweep, clipped to the text column.</summary>
    private void DrawLyricLine(SKCanvas canvas, CompactModel model, SKColor accent, SKRect rect)
    {
        var fade = Math.Clamp(model.LyricFade, 0, 1);
        var alpha = _opacity * fade;
        if (alpha <= 0.01f)
        {
            return;
        }

        // A fade that slides up two DIP makes the line change read as a scroll rather than a hard cut.
        var lift = (1 - fade) * 2f;
        var line = new SKRect(rect.Left, rect.Top + lift, rect.Right, rect.Bottom + lift);
        canvas.Save();
        canvas.ClipRect(new SKRect(rect.Left - 1, rect.Top - 3, rect.Right + 1, rect.Bottom + 3));
        // Without word timing the line paints exactly as before (primary colour, whole line).
        var baseColor = model.LyricHighlight < 0 ? _theme.Foreground : _theme.ForegroundSecondary;
        // Content-fit short lines are centred between the artwork and the wave; an over-long line falls
        // back to left alignment so it ellipsises at the trailing edge instead of losing its beginning.
        var measured = _text.Measure(model.LyricLine, IslandType.CompactMediaTitle, line.Width);
        var align = measured <= line.Width - 4 ? TextAlign.Center : TextAlign.Left;
        KaraokeText.Draw(_text, canvas, model.LyricLine, model.LyricHighlight, IslandType.CompactMediaTitle,
            line, baseColor, accent, align, alpha);
        canvas.Restore();
    }

    // ------------------------------------------------------------------- hud

    private void DrawHud(SKCanvas canvas, CompactModel model, ActivityKind kind, float width, float height)
    {
        var muted = model.IsMuted;
        var accent = muted ? _theme.ForegroundSecondary : _theme.Accent;
        var level = Math.Clamp(model.Level ?? 0, 0, 100);
        var icon = kind == ActivityKind.Brightness
            ? (level <= 40 ? IconKind.BrightnessLow : IconKind.BrightnessHigh)
            : muted
                ? IconKind.VolumeMute
                : level switch
                {
                    <= 25 => IconKind.VolumeLow,
                    <= 66 => IconKind.VolumeMedium,
                    _ => IconKind.VolumeHigh,
                };

        // prototype2 .pill-hud: padding 0 13, gap 8; 17 DIP secondary icon, 10.5 DIP label,
        // 89×3 DIP track (radius 3) at x 67 and a 13/600 value inset 13 DIP from the right edge.
        DrawIcon(canvas, icon, new SKRect(13, (height - 17) / 2, 30, (height + 17) / 2),
            muted ? accent : _theme.ForegroundSecondary);

        var label = model.Title ?? (kind == ActivityKind.Brightness ? "Brightness" : "Volume");
        _text.Draw(canvas, muted ? "Muted" : label, IslandType.CompactSmall,
            new SKRect(38, 0, 67, height), muted ? accent : _theme.ForegroundSecondary, TextAlign.Left, _opacity);

        DrawTrack(canvas, new SKRect(67, (height / 2) - 1.5f, 156, (height / 2) + 1.5f), level / 100.0,
            accent, SKColors.White.WithAlpha(36));

        _text.Draw(canvas, model.Badge ?? _numbers.Percent(level), IslandType.CompactHudValue,
            new SKRect(156, 0, width - 13, height), muted ? accent : _theme.Foreground, TextAlign.Right, _opacity);
    }

    // ---------------------------------------------------------- notification

    private void DrawNotification(SKCanvas canvas, CompactModel model, float width, float height)
    {
        // prototype2 .pill-notif: padding 0 10 0 8, gap 8; 20 DIP app glyph (radius 6, app colour, white
        // icon), 11.5 DIP text (app 600 white · title secondary), 16 DIP accent badge with a dark 9/700
        // number inset 10 DIP from the right edge.
        var hasChip = !string.IsNullOrEmpty(model.VerificationCode);
        var chipWidth = hasChip ? 46f : 0f;
        var hasBadge = !hasChip && model.UnreadCount > 0;
        var trailing = hasChip ? chipWidth + 8 : hasBadge ? 24 : 0;
        var right = width - 10 - trailing;

        var accent = _accents.Get(model.AccentHex, _theme.Accent);
        var glyph = new SKRect(8, (height - 20) / 2, 28, (height + 20) / 2);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(accent, _opacity);
        canvas.DrawRoundRect(glyph, 6, 6, _fill);
        DrawIcon(canvas, model.Icon ?? IconKind.Bell, glyph.Deflate(4), SKColors.White);

        var area = new SKRect(36, 0, right, height);
        var app = model.AppName;
        if (!string.IsNullOrEmpty(app))
        {
            var appLayout = _text.GetLayout(app, IslandType.CompactNoticeStrong, area.Width);
            _text.DrawLayout(canvas, appLayout, new SKRect(area.Left, area.Top, area.Left + appLayout.Width, area.Bottom),
                _theme.Foreground, TextAlign.Left, _opacity);
            var separator = area.Left + appLayout.Width + 5;
            if (separator < area.Right - 12)
            {
                _text.Draw(canvas, "·", IslandType.CompactNotice,
                    new SKRect(separator, area.Top, separator + 8, area.Bottom), _theme.ForegroundSecondary,
                    TextAlign.Left, _opacity);
                _text.Draw(canvas, model.Title ?? model.Body, IslandType.CompactNotice,
                    new SKRect(separator + 8, area.Top, area.Right, area.Bottom), _theme.ForegroundSecondary,
                    TextAlign.Left, _opacity);
            }
        }
        else
        {
            _text.Draw(canvas, model.Title ?? model.Body, IslandType.CompactNoticeStrong, area,
                _theme.Foreground, TextAlign.Left, _opacity);
        }

        if (hasChip)
        {
            var chip = new SKRect(width - 10 - chipWidth, (height - 20) / 2, width - 10, (height + 20) / 2);
            DrawChip(canvas, model.VerificationCode, chip, _theme.Info, _theme.Foreground);
        }
        else if (hasBadge)
        {
            var badge = new SKRect(width - 10 - 16, (height - 16) / 2, width - 10, (height + 16) / 2);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(_theme.Accent, _opacity);
            canvas.DrawRoundRect(badge, 8, 8, _fill);
            _text.Draw(canvas, _numbers.Number(model.UnreadCount), IslandType.CompactBadge, badge,
                _theme.Background, TextAlign.Center, _opacity);
        }
    }

    // -------------------------------------------------------------- clipboard

    private void DrawClipboard(SKCanvas canvas, CompactModel model, float width, float height)
    {
        // prototype2 .pill-clip: padding 0 12 0 9, gap 8; 20 DIP accent-tinted slot (radius 6) with a
        // 12 DIP accent icon, 11/600 white label, 3 DIP tertiary dot, 10.5 DIP secondary preview.
        var accent = _accents.Get(model.AccentHex, _theme.Accent);
        var slot = new SKRect(9, (height - 20) / 2, 29, (height + 20) / 2);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(accent, 0.13f * _opacity);
        canvas.DrawRoundRect(slot, 6, 6, _fill);
        DrawIcon(canvas, model.Icon ?? IconKind.Clipboard, slot.Deflate(4), accent);

        var label = model.Badge ?? "Copied";
        _text.Draw(canvas, label, IslandType.CompactClipLabel,
            new SKRect(37, 0, 76, height), _theme.Foreground, TextAlign.Left, _opacity);

        _fill.Color = Alpha(_theme.ForegroundTertiary, _opacity);
        canvas.DrawCircle(77.5f, height / 2, 1.5f, _fill);

        _text.Draw(canvas, model.Body ?? model.Title, IslandType.CompactSmall,
            new SKRect(85, 0, width - 12, height), _theme.ForegroundSecondary, TextAlign.Left, _opacity);
    }

    // ------------------------------------------------------------------ agent

    private void DrawAgent(SKCanvas canvas, CompactModel model, float width, float height)
    {
        var tone = Tone(model.Status);
        // prototype2 .pill-agent: padding 0 12 0 10, gap 8, .agent-ring 20×20 with a 1.5px border at
        // rgba(accent, .18) plus a spinning top/right arc (running); done/error replace the arc with a
        // 10 DIP glyph. The task line is 11.5px/500, the meta 9.5px fg-3, and nothing is drawn after the
        // meta (the old trailing status dot is gone).
        // CSS border-box: a 1.5px border sits inside the 20×20 box, so the stroke radius is
        // (20 - 1.5) / 2 = 9.25 DIP. The base ring keeps the prototype2 fixed accent tint even when
        // the state turns done/error (::before is rgba(76,201,240,.18)).
        var ring = new SKRect(10, (height - 20) / 2, 30, (height + 20) / 2);
        var ringRadius = (20f - AgentMotion.RingStrokeWidth) / 2f;
        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeCap = SKStrokeCap.Butt;
        _stroke.StrokeWidth = AgentMotion.RingStrokeWidth;
        _stroke.Color = Alpha(_theme.Accent, AgentMotion.RingBaseAlpha * _opacity);
        canvas.DrawCircle(ring.MidX, ring.MidY, ringRadius, _stroke);
        if (model.Status == CompactStatus.Success)
        {
            DrawIcon(canvas, IconKind.Check, ring.Deflate(5), tone);
        }
        else if (model.Status == CompactStatus.Danger)
        {
            DrawIcon(canvas, IconKind.Alert, ring.Deflate(5), tone);
        }
        else if (model.Status is CompactStatus.Info or CompactStatus.Warning)
        {
            // prototype2 .agent-ring::after: spin 900ms linear infinite, border-top+right color =
            // currentColor (= accent for running), i.e. a half-circle rotating clockwise.
            var rotation = _reduced ? 0 : AgentMotion.RingRotationDegrees(_agentPhase);
            _stroke.Color = Alpha(model.Status == CompactStatus.Info ? _theme.Accent : tone, _opacity);
            canvas.DrawArc(
                new SKRect(ring.MidX - ringRadius, ring.MidY - ringRadius,
                    ring.MidX + ringRadius, ring.MidY + ringRadius),
                AgentMotion.ArcStartDegrees + (float)rotation,
                AgentMotion.ArcSweepDegrees,
                false,
                _stroke);
        }
        else
        {
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(tone, _opacity);
            canvas.DrawCircle(ring.MidX, ring.MidY, 2.4f, _fill);
        }

        var status = model.StatusText;
        var duration = model.DurationText;
        // Status-only fallback: the main slot already carries the localized status, so repeating it in
        // the meta column would read "运行中 运行中".
        var metaStatus = status;
        if (!string.IsNullOrEmpty(model.Title) && string.Equals(model.Title, status, StringComparison.Ordinal))
        {
            metaStatus = null;
        }

        var metaRight = width - 12;
        var textRight = metaRight;
        var hasStatus = !string.IsNullOrEmpty(metaStatus);
        var hasDuration = !string.IsNullOrEmpty(duration);
        if (hasStatus || hasDuration)
        {
            // Reserve the widest tabular duration ("00:00") for the whole run: a per-second text change
            // (0:09 -> 0:10, 0:59 -> 1:00) may not resize the meta column or push the task line.
            var durationColumn = hasDuration ? _text.Measure("00:00", IslandType.CompactTinyMono) : 0;
            var statusWidth = hasStatus ? _text.Measure(metaStatus!, IslandType.CompactTiny) : 0;
            var separator = hasStatus && hasDuration ? _text.Measure(" · ", IslandType.CompactTiny) : 0;
            var metaWidth = statusWidth + separator + durationColumn;
            var durationLeft = metaRight - durationColumn;
            if (hasDuration)
            {
                _text.Draw(canvas, duration, IslandType.CompactTinyMono,
                    new SKRect(durationLeft, 0, metaRight, height), _theme.ForegroundTertiary,
                    TextAlign.Right, _opacity);
            }

            if (hasStatus)
            {
                var statusRight = durationLeft - separator;
                _text.Draw(canvas, metaStatus, IslandType.CompactTiny,
                    new SKRect(statusRight - statusWidth, 0, statusRight, height),
                    _theme.ForegroundTertiary, TextAlign.Right, _opacity);
            }

            textRight = metaRight - metaWidth - 8;
        }

        _text.Draw(canvas, model.Title ?? model.Body, IslandType.CompactAgentTask,
            new SKRect(38, 0, Math.Max(38, textRight), height), _theme.Foreground, TextAlign.Left, _opacity);
    }

    // ------------------------------------------------------------------ timer

    private void DrawTimer(SKCanvas canvas, CompactModel model, float width, float height)
    {
        DrawIcon(canvas, model.Icon ?? IconKind.Timer, new SKRect(12, (height - 17) / 2, 29, (height + 17) / 2), _theme.Accent);

        _text.Draw(canvas, model.TimerText ?? model.Title ?? "25:00", IslandType.CompactClock,
            new SKRect(35, 0, 93, height), _theme.Foreground, TextAlign.Left, _opacity);

        _text.Draw(canvas, model.StatusText, IslandType.CompactCaption,
            new SKRect(97, 0, width - 40, height), _theme.ForegroundSecondary, TextAlign.Left, _opacity);

        var ring = new SKRect(width - 28, (height - 16) / 2, width - 12, (height + 16) / 2);
        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = 2;
        _stroke.Color = Alpha(_theme.HudTrack, _opacity);
        canvas.DrawCircle(ring.MidX, ring.MidY, 6.4f, _stroke);

        var progress = model.ProgressPercent < 0 ? 0 : Math.Clamp(model.ProgressPercent / 100.0, 0, 1);
        if (progress > 0)
        {
            _stroke.Color = Alpha(_theme.Accent, _opacity);
            canvas.DrawArc(
                new SKRect(ring.MidX - 6.4f, ring.MidY - 6.4f, ring.MidX + 6.4f, ring.MidY + 6.4f),
                -90,
                (float)(360 * progress),
                false,
                _stroke);
        }

        if (model.ProgressPercent >= 0)
        {
            DrawTrack(canvas, new SKRect(35, height - 5, 93, height - 3), progress, _theme.Accent, _theme.HudTrack);
        }
    }

    // ---------------------------------------------------------------- battery

    private void DrawBattery(SKCanvas canvas, CompactModel model, float width, float height)
    {
        var level = Math.Clamp(model.BatteryPercent ?? model.Level ?? 0, 0, 100);
        var tone = model.IsCharging ? _theme.Success : _theme.Danger;
        DrawIcon(canvas, model.IsCharging ? IconKind.Bolt : IconKind.Battery,
            new SKRect(12, (height - 14) / 2, 26, (height + 14) / 2), tone);

        _text.Draw(canvas, model.StatusText ?? (model.IsCharging ? "Plugged in" : "On battery"),
            IslandType.CompactCaption, new SKRect(32, 0, 140, height), _theme.Foreground, TextAlign.Left, _opacity);

        _text.Draw(canvas, model.Badge ?? _numbers.Percent(level), IslandType.CompactValue,
            new SKRect(width - 52, 0, width - 12, height), tone, TextAlign.Right, _opacity);
    }

    // ---------------------------------------------------------------- generic

    private void DrawGeneric(SKCanvas canvas, CompactModel model, ActivityKind kind, float width, float height)
    {
        var icon = model.Icon ?? IconFor(kind);
        DrawIcon(canvas, icon, new SKRect(16, (height - 16) / 2, 32, (height + 16) / 2),
            _accents.Get(model.AccentHex, kind == ActivityKind.Focus ? _theme.ForegroundSecondary : _theme.Accent));

        var hasStatus = !string.IsNullOrEmpty(model.StatusText);
        var textRight = width - 12 - (hasStatus ? 60 : 0);
        if (height >= 42)
        {
            _text.Draw(canvas, model.Title, IslandType.CompactTitle,
                new SKRect(40, 7, textRight, 23), _theme.Foreground, TextAlign.Left, _opacity);
            _text.Draw(canvas, model.Subtitle ?? model.StatusText, IslandType.CompactCaption,
                new SKRect(40, 23, textRight, 37), _theme.ForegroundSecondary, TextAlign.Left, _opacity);
        }
        else
        {
            _text.Draw(canvas, model.Title, IslandType.CompactTitle,
                new SKRect(40, 0, textRight, height), _theme.Foreground, TextAlign.Left, _opacity);
            _text.Draw(canvas, model.StatusText, IslandType.CompactCaption,
                new SKRect(width - 72, 0, width - 12, height), Tone(model.Status), TextAlign.Right, _opacity);
        }

        if (kind == ActivityKind.Progress && model.ProgressPercent >= 0)
        {
            DrawTrack(canvas, new SKRect(40, height - 6, width - 12, height - 4),
                model.ProgressPercent / 100.0, _theme.Accent, _theme.HudTrack);
        }
    }

    // ------------------------------------------------------------------ draw helpers

    private void DrawIcon(SKCanvas canvas, IconKind kind, SKRect area, SKColor color)
        => IconSet.Draw(canvas, kind, area, Alpha(color, _opacity), _icon);

    private void DrawArt(SKCanvas canvas, SKRect rect, float radius, SKColor accent,
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
                rect.MidX - (width / 2), rect.MidY - (height / 2),
                rect.MidX + (width / 2), rect.MidY + (height / 2));
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
        var size = rect.Width;
        var shader = _art.Get(accent, _theme.ArtEnd, large: size > 48);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Shader = shader;
        _fill.Color = SKColors.White.WithAlpha((byte)Math.Clamp(_opacity * 255, 0, 255));
        canvas.DrawRoundRect(new SKRect(0, 0, size, rect.Height), radius, radius, _fill);
        _fill.Shader = null;
        DrawIcon(canvas, IconKind.Media,
            new SKRect(size * 0.22f, rect.Height * 0.22f, size * 0.78f, rect.Height * 0.78f),
            SKColors.White.WithAlpha(217));
        canvas.Restore();
    }

    private static readonly double[] WaveDelaysMs = [-620, -180, -380, -60, -500];
    private const double WaveIterationMs = 880;
    private const double WaveCycleMs = WaveIterationMs * 2;
    private const float WaveMinHeight = 4f;
    private const float WaveMaxHeight = 13f;
    private const float WaveEndHeight = 6f;

    private void DrawWave(SKCanvas canvas, SKRect area, SKColor accent, bool active)
    {
        // prototype2 .wave: five 2 DIP bars, gap 1.6, animation waveBar 880ms ease-in-out infinite
        // alternate with per-bar delays -620/-180/-380/-60/-500 ms. Keyframes: 4px → 13px → 6px
        // (reverse 6px → 13px → 4px); paused is 4px at 45 % opacity, reduced motion is 5px.
        const float gap = 1.6f;
        const float barWidth = 2f;
        const int count = 5;
        var total = (count * barWidth) + ((count - 1) * gap);
        var startX = area.Left + ((area.Width - total) / 2);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(active ? accent : _theme.ForegroundTertiary, (active ? 1f : 0.45f) * _opacity);
        for (var i = 0; i < count; i++)
        {
            var height = !active
                ? WaveMinHeight
                : _reduced
                    ? 5f
                    : WaveHeightAt(_phase, i);
            var x = startX + (i * (barWidth + gap));
            var y = area.MidY - (height / 2);
            canvas.DrawRoundRect(new SKRect(x, y, x + barWidth, y + height), barWidth / 2, barWidth / 2, _fill);
        }
    }

    /// <summary>Prototype2 wave height for one bar at <paramref name="phaseSeconds"/> (absolute 4..13 DIP).</summary>
    private static float WaveHeightAt(double phaseSeconds, int index)
    {
        var t = ((phaseSeconds * 1000.0) + WaveDelaysMs[index]) % WaveCycleMs;
        if (t < 0)
        {
            t += WaveCycleMs;
        }

        // Alternate direction: iteration 0 runs 4→13→6, iteration 1 reverses 6→13→4. Each of
        // the two keyframe segments is eased independently so the height never snaps.
        var p = t < WaveIterationMs ? t / WaveIterationMs : (t - WaveIterationMs) / WaveIterationMs;
        var reverse = t >= WaveIterationMs;
        var rising = p < 0.5;
        var eased = Smoothstep(rising ? p * 2 : (p - 0.5) * 2);
        var from = rising ? (reverse ? WaveEndHeight : WaveMinHeight) : WaveMaxHeight;
        var to = rising ? WaveMaxHeight : (reverse ? WaveMinHeight : WaveEndHeight);
        return Lerp(from, to, eased);
    }

    private static float Lerp(float from, float to, double t) => from + ((to - from) * (float)Math.Clamp(t, 0, 1));

    private static double Smoothstep(double t) => t * t * (3 - (2 * t));

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

    private void DrawChip(SKCanvas canvas, string? text, SKRect rect, SKColor accent, SKColor foreground)
    {
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(accent, 0.20f * _opacity);
        canvas.DrawRoundRect(rect, rect.Height / 2, rect.Height / 2, _fill);
        _text.Draw(canvas, text, IslandType.CompactTiny, rect, foreground, TextAlign.Center, _opacity);
    }

    private SKColor Tone(CompactStatus status) => status switch
    {
        CompactStatus.Info => _theme.Info,
        CompactStatus.Success => _theme.Success,
        CompactStatus.Warning => _theme.Warning,
        CompactStatus.Danger => _theme.Danger,
        _ => _theme.ForegroundSecondary,
    };

    private static IconKind IconFor(ActivityKind kind) => kind switch
    {
        ActivityKind.Lookup => IconKind.Search,
        ActivityKind.LockKeys => IconKind.Lock,
        ActivityKind.Device => IconKind.Usb,
        ActivityKind.Bluetooth => IconKind.Bluetooth,
        ActivityKind.Focus => IconKind.Focus,
        ActivityKind.Progress => IconKind.Download,
        ActivityKind.Timer => IconKind.Timer,
        ActivityKind.BatteryLow => IconKind.Battery,
        ActivityKind.Notification => IconKind.Bell,
        ActivityKind.Clipboard => IconKind.Clipboard,
        ActivityKind.Agent => IconKind.Agent,
        ActivityKind.Media => IconKind.Media,
        ActivityKind.Volume => IconKind.Volume,
        ActivityKind.Brightness => IconKind.Brightness,
        _ => IconKind.Clock,
    };

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