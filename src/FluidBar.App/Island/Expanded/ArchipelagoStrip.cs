using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.State;
using FluidBar.App.Theme;
using FluidBar.Core.Activities;

namespace FluidBar.App.Island.Expanded;

/// <summary>
/// Page Archipelago (user-chosen navigation variant A, 2026-10-18): a floating mini-island strip that
/// replaces the page dots. Hidden at rest; revealed by drag/wheel and by hovering the bottom 12 DIP
/// for 300 ms; auto-hides 1.2 s after the last activity. Six 48×26 mini-islands (gap 8, radius 13),
/// active one grows to 72×30 with the localized page name, and each carries a live micro-preview.
/// </summary>
public sealed class ArchipelagoStrip : Control
{
    /// <summary>Strip height (DIP).</summary>
    public const double StripHeight = 34;

    /// <summary>Inactive mini-island width (DIP).</summary>
    public const double MiniWidth = 48;

    /// <summary>Inactive mini-island height (DIP).</summary>
    public const double MiniHeight = 26;

    /// <summary>Active mini-island width (DIP).</summary>
    public const double ActiveWidth = 72;

    /// <summary>Active mini-island height (DIP).</summary>
    public const double ActiveHeight = 30;

    /// <summary>Gap between mini-islands (DIP).</summary>
    public const double Gap = 8;

    /// <summary>Corner radius (DIP).</summary>
    public const double Radius = 13;

    /// <summary>Distance from the shell bottom (DIP).</summary>
    public const double BottomOffset = 14;

    /// <summary>Reveal delay when hovering the bottom 12 DIP (ms).</summary>
    public const int HoverRevealMs = 300;

    /// <summary>Auto-hide delay after the last interaction (ms).</summary>
    public const int AutoHideMs = 1200;

    /// <summary>Total strip width including the active-island growth (DIP).</summary>
    public const double TotalWidth = (6 * MiniWidth) + (5 * Gap) + (ActiveWidth - MiniWidth);

    private static readonly IconKind[] Icons =
    [
        IconKind.Home,
        IconKind.Media,
        IconKind.Bell,
        IconKind.Clipboard,
        IconKind.Agent,
        IconKind.Grid,
    ];

    private ExpandedPageContext? _context;
    private IReadOnlyList<string> _titles = [];
    private double _pageOffset;
    private double _visibility;
    private long _nowMs;

    /// <summary>The strip rectangle in panel-local DIP.</summary>
    public Rect StripRect
    {
        get
        {
            var width = Bounds.Width > 1 ? Bounds.Width : ExpandedPanel.PanelWidth;
            var height = Bounds.Height > 1 ? Bounds.Height : ExpandedPanel.PanelHeight;
            var x = (width - TotalWidth) / 2;
            var y = height - BottomOffset - StripHeight;
            return new Rect(x, y, TotalWidth, StripHeight);
        }
    }

    /// <summary>Current visibility 0..1 (fade-in/out).</summary>
    public double Visibility => _visibility;

    /// <summary>Updates strip data and animation state; repaints.</summary>
    public void Update(
        ExpandedPageContext? context,
        IReadOnlyList<string> titles,
        double pageOffset,
        double visibility,
        long nowMs)
    {
        _context = context;
        _titles = titles;
        _pageOffset = pageOffset;
        _visibility = Math.Clamp(visibility, 0, 1);
        _nowMs = nowMs;
        InvalidateVisual();
    }

    /// <summary>Whether a panel-local point lies inside the strip's scrim.</summary>
    public bool Contains(Point point) => StripRect.Inflate(new Thickness(8, 4)).Contains(point);

