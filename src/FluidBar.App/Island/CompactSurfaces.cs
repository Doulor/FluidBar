using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.Theme;

namespace FluidBar.App.Island;

/// <summary>Factory for compact surfaces; keeps the visual selector in one place.</summary>
public static class CompactSurfaceFactory
{
    /// <summary>Creates the surface matching <paramref name="style"/>.</summary>
    public static CompactSurface Create(CompactStyle style) => style switch
    {
        CompactStyle.Media => new MediaCompactSurface(),
        CompactStyle.Hud => new HudCompactSurface(),
        CompactStyle.Notification => new NotificationCompactSurface(),
        CompactStyle.Clipboard => new ClipboardCompactSurface(),
        CompactStyle.Agent => new AgentCompactSurface(),
        CompactStyle.Timer => new TimerCompactSurface(),
        CompactStyle.Battery => new BatteryCompactSurface(),
        CompactStyle.LockKeys => new BasicCompactSurface(CompactStyle.LockKeys, IconKind.Lock),
        CompactStyle.Device => new BasicCompactSurface(CompactStyle.Device, IconKind.Usb),
        CompactStyle.Bluetooth => new BasicCompactSurface(CompactStyle.Bluetooth, IconKind.Bluetooth),
        CompactStyle.Focus => new BasicCompactSurface(CompactStyle.Focus, IconKind.Focus),
        CompactStyle.Progress => new BasicCompactSurface(CompactStyle.Progress, IconKind.Download),
        CompactStyle.Lookup => new BasicCompactSurface(CompactStyle.Lookup, IconKind.Search),
        _ => new IdleCompactSurface(),
    };

    /// <summary>Creates the surface matching <paramref name="model"/>.</summary>
    public static CompactSurface CreateFor(CompactContentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Create(model.Style);
    }
}

/// <summary>Idle pill: clock + battery + page dots (132×36).</summary>
public sealed class IdleCompactSurface : CompactSurface
{
    /// <inheritdoc />
    public override CompactAction HitTest(Point point, bool isPeeked) => CompactAction.Expand;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = CompactSize.Width;
        var height = CompactSize.Height;
        var time = Model.ClockText ?? DateTime.Now.ToString("HH:mm", Localizer.CurrentCulture);
        var timeArea = new Rect(UiMetrics.CompactInset, 0, 58, height);
        DrawLine(context, time, timeArea, Fonts.MonoSemiBold, Typography.CompactClockSize,
            IslandBrushes.Foreground, TextAlignment.Left);

        if (Model.IsPeeked)
        {
            var dateArea = new Rect(width - UiMetrics.CompactInset - 62, 0, 62, height);
            DrawLine(context, Model.DateText, dateArea, Fonts.Regular, Typography.CaptionSize,
                IslandBrushes.Secondary, TextAlignment.Right);
            return;
        }

        var batteryArea = new Rect(width - UiMetrics.CompactInset - 16, (height - 16) / 2, 16, 16);
        Icons.Draw(context, Model.IsCharging ? IconKind.Bolt : IconKind.Battery, batteryArea,
            Model.IsCharging ? IslandBrushes.Success : IslandBrushes.Secondary);
        if (Model.BatteryPercent is { } percent)
        {
            var valueArea = new Rect(batteryArea.X - 30, 0, 26, height);
            DrawValue(context, Localizer.Format(LocKeys.Island.Idle.Battery, ("level", percent)), valueArea,
                IslandBrushes.Secondary, Typography.CaptionSize);
        }
    }
}

/// <summary>Now-playing pill: artwork + title/artist + transport (226×40, peek 236×46).</summary>
public sealed class MediaCompactSurface : CompactSurface
{
    private static readonly double PlaySize = 24;

