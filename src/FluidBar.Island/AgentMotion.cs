using SkiaSharp;

using FluidBar.Core.Motion;

namespace FluidBar.Island;

/// <summary>
/// Prototype2 agent motion values (measured with playwright from
/// <c>http://127.0.0.1:8123/index.html?state=expanded&amp;tab=agent&amp;arch=1</c>):
/// the compact ring and the expanded spinner-ring both run <c>spin 900ms linear infinite</c> with a
/// 180° (top+right border) arc, and the expanded running progress bar pulses its width
/// <c>48% → 72%</c> over <c>2.4s ease-in-out infinite alternate</c>.
/// </summary>
public static class AgentMotion
{
    /// <summary>One full ring revolution (900 ms, linear, clockwise).</summary>
    public const double RingPeriodSeconds = 0.9;

    /// <summary>One pulse half-cycle: 48% → 72% over 2.4 s, then alternate back.</summary>
    public const double ProgressHalfPeriodSeconds = 2.4;

    /// <summary>Compact / expanded ring stroke width (1.5 DIP).</summary>
    public const float RingStrokeWidth = 1.5f;

    /// <summary>Compact ring base border alpha (rgba(accent, .18)).</summary>
    public const float RingBaseAlpha = 0.18f;

    /// <summary>Expanded spinner base border alpha (rgba(accent, .25)).</summary>
    public const float SpinnerBaseAlpha = 0.25f;

    /// <summary>CSS border arcs paint half a circle (top + right borders).</summary>
    public const float ArcSweepDegrees = 180f;

    /// <summary>Skia start angle for the top+right half: the top-left diagonal (-135°).</summary>
    public const float ArcStartDegrees = -135f;

    /// <summary>Minimum indeterminate width fraction (prototype 48%).</summary>
    public const double ProgressMinFraction = 0.48;

    /// <summary>Maximum indeterminate width fraction (prototype 72%).</summary>
    public const double ProgressMaxFraction = 0.72;

    /// <summary>Accent fill opacity of the progress span (prototype .75).</summary>
    public const float ProgressOpacity = 0.75f;

    /// <summary>Progress track/fill height (1.5 DIP).</summary>
    public const float ProgressHeight = 1.5f;

    /// <summary>Progress fill corner radius (2 DIP).</summary>
    public const float ProgressRadius = 2f;

    private static readonly CubicBezierEasing PulseEasing = new(0.42, 0, 0.58, 1);

    /// <summary>Clockwise rotation in degrees for the shared animation clock (wraps every 900 ms).</summary>
    public static double RingRotationDegrees(double seconds)
    {
        if (!double.IsFinite(seconds))
        {
            return 0;
        }

        var phase = seconds % RingPeriodSeconds;
        if (phase < 0)
        {
            phase += RingPeriodSeconds;
        }

        return (phase / RingPeriodSeconds) * 360.0;
    }

    /// <summary>
    /// Indeterminate progress (0..100) at the given clock: the prototype span pulses its width between
    /// 48% and 72% with ease-in-out and <c>alternate</c> (2.4 s each way, 4.8 s round trip).
    /// </summary>
    public static double IndeterminateProgressPercent(double seconds)
    {
        if (!double.IsFinite(seconds))
        {
            seconds = 0;
        }

        var cycle = ProgressHalfPeriodSeconds * 2;
        var local = seconds % cycle;
        if (local < 0)
        {
            local += cycle;
        }

        var half = local <= ProgressHalfPeriodSeconds
            ? local / ProgressHalfPeriodSeconds
            : (cycle - local) / ProgressHalfPeriodSeconds;
        var eased = Math.Clamp(PulseEasing.Evaluate(half), 0, 1);
        var fraction = ProgressMinFraction + ((ProgressMaxFraction - ProgressMinFraction) * eased);
        return Math.Clamp(fraction, ProgressMinFraction, ProgressMaxFraction) * 100.0;
    }
}

/// <summary>
/// Multi-agent glyph: stacked rings inside the single agent ring (user spec 2026-10-24).
/// <para>
/// 1 agent = today's ring + arc. 2 = a second, counter-rotating ring at a different speed inside it.
/// 3 = the second ring stays and the third agent is a solid glowing dot in the middle. Anything above
/// three is not drawn. Rings come in/out with a merge: the inner ring grows out of the outer ring's
/// radius (and shrinks back into it when the agent finishes).
/// </para>
/// </summary>
public static class AgentRingStack
{
    /// <summary>Maximum agents the glyph ever shows.</summary>
    public const int MaxAgents = 3;

