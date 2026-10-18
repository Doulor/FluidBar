using SkiaSharp;

namespace FluidBar.SkiaIslandSpike;

/// <summary>Offscreen raster renderer at a fractional DPI scale (150 % = 1.5 by default).</summary>
internal sealed class IslandRenderer : IDisposable
{
    private readonly SKSurface _surface;
    private readonly SKCanvas _canvas;

    public IslandRenderer(double widthDip, double heightDip, float scale)
    {
        Scale = scale;
        WidthDip = widthDip;
        HeightDip = heightDip;
        WidthPx = (int)Math.Ceiling(widthDip * scale);
        HeightPx = (int)Math.Ceiling(heightDip * scale);
        var info = new SKImageInfo(WidthPx, HeightPx, SKColorType.Rgba8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info) ?? throw new InvalidOperationException("SKSurface.Create failed.");
        _canvas = _surface.Canvas;
        _canvas.Clear(SKColors.Transparent);
    }

    public float Scale { get; }
    public double WidthDip { get; }
    public double HeightDip { get; }
    public int WidthPx { get; }
    public int HeightPx { get; }

    public void Render(IslandScene scene, SKRect? dirtyDip = null, bool useStaticBackground = true)
    {
        _canvas.Save();
        _canvas.Scale(Scale);
        scene.Render(_canvas, dirtyDip, useStaticBackground);
        _canvas.Restore();
    }

    public SKImage Snapshot() => _surface.Snapshot();

    /// <summary>Probe hook: draws one node action with the renderer's DPI transform.</summary>
    public void DrawProbe(Action<SKCanvas> draw)
    {
        _canvas.Save();
        _canvas.Scale(Scale);
        draw(_canvas);
        _canvas.Restore();
    }

    public void SavePng(string path, bool reset = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = _surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        if (reset)
        {
            _canvas.Clear(SKColors.Transparent);
        }
    }

    public SKBitmap ReadPixels()
    {
        var bitmap = new SKBitmap(WidthPx, HeightPx, SKColorType.Rgba8888, SKAlphaType.Premul);
        _surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
        return bitmap;
    }

    public void Dispose()
    {
        _surface.Dispose();
    }
}