    /// <inheritdoc />
    public override CompactAction HitTest(Point point, bool isPeeked)
    {
        var width = CompactSize.Width + (isPeeked ? 10 : 0);
        var height = CompactSize.Height + (isPeeked ? 6 : 0);
        var controls = isPeeked ? new Rect(width - 12 - 80, 0, 80, height) : new Rect(width - 12 - PlaySize, 0, PlaySize, height);
        if (!controls.Contains(point))
        {
            return CompactAction.Expand;
        }

        if (!isPeeked)
        {
            return CompactAction.TogglePlay;
        }

        var third = controls.Width / 3;
        var index = (int)((point.X - controls.X) / third);
        return index switch
        {
            0 => CompactAction.Previous,
            1 => CompactAction.TogglePlay,
            _ => CompactAction.Next,
        };
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = CompactSize.Width + (Model.IsPeeked ? 10 : 0);
        var height = CompactSize.Height + (Model.IsPeeked ? 6 : 0);
        var inset = 10.0;
        var art = new Rect(inset, (height - UiMetrics.MediaArtSize) / 2, UiMetrics.MediaArtSize, UiMetrics.MediaArtSize);
        DrawArtwork(context, art);

        var controlsWidth = Model.IsPeeked ? 80 : PlaySize;
        var textX = art.Right + 8;
        var textWidth = width - textX - inset - controlsWidth - 6;
        if (textWidth > 20)
        {
            var titleArea = new Rect(textX, 6, textWidth, 16);
            DrawLine(context, Model.Title, titleArea, Fonts.SemiBold, Typography.TitleSize,
                IslandBrushes.Foreground);
            var subtitleArea = new Rect(textX, 22, textWidth, 14);
            DrawLine(context, Model.Subtitle, subtitleArea, Fonts.Regular, Typography.CaptionSize,
                IslandBrushes.Secondary);
        }

        var centeredY = (height - PlaySize) / 2;
        if (!Model.IsPeeked)
        {
            DrawTransport(context, Model.IsActive ? IconKind.Pause : IconKind.Play,
                new Rect(width - inset - PlaySize, centeredY, PlaySize, PlaySize), IslandBrushes.Foreground);
            return;
        }

        var startX = width - inset - 80;
        DrawTransport(context, IconKind.Previous, new Rect(startX, centeredY, 24, 24), IslandBrushes.Secondary);
        DrawTransport(context, Model.IsActive ? IconKind.Pause : IconKind.Play,
            new Rect(startX + 28, centeredY, 24, 24), IslandBrushes.Foreground);
        DrawTransport(context, IconKind.Next, new Rect(startX + 56, centeredY, 24, 24), IslandBrushes.Secondary);
    }

    private static void DrawTransport(DrawingContext context, IconKind icon, Rect area, IBrush brush)
        => Icons.Draw(context, icon, area.Deflate(4), brush);
}

/// <summary>
/// Single-line HUD for volume/brightness (spec 2.8): icon · label · track · value, 208×40.
/// </summary>
public sealed class HudCompactSurface : CompactSurface
{
    /// <inheritdoc />
    public override CompactAction HitTest(Point point, bool isPeeked) => CompactAction.Expand;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        const double width = 208;
        const double height = 40;
        const double inset = 12;
        var isVolume = Model.Kind is null or Core.Activities.ActivityKind.Volume;
        var muted = Model.IsMuted;
        var value = Math.Clamp(Model.Level ?? 0, 0, 100);
        var accent = muted ? IslandBrushes.Secondary : AccentBrush();

        var iconArea = new Rect(inset, (height - 16) / 2, 16, 16);
        var icon = isVolume
            ? (muted ? IconKind.VolumeMute : value switch
            {
                <= 25 => IconKind.VolumeLow,
                <= 66 => IconKind.VolumeMedium,
                _ => IconKind.VolumeHigh,
            })
            : (value <= 40 ? IconKind.BrightnessLow : IconKind.BrightnessHigh);
        Icons.Draw(context, icon, iconArea, accent);

        var labelKey = isVolume ? LocKeys.Hud.Volume : LocKeys.Hud.Brightness;
        var label = muted ? Localizer.Get(LocKeys.Hud.Muted) : Localizer.Get(labelKey);
        var labelArea = new Rect(iconArea.Right + 6, 0, 26, height);
        DrawLine(context, label, labelArea, Fonts.Regular, Typography.CaptionSize,
            muted ? accent : IslandBrushes.Secondary);

