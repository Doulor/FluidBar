namespace FluidBar.Core.Interaction;

/// <summary>
/// Frozen tokens for the horizontal page pager that replaces the rejected left rail
/// (user feedback 2026-10-18). Values are DIP except velocities (DIP/ms).
/// <para>
/// Pager v2 (approved 2026-10-18): stronger pointer damping (0.80), a dedicated snap spring
/// (T 0.44 s / ζ 0.97), heavier wheel notch (140 DIP) with a 180 ms cooldown, plus the
/// scroll-chain hand-off thresholds. These supersede the first-cut values (0.90 / 0.34 s / 100 / 120).
/// </para>
/// </summary>
public static class PagerConstants
{
    /// <summary>Number of expanded pages: home / media / notifications / clipboard / agent / widgets.</summary>
    public const int PageCount = 6;

    /// <summary>Horizontal page pitch in DIP (one full page step).</summary>
    public const double PagePitchDip = 400;

    /// <summary>Pointer translation factor: the content follows the pointer at 80 % (pager v2).</summary>
    public const double PointerDamping = 0.80;

    /// <summary>Edge rubber-band factor: overscroll beyond the first/last page reacts at 35 %.</summary>
    public const double EdgeResistance = 0.35;

    /// <summary>Minimum drag distance (DIP) before a release may change page.</summary>
    public const double PageThresholdDip = 24;

    /// <summary>Velocity (DIP/ms) above which a short drag is treated as a flick.</summary>
    public const double FlickVelocityDipPerMs = 0.6;

    /// <summary>Drag distance (in pages) that switches from "one page max" to distance-based paging.</summary>
    public const double LongDragPageFraction = 0.5;

    /// <summary>Wheel/trackpad delta (DIP) accumulated before one page step (pager v2).</summary>
    public const double WheelNotchDip = 140;

    /// <summary>Minimum time between two wheel-driven page changes (pager v2).</summary>
    public const int WheelCooldownMs = 180;

    /// <summary>Snap spring period T in seconds for the pager (pager v2, stronger damping).</summary>
    public const double SnapSpringPeriodSeconds = 0.44;

    /// <summary>Snap spring damping ratio ζ for the pager (pager v2, stronger damping).</summary>
    public const double SnapSpringDampingRatio = 0.97;

    /// <summary>Default retarget frame delta used by <see cref="PagerSnapMotion"/> for velocity clamping.</summary>
    public const double RetargetFrameSeconds = 1.0 / 120.0;

    /// <summary>Scroll-chain: overscroll DIP at a content boundary before a wheel page step may fire.</summary>
    public const double ScrollChainOverscrollDip = 40;

    /// <summary>Scroll-chain: dwell ms at a content boundary before a wheel page step may fire.</summary>
    public const int ScrollChainDwellMs = 120;

    /// <summary>Default expanded content width (440 − 2 × 20 inset).</summary>
    public const double ContentWidthDip = 400;

    /// <summary>Default horizontal content inset for the no-rail panel.</summary>
    public const double ContentHorizontalInsetDip = 20;

    /// <summary>Default vertical content inset for the no-rail panel.</summary>
    public const double ContentVerticalInsetDip = 16;
}
