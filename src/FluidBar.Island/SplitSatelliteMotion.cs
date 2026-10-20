namespace FluidBar.Island;

/// <summary>Lifecycle of one split satellite.</summary>
public enum SplitSatelliteState
{
    /// <summary>Not born yet (nothing to draw).</summary>
    Hidden,

    /// <summary>Pre-pressing against the parent edge, then springing out.</summary>
    Birth,

    /// <summary>Settled and idle (breathing, ring spinning).</summary>
    Idle,

    /// <summary>Flying back into the parent island.</summary>
    Merge,

    /// <summary>Merged into the parent (nothing to draw).</summary>
    Merged,
}

/// <summary>One render-ready frame of a split satellite.</summary>
/// <param name="State">Lifecycle state.</param>
/// <param name="X">Centre X in DIP.</param>
/// <param name="Y">Centre Y in DIP.</param>
/// <param name="Scale">Composed scale (birth/merge, hover, press and breathing).</param>
/// <param name="StretchX">Normalised stretch along X (0 = none, 0.08 = +8 %).</param>
/// <param name="StretchY">Normalised stretch along Y.</param>
/// <param name="Highlight">Hover highlight strength (0..1).</param>
/// <param name="Brightness">Brightness delta (-0.06 while pressed).</param>
/// <param name="BreathScale">Breathing component alone (1 +/- <c>BreathAmplitude</c>).</param>
/// <param name="RingAngleDegrees">Agent ring angle in degrees (constant spin while idle).</param>
/// <param name="PressDepth">Press progress (0 = released, 1 = fully pressed).</param>
public readonly record struct SplitSatelliteValues(
    SplitSatelliteState State,
    double X,
    double Y,
    double Scale,
    double StretchX,
    double StretchY,
    double Highlight,
    double Brightness,
    double BreathScale,
    double RingAngleDegrees,
    double PressDepth);

/// <summary>
/// Motion driver for one split satellite: birth (preload - stretch - elastic settle), merge
/// (reverse with a small overshoot), hover/press, idle breathing and constant ring spin.
/// <para>
/// Every transition retargets the existing springs instead of resetting them, so the animation always
/// continues from the current visual values: an interruption mid-birth, a fast double tap or a wheel
/// event never snaps back to a start pose. Springs integrate in <= 4 ms substeps, so the result is
/// frame-rate independent and deterministic for tests.
/// </para>
/// </summary>
public sealed class SplitSatelliteMotion
{
    private readonly Spring _x = new(SatelliteMotionTokens.PositionSnapEpsilon,
        SatelliteMotionTokens.PositionSnapVelocity);
    private readonly Spring _y = new(SatelliteMotionTokens.PositionSnapEpsilon,
        SatelliteMotionTokens.PositionSnapVelocity);
    private readonly Spring _scale = new(1, SatelliteMotionTokens.MorphPeriodSeconds, SatelliteMotionTokens.MorphDampingRatio);
    private readonly Spring _hover = new(0, SatelliteMotionTokens.PressPeriodSeconds, SatelliteMotionTokens.PressDampingRatio);
    private readonly Spring _press = new(0, SatelliteMotionTokens.PressPeriodSeconds, SatelliteMotionTokens.PressDampingRatio);

    private double _elapsed;
    private double _preloadRemaining;
    private double _stretchX;
    private double _stretchY;
    private double _idleSeconds;
    private double _ringAngle;
    private bool _mergeOvershootPending;
    private double _mergeOvershootRemaining;
    private double _mergeFromX;
    private double _mergeFromY;
    private double _mergeToX;
    private double _mergeToY;

    /// <summary>Current lifecycle state.</summary>
    public SplitSatelliteState State { get; private set; } = SplitSatelliteState.Hidden;

    /// <summary>True while the springs have not settled yet.</summary>
    public bool IsAnimating => State is SplitSatelliteState.Birth or SplitSatelliteState.Merge || !_scale.Settled;

    /// <summary>Elapsed seconds since the current birth/merge started (diagnostics/tests).</summary>
    public double ElapsedSeconds => _elapsed;

