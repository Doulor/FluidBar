using FluidBar.Core.Motion;

namespace FluidBar.Core.Interaction;

/// <summary>
/// Snap animation for the horizontal pager: a single dedicated spring authored as
/// <b>T = 0.44 s, ζ = 0.97</b> (pager v2, stronger damping) driving the content offset towards the
/// target page. It is intentionally decoupled from <c>motion.tab</c> (the first-cut version reused
/// that token; the approved design wants a slower, calmer page snap).
/// Interruptible: <see cref="Interrupt"/> freezes the spring at its current visual offset so a new
/// drag starts from what the user actually sees (not from the target page).
/// </summary>
public sealed class PagerSnapMotion
{
    private static readonly SpringParameters SnapSpring = new(
        PagerConstants.SnapSpringPeriodSeconds,
        PagerConstants.SnapSpringDampingRatio);

    private readonly Spring _spring;

    /// <summary>Creates the snap spring at <paramref name="initialOffsetDip"/>.</summary>
    /// <param name="initialOffsetDip">Initial content offset in DIP.</param>
    public PagerSnapMotion(double initialOffsetDip = 0)
    {
        _spring = new Spring(SnapSpring, initialOffsetDip);
    }

    /// <summary>The authored spring parameters (T 0.44 s, ζ 0.97; pager v2 — not <c>motion.tab</c>).</summary>
    public static SpringParameters SpringParameters => SnapSpring;

    /// <summary>Current visual content offset in DIP.</summary>
    public double CurrentOffsetDip => _spring.Value;

    /// <summary>Target content offset in DIP.</summary>
    public double TargetOffsetDip => _spring.Target;

    /// <summary>Current angular velocity in DIP/s (diagnostics/gesture handoff).</summary>
    public double VelocityDipPerSecond => _spring.Velocity;

    /// <summary>Whether the snap animation is at rest (frame loop may stop).</summary>
    public bool IsAtRest => _spring.IsAtRest;

    /// <summary>Current fractional page position (derived from the visual offset).</summary>
    public double PagePosition => CurrentOffsetDip / PagerConstants.PagePitchDip;

    /// <summary>Snaps to a page offset (clamped page, production default pitch).</summary>
    /// <param name="page">Target page.</param>
    /// <param name="pageCount">Number of pages.</param>
    public void SnapToPage(int page, int pageCount = PagerConstants.PageCount)
        => SnapToOffset(PagePagerPolicy.PageToOffset(page, PagerConstants.PagePitchDip, pageCount));

    /// <summary>
    /// Retargets the spring to a content offset (DIP) from rest, matching prototype2's
    /// <c>springTo(target, 0)</c>: a wheel/keyboard page step always starts from zero velocity, so
    /// every step follows the authored T/ζ curve instead of inheriting the previous step's speed.
    /// </summary>
    /// <param name="offsetDip">Target content offset in DIP.</param>
    public void SnapToOffset(double offsetDip) => SnapToOffset(offsetDip, 0);

    /// <summary>
    /// Retargets the spring to a content offset (DIP) with an explicit release velocity handoff.
    /// The velocity is the pointer's release speed (<see cref="PagerConstants.PointerDamping"/>) and
    /// lets a flick keep travelling instead of stopping dead and re-accelerating from rest.
    /// </summary>
    /// <param name="offsetDip">Target content offset in DIP.</param>
    /// <param name="velocityDipPerSecond">Initial content velocity in DIP/s (positive = next page).</param>
    public void SnapToOffset(double offsetDip, double velocityDipPerSecond)
    {
        if (!double.IsFinite(offsetDip))
        {
            offsetDip = 0;
        }

        if (!double.IsFinite(velocityDipPerSecond) || velocityDipPerSecond == 0)
        {
            // Start from rest: the spring keeps the visual value but no inherited velocity.
            _spring.Reset(_spring.Value);
            _spring.Retarget(offsetDip, PagerConstants.RetargetFrameSeconds);
            return;
        }

        _spring.Reset(_spring.Value, velocityDipPerSecond);
        _spring.Retarget(offsetDip, PagerConstants.RetargetFrameSeconds);
    }

    /// <summary>
    /// Interrupts the snap animation: the spring is frozen at its current visual offset and becomes
    /// at rest, so a drag can start from there. Returns the offset to use as the drag origin.
    /// </summary>
    public double Interrupt()
    {
        var current = _spring.Value;
        _spring.SnapTo(current);
        return current;
    }

    /// <summary>Advances the snap animation by one frame; returns the new visual offset.</summary>
    public double Step(double dtSeconds) => _spring.Step(dtSeconds);

    /// <summary>Hard-jumps to an offset without animation (mode switch / restore).</summary>
    public void JumpTo(double offsetDip)
    {
        if (!double.IsFinite(offsetDip))
        {
            offsetDip = 0;
        }

        _spring.Reset(offsetDip);
    }

    /// <summary>Distance left until the target offset is reached.</summary>
    public double RemainingDistanceDip => Math.Abs(TargetOffsetDip - CurrentOffsetDip);
}
