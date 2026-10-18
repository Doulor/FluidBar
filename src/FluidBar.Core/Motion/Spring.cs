namespace FluidBar.Core.Motion;

/// <summary>
/// One scalar spring integrated with fixed substeps (spec 4.2): semi-implicit Euler,
/// <c>H = 1/300 s</c>, <c>dt</c> clamped to <c>[1 ms, 50 ms]</c>. Frame-rate independent by
/// construction, so 60/120/144/165 Hz produce the same trajectory.
/// </summary>
public sealed class Spring
{
    /// <summary>Fixed integration substep in seconds (spec 4.2: 1/300).</summary>
    public const double FixedSubstepSeconds = 1.0 / 300.0;

    /// <summary>Minimum accepted frame delta (1 ms).</summary>
    public const double MinDeltaSeconds = 0.001;

    /// <summary>Maximum accepted frame delta (50 ms); larger deltas are clamped, not integrated.</summary>
    public const double MaxDeltaSeconds = 0.050;

    /// <summary>At-rest distance threshold in DIP (spec 4.4: 0.04).</summary>
    public const double SettleDistance = 0.04;

    /// <summary>At-rest velocity threshold in DIP/s (spec 4.4: 0.35).</summary>
    public const double SettleVelocity = 0.35;

    /// <summary>Consecutive frames under both thresholds required to settle (spec 4.4: 2).</summary>
    public const int SettleFrameCount = 2;

    /// <summary>Velocity retention when a retarget keeps the direction (spec 4.3: 100 %).</summary>
    public const double SameDirectionRetention = 1.0;

    /// <summary>Velocity retention when a retarget reverses direction (spec 4.3: ×0.35).</summary>
    public const double OppositeDirectionRetention = 0.35;

    /// <summary>Maximum <c>|v|</c> as a fraction of the remaining distance per dt (spec 4.3: 0.2).</summary>
    public const double MaxDistanceRatioPerDelta = 0.2;

    /// <summary>Snap distance for "target is basically here" (spec 4.3: 0.5 DIP).</summary>
    public const double SnapDistance = 0.5;

    /// <summary>Snap velocity for "already still" (spec 4.3: 1 DIP/s).</summary>
    public const double SnapVelocity = 1.0;

    private int _restFrames;
    private long _substeps;

    /// <summary>Creates a spring at <paramref name="value"/> with zero velocity by default.</summary>
    public Spring(SpringParameters parameters, double value = 0, double velocity = 0)
    {
        Parameters = parameters;
        Value = value;
        Target = value;
        Velocity = velocity;
        IsAtRest = velocity == 0;
        _restFrames = IsAtRest ? SettleFrameCount : 0;
    }

    /// <summary>Current spring parameters.</summary>
    public SpringParameters Parameters { get; private set; }

    /// <summary>Current value in DIP.</summary>
    public double Value { get; private set; }

    /// <summary>Current target in DIP.</summary>
    public double Target { get; private set; }

    /// <summary>Current velocity in DIP/s.</summary>
    public double Velocity { get; private set; }

    /// <summary>Whether the spring is at rest and the frame loop may stop.</summary>
    public bool IsAtRest { get; private set; }

    /// <summary>Total fixed substeps integrated (diagnostics/tests).</summary>
    public long Substeps => _substeps;

    /// <summary>Changes parameters mid-flight (reduced-motion toggles).</summary>
    public void SetParameters(SpringParameters parameters)
    {
        Parameters = parameters;
        IsAtRest = false;
        _restFrames = 0;
    }

    /// <summary>
    /// Integrates one frame. <paramref name="dtSeconds"/> is clamped to
    /// <c>[1 ms, 50 ms]</c>; the return value is the new <see cref="Value"/>.
    /// </summary>
    public double Step(double dtSeconds)
    {
        var dt = Math.Clamp(double.IsFinite(dtSeconds) ? dtSeconds : MinDeltaSeconds, MinDeltaSeconds, MaxDeltaSeconds);
        if (IsAtRest)
        {
            return Value;
        }

        var stiffness = Parameters.Stiffness;
        var damping = Parameters.Damping;
        var value = Value;
        var velocity = Velocity;
        var target = Target;
        var remaining = dt;
        while (remaining > 1e-9)
        {
            var h = Math.Min(FixedSubstepSeconds, remaining);
            var acceleration = (-stiffness * (value - target)) - (damping * velocity);
            velocity += acceleration * h;
            value += velocity * h;
            remaining -= h;
            _substeps++;
        }

        Value = value;
        Velocity = velocity;

        if (Math.Abs(Value - Target) < SettleDistance && Math.Abs(Velocity) < SettleVelocity)
        {
            _restFrames++;
            if (_restFrames >= SettleFrameCount)
            {
                SnapTo(Target);
            }
        }
        else
        {
            _restFrames = 0;
        }

        return Value;
    }