    /// <summary>Current render-ready values.</summary>
    public SplitSatelliteValues Values
    {
        get
        {
            var breathing = State == SplitSatelliteState.Idle
                ? 1 + (SatelliteMotionTokens.BreathAmplitude *
                    Math.Sin(2 * Math.PI * _idleSeconds / SatelliteMotionTokens.BreathPeriodSeconds))
                : 1.0;
            var pressMultiplier = 1 + ((SatelliteMotionTokens.PressScale - 1) * _press.Value);
            var hoverMultiplier = 1 + ((SatelliteMotionTokens.HoverScale - 1) * _hover.Value);
            return new SplitSatelliteValues(
                State,
                _x.Value,
                _y.Value,
                _scale.Value * pressMultiplier * hoverMultiplier * breathing,
                _stretchX,
                _stretchY,
                _hover.Value * SatelliteMotionTokens.HoverHighlight,
                SatelliteMotionTokens.PressBrightness * _press.Value,
                breathing,
                _ringAngle,
                _press.Value);
        }
    }

    /// <summary>
    /// Starts (or redirects) the birth animation from the parent edge to the satellite seat. When the
    /// satellite is already visible the springs keep their current values/velocities, so the motion
    /// continues from where it is instead of restarting.
    /// </summary>
    /// <param name="fromX">Parent edge X in DIP.</param>
    /// <param name="fromY">Parent edge Y in DIP.</param>
    /// <param name="toX">Seat X in DIP.</param>
    /// <param name="toY">Seat Y in DIP.</param>
    /// <param name="directionX">Travel direction X (unit-ish, used for the stretch axis).</param>
    /// <param name="directionY">Travel direction Y.</param>
    public void BeginBirth(double fromX, double fromY, double toX, double toY, double directionX, double directionY)
    {
        var firstBirth = State == SplitSatelliteState.Hidden;
        if (firstBirth)
        {
            _x.Value = fromX;
            _y.Value = fromY;
            _scale.Value = SatelliteMotionTokens.PreloadScale;
            _scale.Velocity = 0;
            _stretchX = 0;
            _stretchY = 0;
            _preloadRemaining = SatelliteMotionTokens.PreloadSeconds;
        }
        else if (State is SplitSatelliteState.Merged or SplitSatelliteState.Merge)
        {
            // Re-birth from the parent edge: continue from the current pose (interruption friendly).
            _preloadRemaining = SatelliteMotionTokens.PreloadSeconds * 0.5;
        }

        State = SplitSatelliteState.Birth;
        _elapsed = 0;
        _mergeOvershootPending = false;
        _x.Target = toX;
        _y.Target = toY;
        _x.SetSpring(SatelliteMotionTokens.MorphPeriodSeconds, SatelliteMotionTokens.MorphDampingRatio);
        _y.SetSpring(SatelliteMotionTokens.MorphPeriodSeconds, SatelliteMotionTokens.MorphDampingRatio);
        _scale.SetSpring(SatelliteMotionTokens.MorphPeriodSeconds, SatelliteMotionTokens.MorphDampingRatio);
        _scale.Target = 1.0;
        var length = Math.Sqrt((directionX * directionX) + (directionY * directionY));
        if (length > 1e-6)
        {
            _stretchX = SatelliteMotionTokens.StretchAmplitude * directionX / length;
            _stretchY = SatelliteMotionTokens.StretchAmplitude * directionY / length;
        }
    }

    /// <summary>
    /// Starts (or redirects) the merge: the satellite is attracted to the parent edge, overshoots the
    /// target slightly at the end and then settles to zero offset.
    /// </summary>
    /// <param name="toX">Parent edge X in DIP.</param>
    /// <param name="toY">Parent edge Y in DIP.</param>
    public void BeginMerge(double toX, double toY)
    {
        State = SplitSatelliteState.Merge;
        _elapsed = 0;
        _mergeFromX = _x.Value;
        _mergeFromY = _y.Value;
        _mergeToX = toX;
        _mergeToY = toY;
        _preloadRemaining = 0;
        _x.Target = toX;
        _y.Target = toY;
        _x.SetSpring(SatelliteMotionTokens.MorphPeriodSeconds, SatelliteMotionTokens.MorphDampingRatio);
        _y.SetSpring(SatelliteMotionTokens.MorphPeriodSeconds, SatelliteMotionTokens.MorphDampingRatio);
        _scale.SetSpring(SatelliteMotionTokens.MorphPeriodSeconds, SatelliteMotionTokens.MorphDampingRatio);
        _scale.Target = 0.94;
        _mergeOvershootPending = true;
        _mergeOvershootRemaining = 0;
    }

    /// <summary>Turns the hover state on or off (1.02 pop + highlight lift).</summary>
    /// <param name="hover">True while the pointer is over the satellite.</param>
    public void SetHover(bool hover) => _hover.Target = hover ? 1 : 0;

    /// <summary>Presses the satellite (0.94 scale, -6 % brightness, same as the main island).</summary>
    public void Press() => _press.Target = 1;

