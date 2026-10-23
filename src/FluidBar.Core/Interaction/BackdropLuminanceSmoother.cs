namespace FluidBar.Core.Interaction;

/// <summary>
/// Eases the backdrop luminance used by the liquid-glass tint/scrim so a desktop that jumps from a white
/// page to a dark one does not flip the glass look in a single frame (reported 2026-10-23). Exponential
/// smoothing with a fixed time constant; frame-rate independent.
/// </summary>
public sealed class BackdropLuminanceSmoother
{
    private readonly double _timeConstantMs;
    private readonly double _settleEpsilon;
    private double _value;

    /// <summary>Creates a smoother.</summary>
    /// <param name="initial">Starting luminance (0..1).</param>
    /// <param name="timeConstantMs">Time constant: ~63 % of the distance is covered in this many ms.</param>
    /// <param name="settleEpsilon">Distance at which the value counts as settled.</param>
    public BackdropLuminanceSmoother(double initial = 0.5, double timeConstantMs = 160, double settleEpsilon = 0.004)
    {
        _value = Math.Clamp(initial, 0, 1);
        Target = _value;
        _timeConstantMs = Math.Max(16, timeConstantMs);
        _settleEpsilon = Math.Max(0.0005, settleEpsilon);
    }

    /// <summary>Smoothed luminance (0..1).</summary>
    public double Value => _value;

    /// <summary>Latest target luminance (0..1).</summary>
    public double Target { get; private set; }

    /// <summary>True when the value has reached the target.</summary>
    public bool IsSettled => Math.Abs(_value - Target) <= _settleEpsilon;

    /// <summary>Sets a new target immediately (first frame / host reset).</summary>
    /// <param name="target">Luminance 0..1.</param>
    public void Reset(double target)
    {
        Target = Math.Clamp(target, 0, 1);
        _value = Target;
    }

    /// <summary>Advances the smoothing.</summary>
    /// <param name="target">New measured luminance (0..1).</param>
    /// <param name="deltaMs">Elapsed time since the previous step, in ms.</param>
    /// <returns>The smoothed value.</returns>
    public double Step(double target, double deltaMs)
    {
        Target = Math.Clamp(target, 0, 1);
        // Cap the step so a late frame (idle -> first paint) cannot jump most of the distance at once.
        var dt = Math.Clamp(deltaMs, 0, 50);
        if (dt <= 0)
        {
            return _value;
        }

        var k = 1 - Math.Exp(-dt / _timeConstantMs);
        _value += (Target - _value) * k;
        if (Math.Abs(Target - _value) <= _settleEpsilon)
        {
            _value = Target;
        }

        return _value;
    }
}
