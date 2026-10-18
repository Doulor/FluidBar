using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.SkiaIslandSpike;

/// <summary>
/// Expand/collapse morph harness: 132×36 → 440×284 with content opacity derived from height. Used to
/// measure the animation path (live path + shadow) rather than only settled frames.
/// </summary>
internal sealed class MorphHarness : IDisposable
{
    private readonly TextEngine _text;
    private readonly ShadowCache _shadows;
    private readonly SKImage _largeShadow;
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private readonly float _scale;

    public MorphHarness(TextEngine text, ShadowCache shadows, float scale)
    {
        _text = text;
        _shadows = shadows;
        _scale = scale;
        _largeShadow = shadows.Get(
            Tokens.ExpandedWidth, Tokens.ExpandedHeight, Tokens.ExpandedRadius, scale,
            Tokens.ShadowBlur / 2, Tokens.ShadowOffsetY, (byte)(Tokens.ShadowOpacity * 255));
    }

    public void Draw(SKCanvas canvas, double t, ShadowMode mode)
    {
        var eased = t * t * (3 - (2 * t));
        var w = Tokens.CompactIdleWidth + ((Tokens.ExpandedWidth - Tokens.CompactIdleWidth) * eased);
        var h = Tokens.CompactIdleHeight + ((Tokens.ExpandedHeight - Tokens.CompactIdleHeight) * eased);
        var x = (Tokens.WindowWidth - w) / 2;
        const double y = Tokens.WindowPadding;
        var radius = (Tokens.CompactIdleHeight / 2) + ((Tokens.ExpandedRadius - (Tokens.CompactIdleHeight / 2)) * eased);
        var alpha = Math.Clamp((h - (0.62 * Tokens.ExpandedHeight)) / (0.35 * Tokens.ExpandedHeight), 0, 1);

        canvas.Clear(SKColors.Transparent);

        if (mode == ShadowMode.LiveFilter)
        {
            using var filter = SKImageFilter.CreateDropShadowOnly(
                0,
                (float)Tokens.ShadowOffsetY,
                (float)(Tokens.ShadowBlur / 2),
                (float)(Tokens.ShadowBlur / 2),
                new SKColor(0, 0, 0, (byte)(Tokens.ShadowOpacity * 255)),
                null);
            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.Black,
                ImageFilter = filter,
            };
            using var shadowPath = Squircle.Build(w, h, CornerRadii.Uniform(radius));
            canvas.Save();
            canvas.Translate((float)x, (float)y);
            canvas.DrawPath(shadowPath, shadowPaint);
            canvas.Restore();
        }
        else if (mode == ShadowMode.ScaledCache)
        {
            _shadows.Draw(canvas, _largeShadow, x, y, w, h, _scale);
        }

        using var body = Squircle.Build(w, h, CornerRadii.Uniform(radius));
        canvas.Save();
        canvas.Translate((float)x, (float)y);
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Tokens.Background.WithAlpha((byte)(Tokens.FillOpacity * 255));
        canvas.DrawPath(body, _paint);
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = (float)Tokens.Hairline;
        _paint.Color = Tokens.LineStrong;
        canvas.DrawPath(body, _paint);
        canvas.Restore();

        if (alpha <= 0.01)
        {
            return;
        }

        var px = (float)(x + Tokens.ContentInsetX);
        var py = (float)(y + Tokens.ContentInsetY);
        _text.Draw(canvas, "Control center", new TextStyle(FontRole.DisplaySemiBold, 15f),
            new SKRect(px, py - 2, px + 340, py + 18), Tokens.Foreground, opacity: (float)alpha);
        _text.Draw(canvas, "19:16", new TextStyle(FontRole.DisplayBold, 34f),
            new SKRect(px + 16, py + 22, px + 188, py + 68), Tokens.Foreground, opacity: (float)alpha);
        _text.Draw(canvas, "Sunday, October 18", new TextStyle(FontRole.DisplaySemiBold, 13f),
            new SKRect(px + 16, py + 68, px + 192, py + 88), Tokens.ForegroundSecondary, opacity: (float)alpha);
        _text.Draw(canvas, "Good evening", new TextStyle(FontRole.Ui, 11f),
            new SKRect(px + 16, py + 88, px + 192, py + 106), Tokens.ForegroundTertiary, opacity: (float)alpha);
        _text.Draw(canvas, "Battery", new TextStyle(FontRole.Ui, 10f),
            new SKRect(px + 328, py + 26, px + 376, py + 58), Tokens.ForegroundSecondary, TextAlign.Right, (float)alpha);
        _text.Draw(canvas, "System", new TextStyle(FontRole.Ui, 10f),
            new SKRect(px + 328, py + 66, px + 376, py + 98), Tokens.ForegroundSecondary, TextAlign.Right, (float)alpha);
        _text.Draw(canvas, "Focus", new TextStyle(FontRole.Ui, 10f),
            new SKRect(px + 328, py + 106, px + 376, py + 138), Tokens.ForegroundSecondary, TextAlign.Right, (float)alpha);
    }

    public void Dispose() => _paint.Dispose();

    public enum ShadowMode
    {
        ScaledCache,
        LiveFilter,
        None,
    }
}
