using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.Theme;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;

namespace FluidBar.App.Island;

/// <summary>Semantic tone for compact status text (one accent per state, spec 3.3).</summary>
public enum StatusTone
{
    /// <summary>Neutral/secondary text.</summary>
    Neutral,

    /// <summary>Informational blue.</summary>
    Info,

    /// <summary>Success green.</summary>
    Success,

    /// <summary>Warning orange.</summary>
    Warning,

    /// <summary>Danger red.</summary>
    Danger,
}

/// <summary>What a click on a compact surface means.</summary>
public enum CompactAction
{
    /// <summary>No interactive target.</summary>
    None,

    /// <summary>Expand the island (anywhere on the pill).</summary>
    Expand,

    /// <summary>Play/pause the media session.</summary>
    TogglePlay,

    /// <summary>Previous track (peek only).</summary>
    Previous,

    /// <summary>Next track (peek only).</summary>
    Next,
}

/// <summary>
/// Base class for custom-drawn compact island surfaces. Drawing is imperative so the compact content
/// never participates in per-frame layout: the shell animates around it and the surface repaints only
/// when its data, language or theme changes.
/// </summary>
public abstract class CompactSurface : Control, ILocalizable
{
    private CompactContentModel _model = new() { Style = CompactStyle.Idle };
    private ILocalizer? _localizer;

    /// <summary>Current surface data.</summary>
    public CompactContentModel Model
    {
        get => _model;
        protected set => _model = value;
    }

    /// <summary>Current localizer (never null after the first <see cref="ApplyLocalization"/>).</summary>
    protected ILocalizer Localizer => _localizer ?? LocalizationRuntime.Current;

    /// <summary>Compact size token for <paramref name="style"/> (Core layout tokens).</summary>
    public static VisualSize SizeFor(CompactStyle style) => LayoutTokens.CompactSize(style switch
    {
        CompactStyle.Idle => null,
        CompactStyle.Media => ActivityKind.Media,
        CompactStyle.Hud => ActivityKind.Volume,
        CompactStyle.Notification => ActivityKind.Notification,
        CompactStyle.Clipboard => ActivityKind.Clipboard,
        CompactStyle.Agent => ActivityKind.Agent,
        CompactStyle.Timer => ActivityKind.Timer,
        CompactStyle.Battery => ActivityKind.BatteryLow,
        CompactStyle.LockKeys => ActivityKind.LockKeys,
        CompactStyle.Device => ActivityKind.Device,
        CompactStyle.Bluetooth => ActivityKind.Bluetooth,
        CompactStyle.Focus => ActivityKind.Focus,
        CompactStyle.Progress => ActivityKind.Progress,
        CompactStyle.Lookup => ActivityKind.Lookup,
        _ => null,
    });

    /// <summary>Compact size of this surface instance.</summary>
    public VisualSize CompactSize => SizeFor(_model.Style);

    /// <summary>Applies new data; repaints only when something visible changed.</summary>
    public virtual void UpdateModel(CompactContentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public virtual void ApplyLocalization(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
        InvalidateVisual();
    }

    /// <summary>Maps a pointer position (surface-local DIP) to an action.</summary>
    public abstract CompactAction HitTest(Point point, bool isPeeked);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = CompactSize;
        return new Size(size.Width, size.Height);
    }

    /// <summary>Resolves the accent brush for the current model.</summary>
    protected IBrush AccentBrush()
        => new SolidColorBrush(Accents.ParseOrDefault(_model.AccentHex, Accents.Fallback));

    /// <summary>Draws one line of text with character ellipsis and optional alignment.</summary>
    protected static void DrawLine(
        DrawingContext context,
        string? text,
        Rect area,
        Typeface typeface,
        double fontSize,
        IBrush brush,
        TextAlignment alignment = TextAlignment.Left,
        double opacity = 1.0)
    {
        if (string.IsNullOrEmpty(text) || area.Width <= 1 || opacity <= 0.01)
        {
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush)
        {
            MaxTextWidth = Math.Max(1, area.Width),
            MaxTextHeight = Math.Max(1, area.Height),
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };
        using var state = context.PushOpacity(opacity);
        var y = area.Y + ((area.Height - formatted.Height) / 2);
        context.DrawText(formatted, new Point(area.X, y));
    }

