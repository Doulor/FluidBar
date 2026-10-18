using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Localization;
using FluidBar.App.State;
using FluidBar.App.Theme;
using FluidBar.Core.Activities;

namespace FluidBar.App.Island.Expanded;

/// <summary>
/// Page Archipelago (user-chosen variant A, revised 2026-10-18): six detached mini-islands floating
/// BELOW the expanded island over the desktop — never inside the panel, never covering content.
/// 48×26 islands (gap 8, radius 13), active 72×30 + localized page name, live micro-previews.
/// Motion is render-only via <see cref="ArchipelagoMotion"/> (Core springs): translateY/opacity/scale,
/// no layout, no CSS-style transitions. Only the agent dot and media wave animate, and only while the
/// strip is visible.
/// </summary>
public sealed class ArchipelagoStrip : Control
{
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

    /// <summary>Reveal delay when hovering the bottom band (ms).</summary>
    public const int HoverRevealMs = 300;

    /// <summary>Auto-hide delay after the last interaction (ms).</summary>
    public const int AutoHideMs = 1200;

    /// <summary>
    /// Total strip width including the active-island growth (DIP). The flow layout keeps this constant:
    /// the active island's 24 DIP growth is taken out of its neighbours' widths, never out of the gaps.
    /// </summary>
    public const double TotalWidth = (6 * MiniWidth) + (5 * Gap) + (ActiveWidth - MiniWidth);

    /// <summary>Strip height (DIP) — detached islands, no internal scrim.</summary>
    public const double StripHeight = 34;

    private ExpandedPageContext? _context;
    private IReadOnlyList<string> _titles = [];
    private ArchipelagoMotion? _motion;
    private double _previewPhase;

    /// <summary>The strip rectangle in panel-local DIP (below the expanded shell).</summary>
    public Rect StripRect
    {
        get
        {
            var width = Bounds.Width > 1 ? Bounds.Width : ExpandedPanel.PanelWidth;
            var x = (width - TotalWidth) / 2;
            var y = ExpandedPanel.PanelHeight + HostLayout.ArchipelagoGap;
            return new Rect(x, y, TotalWidth, StripHeight);
        }
    }

    /// <summary>Current reveal opacity (tests).</summary>
    public double Visibility => _motion?.Opacity ?? 0;

    /// <summary>Current smoothed active page position (tests).</summary>
    public double ActivePosition => _motion?.ActivePosition ?? 0;

    /// <summary>Updates strip data and motion state; repaints.</summary>
    public void Update(
        ExpandedPageContext? context,
        IReadOnlyList<string> titles,
        ArchipelagoMotion? motion,
        double previewPhase)
    {
        _context = context;
        _titles = titles;
        _motion = motion;
        _previewPhase = previewPhase;
        InvalidateVisual();
    }

    /// <summary>Whether a panel-local point lies inside the floating strip area.</summary>
    public bool Contains(Point point)
        => (_motion?.Opacity ?? 0) > 0.01 && StripRect.Inflate(new Thickness(10, 6)).Contains(point);

