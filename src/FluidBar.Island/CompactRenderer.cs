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

    public void Draw(SKCanvas canvas, in IslandRenderState state)
    {
        _theme = state.Theme;
        _opacity = (float)Math.Clamp(state.Opacity * state.ContentOpacity, 0, 1);
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
        // Idle shell is only 132 DIP wide (LayoutTokens.IdleSize), so the clock slot, the status dot
        // and the date column must share it: the old 70/76/88 split left the date just 32 DIP and
        // "10月 20" / "Oct 18" were always ellipsised to "10月 ...". Measured clock is ~31 DIP, so the
        // dot moves to 64 and the date owns everything from 72 to the trailing inset.
        _text.Draw(canvas, model.ClockText ?? "00:00", IslandType.CompactClock,
            new SKRect(12, 0, 58, height), _theme.Foreground, TextAlign.Left, _opacity);

        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.Success, _opacity);
        canvas.DrawCircle(64, height / 2, 2.5f, _fill);

        _text.Draw(canvas, model.DateText, IslandType.CompactSmall,
            new SKRect(72, 0, width - 10, height), _theme.ForegroundSecondary, TextAlign.Left, _opacity);
    }

    // ----------------------------------------------------------------- media

    private void DrawMedia(SKCanvas canvas, CompactModel model, float width, float height)
    {
        var accent = _accents.Get(model.AccentHex, _theme.TrackAccent);
        var art = new SKRect(8, (height - 28) / 2, 36, (height + 28) / 2);
        DrawArt(canvas, art, 8, accent, model.Artwork, model.ArtworkKey);

        var textWidth = Math.Max(0, width - 44 - 8 - 26 - 8 - 22);
        var titleArea = new SKRect(44, 6, 44 + textWidth, 22);
        var subtitleArea = new SKRect(44, 22, 44 + textWidth, 36);
        _text.Draw(canvas, model.Title, IslandType.CardTitle, titleArea, _theme.Foreground, TextAlign.Left, _opacity);
        _text.Draw(canvas, model.Subtitle, IslandType.CardSubtitle, subtitleArea, _theme.ForegroundSecondary, TextAlign.Left, _opacity);

        var wave = new SKRect(width - 60, 13, width - 38, 27);
        DrawWave(canvas, wave, accent, model.IsActive);

        var play = new SKRect(width - 34, 7, width - 8, 33);
        DrawIcon(canvas, model.IsActive ? IconKind.Pause : IconKind.Play, play.Deflate(4), _theme.Foreground);

        // No timeline = no bar at all (an always-empty 0 % bar read as a stuck progress).
        if (model.ProgressPercent >= 0)
        {
            var track = new SKRect(8, height - 2, width - 8, height);
            DrawTrack(canvas, track, Math.Clamp(model.ProgressPercent / 100.0, 0, 1), accent, _theme.HudTrack);
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

        DrawIcon(canvas, icon, new SKRect(12, (height - 17) / 2, 29, (height + 17) / 2), accent);

        var label = model.Title ?? (kind == ActivityKind.Brightness ? "Brightness" : "Volume");
        _text.Draw(canvas, muted ? "Muted" : label, IslandType.CompactCaption,
            new SKRect(35, 0, 65, height), muted ? accent : _theme.ForegroundSecondary, TextAlign.Left, _opacity);

        DrawTrack(canvas, new SKRect(71, (height / 2) - 1.5f, 156, (height / 2) + 1.5f), level / 100.0,
            accent, _theme.HudTrack);

        _text.Draw(canvas, model.Badge ?? _numbers.Percent(level), IslandType.CompactValue,
            new SKRect(168, 0, width - 12, height), muted ? accent : _theme.Foreground, TextAlign.Right, _opacity);
    }

    // ---------------------------------------------------------- notification

    private void DrawNotification(SKCanvas canvas, CompactModel model, float width, float height)
    {
        var hasChip = !string.IsNullOrEmpty(model.VerificationCode);
        var chipWidth = hasChip ? 46f : 0f;
        var hasBadge = !hasChip && model.UnreadCount > 0;
        var right = width - 12 - (hasChip ? chipWidth + 8 : hasBadge ? 22 : 0);

        var iconArea = new SKRect(12, (height - 20) / 2, 32, (height + 20) / 2);
        DrawIcon(canvas, model.Icon ?? IconKind.Bell, iconArea.Deflate(4),
            _accents.Get(model.AccentHex, _theme.Accent));

        var area = new SKRect(40, 0, right, height);
        var app = model.AppName;
        if (!string.IsNullOrEmpty(app))
        {
            var appLayout = _text.GetLayout(app, IslandType.CompactSmallSemiBold, area.Width);
            _text.DrawLayout(canvas, appLayout, new SKRect(area.Left, area.Top, area.Left + appLayout.Width, area.Bottom),
                _theme.Foreground, TextAlign.Left, _opacity);
            var separator = area.Left + appLayout.Width + 4;
            if (separator < area.Right - 12)
            {
                _text.Draw(canvas, "·", IslandType.CompactSmall,
                    new SKRect(separator, area.Top, separator + 8, area.Bottom), _theme.ForegroundSecondary,
                    TextAlign.Left, _opacity);
                _text.Draw(canvas, model.Title ?? model.Body, IslandType.CompactSmall,
                    new SKRect(separator + 10, area.Top, area.Right, area.Bottom), _theme.ForegroundSecondary,
                    TextAlign.Left, _opacity);
            }
        }
        else
        {
            _text.Draw(canvas, model.Title ?? model.Body, IslandType.CompactSmallSemiBold, area,
                _theme.Foreground, TextAlign.Left, _opacity);
        }

        if (hasChip)
        {
            var chip = new SKRect(width - 12 - chipWidth, (height - 20) / 2, width - 12, (height + 20) / 2);
            DrawChip(canvas, model.VerificationCode, chip, _theme.Info, _theme.Foreground);
        }
        else if (hasBadge)
        {
            var badge = new SKRect(width - 12 - 16, (height - 16) / 2, width - 12, (height + 16) / 2);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = Alpha(_theme.Accent, 0.20f * _opacity);
            canvas.DrawRoundRect(badge, 8, 8, _fill);
            _text.Draw(canvas, _numbers.Number(model.UnreadCount), IslandType.CompactTiny, badge,
                _theme.Accent, TextAlign.Center, _opacity);
        }
    }

    // -------------------------------------------------------------- clipboard

    private void DrawClipboard(SKCanvas canvas, CompactModel model, float width, float height)
    {
        DrawIcon(canvas, model.Icon ?? IconKind.Clipboard, new SKRect(16, (height - 16) / 2, 32, (height + 16) / 2),
            _accents.Get(model.AccentHex, _theme.Success));

        var label = model.Badge ?? "Copied";
        _text.Draw(canvas, label, IslandType.CompactSmallSemiBold,
            new SKRect(40, 0, 88, height), _theme.Success, TextAlign.Left, _opacity);

        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.ForegroundTertiary, _opacity);
        canvas.DrawCircle(90, height / 2, 1.5f, _fill);

        _text.Draw(canvas, model.Body ?? model.Title, IslandType.CompactSmall,
            new SKRect(98, 0, width - 12, height), _theme.Foreground, TextAlign.Left, _opacity);
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
        var heights = active ? ActiveWave : IdleWave;
        const float gap = 2f;
        var barWidth = Math.Max(1.5f, (area.Width - (gap * 4)) / 5);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(active ? accent : _theme.ForegroundTertiary, _opacity);
        for (var i = 0; i < heights.Length; i++)
        {
            var wobble = active && !_reduced ? 0.85 + (0.15 * Math.Sin((_phase * 3) + i)) : 1.0;
            var height = area.Height * heights[i] * (float)wobble;
            var x = area.Left + (i * (barWidth + gap));
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
