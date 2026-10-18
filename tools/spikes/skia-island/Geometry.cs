using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.SkiaIslandSpike;

/// <summary>
/// Squircle path construction. The exponent comes from Core <see cref="ShellGeometry"/> (single source
/// of truth); the outline is sampled here in visual order (Core <c>SampleOutline</c> emits corners in
/// an order that cannot be connected directly — see RESULTS.md "upstream findings").
/// </summary>
internal static class Squircle
{
    public const int SamplesPerQuadrant = 32;

    private static readonly Dictionary<Key, SKPath> Cache = [];
    private static readonly Lock Gate = new();

    public static SKPath ForSize(double width, double height, CornerRadii radii)
    {
        var key = new Key(
            (long)Math.Round(width * 2),
            (long)Math.Round(height * 2),
            (long)Math.Round(radii.TopLeft * 2),
            (long)Math.Round(radii.TopRight * 2),
            (long)Math.Round(radii.BottomRight * 2),
            (long)Math.Round(radii.BottomLeft * 2));

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var path = Build(width, height, radii);
            Cache[key] = path;
            return path;
        }
    }

    public static SKPath Build(double width, double height, CornerRadii radii)
    {
        var path = new SKPath { FillType = SKPathFillType.Winding };
        if (width <= 0 || height <= 0)
        {
            return path;
        }

        var halfMin = Math.Min(width, height) / 2;
        var geometry = ShellGeometry.Create(new VisualRect(0, 0, width, height), radii);
        var exponent = 2.0 / geometry.ExponentK;

        var tl = Math.Clamp(radii.TopLeft, 0, halfMin);
        var tr = Math.Clamp(radii.TopRight, 0, halfMin);
        var br = Math.Clamp(radii.BottomRight, 0, halfMin);
        var bl = Math.Clamp(radii.BottomLeft, 0, halfMin);
        var w = (float)width;
        var h = (float)height;

        path.MoveTo((float)tl, 0);
        path.LineTo((float)(width - tr), 0);
        AddCorner(path, (float)(width - tr), (float)tr, tr, exponent, Corner.TopRight);
        path.LineTo(w, (float)(height - br));
        AddCorner(path, (float)(width - br), (float)(height - br), br, exponent, Corner.BottomRight);
        path.LineTo((float)bl, h);
        AddCorner(path, (float)bl, (float)(height - bl), bl, exponent, Corner.BottomLeft);
        path.LineTo(0, (float)tl);
        AddCorner(path, (float)tl, (float)tl, tl, exponent, Corner.TopLeft);
        path.Close();
        return path;
    }

    private static void AddCorner(SKPath path, float centerX, float centerY, double radius, double exponent, Corner corner)
    {
        if (radius <= 0.01)
        {
            return;
        }

        for (var i = 1; i < SamplesPerQuadrant; i++)
        {
            var t = (Math.PI / 2) * (i / (double)SamplesPerQuadrant);
            var cos = Math.Pow(Math.Abs(Math.Cos(t)), exponent);
            var sin = Math.Pow(Math.Abs(Math.Sin(t)), exponent);
            var x = corner switch
            {
                Corner.TopRight => centerX + (radius * sin),
                Corner.BottomRight => centerX + (radius * cos),
                Corner.BottomLeft => centerX - (radius * sin),
                _ => centerX - (radius * cos),
            };
            var y = corner switch
            {
                Corner.TopRight => centerY - (radius * cos),
                Corner.BottomRight => centerY + (radius * sin),
                Corner.BottomLeft => centerY + (radius * cos),
                _ => centerY - (radius * sin),
            };
            path.LineTo((float)x, (float)y);
        }
    }

    private enum Corner
    {
        TopRight,
        BottomRight,
        BottomLeft,
        TopLeft,
    }

    private readonly record struct Key(long Width, long Height, long Tl, long Tr, long Br, long Bl);
}

/// <summary>Small rectangle helpers kept in DIP.</summary>
internal static class RectExtensions
{
    public static SKRect ToSk(this SKRect rect) => rect;

    public static SKRect Inset(this SKRect rect, float dx, float dy)
        => new(rect.Left + dx, rect.Top + dy, rect.Right - dx, rect.Bottom - dy);

    public static SKRect Deflate(this SKRect rect, float amount)
        => new(rect.Left + amount, rect.Top + amount, rect.Right - amount, rect.Bottom - amount);

    public static bool Intersects(this SKRect a, SKRect b)
        => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
}
