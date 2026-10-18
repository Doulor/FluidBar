using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.SkiaIslandSpike;

/// <summary>
/// Cached soft shadow: the squircle is blurred once per (size, radius, DPI) into an SKImage and then
/// blitted. During a size morph the cache is keyed by 0.5 DIP buckets; settled frames reuse one image.
/// </summary>
internal sealed class ShadowCache : IDisposable
{
    public const double MarginDip = 40;

    private readonly Dictionary<Key, SKImage> _images = [];
    private int _builds;

    public int Count => _images.Count;
    public long PixelBytes => _images.Values.Sum(static image => (long)image.Info.Width * image.Info.Height * 4);
    public int Builds => _builds;

    public SKImage Get(double width, double height, double radius, float scale, double sigmaDip, double offsetYDip, byte alpha)
    {
        var key = new Key(
            (long)Math.Round(width * 2),
            (long)Math.Round(height * 2),
            (long)Math.Round(radius * 2),
            (long)Math.Round(scale * 1000),
            (long)Math.Round(sigmaDip * 100),
            (long)Math.Round(offsetYDip * 100),
            alpha);

        if (_images.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = Build(width, height, radius, scale, sigmaDip, offsetYDip, alpha);
        _images[key] = image;
        _builds++;
        return image;
    }

    private static SKImage Build(double width, double height, double radius, float scale, double sigmaDip, double offsetYDip, byte alpha)
    {
        var margin = MarginDip;
        var pixelWidth = (int)Math.Ceiling((width + (margin * 2)) * scale);
        var pixelHeight = (int)Math.Ceiling((height + (margin * 2)) * scale);
        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var path = Squircle.Build(width * scale, height * scale, CornerRadii.Uniform(radius * scale));
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black,
            ImageFilter = SKImageFilter.CreateDropShadowOnly(
                0,
                (float)(offsetYDip * scale),
                (float)(sigmaDip * scale),
                (float)(sigmaDip * scale),
                new SKColor(0, 0, 0, alpha),
                null),
        };

        canvas.Save();
        canvas.Translate((float)(margin * scale), (float)(margin * scale));
        canvas.DrawPath(path, paint);
        canvas.Restore();
        return surface.Snapshot();
    }

    public void Draw(SKCanvas canvas, SKImage image, double x, double y, double width, double height, float scale)
    {
        var margin = MarginDip;
        var dest = new SKRect(
            (float)(x - margin),
            (float)(y - margin),
            (float)(x + width + margin),
            (float)(y + height + margin));
        canvas.DrawImage(image, dest);
    }

    public void Dispose()
    {
        foreach (var image in _images.Values)
        {
            image.Dispose();
        }

        _images.Clear();
    }

    private readonly record struct Key(long Width, long Height, long Radius, long Scale, long Sigma, long Offset, byte Alpha);
}

/// <summary>
/// Static shell layer (shadow + fill + hairline) baked once per (size, radius, DPI). Settled frames
/// blit one image; dirty content rects blit the same image clipped to the dirty rect.
/// </summary>
internal sealed class StaticLayerCache : IDisposable
{
    private readonly Dictionary<Key, SKImage> _images = [];
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private int _builds;

    public int Builds => _builds;
    public long PixelBytes => _images.Values.Sum(static image => (long)image.Info.Width * image.Info.Height * 4);

    public SKImage Get(
        double windowWidth,
        double windowHeight,
        double shellX,
        double shellY,
        double shellWidth,
        double shellHeight,
        double radius,
        float scale,
        bool fill,
        bool acrylic,
        SKColor background,
        SKColor stroke,
        ShadowCache shadows)
    {
        var key = new Key(
            (long)Math.Round(shellWidth * 2),
            (long)Math.Round(shellHeight * 2),
            (long)Math.Round(radius * 2),
            (long)Math.Round(shellX * 2),
            (long)Math.Round(shellY * 2),
            (long)Math.Round(scale * 1000),
            fill,
            acrylic,
            background,
            stroke);
        if (_images.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var info = new SKImageInfo(
            (int)Math.Ceiling(windowWidth * scale),
            (int)Math.Ceiling(windowHeight * scale),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        DrawBackground(
            canvas,
            shellX,
            shellY,
            shellWidth,
            shellHeight,
            radius,
            fill,
            acrylic,
            background,
            stroke,
            shadows,
            scale);

        var image = surface.Snapshot();
        _images[key] = image;
        _builds++;
        return image;
    }

    public void DrawBackground(
        SKCanvas canvas,
        double shellX,
        double shellY,
        double shellWidth,
        double shellHeight,
        double radius,
        bool fill,
        bool acrylic,
        SKColor background,
        SKColor stroke,
        ShadowCache shadows,
        float scale)
    {
        var paint = _paint;
        var shadow = shadows.Get(
            shellWidth, shellHeight, radius, scale,
            Tokens.ShadowBlur / 2, Tokens.ShadowOffsetY, (byte)(Tokens.ShadowOpacity * 255));
        shadows.Draw(canvas, shadow, shellX, shellY, shellWidth, shellHeight, scale);

        var path = Squircle.ForSize(shellWidth, shellHeight, CornerRadii.Uniform(radius));
        canvas.Save();
        canvas.Translate((float)shellX, (float)shellY);

        if (fill)
        {
            paint.Style = SKPaintStyle.Fill;
            paint.Color = background.WithAlpha((byte)(Tokens.FillOpacity * 255));
            if (acrylic)
            {
                paint.Color = background.WithAlpha((byte)(0.72 * 255));
            }

            canvas.DrawPath(path, paint);
            if (acrylic)
            {
                using var shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(0, (float)shellHeight),
                    [new SKColor(0xFF, 0xFF, 0xFF, 0x20), SKColors.Transparent],
                    [0f, 0.35f],
                    SKShaderTileMode.Clamp);
                paint.Shader = shader;
                canvas.DrawPath(path, paint);
                paint.Shader = null;
            }
        }

        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = (float)Tokens.Hairline;
        paint.Color = stroke;
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    public void Dispose()
    {
        foreach (var image in _images.Values)
        {
            image.Dispose();
        }

        _images.Clear();
        _paint.Dispose();
    }

    private readonly record struct Key(
        long Width, long Height, long Radius, long X, long Y, long Scale,
        bool Fill, bool Acrylic, SKColor Background, SKColor Stroke);
}



