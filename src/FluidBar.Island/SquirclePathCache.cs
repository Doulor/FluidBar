using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Bounded cache of squircle paths built from Core <see cref="ShellGeometry.SampleOutline"/> (the single
/// source of truth for the shell shape). Keys are bucketed at 0.5 DIP so a spring morph reuses paths
/// instead of rebuilding them every frame.
/// </summary>
internal sealed class SquirclePathCache : IDisposable
{
    /// <summary>Outline samples per corner quadrant.</summary>
    public const int SamplesPerQuadrant = 24;

    private readonly Dictionary<Key, Entry> _cache = [];
    private readonly LinkedList<Key> _lru = [];
    private readonly int _capacity;

    public SquirclePathCache(int capacity = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public int Count => _cache.Count;

    public int Capacity => _capacity;

    public long Builds { get; private set; }

    public SKPath Get(double width, double height, CornerRadii radii)
    {
        var key = Key.From(width, height, radii);
        if (_cache.TryGetValue(key, out var entry))
        {
            _lru.Remove(entry.Node);
            _lru.AddFirst(entry.Node);
            return entry.Path;
        }

        var path = Build(width, height, radii);
        var node = _lru.AddFirst(key);
        _cache[key] = new Entry(path, node);
        Builds++;
        if (_cache.Count > _capacity)
        {
            var last = _lru.Last!;
            if (_cache.Remove(last.Value, out var evicted))
            {
                evicted.Path.Dispose();
            }

            _lru.RemoveLast();
        }

        return path;
    }

    /// <summary>Builds a new squircle path in local DIP coordinates (0,0 → width,height).</summary>
    public static SKPath Build(double width, double height, CornerRadii radii)
    {
        var path = new SKPath { FillType = SKPathFillType.Winding };
        if (!(width > 0) || !(height > 0))
        {
            return path;
        }

        var geometry = ShellGeometry.Create(new VisualRect(0, 0, width, height), radii);
        var points = geometry.SampleOutline(SamplesPerQuadrant);
        if (points.IsDefaultOrEmpty)
        {
            return path;
        }

        path.MoveTo((float)points[0].X, (float)points[0].Y);
        for (var i = 1; i < points.Length; i++)
        {
            path.LineTo((float)points[i].X, (float)points[i].Y);
        }

        path.Close();
        return path;
    }

    public void Dispose()
    {
        foreach (var entry in _cache.Values)
        {
            entry.Path.Dispose();
        }

        _cache.Clear();
        _lru.Clear();
    }

    private readonly record struct Key(long Width, long Height, long TopLeft, long TopRight, long BottomRight, long BottomLeft)
    {
        public static Key From(double width, double height, CornerRadii radii) => new(
            (long)Math.Round(width * 2),
            (long)Math.Round(height * 2),
            (long)Math.Round(radii.TopLeft * 2),
            (long)Math.Round(radii.TopRight * 2),
            (long)Math.Round(radii.BottomRight * 2),
            (long)Math.Round(radii.BottomLeft * 2));
    }

    private sealed record Entry(SKPath Path, LinkedListNode<Key> Node);
}

/// <summary>
/// Cached soft shadow: the squircle is blurred once per (size, radius, DPI, colour) into an SKImage and
/// then blitted. No live <c>SKImageFilter</c> runs per frame. During a size morph the key is bucketed at
/// 0.5 DIP; settled frames reuse one image. Bounded LRU so a long drag cannot grow memory.
/// </summary>
internal sealed class ShadowCache : IDisposable
{
    /// <summary>Transparent margin baked around the shadow in DIP.</summary>
    public const double MarginDip = 40;

    private readonly Dictionary<Key, Entry> _cache = [];
    private readonly LinkedList<Key> _lru = [];
    private readonly int _capacity;
    private readonly SKPaint _drawPaint = new() { IsAntialias = true };

    public ShadowCache(int capacity = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public int Count => _cache.Count;

    public int Capacity => _capacity;

    public int Builds { get; private set; }

    public long PixelBytes
    {
        get
        {
            long total = 0;
            foreach (var entry in _cache.Values)
            {
                total += (long)entry.Image.Info.Width * entry.Image.Info.Height * 4;
            }

            return total;
        }
    }

    public SKImage Get(
        double width,
        double height,
        CornerRadii radii,
        float scale,
        SKColor color,
        double sigmaDip,
        double offsetYDip,
        byte alpha)
    {
        var key = Key.From(width, height, radii, scale, color, sigmaDip, offsetYDip, alpha);
        if (_cache.TryGetValue(key, out var entry))
        {
            _lru.Remove(entry.Node);
            _lru.AddFirst(entry.Node);
            return entry.Image;
        }

        var image = Build(width, height, radii, scale, color, sigmaDip, offsetYDip, alpha);
        var node = _lru.AddFirst(key);
        _cache[key] = new Entry(image, node);
        Builds++;
        if (_cache.Count > _capacity)
        {
            var last = _lru.Last!;
            if (_cache.Remove(last.Value, out var evicted))
            {
                evicted.Image.Dispose();
            }

            _lru.RemoveLast();
        }

        return image;
    }

    public void Draw(SKCanvas canvas, SKImage image, SKRect shellRect, float opacity)
    {
        if (opacity <= 0.01f)
        {
            return;
        }

        var margin = (float)MarginDip;
        var dest = new SKRect(
            shellRect.Left - margin,
            shellRect.Top - margin,
            shellRect.Right + margin,
            shellRect.Bottom + margin);
        _drawPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(opacity * 255, 0, 255));
        canvas.DrawImage(image, dest, _drawPaint);
    }

    private static SKImage Build(
        double width,
        double height,
        CornerRadii radii,
        float scale,
        SKColor color,
        double sigmaDip,
        double offsetYDip,
        byte alpha)
    {
        var margin = MarginDip;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling((width + (margin * 2)) * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling((height + (margin * 2)) * scale));
        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Skia could not create the shadow surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var path = SquirclePathCache.Build(width, height, radii);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            ImageFilter = SKImageFilter.CreateDropShadowOnly(
                0,
                (float)(offsetYDip * scale),
                (float)(sigmaDip * scale),
                (float)(sigmaDip * scale),
                color.WithAlpha(alpha),
                null),
        };

        canvas.Save();
        canvas.Scale(scale);
        canvas.Translate((float)margin, (float)margin);
        canvas.DrawPath(path, paint);
        canvas.Restore();
        return surface.Snapshot();
    }

    public void Dispose()
    {
        foreach (var entry in _cache.Values)
        {
            entry.Image.Dispose();
        }

        _cache.Clear();
        _lru.Clear();
        _drawPaint.Dispose();
    }

    private readonly record struct Key(
        long Width,
        long Height,
        long TopLeft,
        long TopRight,
        long BottomRight,
        long BottomLeft,
        long Scale,
        SKColor Color,
        long Sigma,
        long Offset,
        byte Alpha)
    {
        public static Key From(
            double width,
            double height,
            CornerRadii radii,
            float scale,
            SKColor color,
            double sigmaDip,
            double offsetYDip,
            byte alpha) => new(
            (long)Math.Round(width * 2),
            (long)Math.Round(height * 2),
            (long)Math.Round(radii.TopLeft * 2),
            (long)Math.Round(radii.TopRight * 2),
            (long)Math.Round(radii.BottomRight * 2),
            (long)Math.Round(radii.BottomLeft * 2),
            (long)Math.Round(scale * 1000),
            color,
            (long)Math.Round(sigmaDip * 100),
            (long)Math.Round(offsetYDip * 100),
            alpha);
    }

    private sealed record Entry(SKImage Image, LinkedListNode<Key> Node);
}
