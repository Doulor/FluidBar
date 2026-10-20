using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Karaoke text: paints the whole line dim, then re-paints the sung prefix in the highlight colour,
/// clipped at the progress fraction (the KRC word timeline drives that fraction).
/// </summary>
internal static class KaraokeText
{
    /// <summary>Draws one line with a highlight sweep.</summary>
    /// <param name="text">Scaled text helper (font scale already applied).</param>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="line">Lyric line.</param>
    /// <param name="highlight">Swept fraction 0..1; negative or 1 means "no per-word data" (plain line).</param>
    /// <param name="style">Text style.</param>
    /// <param name="area">Layout rectangle (one line).</param>
    /// <param name="baseColor">Colour for the not-yet-sung part.</param>
    /// <param name="highlightColor">Colour for the sung part (artwork accent).</param>
    /// <param name="align">Horizontal alignment.</param>
    /// <param name="opacity">Layer opacity.</param>
    public static void Draw(ScaledText text, SKCanvas canvas, string? line, double highlight, TextStyle style,
        SKRect area, SKColor baseColor, SKColor highlightColor, TextAlign align, float opacity)
    {
        if (string.IsNullOrEmpty(line) || area.Width <= 1 || area.Height <= 1 || opacity <= 0.01f)
        {
            return;
        }

        text.Draw(canvas, line, style, area, baseColor, align, opacity);

        // Without word timing the whole line is simply the line (no partial sweep to fake).
        if (highlight < 0 || highlight >= 0.999)
        {
            if (highlight < 0)
            {
                return;
            }
        }

        var measured = Math.Min(area.Width, text.Measure(line, style));
        var sweep = (float)Math.Clamp(highlight, 0, 1) * measured;
        if (sweep <= 0.5f)
        {
            return;
        }

        var clipLeft = align switch
        {
            TextAlign.Center => area.Left + ((area.Width - measured) / 2),
            TextAlign.Right => area.Right - measured,
            _ => area.Left,
        };

        canvas.Save();
        canvas.ClipRect(new SKRect(clipLeft - 1, area.Top - 1, clipLeft + sweep, area.Bottom + 1));
        text.Draw(canvas, line, style, area, highlightColor, align, opacity);
        canvas.Restore();
    }
}
