using SkiaSharp;

namespace FluidBar.Island;

/// <summary>A window-independent drag image. Windows carries this bitmap between applications.</summary>
public static class FileDragPreviewRenderer
{
    public const int Width = 264;
    public const int Height = 66;

    /// <summary>Renders straight-alpha BGRA, as required by IDragSourceHelper.</summary>
    public static SKBitmap Render(IslandTheme theme, string title, string hint, float dpiScale = 1)
    {
        var scale = Math.Clamp(dpiScale, 0.75f, 3f);
        var bitmap = new SKBitmap(new SKImageInfo((int)Math.Ceiling(Width * scale), (int)Math.Ceiling(Height * scale),
            SKColorType.Bgra8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        using var paint = new SKPaint { IsAntialias = true, Color = theme.BackgroundRaised.WithAlpha(245) };
        var card = new SKRect(2, 2, Width - 2, Height - 2);
        canvas.DrawRoundRect(card, 14, 14, paint);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.Color = theme.Foreground.WithAlpha(35);
        canvas.DrawRoundRect(card, 14, 14, paint);
        paint.Style = SKPaintStyle.Fill;
        paint.Color = theme.Info.WithAlpha(32);
        var glyph = new SKRect(14, 15, 48, 49);
        canvas.DrawRoundRect(glyph, 9, 9, paint);
        IconSet.Draw(canvas, IconKind.Document, glyph.Deflate(7), theme.Info, paint);
        using var text = new TextEngine();
        text.Draw(canvas, title, new TextStyle(FontRole.UiSemiBold, 12), new SKRect(60, 12, Width - 14, 32), theme.Foreground);
        text.Draw(canvas, hint, new TextStyle(FontRole.Ui, 10), new SKRect(60, 35, Width - 14, 51), theme.ForegroundSecondary);
        return bitmap;
    }
}
