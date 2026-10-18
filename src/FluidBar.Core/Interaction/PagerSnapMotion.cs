using FluidBar.Core.Motion;

namespace FluidBar.Core.Interaction;

/// <summary>
/// Snap animation for the horizontal pager: a single spring authored as
/// <c>motion.tab</c> (T = 0.34 s, ζ = 0.92) driving the content offset towards the target page.
/// Interruptible: <see cref="Interrupt"/> freezes the spring at its current visual offset so a new
/// drag starts from what the user actually sees (not from the target page).
/// </summary>
public sealed class PagerSnapMotion
{
    private readonly Spring _spring;

    /// <summary>Creates the snap spring at <paramref name="initialOffsetDip"/>.</summary>
    /// <param name="initialOffsetDip">Initial content offset in DIP.</param>
    public PagerSnapMotion(double initialOffsetDip = 0)
    {
        _spring = new Spring(MotionTokens.Tab.Spring, initialOffsetDip);
    }

    /// <summary>The authored spring parameters (T 0.34 s, ζ 0.92 — equal to <c>motion.tab</c>).</summary>
    public static SpringParameters SpringParameters => MotionTokens.Tab.Spring;

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

    /// <summary>Retargets the spring to an arbitrary content offset (DIP).</summary>
    public void SnapToOffset(double offsetDip)
    {
        if (!double.IsFinite(offsetDip))
        {
            offsetDip = 0;
        }

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
