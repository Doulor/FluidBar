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
    private readonly TextEngine _text;
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
    private bool _reduced;

    private static readonly CompactModel EmptyModel = new();
    private static readonly float[] ActiveWave = [0.35f, 0.7f, 1f, 0.6f, 0.4f];
    private static readonly float[] IdleWave = [0.3f, 0.5f, 0.4f, 0.55f, 0.35f];

    public CompactRenderer(TextEngine text, AccentPalette accents, ArtShaderCache art,
        ArtworkImageCache artwork, NumberTextCache numbers)
    {
        _text = text;
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
        _phase = state.MotionPhase;
        _reduced = state.ReducedMotion;
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
        var art = new SKRect(8, (height - 28) / 2, 36, (height + 28) / 2);
        DrawArt(canvas, art, 8, accent, model.Artwork, model.ArtworkKey);

        // prototype2 .pill-media: padding 0 10 0 8, gap 9; 28 DIP artwork, 13/600 title, 10/400
        // subtitle, 20×15 wave, 26 DIP translucent play button, full-width 1.5 DIP progress bar.
        var wave = new SKRect(width - 65, (height - 15) / 2, width - 45, (height + 15) / 2);
        var textRight = wave.Left - 9;
        _text.Draw(canvas, model.Title, IslandType.CompactMediaTitle,
            new SKRect(45, 5.5f, textRight, 21), _theme.Foreground, TextAlign.Left, _opacity);
        _text.Draw(canvas, model.Subtitle, IslandType.CompactMediaSub,
            new SKRect(45, 21, textRight, 35), _theme.ForegroundSecondary, TextAlign.Left, _opacity);

        DrawWave(canvas, wave, accent, model.IsActive);

        var play = new SKRect(width - 36, (height - 26) / 2, width - 10, (height + 26) / 2);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(SKColors.White.WithAlpha(26), _opacity);
        canvas.DrawCircle(play.MidX, play.MidY, play.Width / 2, _fill);
        DrawIcon(canvas, model.IsActive ? IconKind.Pause : IconKind.Play, play.Deflate(7), _theme.Foreground);

        // No timeline = no bar at all (an always-empty 0 % bar read as a stuck progress).
        if (model.ProgressPercent >= 0)
        {
            var track = new SKRect(0, height - 1.5f, width, height);
            DrawTrack(canvas, track, Math.Clamp(model.ProgressPercent / 100.0, 0, 1), accent,
                SKColors.White.WithAlpha(20));
        }
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
        var ring = new SKRect(12, (height - 16) / 2, 28, (height + 16) / 2);
        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = 1.8f;
        _stroke.Color = Alpha(tone, _opacity);
        canvas.DrawCircle(ring.MidX, ring.MidY, 7, _stroke);
        if (model.Status == CompactStatus.Success)
        {
            DrawIcon(canvas, IconKind.Check, ring.Deflate(4), tone);
        }
        else if (model.Status == CompactStatus.Danger)
        {
            DrawIcon(canvas, IconKind.Alert, ring.Deflate(4), tone);
        }
        else
        {
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(tone, _opacity);
            canvas.DrawCircle(ring.MidX, ring.MidY, 2.4f, _fill);
        }

        var metaWidth = 78f;
        _text.Draw(canvas, model.Title ?? model.Body, IslandType.CardTitle,
            new SKRect(36, 0, width - 36 - metaWidth - 6, height), _theme.Foreground, TextAlign.Left, _opacity);

        var status = model.StatusText;
        var duration = model.DurationText;
        var metaRight = width - 12 - 8;
        if (!string.IsNullOrEmpty(duration))
        {
            var durationWidth = Math.Min(40f, _text.Measure(duration, IslandType.CompactTiny));
            _text.Draw(canvas, duration, IslandType.CompactTiny,
                new SKRect(metaRight - durationWidth, 0, metaRight, height), _theme.ForegroundTertiary,
                TextAlign.Right, _opacity);
            metaRight -= durationWidth + 4;
        }

        _text.Draw(canvas, status, IslandType.CompactTiny,
            new SKRect(metaRight - metaWidth, 0, metaRight, height), _theme.ForegroundSecondary,
            TextAlign.Right, _opacity);

        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(tone, _opacity);
        canvas.DrawCircle(width - 12 - 3, height / 2, 3, _fill);
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

    private void DrawWave(SKCanvas canvas, SKRect area, SKColor accent, bool active)
    {
        // prototype2 .wave: five 2 DIP bars, 1.6 DIP gaps, fully rounded caps, accent colour.
        var heights = active ? ActiveWave : IdleWave;
        const float gap = 1.6f;
        const float barWidth = 2f;
        var total = (heights.Length * barWidth) + ((heights.Length - 1) * gap);
        var startX = area.Left + ((area.Width - total) / 2);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(active ? accent : _theme.ForegroundTertiary, _opacity);
        for (var i = 0; i < heights.Length; i++)
        {
            var wobble = active && !_reduced ? 0.85 + (0.15 * Math.Sin((_phase * 3) + i)) : 1.0;
            var height = area.Height * heights[i] * (float)wobble;
            var x = startX + (i * (barWidth + gap));
            var y = area.MidY - (height / 2);
            canvas.DrawRoundRect(new SKRect(x, y, x + barWidth, y + height), barWidth / 2, barWidth / 2, _fill);
        }
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