    /// <summary>
    /// Retargets the spring applying the spec 4.3 velocity rules:
    /// keep 100 % when the direction is unchanged, multiply by 0.35 when reversed,
    /// clamp <c>|v| ≤ 0.2·|Δ|/dt</c>, and snap when the target is already reached.
    /// </summary>
    /// <param name="target">New target value.</param>
    /// <param name="dtSeconds">Frame delta used for the velocity clamp (clamped to 1..50 ms).</param>
    public void Retarget(double target, double dtSeconds)
    {
        var delta = target - Value;
        var dt = Math.Clamp(double.IsFinite(dtSeconds) ? dtSeconds : MinDeltaSeconds, MinDeltaSeconds, MaxDeltaSeconds);
        if (Math.Abs(delta) < SnapDistance && Math.Abs(Velocity) < SnapVelocity)
        {
            SnapTo(target);
            return;
        }

        if (delta == 0)
        {
            SnapTo(target);
            return;
        }

        var velocity = Velocity;
        if (velocity != 0 && Math.Sign(velocity) != Math.Sign(delta))
        {
            velocity *= OppositeDirectionRetention;
        }
        else
        {
            velocity *= SameDirectionRetention;
        }

        var maxVelocity = MaxDistanceRatioPerDelta * Math.Abs(delta) / dt;
        Velocity = Math.Clamp(velocity, -maxVelocity, maxVelocity);
        Target = target;
        IsAtRest = false;
        _restFrames = 0;
    }

    /// <summary>Jumps directly to <paramref name="target"/> with zero velocity (T10 snap).</summary>
    public void SnapTo(double target)
    {
        Value = target;
        Target = target;
        Velocity = 0;
        IsAtRest = true;
        _restFrames = SettleFrameCount;
    }

    /// <summary>Resets position/velocity (sleep/wake, restore, tests).</summary>
    public void Reset(double value = 0, double velocity = 0)
    {
        Value = value;
        Target = value;
        Velocity = velocity;
        IsAtRest = velocity == 0;
        _restFrames = IsAtRest ? SettleFrameCount : 0;
    }

    /// <summary>
    /// Simulates the spring at a fixed frame delta until it is at rest.
    /// Used by settle-time acceptance tests; returns elapsed seconds.
    /// </summary>
    /// <param name="target">Target value.</param>
    /// <param name="dtSeconds">Frame delta to simulate (clamped by <see cref="Step"/>).</param>
    /// <param name="maxSeconds">Safety bound; throws when exceeded.</param>
    public double SettleTimeTo(double target, double dtSeconds, double maxSeconds = 10)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dtSeconds, 0);
        var elapsed = 0.0;
        Retarget(target, dtSeconds);
        while (!IsAtRest)
        {
            Step(dtSeconds);
            elapsed += dtSeconds;
            if (elapsed > maxSeconds)
            {
                throw new InvalidOperationException(
                    $"Spring {Parameters} did not settle within {maxSeconds}s for target {target}.");
            }
        }

        return elapsed;
    }
}

/// <summary>
/// A set of springs stepped in one loop (spec 4.2 "all springs share one loop and short-circuit
/// when everything is at rest"). The host steps the set once per frame; when <see cref="AllAtRest"/>
/// is true the frame loop is cancelled.
/// </summary>
public sealed class SpringSet
{
    private readonly List<Spring> _springs = [];

    /// <summary>Registers a spring and returns it.</summary>
    public Spring Add(Spring spring)
    {
        ArgumentNullException.ThrowIfNull(spring);
        _springs.Add(spring);
        return spring;
    }

    /// <summary>Read-only view of the springs.</summary>
    public IReadOnlyList<Spring> Springs => _springs;

    /// <summary>Number of springs currently not at rest.</summary>
    public int ActiveCount => _springs.Count(static s => !s.IsAtRest);

    /// <summary>Whether every spring is at rest (frame loop stops).</summary>
    public bool AllAtRest => _springs.All(static s => s.IsAtRest);

    /// <summary>Removes all springs.</summary>
    public void Clear() => _springs.Clear();

    /// <summary>Steps every active spring by <paramref name="dtSeconds"/>.</summary>
    public void Step(double dtSeconds)
    {
        foreach (var spring in _springs)
        {
            if (!spring.IsAtRest)
            {
                spring.Step(dtSeconds);
            }
        }
    }

    /// <summary>Snaps every spring to its current target (T10).</summary>
    public void SnapAllToTargets()
    {
        foreach (var spring in _springs)
        {
            spring.SnapTo(spring.Target);
        }
    }
}