    /// <summary>Releases the press; the spring rebounds.</summary>
    public void Release() => _press.Target = 0;

    /// <summary>Advances the animation by <paramref name="dt"/> seconds.</summary>
    /// <param name="dt">Elapsed time in seconds (frame delta).</param>
    /// <returns>True when something visibly changed.</returns>
    public bool Update(double dt)
    {
        if (dt <= 0 || State == SplitSatelliteState.Hidden)
        {
            return false;
        }

        var remaining = dt;
        while (remaining > 0)
        {
            var step = Math.Min(remaining, SatelliteMotionTokens.MaxSubstepSeconds);
            remaining -= step;
            _elapsed += step;

            if (State == SplitSatelliteState.Birth && _preloadRemaining > 0)
            {
                _preloadRemaining = Math.Max(0, _preloadRemaining - step);
                _scale.Value = SatelliteMotionTokens.PreloadScale;
                _scale.Velocity = 0;
                continue; // the satellite is still pressed against the parent edge
            }

            if (State == SplitSatelliteState.Birth)
            {
                // elastic relax of the 8 % stretch
                var decay = Math.Exp(-_elapsed / SatelliteMotionTokens.StretchDecaySeconds);
                _stretchX *= decay;
                _stretchY *= decay;
            }

            _x.Step(step);
            _y.Step(step);
            _scale.Step(step);
            _hover.Step(step);
            _press.Step(step);

            if (State == SplitSatelliteState.Birth && _x.Settled && _y.Settled && _scale.Settled)
            {
                State = SplitSatelliteState.Idle;
                _idleSeconds = 0;
            }
            else if (State == SplitSatelliteState.Merge)
            {
                if (_mergeOvershootPending && _x.Settled && _y.Settled)
                {
                    // One small overshoot at the end: aim 2 % past the landing point for 80 ms, then
                    // settle back exactly on it.
                    _mergeOvershootPending = false;
                    _mergeOvershootRemaining = 0.08;
                    _x.Target = _mergeToX + ((SatelliteMotionTokens.MergeOvershootFactor - 1) *
                        (_mergeToX - _mergeFromX));
                    _y.Target = _mergeToY + ((SatelliteMotionTokens.MergeOvershootFactor - 1) *
                        (_mergeToY - _mergeFromY));
                    _x.Velocity = 0;
                    _y.Velocity = 0;
                }

                if (_mergeOvershootRemaining > 0)
                {
                    _mergeOvershootRemaining = Math.Max(0, _mergeOvershootRemaining - step);
                    if (_mergeOvershootRemaining == 0)
                    {
                        _x.Target = _mergeToX;
                        _y.Target = _mergeToY;
                    }
                }

                if (_x.Settled && _y.Settled && _scale.Settled && _mergeOvershootRemaining <= 0)
                {
                    State = SplitSatelliteState.Merged;
                }
            }
            else if (State == SplitSatelliteState.Idle)
            {
                _idleSeconds += step;
                _ringAngle = (_ringAngle +
                    (360 * SatelliteMotionTokens.AgentRingTurnsPerSecond * step)) % 360;
            }
        }

        return true;
    }

    /// <summary>Frame-rate independent damped spring (semi-implicit Euler, snapping when settled).</summary>
    private sealed class Spring
    {
        private readonly double _epsilon;
        private readonly double _velocityEpsilon;
        private double _omega = 2 * Math.PI / 0.3;
        private double _damping = 2 * 0.8 * (2 * Math.PI / 0.3);

        public Spring(double epsilon = SatelliteMotionTokens.UnitSnapEpsilon,
            double velocityEpsilon = SatelliteMotionTokens.UnitSnapVelocity)
        {
            _epsilon = epsilon;
            _velocityEpsilon = velocityEpsilon;
        }

        public Spring(double value, double period, double zeta)
            : this()
        {
            Value = value;
            Target = value;
            SetSpring(period, zeta);
        }

        public double Value { get; set; }

        public double Target { get; set; }

        public double Velocity { get; set; }

        public bool Settled => Math.Abs(Value - Target) < _epsilon && Math.Abs(Velocity) < _velocityEpsilon;

        public void SetSpring(double period, double zeta)
        {
            _omega = 2 * Math.PI / period;
            _damping = 2 * zeta * _omega;
        }

        public void Step(double dt)
        {
            var acceleration = (-_omega * _omega * (Value - Target)) - (_damping * Velocity);
            Velocity += acceleration * dt;
            Value += Velocity * dt;
            if (Settled)
            {
                Value = Target;
                Velocity = 0;
            }
        }
    }
}
