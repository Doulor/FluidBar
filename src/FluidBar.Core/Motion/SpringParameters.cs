namespace FluidBar.Core.Motion;

/// <summary>
/// Spring authored as period <c>T</c> (seconds) and damping ratio <c>ζ</c> (spec 4.1).
/// The integrator derives <c>k = (2π/T)²</c> and <c>c = 4πζ/T</c>; never author k/c directly.
/// </summary>
public readonly record struct SpringParameters
{
    /// <summary>Creates parameters, validating the physically meaningful ranges.</summary>
    /// <param name="periodSeconds">Oscillation period T in seconds; must be positive.</param>
    /// <param name="dampingRatio">Damping ratio ζ ≥ 0 (1 = critically damped).</param>
    public SpringParameters(double periodSeconds, double dampingRatio)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(periodSeconds, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(dampingRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dampingRatio, 10);
        PeriodSeconds = periodSeconds;
        DampingRatio = dampingRatio;
    }

    /// <summary>Oscillation period T in seconds.</summary>
    public double PeriodSeconds { get; }

    /// <summary>Damping ratio ζ.</summary>
    public double DampingRatio { get; }

    /// <summary>Angular frequency <c>ω = 2π/T</c>.</summary>
    public double AngularFrequency => 2 * Math.PI / PeriodSeconds;

    /// <summary>Stiffness <c>k = (2π/T)²</c>.</summary>
    public double Stiffness => AngularFrequency * AngularFrequency;

    /// <summary>Damping coefficient <c>c = 4πζ/T = 2ζω</c>.</summary>
    public double Damping => 2 * DampingRatio * AngularFrequency;

    /// <summary>Whether the spring is critically/over damped (no oscillation).</summary>
    public bool IsCriticallyDamped => DampingRatio >= 1.0;

    /// <summary>
    /// Reference 2 % settling time (<c>t₂%</c>) used by the ±20 % acceptance tests. It is computed
    /// by <see cref="SettleTimePredictor"/> with an independent high-resolution RK4 integration of
    /// the same ODE, i.e. the expected value does not come from the runtime integrator.
    /// The spec 4.1 table lists rounded <c>0.622·T/ζ</c> approximations; for strongly damped
    /// tokens (hide, tab, expand.height) the real 2 % time is slightly longer, which this
    /// reference captures.
    /// </summary>
    public double PredictedSettleSeconds
        => DampingRatio <= 0 ? double.PositiveInfinity : SettleTimePredictor.PredictedSettleSeconds(this);

    /// <summary>Creates parameters from classical k/c values (conversion helper/tests).</summary>
    public static SpringParameters FromStiffnessDamping(double stiffness, double damping)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stiffness, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(damping);
        var omega = Math.Sqrt(stiffness);
        return new SpringParameters(2 * Math.PI / omega, damping / (2 * omega));
    }

    /// <inheritdoc />
    public override string ToString() => $"(T={PeriodSeconds:0.###}s, ζ={DampingRatio:0.###})";
}
