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
