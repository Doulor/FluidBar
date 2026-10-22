using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Karaoke text: paints the whole line dim, then re-paints the sung prefix in the highlight colour,
/// clipped at the progress fraction (the KRC word timeline drives that fraction).
/// </summary>
public static class KaraokeText
{
    /// <summary>Radius of the tight halo, as a fraction of the font size (the visible core of the glow).</summary>
    private const float GlowSigmaNearRatio = 0.14f;

    /// <summary>Radius of the wide halo, as a fraction of the font size (the soft bloom around it).</summary>
    private const float GlowSigmaFarRatio = 0.30f;

    /// <summary>Opacity of the tight halo relative to the sung colour.</summary>
    private const float GlowNearOpacity = 0.72f;

    /// <summary>Opacity of the wide halo relative to the sung colour.</summary>
    private const float GlowFarOpacity = 0.32f;

    private static readonly object GlowGate = new();
    private static readonly Dictionary<int, SKMaskFilter> GlowFilters = new();

    /// <summary>Blur filter reused across frames (one native object per radius, keyed in 0.1 units).</summary>
    private static SKMaskFilter SharedGlowFilter(float sigma)
    {
        var key = (int)MathF.Round(sigma * 10f);
        lock (GlowGate)
        {
            if (!GlowFilters.TryGetValue(key, out var filter))
            {
                if (GlowFilters.Count > 16)
                {
                    foreach (var stale in GlowFilters.Values)
                    {
                        stale.Dispose();
                    }

                    GlowFilters.Clear();
                }

                filter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, key / 10f);
                GlowFilters[key] = filter;
            }

            return filter;
        }
    }

    /// <summary>
    /// Unsung lyric colour: the foreground at ~47 % opacity. Deliberately much dimmer than the sung
    /// colour so the karaoke boundary reads even on a dark glass card (user request: ΔL &gt; 30).
    /// </summary>
    public static SKColor UnsungColor(SKColor foreground) => foreground.WithAlpha(120);

    /// <summary>
    /// Sung lyric colour: the artwork accent pushed toward white (keeps hue, raises luminance) so the
    /// sung prefix is clearly brighter than the unsung remainder.
    /// </summary>
    public static SKColor SungColor(SKColor accent)
    {
        const float lift = 0.38f;
        return new SKColor(
            (byte)Math.Clamp(accent.Red + ((255 - accent.Red) * lift), 0, 255),
            (byte)Math.Clamp(accent.Green + ((255 - accent.Green) * lift), 0, 255),
            (byte)Math.Clamp(accent.Blue + ((255 - accent.Blue) * lift), 0, 255),
            accent.Alpha);
    }

    /// <summary>
    /// Picks the karaoke accent: the artwork accent when it is saturated enough, otherwise the theme
    /// track accent. Monochrome covers (common for KuGou lyric videos) would otherwise make the sung
    /// prefix a slightly brighter white with no visible colour.
    /// </summary>
    public static SKColor SaturatedAccent(SKColor accent, SKColor fallback)
    {
        var max = Math.Max(accent.Red, Math.Max(accent.Green, accent.Blue));
        var min = Math.Min(accent.Red, Math.Min(accent.Green, accent.Blue));
        var saturation = max <= 0 ? 0 : (max - min) / (double)max;
        return saturation < 0.18 ? fallback : accent;
    }

    /// <summary>WCAG relative luminance (0..1) for a colour, used by the contrast tests.</summary>
    public static double RelativeLuminance(SKColor color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.Red)) + (0.7152 * Channel(color.Green)) + (0.0722 * Channel(color.Blue));
    }

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
        canvas.ClipRect(new SKRect(clipLeft - 1, area.Top - 4, clipLeft + sweep, area.Bottom + 4));

        // A neon halo under the sung prefix: the artwork accent already lifts the "read" part, and the glow
        // makes the sweep boundary read at a glance without raising the unread contrast. Two blurred passes
        // (wide bloom + tight ring) under a crisp pass, all clipped to the sweep so the glow never spills
        // onto the unsung half. The first version was a single faint pass and read as "no glow at all"
        // (user report 2026-10-22: "发光效果不明显").
        var fontSize = text.Metrics.Style(style).Size;
        var far = highlightColor.WithAlpha((byte)Math.Clamp(highlightColor.Alpha * GlowFarOpacity, 0, 255));
        var near = highlightColor.WithAlpha((byte)Math.Clamp(highlightColor.Alpha * GlowNearOpacity, 0, 255));
        text.Draw(canvas, line, style, area, far, align, opacity,
            SharedGlowFilter(MathF.Max(2.4f, fontSize * GlowSigmaFarRatio)));
        text.Draw(canvas, line, style, area, near, align, opacity,
            SharedGlowFilter(MathF.Max(1.2f, fontSize * GlowSigmaNearRatio)));
        text.Draw(canvas, line, style, area, highlightColor, align, opacity);
        canvas.Restore();
    }
}
