using System;

namespace FluidBar.Core.Motion;

/// <summary>
/// CSS <c>cubic-bezier(x1, y1, x2, y2)</c> timing function: solves the unit cubic Bezier curve for the
/// output value <c>y</c> at a normalized input time <c>x</c>. Control-point x values must stay inside
/// <c>[0, 1]</c> (CSS timing-function rule); y values may overshoot.
/// </summary>
public readonly struct CubicBezierEasing
{
    private const double Tolerance = 1e-7;
    private const int NewtonIterations = 8;

    /// <summary>Creates an easing curve from its two control points.</summary>
    /// <param name="x1">First control point x (0..1).</param>
    /// <param name="y1">First control point y.</param>
    /// <param name="x2">Second control point x (0..1).</param>
    /// <param name="y2">Second control point y.</param>
    public CubicBezierEasing(double x1, double y1, double x2, double y2)
    {
        if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2))
        {
            throw new ArgumentOutOfRangeException(nameof(x1), "Cubic-bezier control points must be finite.");
        }

        if (x1 < 0 || x1 > 1 || x2 < 0 || x2 > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(x1), "Cubic-bezier x control points must be within [0, 1].");
        }

        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    /// <summary>First control point x.</summary>
    public double X1 { get; }

    /// <summary>First control point y.</summary>
    public double Y1 { get; }

    /// <summary>Second control point x.</summary>
    public double X2 { get; }

    /// <summary>Second control point y.</summary>
    public double Y2 { get; }

    /// <summary>Evaluates the curve: <paramref name="progress"/> is the normalized input time.</summary>
    /// <param name="progress">Normalized time (values outside 0..1 are clamped).</param>
    /// <returns>The eased output value (0 at progress 0, 1 at progress 1).</returns>
    public double Evaluate(double progress)
    {
        if (!double.IsFinite(progress) || progress <= 0)
        {
            return 0;
        }

        if (progress >= 1)
        {
            return 1;
        }

        // Linear curves (identical control points) need no root finding.
        if (Math.Abs(X1 - Y1) < Tolerance && Math.Abs(X2 - Y2) < Tolerance)
        {
            return progress;
        }

        var t = SolveParameter(progress);
        return SampleY(t);
    }

    /// <summary>The parameter <c>t</c> at which the curve's x equals <paramref name="x"/>.</summary>
    private double SolveParameter(double x)
    {
        var t = x;
        for (var i = 0; i < NewtonIterations; i++)
        {
            var error = SampleX(t) - x;
            if (Math.Abs(error) < Tolerance)
            {
                return t;
            }

            var derivative = SampleDerivativeX(t);
            if (Math.Abs(derivative) < 1e-9)
            {
                break;
            }

            t -= error / derivative;
        }

        // Newton stalled (flat segment): bisection over the parameter domain.
        var low = 0.0;
        var high = 1.0;
        t = x;
        while (high - low > Tolerance)
        {
            if (SampleX(t) < x)
            {
                low = t;
            }
            else
            {
                high = t;
            }

            t = (low + high) * 0.5;
            if (Math.Abs(SampleX(t) - x) < Tolerance)
            {
                break;
            }
        }

        return t;
    }

    private double SampleX(double t) => Sample(X1, X2, t);

    private double SampleY(double t) => Sample(Y1, Y2, t);

    private static double Sample(double p1, double p2, double t)
    {
        var inv = 1 - t;
        return (3 * inv * inv * t * p1) + (3 * inv * t * t * p2) + (t * t * t);
    }

    private double SampleDerivativeX(double t)
    {
        var inv = 1 - t;
        return (3 * inv * inv * X1) + (6 * inv * t * (X2 - X1)) + (3 * t * t * (1 - X2));
    }

    /// <inheritdoc />
    public override string ToString() => $"cubic-bezier({X1:0.###}, {Y1:0.###}, {X2:0.###}, {Y2:0.###})";
}

/// <summary>
/// Prototype2 shell transition tokens (measured from <c>docs/rewrite/prototype2/styles.css</c> with
/// Playwright: <c>getComputedStyle(.island).transition</c> and <c>Animation.currentTime</c> sampling).
/// The expanded shell is a CSS transition, not a spring: width/height 380 ms with the shell curve,
/// radius 260 ms ease-out, and the compact/expanded/chrome layers fade with their own timings.
/// </summary>
public static class ShellTransition
{
    /// <summary>Shell width/height transition duration (380 ms; prototype2 <c>--dur-shell</c>).</summary>
    public const double ShellDurationSeconds = 0.380;

    /// <summary>Shell border-radius transition duration (260 ms).</summary>
    public const double RadiusDurationSeconds = 0.260;

    /// <summary>Compact layer fade-out when expanding (90 ms, ease-in, no delay).</summary>
    public const double CompactFadeOutSeconds = 0.090;

    /// <summary>Compact layer fade-in when collapsing (140 ms, ease-out).</summary>
    public const double CompactFadeInSeconds = 0.140;

    /// <summary>Compact layer fade-in delay when collapsing (40 ms).</summary>
    public const double CompactFadeInDelaySeconds = 0.040;