        var valueArea = new Rect(width - inset - 34, 0, 34, height);
        var trackArea = new Rect(labelArea.Right + 8, (height - 4) / 2, valueArea.X - 8 - (labelArea.Right + 8), 4);
        DrawTrack(context, trackArea, value / 100.0, accent,
            new SolidColorBrush(Color.FromArgb(0xFF, 0x23, 0x23, 0x2A)), muted ? 1 : 0);

        DrawValue(context, Localizer.Format(LocKeys.Hud.Value, ("value", value)), valueArea,
            muted ? accent : IslandBrushes.Foreground);
    }
}

/// <summary>Phone-style notification card: app tile + title/body + app name/time (400×96).</summary>
public sealed class NotificationCompactSurface : CompactSurface
{
    /// <summary>Card width token (DIP).</summary>
    public const double PillWidth = 400;

    /// <summary>Card height token (DIP).</summary>
    public const double PillHeight = 96;

    /// <summary>Verification-code chip width (DIP).</summary>
    public const double ChipWidth = 76;

    /// <summary>Fixed 56 DIP app tile.</summary>
    public static Rect IconRect => new(16, 20, 56, 56);

    /// <summary>Text column (zero-width safe) for a card with or without a code chip.</summary>
    public static Rect TextColumn(bool hasChip)
    {
        _ = hasChip;
        return new Rect(86, 0, PillWidth - 86 - 78, PillHeight);
    }

    /// <summary>Title line inside the text column.</summary>
    public static Rect TitleRect(bool hasChip)
    {
        var column = TextColumn(hasChip);
        return new Rect(column.X, 16, column.Width, 24);
    }

    /// <summary>Body line inside the text column.</summary>
    public static Rect BodyRect(bool hasChip)
    {
        var column = TextColumn(hasChip);
        return new Rect(column.X, 40, column.Width, 20);
    }

    /// <summary>Bottom-left verification-code chip.</summary>
    public static Rect ChipRect => new(86, 68, ChipWidth, 18);

    /// <inheritdoc />
    public override CompactAction HitTest(Point point, bool isPeeked) => CompactAction.Expand;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var hasChip = !string.IsNullOrEmpty(Model.VerificationCode) || !string.IsNullOrEmpty(Model.Badge);

        // Overflow hidden: every glyph/stroke is clipped to the pill, so an overlong string can never
        // paint above the text or outside the surface.
        using var clip = context.PushClip(new RoundedRect(
            CompactPillLayout.Bounds(PillWidth, PillHeight), PillHeight / 2));
        Icons.Draw(context, IconKind.Bell, IconRect, AccentBrush());

        var titleArea = TitleRect(hasChip);
        DrawLine(context, Model.Title, titleArea, Fonts.SemiBold, Typography.TitleSize, IslandBrushes.Foreground);

        var body = Model.Body ?? Model.Subtitle;
        DrawLine(context, body, BodyRect(hasChip), Fonts.Regular, Typography.CaptionSize, IslandBrushes.Secondary);

        if (hasChip)
        {
            var text = Model.VerificationCode ?? Model.Badge!;
            DrawChip(context, text, ChipRect, new SolidColorBrush(Accents.Info, 0.20), IslandBrushes.Foreground);
        }
    }
}

/// <summary>Clipboard preview pill: icon + preview + state chip (264×36).</summary>
public sealed class ClipboardCompactSurface : CompactSurface
{
    /// <summary>Pill width token (DIP).</summary>
    public const double PillWidth = 264;

    /// <summary>Pill height token (DIP).</summary>
    public const double PillHeight = 36;

    /// <summary>Trailing state chip width (DIP).</summary>
    public const double ChipWidth = 52;

    /// <summary>Fixed clipboard slot.</summary>
    public static Rect IconRect => CompactPillLayout.IconRect(PillHeight);

