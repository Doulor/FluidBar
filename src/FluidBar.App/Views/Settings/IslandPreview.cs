using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Island;
using FluidBar.App.Localization;
using FluidBar.App.Rendering;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.Core.Layout;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Live miniature island preview for the Appearance section (prototype decision 9): reflects palette,
/// accent, fill opacity, radius and font scale immediately as the user drags a slider.
/// </summary>
public sealed class IslandPreview : Control
{
    private AppSettings _settings = AppSettings.Default;
    private ILocalizer? _localizer;

    /// <summary>Creates the preview.</summary>
    public IslandPreview()
    {
        Height = 96;
    }

    /// <summary>Applies a settings snapshot and repaints.</summary>
    public void Update(AppSettings settings, ILocalizer localizer)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(availableSize.Width is > 1 and < 1000 ? availableSize.Width : 320, 96);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = 220.0 * Math.Clamp(_settings.FontScale, 0.8, 1.6);
        var height = 48.0;
        var x = (Bounds.Width - width) / 2;
        var y = (Bounds.Height - height) / 2;
        var rect = new Rect(x, y, width, height);
        var radii = CornerRadii.Uniform(height / 2);
        var geometry = SquircleGeometryFactory.GetOrCreate(width, height, radii);
        var palette = _settings.Colors;
        var accent = FluidBar.App.Theme.Accents.ParseOrDefault(_settings.AccentFallback, FluidBar.App.Theme.Accents.Fallback);

        using (context.PushTransform(Matrix.CreateTranslation(rect.X, rect.Y)))
        {
            var shadow = new SolidColorBrush(Colors.Black, 0.30);
            using (context.PushTransform(Matrix.CreateTranslation(0, 6)))
            {
                context.DrawGeometry(shadow, null, geometry);
            }

            var fill = new SolidColorBrush(palette.Background, Math.Clamp(_settings.FillOpacity, LayoutTokens.MinFillOpacity, 1));
            context.DrawGeometry(fill, new Pen(new SolidColorBrush(palette.LineStrong), 1), geometry);

            var clock = DateTimeOffset.Now.ToString("HH:mm", CultureInfo.CurrentCulture);
            var time = new FormattedText(clock, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Fonts.MonoSemiBold, 13.5 * Math.Clamp(_settings.FontScale, 0.8, 1.6), new SolidColorBrush(palette.Foreground));
            context.DrawText(time, new Point(14, (height - time.Height) / 2));

            var caption = new FormattedText(
                _localizer?.Get(LocKeys.Settings.Appearance.PreviewHint) ?? string.Empty,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Fonts.Regular,
                11 * Math.Clamp(_settings.FontScale, 0.8, 1.6),
                new SolidColorBrush(palette.ForegroundSecondary))
            {
                MaxTextWidth = Math.Max(1, width - time.Width - 48),
                Trimming = TextTrimming.CharacterEllipsis,
            };
            context.DrawText(caption, new Point(time.Width + 24, (height - caption.Height) / 2));

            context.DrawEllipse(new SolidColorBrush(accent), null, new Point(width - 16, height / 2), 4, 4);
        }
    }
}
