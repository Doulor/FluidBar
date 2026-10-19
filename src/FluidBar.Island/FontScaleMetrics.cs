using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Font-scale metrics for the island (settings → appearance → text size, 0.8–1.6).
/// <para>
/// Every text style is multiplied by <see cref="Scale"/>; layout heights are grown by the difference
/// between the scaled line height and the 1.0 line height, so <see cref="One"/> reproduces the approved
/// 1.0 geometry exactly (the golden images stay pixel-identical) while a larger scale pushes the
/// containers open and a smaller one only shrinks the glyphs inside their existing boxes.
/// </para>
/// </summary>
public readonly record struct FontScaleMetrics(double Scale)
{
    /// <summary>The minimum supported scale (<see cref="AppSettingsLimits"/> mirror; 0.8).</summary>
    public const double MinScale = 0.8;

    /// <summary>The maximum supported scale (1.6).</summary>
    public const double MaxScale = 1.6;

    /// <summary>Neutral metrics (scale 1.0: every derived height delta is zero).</summary>
    public static FontScaleMetrics One { get; } = new(1.0);

    /// <summary>The scale clamped to the supported range.</summary>
    public double Value => Math.Clamp(double.IsFinite(Scale) ? Scale : 1.0, MinScale, MaxScale);

    /// <summary>Creates clamped metrics.</summary>
    /// <param name="scale">Requested scale.</param>
    public static FontScaleMetrics From(double scale) => new(Math.Clamp(double.IsFinite(scale) ? scale : 1.0, MinScale, MaxScale));

    /// <summary>Scales a base text style (0.8 shrinks, 1.6 grows; role is preserved).</summary>
    public TextStyle Style(TextStyle style) => new(style.Role, (float)(style.Size * Value));

    /// <summary>Scales a base length that should follow the font (insets, stacked text bands).</summary>
    public float Length(float baseValue) => (float)(baseValue * Value);

    /// <summary>
    /// Additional height a text line needs over its 1.0 line height. Font line spacing is close to a
    /// constant multiple of the size for the island's faces (Segoe UI display/text and the CJK
    /// fallback), so the role ratio only changes the growth, never the 1.0 baseline.
    /// </summary>
    public float LineGrow(TextStyle style) => (float)(style.Size * Ratio(style.Role) * (Value - 1.0));

    /// <summary>Approximate line-spacing / font-size ratio for a role.</summary>
    public static double Ratio(FontRole role) => role switch
    {
        FontRole.Mono or FontRole.MonoSemiBold => 1.32,
        _ => 1.34,
    };
}

/// <summary>
/// Thin scaling facade over <see cref="TextEngine"/>: every draw/measure call multiplies the style by
/// the current font scale, so the renderers keep their call sites and 1.0 stays byte-identical.
/// </summary>
public sealed class ScaledText
{
    private readonly TextEngine _engine;

    /// <summary>Wraps an engine (the engine is not owned).</summary>
    /// <param name="engine">Shared text engine.</param>
    public ScaledText(TextEngine engine) => _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    /// <summary>Current font scale (1.0 = the approved prototype sizes).</summary>
    public double Scale { get; private set; } = 1.0;

    /// <summary>Underlying engine (caches, diagnostics).</summary>
    public TextEngine Engine => _engine;

    /// <summary>Applies the render state's font scale.</summary>
    /// <param name="scale">Font scale from settings.</param>
    public void SetScale(double scale) => Scale = Math.Clamp(double.IsFinite(scale) ? scale : 1.0, FontScaleMetrics.MinScale, FontScaleMetrics.MaxScale);

    /// <summary>Current metrics.</summary>
    public FontScaleMetrics Metrics => FontScaleMetrics.From(Scale);

    /// <summary>Line height of a base style at the current scale.</summary>
    public float LineHeight(TextStyle style) => _engine.LineHeight(Metrics.Style(style));

    /// <summary>Measures one line at the current scale.</summary>
    public float Measure(string text, TextStyle style, float maxWidth = float.PositiveInfinity)
        => _engine.Measure(text, Metrics.Style(style), maxWidth);

    /// <summary>Gets a cached layout at the current scale.</summary>
    public TextLayout GetLayout(string text, TextStyle style, float maxWidth)
        => _engine.GetLayout(text, Metrics.Style(style), maxWidth);

    /// <summary>Draws one line at the current scale.</summary>
    public void Draw(SKCanvas canvas, string? text, TextStyle style, SKRect area, SKColor color,
        TextAlign align = TextAlign.Left, float opacity = 1f)
        => _engine.Draw(canvas, text, Metrics.Style(style), area, color, align, opacity);

    /// <summary>Draws a previously measured layout.</summary>
    public void DrawLayout(SKCanvas canvas, TextLayout layout, SKRect area, SKColor color, TextAlign align, float opacity = 1f)
        => _engine.DrawLayout(canvas, layout, area, color, align, opacity);
}