    /// <summary>Hit-tests the mini-islands; returns the page index or -1.</summary>
    public int HitTest(Point point)
    {
        // Visibility gating is the panel's job (it only routes strip input while the strip is shown);
        // the strip itself must stay hittable during its fade-in so the first tap is never lost.
        for (var i = 0; i < 6; i++)
        {
            if (IslandRect(i).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Island rectangle for page <paramref name="index"/> (active island uses the current offset).</summary>
    public Rect IslandRect(int index)
    {
        var strip = StripRect;
        var active = Math.Clamp((int)Math.Round(_pageOffset, MidpointRounding.AwayFromZero), 0, 5);
        var interpolatedActiveX = strip.X + (_pageOffset * (MiniWidth + Gap));
        var baseX = strip.X + (index * (MiniWidth + Gap));
        if (index == active)
        {
            return new Rect(
                Math.Clamp(interpolatedActiveX, strip.X, strip.Right - ActiveWidth),
                strip.Y + ((StripHeight - ActiveHeight) / 2),
                ActiveWidth,
                ActiveHeight);
        }

        return new Rect(
            baseX,
            strip.Y + ((StripHeight - MiniHeight) / 2),
            MiniWidth,
            MiniHeight);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(ExpandedPanel.PanelWidth, ExpandedPanel.PanelHeight);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (_visibility <= 0.01 || _context is null)
        {
            return;
        }

        var palette = _context.Settings.Colors;
        using var fade = context.PushOpacity(_visibility);
        var strip = StripRect;

        // Subtle scrim floating above the bottom edge.
        var scrim = strip.Inflate(new Thickness(10, 5));
        context.DrawRectangle(
            new SolidColorBrush(palette.Background, 0.80),
            new Pen(new SolidColorBrush(palette.LineStrong, 0.7), 1),
            new RoundedRect(scrim, 18));

        var active = Math.Clamp((int)Math.Round(_pageOffset, MidpointRounding.AwayFromZero), 0, 5);
        for (var i = 0; i < 6; i++)
        {
            var rect = IslandRect(i);
            if (i == active)
            {
                context.DrawRectangle(
                    new SolidColorBrush(Accents.Fallback, 0.18),
                    new Pen(new SolidColorBrush(Accents.Fallback, 0.55), 1),
                    new RoundedRect(rect, Radius));
                var name = _titles.Count > i ? _titles[i] : string.Empty;
                DrawText(context, name, rect.Deflate(new Thickness(8, 0)), Fonts.SemiBold, 10.5,
                    new SolidColorBrush(palette.Foreground), TextAlignment.Center);
                continue;
            }

            context.DrawRectangle(
                new SolidColorBrush(palette.BackgroundRaised, 0.92),
                new Pen(new SolidColorBrush(palette.Line), 1),
                new RoundedRect(rect, Radius));
            DrawPreview(context, i, rect.Deflate(new Thickness(5, 3)), palette);
        }
    }

    private void DrawPreview(DrawingContext context, int index, Rect area, IslandColors palette)
    {
        var content = _context!.Content;
        var secondary = new SolidColorBrush(palette.ForegroundSecondary);
        var foreground = new SolidColorBrush(palette.Foreground);
        switch (index)
        {
            case 0:
                DrawText(context, content.Now.ToString("HH:mm", CultureInfo.CurrentCulture), area,
                    Fonts.MonoSemiBold, 9.5, foreground, TextAlignment.Center);
                break;
            case 1:
            {
                var art = new Rect(area.X, area.Y + 2, 16, 16);
                context.DrawRectangle(new SolidColorBrush(Accents.Track), null, new RoundedRect(art, 4));
                var waveX = art.Right + 4;
                var heights = new[] { 0.5, 0.9, 0.65 };
                for (var bar = 0; bar < 3; bar++)
                {
                    var height = (area.Height - 6) * heights[bar];
                    context.DrawRectangle(new SolidColorBrush(Accents.Track),
                        null,
                        new RoundedRect(
                            new Rect(waveX + (bar * 4), area.Center.Y - (height / 2), 2, height),
                            1));
                }

                break;
            }

            case 2:
                context.DrawEllipse(new SolidColorBrush(Accents.Danger), null, new Point(area.X + 4, area.Y + 4), 2.5, 2.5);
                context.DrawRectangle(secondary, null, new RoundedRect(new Rect(area.X, area.Y + 9, area.Width, 2), 1));
                context.DrawRectangle(secondary, null, new RoundedRect(new Rect(area.X, area.Y + 14, area.Width * 0.7, 2), 1));
                break;
            case 3:
                context.DrawRectangle(secondary, null, new RoundedRect(new Rect(area.X, area.Y + 2, area.Width, 2), 1));
                context.DrawRectangle(secondary, null, new RoundedRect(new Rect(area.X, area.Y + 8, area.Width * 0.8, 2), 1));
                context.DrawRectangle(secondary, null, new RoundedRect(new Rect(area.X, area.Y + 14, area.Width * 0.6, 2), 1));
                break;
            case 4:
            {
                var pulse = 0.55 + (0.45 * Math.Sin(_nowMs / 300.0));
                var center = new Point(area.Center.X, area.Center.Y);
                context.DrawEllipse(new SolidColorBrush(Accents.Info, 0.25 + (0.2 * pulse)), null, center, 6, 6);
                context.DrawEllipse(new SolidColorBrush(Accents.Info, 0.6 + (0.4 * pulse)), null, center, 2.6, 2.6);
                break;
            }

            case 5:
            {
                var cell = 5.0;
                for (var row = 0; row < 3; row++)
                {
                    for (var column = 0; column < 4; column++)
                    {
                        var selected = row == 1 && column == 2;
                        context.DrawRectangle(
                            new SolidColorBrush(selected ? Accents.Fallback : palette.ForegroundTertiary, selected ? 0.9 : 0.4),
                            null,
                            new RoundedRect(
                                new Rect(area.X + 2 + (column * (cell + 2)), area.Y + 4 + (row * (cell + 1)), cell, cell),
                                1.2));
                    }
                }

                break;
            }
        }

    }

    private static void DrawText(
        DrawingContext context,
        string? text,
        Rect area,
        Typeface typeface,
        double fontSize,
        IBrush brush,
        TextAlignment alignment)
    {
        if (string.IsNullOrEmpty(text) || area.Width <= 1)
        {
            return;
        }

        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, fontSize, brush)
        {
            MaxTextWidth = Math.Max(1, area.Width),
            MaxTextHeight = Math.Max(1, area.Height),
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };
        context.DrawText(formatted, new Point(area.X, area.Y + ((area.Height - formatted.Height) / 2)));
    }
}
