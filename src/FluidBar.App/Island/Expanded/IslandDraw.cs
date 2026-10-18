using System.Globalization;
using Avalonia;
using Avalonia.Media;
using FluidBar.App.Controls;
using FluidBar.App.Theme;

namespace FluidBar.App.Island.Expanded;

/// <summary>Shared imperative drawing helpers for expanded pages (no layout per frame).</summary>
internal static class IslandDraw
{
    /// <summary>Draws one line of text inside <paramref name="area"/> with character ellipsis.</summary>
    public static void Text(
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

    /// <summary>Draws a single line with a leading (already measured) segment followed by a second one.</summary>
    public static void InlineText(
        DrawingContext context,
        string? leading,
        string? trailing,
        Rect area,
        Typeface leadingFace,
        double leadingSize,
        Typeface trailingFace,
        double trailingSize,
        IBrush leadingBrush,
        IBrush trailingBrush)
    {
        if (string.IsNullOrEmpty(leading))
        {
            Text(context, trailing, area, trailingFace, trailingSize, trailingBrush);
            return;
        }

        var formatted = new FormattedText(leading, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            leadingFace, leadingSize, leadingBrush);
        var width = Math.Min(formatted.Width, area.Width);
        context.DrawText(formatted, new Point(area.X, area.Y + ((area.Height - formatted.Height) / 2)));
        var remaining = area.Width - width - 4;
        if (remaining > 8 && !string.IsNullOrEmpty(trailing))
        {
            Text(context, trailing, new Rect(area.X + width + 4, area.Y, remaining, area.Height),
                trailingFace, trailingSize, trailingBrush);
        }
    }

    /// <summary>Draws a rounded card.</summary>
    public static void Card(DrawingContext context, Rect area, IslandColors palette, double radius = 12, double alpha = 1.0)
    {
        var fill = new SolidColorBrush(palette.BackgroundRaised, alpha);
        context.DrawRectangle(fill, null, new RoundedRect(area, radius));
    }

    /// <summary>Draws a hairline separator.</summary>
    public static void Divider(DrawingContext context, Rect area, IslandColors palette)
        => context.DrawRectangle(new SolidColorBrush(palette.Line), null, new Rect(area.X, area.Y, area.Width, 1));

    /// <summary>Draws a small status chip with centered text.</summary>
    public static void Chip(
        DrawingContext context,
        string text,
        Rect area,
        Color accent,
        IBrush foreground,
        double fontSize = Typography.CaptionSize)
    {
        var fill = new SolidColorBrush(accent, 0.18);
        context.DrawRectangle(fill, null, new RoundedRect(area, area.Height / 2));
        Text(context, text, new Rect(area.X + 6, area.Y, Math.Max(1, area.Width - 12), area.Height), Fonts.SemiBold, fontSize, foreground, TextAlignment.Center);
    }

    /// <summary>Draws a 4 DIP progress track.</summary>
    public static void Track(DrawingContext context, Rect area, double fraction, IBrush accent, IslandColors palette)
    {
        var radius = area.Height / 2;
        context.DrawRectangle(new SolidColorBrush(palette.BackgroundInset), null, new RoundedRect(area, radius));
        var clamped = Math.Clamp(fraction, 0, 1);
        if (clamped > 0)
        {
            var width = Math.Max(area.Height, area.Width * clamped);
            context.DrawRectangle(accent, null, new RoundedRect(new Rect(area.X, area.Y, width, area.Height), radius));
        }
    }

    /// <summary>Draws a circular icon button background + glyph.</summary>
    public static void IconButton(
        DrawingContext context,
        Rect hitArea,
        double visualSize,
        IconKind icon,
        IBrush brush,
        IslandColors palette,
        bool elevated = false)
    {
        var visual = new Rect(
            hitArea.Center.X - (visualSize / 2),
            hitArea.Center.Y - (visualSize / 2),
            visualSize,
            visualSize);
        if (elevated)
        {
            context.DrawRectangle(new SolidColorBrush(palette.LineStrong), null, new RoundedRect(visual, visualSize / 2));
        }

        Icons.Draw(context, icon, visual.Deflate(visualSize * 0.27), brush);
    }

    /// <summary>Draws a media artwork tile with an accent gradient.</summary>
    public static void Artwork(DrawingContext context, Rect area, Color accent, double radius = 12)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(accent, 0),
                new GradientStop(Color.FromArgb(0xFF, 0x18, 0x18, 0x20), 1),
            },
        };
        context.DrawRectangle(brush, null, new RoundedRect(area, radius));
        Icons.Draw(context, IconKind.Media, area.Deflate(area.Width * 0.28), new SolidColorBrush(Colors.White, 0.8));
    }
}
