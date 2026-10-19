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
/// Live appearance preview (<c>.appearance-preview</c> → <c>.ap-canvas</c> → <c>.ap-shell</c>): an 84 px
/// gradient canvas holding the prototype's 300×62 shell, which reflects palette, accent, fill opacity,
/// panel radius and font scale immediately as the user drags a slider.
/// </summary>
public sealed class IslandPreview : Control
{
    private AppSettings _settings = AppSettings.Default;
    private ILocalizer? _localizer;

    /// <summary>Creates the preview (.ap-canvas height).</summary>
    public IslandPreview()
    {
        Height = SettingsMetrics.ApCanvasHeight;
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
        => new(availableSize.Width is > 1 and < 4000 ? availableSize.Width : 560, SettingsMetrics.ApCanvasHeight);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;

        // .ap-canvas background: linear wash + two radial glows.
        context.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.55, 1, RelativeUnit.Relative),
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
        context.FillRectangle(new RadialGradientBrush
        {
            Center = new RelativePoint(0.92, 1.04, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.92, 1.04, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.3, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(51, 160, 110, 88), 0),
                new GradientStop(Color.FromArgb(0, 160, 110, 88), 0.62),
            },
        }, new Rect(0, 0, width, height));

        var palette = _settings.Colors;
        var accent = Accents.ParseOrDefault(_settings.AccentFallback, Accents.Fallback);
        var scale = Math.Clamp(_settings.FontScale, 0.8, 1.6);
        var shellWidth = SettingsMetrics.ApShellWidth * scale;
        var shellHeight = SettingsMetrics.ApShellHeight;
        var shell = new Rect((width - shellWidth) / 2, (height - shellHeight) / 2, shellWidth, shellHeight);
        var radius = Math.Clamp(_settings.PanelRadius, 0, shellHeight / 2);

        // box-shadow: 0 18px 36px -18px rgba(0,0,0,.8)
        for (var i = 3; i >= 1; i--)
        {
            var spread = (i * 2.0);
            context.DrawRectangle(
                new SolidColorBrush(Colors.Black, 0.14),
                null,
                new Rect(shell.X + (spread / 2), shell.Y + (spread / 2) + 3, shell.Width - spread, shell.Height),
                Math.Max(0, radius - (spread / 2)),
                Math.Max(0, radius - (spread / 2)));
        }

        var fill = Math.Clamp(_settings.FillOpacity, LayoutTokens.MinFillOpacity, 1);
        context.DrawRectangle(
            new SolidColorBrush(Color.Parse("#08080A"), fill),
            new Pen(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), 1),
            shell,
            radius,
            radius);

        using (context.PushClip(new RoundedRect(shell, radius)))
        {
            // .ap-art 34×34, 8 px radius, gradient from the accent.
            var art = new Rect(
                shell.X + SettingsMetrics.ApShellPadX,
                shell.Center.Y - (SettingsMetrics.ApArtSize / 2),
                SettingsMetrics.ApArtSize,
                SettingsMetrics.ApArtSize);
            context.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(accent, 0),
                    new GradientStop(Color.FromArgb(255, 40, 44, 60), 1),
                },
            }, null, art, SettingsMetrics.ApArtRadius, SettingsMetrics.ApArtRadius);

            // .ap-copy b 18 px / 700 display, span 8.5 px --ui-fg-2.
            var textLeft = art.Right + SettingsMetrics.ApShellGap;
            var clock = new FormattedText(
                DateTimeOffset.Now.ToString("HH:mm", CultureInfo.CurrentCulture),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(Fonts.Display, FontStyle.Normal, FontWeight.Bold),
                SettingsMetrics.ApClockSize * scale,
                Brushes.White);
            context.DrawText(clock, new Point(textLeft, shell.Center.Y - clock.Height + 1));

            var track = new FormattedText(
                "Afterglow · Nova Lane",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Fonts.Regular,
                SettingsMetrics.ApTrackSize,
                new SolidColorBrush(Color.Parse("#9B9BA5")));
            context.DrawText(track, new Point(textLeft, shell.Center.Y + 2));

            // .ap-wave: four 2 px bars (4/11/7/13 px, 90/75/90/60% opacity).
            var playCenterX = shell.Right - SettingsMetrics.ApShellPadX - (SettingsMetrics.ApPlaySize / 2);
            var waveRight = playCenterX - (SettingsMetrics.ApPlaySize / 2) - 8;
            var heights = new[] { 4.0, 11.0, 7.0, 13.0 };
            var opacities = new[] { 0.9, 0.75, 0.9, 0.6 };
            for (var i = 0; i < heights.Length; i++)
            {
                var barHeight = heights[i];
                context.DrawRectangle(
                    new SolidColorBrush(accent, opacities[i]),
                    null,
                    new Rect(waveRight - ((heights.Length - i) * 4), shell.Center.Y - (barHeight / 2), 2, barHeight),
                    1,
                    1);
            }

            // .ap-play 25×25 circle at rgba(255,255,255,.10) with a pause glyph.
            var playCenter = new Point(playCenterX, shell.Center.Y);
            context.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
                null,
                playCenter,
                SettingsMetrics.ApPlaySize / 2,
                SettingsMetrics.ApPlaySize / 2);
            context.DrawRectangle(Brushes.White, null, new Rect(playCenter.X - 3.5, playCenter.Y - 5, 2.5, 10), 1, 1);
            context.DrawRectangle(Brushes.White, null, new Rect(playCenter.X + 1, playCenter.Y - 5, 2.5, 10), 1, 1);

            // .ap-progress: 2 px bar pinned to the bottom edge, 62% accent.
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                new Rect(shell.X, shell.Bottom - 2, shell.Width, 2));
            context.FillRectangle(new SolidColorBrush(accent),
                new Rect(shell.X, shell.Bottom - 2, shell.Width * 0.62, 2));
        }
    }
}
