using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.Island;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Live appearance preview (<c>.appearance-preview</c> → <c>.ap-canvas</c> → <c>.ap-shell</c>): an 84 px
/// gradient canvas holding the prototype's 300×62 shell.
/// <para>
/// Everything inside the shell is painted from <see cref="IslandPreviewModel"/>, which is built by the
/// same <c>SkiaIslandThemeMapper</c> the real island uses. A settings change calls
/// <see cref="Update"/>, which rebuilds the model, raises <see cref="ModelChanged"/> (the pane's meta
/// line) and repaints — no blur timing, no window reopen.
/// </para>
/// </summary>
public sealed class IslandPreview : Control
{
    private ILocalizer? _localizer;

    /// <summary>Creates the preview (.ap-canvas height).</summary>
    public IslandPreview()
    {
        Height = SettingsMetrics.ApCanvasHeight;
        Model = IslandPreviewModel.From(AppSettings.Default);
    }

    /// <summary>Current preview model (settings → island tokens).</summary>
    public IslandPreviewModel Model { get; private set; }

    /// <summary>Completed render passes (test seam: proves the painter actually ran).</summary>
    internal int RenderCount { get; private set; }

    /// <summary>Raised after every model rebuild so callers can refresh derived chrome (meta line).</summary>
    public event Action<IslandPreviewModel>? ModelChanged;