    /// <summary>Single-line text column (zero-width safe).</summary>
    public static Rect TextRect => CompactPillLayout.TextColumn(PillWidth, PillHeight, ChipWidth);

    /// <summary>Trailing state chip rectangle.</summary>
    public static Rect ChipRect => CompactPillLayout.ChipRect(PillWidth, PillHeight, ChipWidth);

    /// <inheritdoc />
    public override CompactAction HitTest(Point point, bool isPeeked) => CompactAction.Expand;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        using var clip = context.PushClip(new RoundedRect(
            CompactPillLayout.Bounds(PillWidth, PillHeight), PillHeight / 2));
        Icons.Draw(context, IconKind.Clipboard, IconRect, AccentBrush());

        DrawLine(context, Model.Body ?? Model.Title, TextRect, Fonts.Regular, Typography.TitleSize,
            IslandBrushes.Foreground);

        var label = string.IsNullOrEmpty(Model.Badge) ? Localizer.Get(LocKeys.Common.Copied) : Model.Badge!;
        DrawChip(context, label, ChipRect, new SolidColorBrush(Accents.Success, 0.18), IslandBrushes.Success);
    }
}

/// <summary>Agent pill: sparkle + session title + status (236×36).</summary>
public sealed class AgentCompactSurface : CompactSurface
{
    /// <inheritdoc />
    public override CompactAction HitTest(Point point, bool isPeeked) => CompactAction.Expand;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        const double width = 236;
        const double height = 36;
        const double inset = 12;
        var iconArea = new Rect(inset, (height - 16) / 2, 16, 16);
        Icons.Draw(context, IconKind.Agent, iconArea, IslandBrushes.Info);

        var statusWidth = 78.0;
        var textArea = new Rect(iconArea.Right + 8, 0, width - (iconArea.Right + 8) - inset - statusWidth - 6, height);
        DrawLine(context, Model.Title, textArea, Fonts.SemiBold, Typography.TitleSize, IslandBrushes.Foreground);

        var status = Model.StatusText ?? Model.Subtitle;
        var statusArea = new Rect(width - inset - statusWidth, 0, statusWidth - 10, height);
        DrawLine(context, status, statusArea, Fonts.Regular, Typography.CaptionSize, ToneBrush(Model.Tone),
            TextAlignment.Right);
        context.DrawEllipse(ToneBrush(Model.Tone), null,
            new Point(width - inset - 3, height / 2), 3, 3);
    }
}

/// <summary>Timer pill: clock icon + countdown + label/progress (190×36).</summary>
public sealed class TimerCompactSurface : CompactSurface
{
    /// <inheritdoc />
    public override CompactAction HitTest(Point point, bool isPeeked) => CompactAction.Expand;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        const double width = 190;
        const double height = 36;
        const double inset = 12;
        var accent = AccentBrush();
        var iconArea = new Rect(inset, (height - 16) / 2, 16, 16);
        Icons.Draw(context, IconKind.Clock, iconArea, accent);

        var text = Model.TimerText ?? Model.Title;
        var valueArea = new Rect(iconArea.Right + 8, 0, 62, height);
        DrawLine(context, text, valueArea, Fonts.MonoSemiBold, Typography.TitleSize, IslandBrushes.Foreground);

        var labelArea = new Rect(valueArea.Right + 4, 0, width - valueArea.Right - 4 - inset, height);
        DrawLine(context, Model.StatusText ?? Localizer.Get(LocKeys.Timer.Pomodoro), labelArea,
            Fonts.Regular, Typography.CaptionSize, IslandBrushes.Secondary, TextAlignment.Right);

        if (Model.ProgressPercent is { } progress)
        {
            var track = new Rect(valueArea.X, height - 6, width - valueArea.X - inset, 2);
            DrawTrack(context, track, progress / 100.0, accent,
                new SolidColorBrush(Color.FromArgb(0xFF, 0x23, 0x23, 0x2A)));
        }
    }
}

