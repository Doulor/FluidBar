namespace FluidBar.Island;

/// <summary>
/// "Soften" yield animation (the preset the user picked from the web prototype): the whole island
/// blurs, loses saturation and fades back while the pointer works on the window underneath. The effect
/// is applied to the composited scene, so it behaves identically for obsidian, simulated glass and
/// liquid glass.
/// </summary>
public static class YieldTokens
{
    /// <summary>Blur radius at full yield, in DIP.</summary>
    public const double SoftenBlurDip = 6.0;

    /// <summary>How much opacity is given up at full yield (0.75 → the island ends at 25 %).</summary>
    public const double SoftenFade = 0.75;

    /// <summary>Saturation multiplier at full yield (1 = unchanged).</summary>
    public const double SoftenSaturation = 0.55;

    /// <summary>Duration of the yield transition, in milliseconds.</summary>
    public const double SoftenDurationMs = 240.0;

    /// <summary>Duration of the restore transition (slightly slower in, per the prototype), in ms.</summary>
    public const double RestoreDurationMs = 260.0;

    /// <summary>Largest shell area (DIP²) that still gets the blur; bigger surfaces fade only (CPU budget).</summary>
    public const double MaxBlurAreaDip2 = 60_000.0;
}