    /// <summary>Applies a settings snapshot and repaints.</summary>
    /// <param name="settings">Live App settings.</param>
    /// <param name="localizer">Localizer for the preview copy (clock/meta).</param>
    public void Update(AppSettings settings, ILocalizer? localizer)
    {
        Model = IslandPreviewModel.From(settings ?? throw new ArgumentNullException(nameof(settings)));
        _localizer = localizer;
        ModelChanged?.Invoke(Model);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => new(availableSize.Width is > 1 and < 4000 ? availableSize.Width : 560, SettingsMetrics.ApCanvasHeight);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        RenderCount++;
        var width = Bounds.Width;
        var height = Bounds.Height;
        _ = _localizer;

        // .ap-canvas background: linear wash + two radial glows (the stage, not the island).
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

        var model = Model;
        var scale = model.FontScale;
        var shellWidth = SettingsMetrics.ApShellWidth * scale;
        var shellHeight = SettingsMetrics.ApShellHeight;
        var shell = new Rect((width - shellWidth) / 2, (height - shellHeight) / 2, shellWidth, shellHeight);
        var radius = model.RadiusFor(shellHeight);

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

        // Shell body: island Background token at the effective fill opacity (glass overrides it, as in
        // the renderer). Border: prototype .ap-shell hairline (foreground @ 8%).
        context.DrawRectangle(
            new SolidColorBrush(model.Background, model.EffectiveFillOpacity),
            new Pen(new SolidColorBrush(WithAlpha(model.Foreground, 0.08)), 1),
            shell,
            radius,
            radius);

        // Simulated glass: same top highlight the renderer paints (white 0x20 → transparent at 35%).
        if (model.LiquidGlass)
        {
            DrawLiquidGlass(context, shell, radius);
        }
        else if (model.Glass)
        {
            context.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF), 0),
                    new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.35),
                },
            }, null, shell, radius, radius);
        }

        using (context.PushClip(new RoundedRect(shell, radius)))
        {
            // .ap-art 34×34, 8 px radius, gradient from the accent to the island ArtEnd token.
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
                    new GradientStop(model.Accent, 0),
                    new GradientStop(model.ArtEnd, 1),
                },
            }, null, art, SettingsMetrics.ApArtRadius, SettingsMetrics.ApArtRadius);

            // .ap-copy b 18 px / 700 display, span 8.5 px secondary foreground.
            var textLeft = art.Right + SettingsMetrics.ApShellGap;
            var clock = new FormattedText(
                DateTimeOffset.Now.ToString("HH:mm", CultureInfo.CurrentCulture),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(Fonts.Display, FontStyle.Normal, FontWeight.Bold),
                SettingsMetrics.ApClockSize * scale,
                new SolidColorBrush(model.Foreground));
            context.DrawText(clock, new Point(textLeft, shell.Center.Y - clock.Height + 1));

            var track = new FormattedText(
                "Afterglow · Nova Lane",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Fonts.Regular,
                SettingsMetrics.ApTrackSize,
                new SolidColorBrush(model.ForegroundSecondary));
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
                    new SolidColorBrush(WithAlpha(model.Accent, opacities[i])),
                    null,
                    new Rect(waveRight - ((heights.Length - i) * 4), shell.Center.Y - (barHeight / 2), 2, barHeight),
                    1,
                    1);
            }

            // .ap-play 25×25 circle at foreground 10% with a pause glyph.
            var playCenter = new Point(playCenterX, shell.Center.Y);
            context.DrawEllipse(
                new SolidColorBrush(WithAlpha(model.Foreground, 0.10)),
                null,
                playCenter,
                SettingsMetrics.ApPlaySize / 2,
                SettingsMetrics.ApPlaySize / 2);
            context.DrawRectangle(new SolidColorBrush(model.Foreground), null,
                new Rect(playCenter.X - 3.5, playCenter.Y - 5, 2.5, 10), 1, 1);
            context.DrawRectangle(new SolidColorBrush(model.Foreground), null,
                new Rect(playCenter.X + 1, playCenter.Y - 5, 2.5, 10), 1, 1);

            // .ap-progress: 2 px bar pinned to the bottom edge, 62% accent over the hairline track.
            context.FillRectangle(new SolidColorBrush(model.Line),
                new Rect(shell.X, shell.Bottom - 2, shell.Width, 2));
            context.FillRectangle(new SolidColorBrush(model.Accent),
                new Rect(shell.X, shell.Bottom - 2, shell.Width * 0.62, 2));
        }
    }

    private static Color WithAlpha(Color color, double alpha)
        => Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255), color.R, color.G, color.B);

    /// <summary>
    /// Tier 2 preview finish: veil, lensing rim, specular top edge and sheen drawn with the island's own
    /// <see cref="LiquidGlassTokens"/> so the 300×62 preview cannot drift from the real shell.
    /// </summary>
    private static void DrawLiquidGlass(DrawingContext context, Rect shell, double radius)
    {
        var band = LiquidGlassTokens.RimBandFor(shell.Height) * 2;

        using (context.PushClip(new RoundedRect(shell, radius)))
        {
            // Body veil: white at the top, shade at the bottom.
            context.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(LiquidGlassTokens.VeilTopAlpha, 0xFF, 0xFF, 0xFF), 0),
                    new GradientStop(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF), 0.38),
                    new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.62),
                    new GradientStop(Color.FromArgb(LiquidGlassTokens.BodyShadeAlpha, 0x00, 0x00, 0x00), 1),
                },
            }, null, shell, radius, radius);

            // Lensing rim: a thick gradient stroke clipped to the inside edge.
            context.DrawRectangle(null, new Pen(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(LiquidGlassTokens.RimLightTopAlpha, 0xFF, 0xFF, 0xFF), 0),
                    new GradientStop(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), 0.26),
                    new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.55),
                    new GradientStop(Color.FromArgb(0x22, 0x00, 0x00, 0x00), 0.84),
                    new GradientStop(Color.FromArgb(LiquidGlassTokens.RimShadeBottomAlpha, 0x00, 0x00, 0x00), 1),
                },
            }, band), shell, radius, radius);

            // Specular top edge.
            context.DrawRectangle(null, new Pen(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(LiquidGlassTokens.SpecularTopAlpha, 0xFF, 0xFF, 0xFF), 0),
                    new GradientStop(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 0.02),
                    new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.12),
                },
            }, 1.15), shell, radius, radius);

            // Sheen band.
            context.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.72, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.08),
                    new GradientStop(Color.FromArgb(LiquidGlassTokens.SheenAlpha, 0xFF, 0xFF, 0xFF), 0.44),
                    new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.78),
                },
            }, null, shell, radius, radius);
        }
    }

}