    /// <summary>Draws a rounded badge chip with centered text.</summary>
    protected static void DrawChip(
        DrawingContext context,
        string text,
        Rect area,
        IBrush background,
        IBrush foreground,
        double fontSize = Typography.CaptionSize)
    {
        context.DrawRectangle(background, null, new RoundedRect(area, area.Height / 2));
        DrawLine(context, text, area, Fonts.SemiBold, fontSize, foreground, TextAlignment.Center);
    }

    /// <summary>Draws a tabular numeric value.</summary>
    protected static void DrawValue(
        DrawingContext context,
        string text,
        Rect area,
        IBrush brush,
        double fontSize = Typography.HudValueSize,
        TextAlignment alignment = TextAlignment.Right)
        => DrawLine(context, text, area, Fonts.MonoSemiBold, fontSize, brush, alignment);

    /// <summary>Draws the progress track (4 DIP high, radius 2) used by HUD/timer/progress.</summary>
    protected static void DrawTrack(DrawingContext context, Rect area, double fraction, IBrush accent, IBrush background, double muted = 0)
    {
        var radius = area.Height / 2;
        context.DrawRectangle(background, null, new RoundedRect(area, radius));
        if (muted > 0)
        {
            context.DrawRectangle(null, new Pen(accent, Math.Max(1, area.Height / 2)), new RoundedRect(area, radius));
            return;
        }

        var clamped = Math.Clamp(fraction, 0, 1);
        if (clamped > 0)
        {
            var width = Math.Max(radius * 2, area.Width * clamped);
            context.DrawRectangle(accent, null, new RoundedRect(new Rect(area.X, area.Y, width, area.Height), radius));
        }
    }

    /// <summary>Draws four static waveform bars (rendering-only, no animation loop).</summary>
    protected static void DrawWave(DrawingContext context, Rect area, IBrush accent, bool active)
    {
        var heights = active ? new[] { 0.45, 0.9, 0.62, 0.78 } : new[] { 0.3, 0.55, 0.4, 0.5 };
        var gap = 2.0;
        var barWidth = Math.Max(1.5, (area.Width - (gap * (heights.Length - 1))) / heights.Length);
        for (var i = 0; i < heights.Length; i++)
        {
            var height = area.Height * heights[i];
            var x = area.X + (i * (barWidth + gap));
            var y = area.Y + ((area.Height - height) / 2);
            context.DrawRectangle(accent, null, new RoundedRect(new Rect(x, y, barWidth, height), barWidth / 2));
        }
    }

    /// <summary>Draws a rounded artwork placeholder using the accent color.</summary>
    protected void DrawArtwork(DrawingContext context, Rect area)
    {
        var accent = Accents.ParseOrDefault(_model.AccentHex, Accents.Track);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(accent, 0),
                new GradientStop(Color.FromArgb(0xFF, 0x1A, 0x1A, 0x22), 1),
            },
        };
        context.DrawRectangle(brush, null, new RoundedRect(area, LayoutTokens.ArtSmallRadius));
        Icons.Draw(context, IconKind.Media, area.Deflate(area.Width * 0.22), new SolidColorBrush(Colors.White, 0.85));
    }

    /// <summary>Current status tone brush.</summary>
    protected static IBrush ToneBrush(StatusTone tone) => tone switch
    {
        StatusTone.Info => IslandBrushes.Info,
        StatusTone.Success => IslandBrushes.Success,
        StatusTone.Warning => IslandBrushes.Warning,
        StatusTone.Danger => IslandBrushes.Danger,
        _ => IslandBrushes.Secondary,
    };
}
