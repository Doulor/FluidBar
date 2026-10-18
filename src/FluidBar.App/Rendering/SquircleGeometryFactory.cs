using Avalonia;
using Avalonia.Media;
using FluidBar.Core.Layout;

namespace FluidBar.App.Rendering;

/// <summary>
/// Builds and caches the superellipse (squircle) outline used for the island shell, ghost pills and
/// content clipping (spec 3.2). Geometry is rebuilt only when the shell size/radius changes by more
/// than half a DIP, so the per-frame path is a cache lookup plus a single draw call.
/// </summary>
public static class SquircleGeometryFactory
{
    /// <summary>Outline samples per corner quadrant (spec 3.2 recommends 15 for ≤44 DIP radii).</summary>
    public const int SamplesPerQuadrant = 15;

    /// <summary>Maximum cached geometries before the cache is dropped (bounded memory, spec 5.8 rule 6).</summary>
    public const int MaxCacheEntries = 256;

    private static readonly Dictionary<CacheKey, Geometry> Cache = [];
    private static readonly Lock Gate = new();

    /// <summary>Builds a squircle outline for a size with per-corner radii, positioned at the origin.</summary>
    public static StreamGeometry Build(double width, double height, CornerRadii radii)
    {
        var geometry = new StreamGeometry();
        if (width <= 0 || height <= 0)
        {
            return geometry;
        }

        var (tl, tr, br, bl, power) = Resolve(width, height, radii);
        var right = width;
        var bottom = height;

        using var context = geometry.Open();
        context.BeginFigure(new Point(tl, 0), isFilled: true);
        context.LineTo(new Point(right - tr, 0));
        AddCorner(context, new Point(right - tr, tr), tr, Corner.TopRight, power);
        context.LineTo(new Point(right, bottom - br));
        AddCorner(context, new Point(right - br, bottom - br), br, Corner.BottomRight, power);
        context.LineTo(new Point(bl, bottom));
        AddCorner(context, new Point(bl, bottom - bl), bl, Corner.BottomLeft, power);
        context.LineTo(new Point(0, tl));
        AddCorner(context, new Point(tl, tl), tl, Corner.TopLeft, power);
        context.EndFigure(true);
        return geometry;
    }

    /// <summary>Returns a cached squircle geometry for the size/radii, rebuilding only on change.</summary>
    public static Geometry GetOrCreate(double width, double height, CornerRadii radii)
    {
        var key = CacheKey.Create(width, height, radii);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (Cache.Count >= MaxCacheEntries)
            {
                Cache.Clear();
            }

            var geometry = Build(key.Width, key.Height, key.Radii);
            Cache[key] = geometry;
            return geometry;
        }
    }

    /// <summary>Clears the cache (DPI/theme change, tests).</summary>
    public static void ClearCache()
    {
        lock (Gate)
        {
            Cache.Clear();
        }
    }

    /// <summary>Number of cached outlines (diagnostics/tests).</summary>
    public static int CacheCount
    {
        get
        {
            lock (Gate)
            {
                return Cache.Count;
            }
        }
    }

    /// <summary>Clamps corner radii and derives the shared superellipse exponent (spec 3.2).</summary>
    public static (double TopLeft, double TopRight, double BottomRight, double BottomLeft, double Power) Resolve(
        double width,
        double height,
        CornerRadii radii)
    {
        var halfMin = Math.Min(width, height) / 2;
        var maxRadius = Math.Clamp(radii.Max, 0, Math.Max(0, halfMin));
        var exponent = halfMin <= 1e-6 ? 2.0 : 2.0 + ((1.0 - (maxRadius / halfMin)) * 1.22);
        return (
            Math.Clamp(radii.TopLeft, 0, halfMin),
            Math.Clamp(radii.TopRight, 0, halfMin),
            Math.Clamp(radii.BottomRight, 0, halfMin),
            Math.Clamp(radii.BottomLeft, 0, halfMin),
            2.0 / exponent);
    }

    private static void AddCorner(
        StreamGeometryContext context,
        Point center,
        double radius,
        Corner corner,
        double power)
    {
        if (radius <= 0.01)
        {
            return;
        }

        for (var i = 1; i < SamplesPerQuadrant; i++)
        {
            var t = (Math.PI / 2) * (i / (double)SamplesPerQuadrant);
            var cos = Math.Pow(Math.Abs(Math.Cos(t)), power);
            var sin = Math.Pow(Math.Abs(Math.Sin(t)), power);
            var point = corner switch
            {
                // Top-right: top edge → right edge.
                Corner.TopRight => new Point(center.X + (radius * sin), center.Y - (radius * cos)),
                // Bottom-right: right edge → bottom edge.
                Corner.BottomRight => new Point(center.X + (radius * cos), center.Y + (radius * sin)),
                // Bottom-left: bottom edge → left edge.
                Corner.BottomLeft => new Point(center.X - (radius * sin), center.Y + (radius * cos)),
                // Top-left: left edge → top edge.
                _ => new Point(center.X - (radius * cos), center.Y - (radius * sin)),
            };
            context.LineTo(point);
        }
    }

    private enum Corner
    {
        TopRight,
        BottomRight,
        BottomLeft,
        TopLeft,
    }

    private readonly record struct CacheKey(
        long WidthHalf,
        long HeightHalf,
        long TopLeftHalf,
        long TopRightHalf,
        long BottomRightHalf,
        long BottomLeftHalf,
        double Width,
        double Height,
        CornerRadii Radii)
    {
        public static CacheKey Create(double width, double height, CornerRadii radii)
            => new(
                (long)Math.Round(width * 2),
                (long)Math.Round(height * 2),
                (long)Math.Round(radii.TopLeft * 2),
                (long)Math.Round(radii.TopRight * 2),
                (long)Math.Round(radii.BottomRight * 2),
                (long)Math.Round(radii.BottomLeft * 2),
                width,
                height,
                radii);

        /// <summary>Aligned size used when building (half-DIP buckets avoid per-frame rebuilds).</summary>
        public KeyValuePair<double, double> Aligned => new(WidthHalf / 2.0, HeightHalf / 2.0);
    }
}
