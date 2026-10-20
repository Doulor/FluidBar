namespace FluidBar.Island;

/// <summary>
/// Motion tokens for the split satellites (the small islands that bud off the main island). Only the
/// split/satellite values live here; the shared table in <c>FluidBar.Core.Motion.MotionTokens</c> is not
/// touched. Values are researched the same way as the core table:
/// <list type="bullet">
/// <item>Telegram-iOS spring presets (DGSpring / display link spring): duration 0.4-0.5 s with
/// ζ 0.65-0.72 for "weighty but not wobbly" pops.</item>
/// <item>iOS/UIKit spring (interactive, damping 0.68, response 0.46 s) as used for app-launch style
/// morphs; the same numbers reproduce the observable overshoot of ~5-7 %.</item>
/// <item>MioIsland press choreography (scale 0.94-0.97, 6 % brightness lift, 60 ms pre-press).</item>
/// </list>
/// </summary>
public static class SatelliteMotionTokens
{
    // ---- birth / merge -------------------------------------------------

    /// <summary>Spring period for the birth and merge morphs (tuned so the whole birth settles in 0.42-0.50 s).</summary>
    public const double MorphPeriodSeconds = 0.30;

    /// <summary>Damping ratio for birth/merge: under-damped enough for one visible overshoot (~6 %).</summary>
    public const double MorphDampingRatio = 0.68;

    /// <summary>Lower bound of the whole birth animation; the spring must settle inside 0.42-0.50 s.</summary>
    public const double MorphMinDurationSeconds = 0.42;

    /// <summary>Upper bound of the whole birth animation.</summary>
    public const double MorphMaxDurationSeconds = 0.50;

    /// <summary>Pre-press window before the satellite leaves the parent edge (gives the motion weight).</summary>
    public const double PreloadSeconds = 0.060;

    /// <summary>Scale held during the preload window (Telegram "squash before pop").</summary>
    public const double PreloadScale = 0.92;

    /// <summary>Stretch applied along the direction of travel at the start of the birth (8 %).</summary>
    public const double StretchAmplitude = 0.08;

    /// <summary>Time constant of the stretch decay; the shape relaxes before the position settles.</summary>
    public const double StretchDecaySeconds = 0.16;

    /// <summary>Merge overshoot: the satellite lands 2 % past the target before settling back.</summary>
    public const double MergeOvershootFactor = 1.02;

    // ---- hover / press -------------------------------------------------

    /// <summary>Hover scale (main island hover uses the same 1.02 pop).</summary>
    public const double HoverScale = 1.02;

    /// <summary>Hover highlight lift (0..1 relative opacity of the specular ring).</summary>
    public const double HoverHighlight = 0.35;

    /// <summary>Press scale, identical to the main island press (0.94).</summary>
    public const double PressScale = 0.94;

    /// <summary>Press brightness change (-6 %, matches the main island press).</summary>
    public const double PressBrightness = -0.06;

    /// <summary>Spring period of the press/release rebound (fast, one small bounce).</summary>
    public const double PressPeriodSeconds = 0.18;

    /// <summary>Damping ratio of the press/release rebound.</summary>
    public const double PressDampingRatio = 0.62;

    // ---- idle breathing -------------------------------------------------

    /// <summary>Breathing period of the idle satellite: deliberately slow (4 s).</summary>
    public const double BreathPeriodSeconds = 4.0;

    /// <summary>Breathing amplitude: +/-1.2 % of the satellite scale, enough to feel alive, not to wobble.</summary>
    public const double BreathAmplitude = 0.012;

    /// <summary>Constant turns per second of the Agent blue ring while idle.</summary>
    public const double AgentRingTurnsPerSecond = 1.0 / 3.0;

    // ---- integration ----------------------------------------------------

    /// <summary>Maximum integration substep; matches the rail spring (4 ms) so behaviour is frame-rate independent.</summary>
    public const double MaxSubstepSeconds = 0.004;

    /// <summary>Snap threshold for the position springs (DIP): 1.25 % of a 40 DIP travel.</summary>
    public const double PositionSnapEpsilon = 0.5;

    /// <summary>Snap threshold for the position springs' velocity (DIP/s).</summary>
    public const double PositionSnapVelocity = 2.0;

    /// <summary>Snap threshold for the scale/hover/press springs (unitless).</summary>
    public const double UnitSnapEpsilon = 0.004;

    /// <summary>Snap threshold for the unit springs' velocity.</summary>
    public const double UnitSnapVelocity = 0.02;
}
