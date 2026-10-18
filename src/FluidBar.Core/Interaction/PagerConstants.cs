namespace FluidBar.Core.Interaction;

/// <summary>
/// Frozen tokens for the horizontal page pager that replaces the rejected left rail
/// (user feedback 2026-10-18). All values are DIP except velocities (DIP/ms).
/// </summary>
public static class PagerConstants
{
    /// <summary>Number of expanded pages: home / media / notifications / clipboard / agent / widgets.</summary>
    public const int PageCount = 6;

    /// <summary>Horizontal page pitch in DIP (the old 440-wide shell minus nothing: one full page step).</summary>
    public const double PagePitchDip = 400;

    /// <summary>Pointer translation factor: the content follows the pointer at 90 %.</summary>
    public const double PointerDamping = 0.90;

    /// <summary>Edge rubber-band factor: overscroll beyond the first/last page reacts at 35 %.</summary>
    public const double EdgeResistance = 0.35;

    /// <summary>Minimum drag distance (DIP) before a release may change page.</summary>
    public const double PageThresholdDip = 24;

    /// <summary>Velocity (DIP/ms) above which a short drag is treated as a flick.</summary>
    public const double FlickVelocityDipPerMs = 0.6;

    /// <summary>Drag distance (in pages) that switches from "one page max" to distance-based paging.</summary>
    public const double LongDragPageFraction = 0.5;

    /// <summary>Wheel/trackpad delta (DIP) accumulated before one page step.</summary>
    public const double WheelNotchDip = 100;

    /// <summary>Minimum time between two wheel-driven page changes.</summary>
    public const int WheelCooldownMs = 120;

    /// <summary>Default retarget frame delta used by <see cref="PagerSnapMotion"/> for velocity clamping.</summary>
    public const double RetargetFrameSeconds = 1.0 / 120.0;

    /// <summary>Default expanded content width (440 − 2 × 20 inset).</summary>
    public const double ContentWidthDip = 400;

    /// <summary>Default horizontal content inset for the no-rail panel.</summary>
    public const double ContentHorizontalInsetDip = 20;

    /// <summary>Default vertical content inset for the no-rail panel.</summary>
    public const double ContentVerticalInsetDip = 16;
}
