namespace FluidBar.Core.Interaction;

/// <summary>
/// Frozen tokens for the horizontal page pager that replaces the rejected left rail
/// (user feedback 2026-10-18). Values are DIP except velocities (DIP/ms).
/// <para>
/// Pager v2: pointer gain 0.85, a dedicated snap spring (T 0.35 s / ζ 0.75), a 60 DIP wheel notch
/// with a 120 ms cooldown, plus the scroll-chain hand-off thresholds.
/// </para>
/// </summary>
public static class PagerConstants
{
    /// <summary>Horizontal page pitch in DIP (one full page step).</summary>
    public const double PagePitchDip = 400;

    /// <summary>Pointer translation factor: the content follows the pointer at 80 % (pager v2).</summary>
    public const double PointerDamping = 0.85;

    /// <summary>Edge rubber-band factor: overscroll beyond the first/last page reacts at 35 %.</summary>
    public const double EdgeResistance = 0.35;

    /// <summary>Minimum drag distance (DIP) before a release may change page at all.</summary>
    public const double PageThresholdDip = 26;

    /// <summary>Velocity (DIP/ms) above which a short drag is treated as a flick (prototype2 PAGER.flick).</summary>
    public const double FlickVelocityDipPerMs = 0.55;

    /// <summary>Minimum raw pointer travel (DIP) for a flick to count (prototype2: |dx| &gt; 10).</summary>
    public const double FlickMinimumDragDip = 10;

    /// <summary>
    /// Damped drag distance (in pages) above which the release decides by distance instead of the
    /// one-page flick / nearest-page rules (prototype2 release: |distance| &gt;= 0.75 pages).
    /// </summary>
    public const double LongDragPageFraction = 0.75;

    /// <summary>
    /// Release velocity handoff ceiling in pages per second (prototype2 clamps its spring seed to
    /// ±14 pages/s, i.e. ±5600 DIP/s at the 400 DIP page pitch).
    /// </summary>
    public const double MaxReleaseVelocityPagesPerSecond = 14;

    /// <summary>
    /// Wheel/trackpad delta (DIP) a page step consumes. Kept low so a light scroll already
    /// turns one page (feel pass 2026-10-21: "后摇" inertia, threshold band 55-70).
    /// </summary>
    public const double WheelNotchDip = 60;

    /// <summary>
    /// DIP produced by ONE physical wheel notch (60). One notch is one page step; the host maps
    /// the raw 120 delta directly to this value so scrolling feels immediate, not accumulated.
    /// </summary>
    public const double WheelDipPerNotch = WheelNotchDip;

    /// <summary>Minimum time between two wheel-driven page changes (page-turn cooldown: 120 ms).</summary>
    public const int WheelCooldownMs = 120;

    /// <summary>Minimum gap before a new wheel intent may start a fresh page turn (90 ms).</summary>
    public const int WheelNewIntentMinMs = 90;

    /// <summary>
    /// Idle gap (ms) that ends a wheel gesture. Notches closer than this belong to the same gesture
    /// and are ignored once a page step fired, so one flick can never flip several pages.
    /// </summary>
    public const int WheelGestureIdleMs = 80;

    /// <summary>
    /// Minimum gap (ms) before a *new* deliberate gesture may turn the next page. Deliberate repeat
    /// scrolls (&gt; 90 ms apart) therefore chain pages, while the 120 ms transition lock keeps one
    /// continuous gesture capped at a single page.
    /// </summary>
    public const int WheelNewIntentMs = 90;

    /// <summary>
    /// Hard lock (ms) inside a gesture after a page step: notches arriving within this window of the
    /// step are dropped unqueued (the transition is still running).
    /// </summary>
    public const int WheelTransitionLockMs = 120;

    /// <summary>Snap spring period T in seconds for the pager (0.35 s, slight overshoot).</summary>
    public const double SnapSpringPeriodSeconds = 0.35;

    /// <summary>Snap spring damping ratio ζ for the pager (0.75, a slight overshoot).</summary>
    public const double SnapSpringDampingRatio = 0.75;

    /// <summary>Default retarget frame delta used by <see cref="PagerSnapMotion"/> for velocity clamping.</summary>
    public const double RetargetFrameSeconds = 1.0 / 120.0;

    /// <summary>Scroll-chain: overscroll DIP at a content boundary before a wheel page step may fire.</summary>
    public const double ScrollChainOverscrollDip = 24;

    /// <summary>Scroll-chain: dwell ms at a content boundary before a wheel page step may fire.</summary>
    public const int ScrollChainDwellMs = 50;

    /// <summary>Default expanded content width (440 − 2 × 20 inset).</summary>
    public const double ContentWidthDip = 400;

    /// <summary>Default horizontal content inset for the no-rail panel.</summary>
    public const double ContentHorizontalInsetDip = 60;

    /// <summary>Default vertical content inset for the no-rail panel.</summary>
    public const double ContentVerticalInsetDip = 16;
}


