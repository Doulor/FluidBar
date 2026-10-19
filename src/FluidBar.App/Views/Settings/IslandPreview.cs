using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.Core.Layout;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Live miniature island preview for the Appearance section (prototype appearance preview): a gradient
/// canvas holding a 300×62 shell that reflects palette, accent, fill opacity, radius and font scale
/// immediately as the user drags a slider.
/// </summary>
public sealed class IslandPreview : Control
{
    private AppSettings _settings = AppSettings.Default;
    private ILocalizer? _localizer;

    /// <summary>Creates the preview.</summary>
    public IslandPreview()
    {
        Height = 104;
    }

    /// <summary>Applies a settings snapshot and repaints.</summary>
    public void Update(AppSettings settings, ILocalizer localizer)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => new(availableSize.Width is > 1 and < 4000 ? availableSize.Width : 560, 104);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;

        // Prototype .ap-canvas: a cool radial wash over a dark diagonal gradient.
        context.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.4, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#162433"), 0),
                new GradientStop(Color.Parse("#0B131C"), 0.55),
                new GradientStop(Color.Parse("#060B11"), 1),
            },
        }, new Rect(0, 0, width, height));
        context.FillRectangle(new RadialGradientBrush
        {
            Center = new RelativePoint(0.14, -0.08, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.14, -0.08, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(1.2, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(115, 78, 140, 188), 0),
                new GradientStop(Color.FromArgb(0, 78, 140, 188), 0.58),
            },
        }, new Rect(0, 0, width, height));

        var palette = _settings.Colors;
        var accent = Accents.ParseOrDefault(_settings.AccentFallback, Accents.Fallback);
        var scale = Math.Clamp(_settings.FontScale, 0.8, 1.6);
        var shellWidth = Math.Min(300 * scale, width - 40);
        var shellHeight = 62 * Math.Clamp(scale, 0.9, 1.2);
        var shell = new Rect((width - shellWidth) / 2, (height - shellHeight) / 2, shellWidth, shellHeight);
        var radius = Math.Min(shellHeight / 2, 18);

        context.DrawRectangle(new SolidColorBrush(Colors.Black, 0.45), null,
            new Rect(shell.X, shell.Y + 10, shell.Width, shell.Height), radius, radius);
        context.DrawRectangle(
            new SolidColorBrush(palette.Background, Math.Clamp(_settings.FillOpacity, LayoutTokens.MinFillOpacity, 1)),
            null, shell, radius, radius);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), null, shell, radius, radius);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(palette.LineStrong), 1), shell, radius, radius);

        // Artwork tile.
        var artSize = 34 * Math.Clamp(scale, 0.9, 1.15);
        var art = new Rect(shell.X + 12, shell.Center.Y - (artSize / 2), artSize, artSize);
        context.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(accent, 0),
                new GradientStop(Color.FromArgb(255, 40, 44, 60), 1),
            },
        }, null, art, 8, 8);

        // Clock + track line.
        var clock = new FormattedText(DateTimeOffset.Now.ToString("HH:mm", CultureInfo.CurrentCulture),
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Fonts.DisplaySemiBold, 17 * scale,
            new SolidColorBrush(palette.Foreground));
        var textLeft = art.Right + 10;
        context.DrawText(clock, new Point(textLeft, shell.Center.Y - clock.Height + 2));

        var track = new FormattedText("Afterglow · Nova Lane", CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, Fonts.Regular, 8.5 * scale, new SolidColorBrush(Color.Parse("#9B9BA5")));
        context.DrawText(track, new Point(textLeft, shell.Center.Y + 3));

        // Waveform + transport on the right.
        var waveRight = shell.Right - 46;
        var heights = new[] { 4.0, 11.0, 7.0, 13.0 };
        var opacities = new[] { 0.9, 0.75, 0.9, 0.6 };
        for (var i = 0; i < heights.Length; i++)
        {
            var barHeight = heights[i] * Math.Clamp(scale, 0.9, 1.2);
            context.DrawRectangle(new SolidColorBrush(accent, opacities[i]), null,
                new Rect(waveRight + (i * 4), shell.Center.Y - (barHeight / 2), 2, barHeight), 1, 1);
        }

        var playCenter = new Point(shell.Right - 24, shell.Center.Y);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)), null, playCenter, 12.5, 12.5);
        context.DrawRectangle(new SolidColorBrush(Colors.White), null,
            new Rect(playCenter.X - 3.5, playCenter.Y - 5, 2.5, 10), 1, 1);
        context.DrawRectangle(new SolidColorBrush(Colors.White), null,
            new Rect(playCenter.X + 1, playCenter.Y - 5, 2.5, 10), 1, 1);

        // Progress line pinned to the bottom edge.
        var progress = new Rect(shell.X, shell.Bottom - 2, shell.Width, 2);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), progress);
        context.FillRectangle(new SolidColorBrush(accent), new Rect(progress.X, progress.Y, progress.Width * 0.62, 2));
    }
}