    /// <summary>Hit-tests the mini-islands; returns the page index or -1 (uses the same flow rects as drawing).</summary>
    public int HitTest(Point point)
    {
        if (_motion is null)
        {
            return -1;
        }

        for (var i = 0; i < 6; i++)
        {
            if (IslandRect(i, _motion.ActivePosition).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Island rectangle for page <paramref name="index"/> at the smoothed active position. Widths follow
    /// the spike's flow layout: <c>48 + 24 * clamp(1 - |i - p|, 0, 1)</c>, so the active island grows into
    /// its neighbours' widths instead of overlapping them. Neighbouring rects never overlap and the
    /// track width stays <see cref="TotalWidth"/> (352 DIP).
    /// </summary>
    public Rect IslandRect(int index, double activePosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 6);
        var strip = StripRect;
        var position = NormalizePosition(activePosition);
        var x = strip.X;
        for (var i = 0; i < index; i++)
        {
            x += WidthFor(i, position) + Gap;
        }

        var width = WidthFor(index, position);
        var active = ActiveIndex(position);
        var height = index == active ? ActiveHeight : MiniHeight;
        return new Rect(x, strip.Y + ((StripHeight - height) / 2), width, height);
    }

    /// <summary>Flow width of island <paramref name="index"/> at active position <paramref name="activePosition"/>.</summary>
    public static double WidthFor(int index, double activePosition)
        => MiniWidth + ((ActiveWidth - MiniWidth) * Influence(index, activePosition));

    /// <summary>Active-island influence in [0,1] (1 at the active page, 0 at a full slot away).</summary>
    public static double Influence(int index, double activePosition)
    {
        var position = NormalizePosition(activePosition);
        return Math.Clamp(1 - Math.Abs(index - position), 0, 1);
    }

    /// <summary>Rounded committed active island for a smoothed position.</summary>
    public static int ActiveIndex(double activePosition)
        => Math.Clamp((int)Math.Round(NormalizePosition(activePosition), MidpointRounding.AwayFromZero), 0, 5);

    /// <summary>
    /// Z-order for the six islands: inactive islands first, active island last (topmost). Kept as a
    /// separate helper so the draw-order regression is testable without a render pass.
    /// </summary>
    public static IReadOnlyList<int> DrawOrder(double activePosition)
    {
        var active = ActiveIndex(activePosition);
        var order = new int[6];
        var cursor = 0;
        for (var i = 0; i < 6; i++)
        {
            if (i != active)
            {
                order[cursor++] = i;
            }
        }

        order[cursor] = active;
        return order;
    }

    private static double NormalizePosition(double activePosition)
        => double.IsFinite(activePosition) ? Math.Clamp(activePosition, 0, 5) : 0;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(ExpandedPanel.PanelWidth, ExpandedPanel.TotalHeight);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var motion = _motion;
        if (motion is null || motion.Opacity <= 0.001 || _context is null)
        {
            return;
        }

        var palette = _context.Settings.Colors;
        var strip = StripRect;
        var center = new Point(strip.Center.X, strip.Center.Y);

        using var fade = context.PushOpacity(motion.Opacity);
        using var transform = context.PushTransform(
            Matrix.CreateTranslation(0, motion.TranslateY)
            * Matrix.CreateTranslation(-center.X, -center.Y)
            * Matrix.CreateScale(motion.Scale, motion.Scale)
            * Matrix.CreateTranslation(center.X, center.Y));

        var active = ActiveIndex(motion.ActivePosition);

        // Inactive islands first, active island last: the grown active pill is never covered by a
        // neighbour's preview (the old loop painted later neighbours over it).
        for (var i = 0; i < 6; i++)
        {
            if (i != active)
            {
                DrawFloatingIsland(context, IslandRect(i, motion.ActivePosition), palette, false, i);
            }
        }

        DrawFloatingIsland(context, IslandRect(active, motion.ActivePosition), palette, true, active);
    }

    private void DrawFloatingIsland(DrawingContext context, Rect rect, IslandColors palette, bool active, int index)
    {
        // Detached-over-desktop look: soft layered shadow + glass surface (no in-panel scrim).
        var shadowAlpha = active ? 0.32 : 0.22;
        for (var layer = 2; layer >= 0; layer--)
        {
            var inflate = layer * 2.0;
            context.DrawRectangle(
                new SolidColorBrush(Colors.Black, shadowAlpha / (layer + 1.5)),
                null,
                new RoundedRect(
                    new Rect(rect.X - inflate, rect.Y - inflate + 3, rect.Width + (inflate * 2), rect.Height + (inflate * 2)),
                    Radius + inflate));
        }

        var surface = new SolidColorBrush(palette.Background, active ? 0.97 : 0.94);
        var stroke = new Pen(
            new SolidColorBrush(active ? Accents.Fallback : palette.LineStrong, active ? 0.65 : 0.9),
            active ? 1.2 : 1);
        context.DrawRectangle(surface, stroke, new RoundedRect(rect, active ? Radius + 1 : Radius));

        if (active)
        {
            var name = _titles.Count > index ? _titles[index] : string.Empty;
            DrawText(context, name, rect.Deflate(new Thickness(8, 0, 8, 0)), Fonts.SemiBold, 10.5,
                new SolidColorBrush(Accents.Fallback), TextAlignment.Center);
            return;
        }

        DrawPreview(context, index, rect.Deflate(new Thickness(5, 3, 5, 3)), palette);
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
                var heights = new[]
                {
                    0.45 + (0.25 * Math.Abs(Math.Sin(_previewPhase * 2.1))),
                    0.6 + (0.3 * Math.Abs(Math.Sin((_previewPhase * 2.6) + 1))),
                    0.4 + (0.2 * Math.Abs(Math.Sin((_previewPhase * 1.7) + 2))),
                };
                for (var bar = 0; bar < 3; bar++)
                {
                    var height = (area.Height - 6) * heights[bar];
                    context.DrawRectangle(new SolidColorBrush(Accents.Track),
                        null,
                        new RoundedRect(new Rect(waveX + (bar * 4), area.Center.Y - (height / 2), 2, height), 1));
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
                var pulse = 0.55 + (0.45 * Math.Sin(_previewPhase * 3.2));
                var center = new Point(area.Center.X, area.Center.Y);
                context.DrawEllipse(new SolidColorBrush(Accents.Info, 0.25 + (0.2 * pulse)), null, center, 6, 6);
                context.DrawEllipse(new SolidColorBrush(Accents.Info, 0.6 + (0.4 * pulse)), null, center, 2.6, 2.6);
                break;
            }

            case 5:
            {
                const double cell = 5;
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