/// <summary>Low-battery pill: battery icon + percentage + warning (208×36).</summary>
public sealed class BatteryCompactSurface : CompactSurface
{
    /// <inheritdoc />
    public override CompactAction HitTest(Point point, bool isPeeked) => CompactAction.Expand;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        const double width = 208;
        const double height = 36;
        const double inset = 12;
        var level = Math.Clamp(Model.Level ?? Model.BatteryPercent ?? 0, 0, 100);
        var tone = Model.IsCharging ? IslandBrushes.Success : IslandBrushes.Danger;
        var iconArea = new Rect(inset, (height - 16) / 2, 16, 16);
        Icons.Draw(context, Model.IsCharging ? IconKind.Bolt : IconKind.Battery, iconArea, tone);

        var valueArea = new Rect(iconArea.Right + 8, 0, 36, height);
        DrawValue(context, Localizer.Format(LocKeys.Hud.Value, ("value", level)), valueArea,
            tone, Typography.TitleSize, TextAlignment.Left);

        var statusArea = new Rect(valueArea.Right + 6, 0, 88, height);
        DrawLine(context, Model.StatusText ?? Localizer.Get(LocKeys.Battery.Low), statusArea,
            Fonts.Regular, Typography.CaptionSize, tone);

        if (!string.IsNullOrEmpty(Model.RemainingText))
        {
            var remaining = new Rect(width - inset - 52, 0, 52, height);
            DrawLine(context, Model.RemainingText, remaining, Fonts.Regular, Typography.CaptionSize,
                IslandBrushes.Secondary, TextAlignment.Right);
        }
    }
}

/// <summary>
/// Generic compact surface for the remaining activity classes (lookup, lock keys, device, bluetooth,
/// focus, progress): icon + primary line (+ secondary line on 44 DIP surfaces). Sizes come from the
/// Core layout tokens so every activity keeps its exact compact token.
/// </summary>
public sealed class BasicCompactSurface : CompactSurface
{
    private readonly IconKind _icon;

    /// <summary>Creates the surface.</summary>
    public BasicCompactSurface(CompactStyle style, IconKind icon)
    {
        _icon = icon;
        UpdateModel(new CompactContentModel { Style = style, Icon = icon });
    }

    /// <inheritdoc />
    public override CompactAction HitTest(Point point, bool isPeeked) => CompactAction.Expand;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var size = CompactSize;
        var width = size.Width;
        var height = size.Height;
        const double inset = 12;
        var iconArea = new Rect(inset, (height - 16) / 2, 16, 16);
        var accent = Model.Style == CompactStyle.Focus ? IslandBrushes.Secondary : AccentBrush();
        Icons.Draw(context, _icon, iconArea, accent);

        var textX = iconArea.Right + 8;
        var textWidth = width - textX - inset - (Model.Badge is { Length: > 0 } ? 50 : 0);
        if (height >= 42)
        {
            DrawLine(context, Model.Title, new Rect(textX, 7, textWidth, 16), Fonts.SemiBold,
                Typography.TitleSize, IslandBrushes.Foreground);
            DrawLine(context, Model.Subtitle ?? Model.StatusText, new Rect(textX, 23, textWidth, 14),
                Fonts.Regular, Typography.CaptionSize, ToneBrush(Model.Tone));
        }
        else
        {
            DrawLine(context, Model.Title, new Rect(textX, 0, textWidth, height), Fonts.SemiBold,
                Typography.TitleSize, IslandBrushes.Foreground);
            if (!string.IsNullOrEmpty(Model.StatusText))
            {
                DrawLine(context, Model.StatusText, new Rect(width - inset - 60, 0, 60, height),
                    Fonts.Regular, Typography.CaptionSize, ToneBrush(Model.Tone), TextAlignment.Right);
            }
        }

        if (Model.Style == CompactStyle.Progress && Model.ProgressPercent is { } progress)
        {
            var track = new Rect(textX, height - 6, width - textX - inset, 2);
            DrawTrack(context, track, progress / 100.0, accent,
                new SolidColorBrush(Color.FromArgb(0xFF, 0x23, 0x23, 0x2A)));
        }
    }
}