    /// <summary>Expanded layer fade-in when expanding (220 ms, ease-out).</summary>
    public const double ExpandedFadeInSeconds = 0.220;

    /// <summary>Expanded layer fade-in delay when expanding (90 ms).</summary>
    public const double ExpandedFadeInDelaySeconds = 0.090;

    /// <summary>Expanded layer fade-out when collapsing (140 ms, ease-out, no delay).</summary>
    public const double ExpandedFadeOutSeconds = 0.140;

    /// <summary>Chrome (title + collapse control) fade duration (160 ms).</summary>
    public const double ChromeFadeSeconds = 0.160;

    /// <summary>Chrome fade-in delay when expanding (90 ms).</summary>
    public const double ChromeFadeInDelaySeconds = 0.090;

    /// <summary>Prototype2 <c>--ease-shell</c>: <c>cubic-bezier(0.3, 0.8, 0.25, 1)</c>.</summary>
    public static CubicBezierEasing ShellEasing { get; } = new(0.3, 0.8, 0.25, 1);

    /// <summary>Prototype2 <c>--ease-out</c>: <c>cubic-bezier(0.2, 0.8, 0.3, 1)</c>.</summary>
    public static CubicBezierEasing EaseOut { get; } = new(0.2, 0.8, 0.3, 1);

    /// <summary>Prototype2 <c>--ease-in</c>: <c>cubic-bezier(0.4, 0, 1, 1)</c>.</summary>
    public static CubicBezierEasing EaseIn { get; } = new(0.4, 0, 1, 1);
}

/// <summary>
/// One scalar value driven by CSS-transition semantics: a change retargets from the current value over
/// <c>duration</c> after an optional <c>delay</c>, eased by a <see cref="CubicBezierEasing"/>. This is the
/// time-based counterpart of <see cref="Spring"/> and is used wherever the prototype relies on a CSS
/// transition (shell width/height/radius and the layer fades) instead of a spring.
/// </summary>
public sealed class CssTransition
{
    private double _from;
    private double _to;
    private double _elapsed;
    private double _duration;
    private double _delay;
    private CubicBezierEasing _easing;
    private bool _running;

    /// <summary>Creates a settled transition at <paramref name="value"/>.</summary>
    /// <param name="value">Initial value.</param>
    public CssTransition(double value)
    {
        _from = value;
        _to = value;
        Value = value;
        _easing = ShellTransition.ShellEasing;
    }

    /// <summary>Current animated value.</summary>
    public double Value { get; private set; }

    /// <summary>Target value the transition is heading to.</summary>
    public double Target => _to;

    /// <summary>Whether the transition has finished (no frame work remains).</summary>
    public bool IsAtRest => !_running;

    /// <summary>Starts or continues a transition towards <paramref name="target"/>.</summary>
    /// <param name="target">Destination value.</param>
    /// <param name="durationSeconds">Transition duration; a non-positive value snaps.</param>
    /// <param name="easing">Timing function.</param>
    /// <param name="delaySeconds">Delay before the eased segment starts.</param>
    public void Retarget(double target, double durationSeconds, CubicBezierEasing easing, double delaySeconds = 0)
    {
        if (!double.IsFinite(target))
        {
            return;
        }

        // CSS does not restart a transition when the computed value is unchanged.
        if (Math.Abs(target - _to) < 1e-9 && (_running || Math.Abs(target - Value) < 1e-9))
        {
            return;
        }

        if (!(durationSeconds > 0) || !double.IsFinite(durationSeconds))
        {
            SnapTo(target);
            return;
        }

        if (Math.Abs(target - Value) < 1e-9)
        {
            SnapTo(target);
            return;
        }

        _from = Value;
        _to = target;
        _duration = durationSeconds;
        _delay = Math.Max(0, double.IsFinite(delaySeconds) ? delaySeconds : 0);
        _easing = easing;
        _elapsed = 0;
        _running = true;
    }

    /// <summary>Steps the transition by a frame delta; returns whether it is still running.</summary>
    /// <param name="deltaSeconds">Frame delta in seconds.</param>
    public bool Step(double deltaSeconds)
    {
        if (!_running)
        {
            return false;
        }

        if (!(deltaSeconds > 0) || !double.IsFinite(deltaSeconds))
        {
            return true;
        }

        _elapsed += deltaSeconds;
        var local = _elapsed - _delay;
        if (local <= 0)
        {
            Value = _from;
            return true;
        }

        var progress = Math.Min(1, local / _duration);
        Value = _from + ((_to - _from) * _easing.Evaluate(progress));
        if (progress >= 1)
        {
            Value = _to;
            _running = false;
            return false;
        }

        return true;
    }

    /// <summary>Jumps directly to <paramref name="value"/> (first appearance, reduced motion, restore).</summary>
    /// <param name="value">New value.</param>
    public void SnapTo(double value)
    {
        Value = value;
        _from = value;
        _to = value;
        _elapsed = 0;
        _duration = 0;
        _delay = 0;
        _running = false;
    }

    /// <summary>Completes instantly at the current target (keeps the transition semantics).</summary>
    public void Complete()
    {
        Value = _to;
        _running = false;
    }
}