    /// <summary>Counter-rotation period of the second ring (1.35 s vs the outer 0.9 s: never in sync).</summary>
    public const double SecondRingPeriodSeconds = 1.35;

    /// <summary>Radius factor of the settled second ring (relative to the outer ring radius).</summary>
    public const float SecondRingRadiusFactor = 0.62f;

    /// <summary>Radius factor the second ring starts/ends at (just inside the outer ring).</summary>
    public const float SecondRingMergeFactor = 0.94f;

    /// <summary>Centre dot radius in DIP at 100 %.</summary>
    public const float DotRadiusDip = 2.3f;

    /// <summary>Clamps a live agent count to what the glyph can show.</summary>
    /// <param name="liveAgents">Number of running/waiting agent sessions.</param>
    public static int Clamp(int liveAgents) => Math.Clamp(liveAgents, 1, MaxAgents);

    /// <summary>Second-ring rotation for an elapsed animation time (counter-clockwise).</summary>
    /// <param name="seconds">Elapsed animation seconds.</param>
    public static double SecondRingRotationDegrees(double seconds)
        => double.IsFinite(seconds) ? -((seconds / SecondRingPeriodSeconds) * 360.0 % 360.0) : 0;

    /// <summary>
    /// Paints the stacked glyph: base circle, outer arc, (optionally) the counter-rotating second ring and
    /// the centre dot. Shares one implementation so the compact pill, the split satellite and any future
    /// host render the identical language.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="stroke">Reusable stroke paint (style/width/cap are set here).</param>
    /// <param name="fill">Reusable fill paint (centre dot).</param>
    /// <param name="glow">Reusable fill paint with a blur mask filter (centre dot halo).</param>
    /// <param name="centre">Glyph centre in canvas coordinates.</param>
    /// <param name="radius">Outer ring centre-line radius in DIP.</param>
    /// <param name="strokeWidth">Outer ring stroke width in DIP.</param>
    /// <param name="liveAgents">Live agent sessions (clamped to 1..3).</param>
    /// <param name="animationSeconds">Elapsed animation time (drives both rotations).</param>
    /// <param name="secondRingBlend">0..1 merge progress of the second ring.</param>
    /// <param name="centreDotBlend">0..1 merge progress of the centre dot.</param>
    /// <param name="alpha">Layer opacity 0..1.</param>
    /// <param name="accent">Base accent colour (fallback when a slot has no tool colour).</param>
    /// <param name="colors">Per-slot tool colours (0xAARRGGBB, 0 = accent); same tool ⇒ same colour.</param>
    /// <param name="reducedMotion">Freezes the rotations when true.</param>
    /// <param name="baseRingAlpha">Alpha of the faint base circle (0 disables it).</param>
    public static void Draw(
        SKCanvas canvas,
        SKPaint stroke,
        SKPaint fill,
        SKPaint glow,
        SKPoint centre,
        float radius,
        float strokeWidth,
        int liveAgents,
        double animationSeconds,
        float secondRingBlend,
        float centreDotBlend,
        float alpha,
        SKColor accent,
        uint[] colors,
        bool reducedMotion,
        float baseRingAlpha = 0.18f)
    {
        var agents = Clamp(liveAgents);
        var first = ColorAt(colors, 0, accent);
        var second = ColorAt(colors, 1, first);
        var third = ColorAt(colors, 2, second);

        stroke.Style = SKPaintStyle.Stroke;
        stroke.StrokeCap = SKStrokeCap.Butt;
        stroke.StrokeWidth = strokeWidth;
        stroke.Shader = null;

        if (baseRingAlpha > 0f)
        {
            stroke.Color = WithAlpha(first, baseRingAlpha * alpha);
            canvas.DrawCircle(centre.X, centre.Y, radius, stroke);
        }

        var rotation = reducedMotion ? 0 : AgentMotion.RingRotationDegrees(animationSeconds);
        stroke.Color = WithAlpha(first, alpha);
        canvas.DrawArc(
            new SKRect(centre.X - radius, centre.Y - radius, centre.X + radius, centre.Y + radius),
            AgentMotion.ArcStartDegrees + (float)rotation,
            AgentMotion.ArcSweepDegrees,
            false,
            stroke);

        if (agents >= 2 && secondRingBlend > 0.01f)
        {
            var secondRadius = radius * (SecondRingMergeFactor
                - ((SecondRingMergeFactor - SecondRingRadiusFactor) * secondRingBlend));
            var secondColor = second;
            var secondAlpha = secondRingBlend * alpha;

            stroke.StrokeWidth = strokeWidth * (0.6f + (0.4f * secondRingBlend));
            stroke.Color = WithAlpha(secondColor, baseRingAlpha * 0.8f * secondAlpha);
            canvas.DrawCircle(centre.X, centre.Y, secondRadius, stroke);

            var secondRotation = reducedMotion ? 0 : SecondRingRotationDegrees(animationSeconds);
            stroke.Color = WithAlpha(secondColor, secondAlpha);
            canvas.DrawArc(
                new SKRect(centre.X - secondRadius, centre.Y - secondRadius,
                    centre.X + secondRadius, centre.Y + secondRadius),
                AgentMotion.ArcStartDegrees + (float)secondRotation,
                AgentMotion.ArcSweepDegrees,
                false,
                stroke);
            stroke.StrokeWidth = strokeWidth;
        }

        if (agents >= 3 && centreDotBlend > 0.01f)
        {
            var dotColor = third;
            var dotRadius = DotRadiusDip * centreDotBlend;
            fill.Style = SKPaintStyle.Fill;
            glow.Color = WithAlpha(dotColor, 0.45f * centreDotBlend * alpha);
            canvas.DrawCircle(centre.X, centre.Y, dotRadius * 2.1f, glow);
            fill.Color = WithAlpha(dotColor, centreDotBlend * alpha);
            canvas.DrawCircle(centre.X, centre.Y, dotRadius, fill);
        }
    }

    /// <summary>Tool colour for a slot (falls back to <paramref name="fallback"/> when unset).</summary>
    /// <param name="colors">Per-slot ARGB colours (0 = unset).</param>
    /// <param name="index">Slot index.</param>
    /// <param name="fallback">Colour used when the slot has none.</param>
    public static SKColor ColorAt(uint[] colors, int index, SKColor fallback)
        => colors is not null && index < colors.Length && colors[index] != 0
            ? new SKColor(colors[index])
            : fallback;

    private static SKColor WithAlpha(SKColor color, float alpha)
        => color.WithAlpha((byte)Math.Clamp(alpha * 255f, 0, 255));

    /// <summary>Rotates a colour's hue by <paramref name="degrees"/> keeping saturation/value.</summary>
    /// <param name="color">Source colour.</param>
    /// <param name="degrees">Hue offset in degrees.</param>
    public static SKColor HueShift(SKColor color, float degrees)
    {
        var r = color.Red / 255f;
        var g = color.Green / 255f;
        var b = color.Blue / 255f;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;

        var hue = 0f;
        if (delta > 0.0001f)
        {
            if (max == r)
            {
                hue = 60f * (((g - b) / delta) % 6f);
            }
            else if (max == g)
            {
                hue = 60f * (((b - r) / delta) + 2f);
            }
            else
            {
                hue = 60f * (((r - g) / delta) + 4f);
            }
        }

        if (hue < 0)
        {
            hue += 360f;
        }

        var saturation = max <= 0f ? 0f : delta / max;
        var value = max;
        hue = (hue + degrees + 360f) % 360f;

        var c = value * saturation;
        var x = c * (1f - MathF.Abs(((hue / 60f) % 2f) - 1f));
        var m = value - c;
        var (rr, gg, bb) = hue switch
        {
            < 60f => (c, x, 0f),
            < 120f => (x, c, 0f),
            < 180f => (0f, c, x),
            < 240f => (0f, x, c),
            < 300f => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return new SKColor(
            (byte)Math.Clamp((rr + m) * 255f, 0, 255),
            (byte)Math.Clamp((gg + m) * 255f, 0, 255),
            (byte)Math.Clamp((bb + m) * 255f, 0, 255),
            color.Alpha);
    }
}
