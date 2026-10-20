using System.Collections.Immutable;

namespace FluidBar.Core.Motion;

/// <summary>One named motion token: stable name, spring parameters and intended usage.</summary>
/// <param name="Name">Token name (spec 4.1, e.g. <c>motion.expand.height</c>).</param>
/// <param name="Spring">Authored spring parameters.</param>
/// <param name="Usage">Human-readable usage note.</param>
public readonly record struct MotionToken(string Name, SpringParameters Spring, string Usage);

/// <summary>
/// The motion token table of spec 4.1. Values are part of the contract: the settle-time tests
/// assert each token converges within ±20 % of <see cref="SpringParameters.PredictedSettleSeconds"/>.
/// </summary>
public static class MotionTokens
{
    /// <summary>Compact → expanded width.</summary>
    public static MotionToken Expand { get; } = new("motion.expand", new SpringParameters(0.36, 0.82), "compact -> expanded (width)");

    /// <summary>Compact → expanded height (lags the width by ~8 %, ζ + 0.04).</summary>
    public static MotionToken ExpandHeight { get; } = new("motion.expand.height", new SpringParameters(0.39, 0.86), "compact -> expanded (height)");

    /// <summary>Expanded → compact (width and height).</summary>
    public static MotionToken Collapse { get; } = new("motion.collapse", new SpringParameters(0.30, 1.00), "expanded -> compact");

    /// <summary>Page switch / content size morph inside the settled shell.</summary>
    public static MotionToken Morph { get; } = new("motion.morph", new SpringParameters(0.28, 0.85), "in-panel page/morph");

    /// <summary>Hover/peek/press-release pop.</summary>
    public static MotionToken Hover { get; } = new("motion.hover", new SpringParameters(0.24, 0.70), "peek / icon pop / drag rebound");

    /// <summary>Press scale.</summary>
    public static MotionToken Press { get; } = new("motion.press", new SpringParameters(0.18, 0.65), "press 0.97");

    /// <summary>Rail indicator / horizontal page motion.</summary>
    public static MotionToken Tab { get; } = new("motion.tab", new SpringParameters(0.34, 0.92), "rail indicator / page x");

    /// <summary>Split-islet cell division: bud → detach → settle (slightly under-damped for the pop).</summary>
    public static MotionToken Split { get; } = new("motion.split", new SpringParameters(0.46, 0.68), "split islet cell division");

    /// <summary>Stack-order exchange after a wheel rotation (one-shot, settles quickly).</summary>
    public static MotionToken StackExchange { get; } = new("motion.stack.exchange", new SpringParameters(0.28, 0.80), "ghost stack order exchange");

    /// <summary>Preview-island morph spring (prototype RAIL_SPRING: T 0.32 s, ζ 0.85).</summary>
    public static MotionToken Rail { get; } = new("motion.rail", new SpringParameters(0.32, 0.85), "preview island morph");

    /// <summary>New-event vertical nudge.</summary>
    public static MotionToken Nudge { get; } = new("motion.nudge", new SpringParameters(0.26, 0.55), "new event -6 DIP vertical kick");

    /// <summary>Fade/hide-out spring.</summary>
    public static MotionToken Hide { get; } = new("motion.hide", new SpringParameters(0.30, 0.95), "fade out / hide");
}

/// <summary>Non-spring durations in milliseconds (spec 4.6).</summary>
public sealed record MotionDurations
{
    /// <summary>The spec values.</summary>
    public static MotionDurations Default { get; } = new();

    /// <summary>Content fade-in.</summary>
    public int FadeInMs { get; init; } = 180;

    /// <summary>Content fade-out.</summary>
    public int FadeOutMs { get; init; } = 120;

    /// <summary>Content replacement cross-fade.</summary>
    public int CrossFadeMs { get; init; } = 220;

    /// <summary>List child stagger.</summary>
    public int StaggerMs { get; init; } = 32;

    /// <summary>Hover intent delay.</summary>
    public int HoverIntentMs { get; init; } = 90;

    /// <summary>Press feedback delay.</summary>
    public int PressFeedbackMs { get; init; } = 90;

    /// <summary>Leave debounce.</summary>
    public int LeaveDebounceMs { get; init; } = 120;

    /// <summary>Peek auto-retract dwell.</summary>
    public int PeekDwellMs { get; init; } = 2400;

    /// <summary>Transient activity visible duration.</summary>
    public int ToastHoldMs { get; init; } = 3200;

    /// <summary>Material swap cross-fade.</summary>
    public int MaterialSwapMs { get; init; } = 180;

    /// <summary>Artwork flip on track change.</summary>
    public int SpinMs { get; init; } = 600;

    /// <summary>Hard animation ceiling (Apple Live Activity: 2000 ms).</summary>
    public int MaxAnimationMs { get; init; } = 2000;

    /// <summary>Clamps a requested duration to the hard ceiling.</summary>
    public TimeSpan Clamp(TimeSpan duration)
        => duration <= TimeSpan.Zero
            ? TimeSpan.Zero
            : duration > TimeSpan.FromMilliseconds(MaxAnimationMs)
                ? TimeSpan.FromMilliseconds(MaxAnimationMs)
                : duration;
}

/// <summary>Cubic-bezier control points for the non-spring transitions.</summary>
/// <param name="X1">First control point x.</param>
/// <param name="Y1">First control point y.</param>
/// <param name="X2">Second control point x.</param>
/// <param name="Y2">Second control point y.</param>
public readonly record struct CubicBezier(double X1, double Y1, double X2, double Y2);

/// <summary>Named easing curves (spec 4.6).</summary>
public static class MotionCurves
{
    /// <summary>Enter curve: <c>cubic-bezier(.2,0,0,1)</c>.</summary>
    public static CubicBezier Enter { get; } = new(0.2, 0, 0, 1);

    /// <summary>Exit curve: <c>cubic-bezier(.4,0,1,1)</c>.</summary>
    public static CubicBezier Exit { get; } = new(0.4, 0, 1, 1);

    /// <summary>Move curve: <c>cubic-bezier(.32,.72,0,1)</c>.</summary>
    public static CubicBezier Move { get; } = new(0.32, 0.72, 0, 1);

    /// <summary>Overshoot curve: <c>cubic-bezier(.16,1.4,.3,1)</c>.</summary>
    public static CubicBezier Overshoot { get; } = new(0.16, 1.4, 0.3, 1);
}

/// <summary>
/// Motion policy: maps authored tokens to effective parameters under reduced motion and the
/// user motion-strength setting, and derives non-animated content opacity from the shell height.
/// </summary>
public sealed record MotionProfile
{
    /// <summary>Default profile (normal motion, 100 % strength).</summary>
    public static MotionProfile Default { get; } = new();

    /// <summary>Minimum accepted motion strength.</summary>
    public const double MinStrength = 0.5;

    /// <summary>Maximum accepted motion strength.</summary>
    public const double MaxStrength = 1.5;

    /// <summary>Reduced-motion preference (Windows "show animations" / settings tri-state).</summary>
    public bool ReducedMotion { get; init; }

    /// <summary>Motion strength from settings (0.5 = calmer, 1.5 = snappier).</summary>
    public double Strength { get; init; } = 1.0;

    /// <summary>Normalized strength, clamped to the supported range.</summary>
    public double NormalizedStrength => Math.Clamp(Strength, MinStrength, MaxStrength);

    /// <summary>
    /// Applies reduced motion (ζ → 1, T × 0.5) and motion strength (T ÷ strength) to a token.
    /// </summary>
    public SpringParameters Resolve(MotionToken token)
    {
        var spring = token.Spring;
        if (ReducedMotion)
        {
            return new SpringParameters(spring.PeriodSeconds * 0.5, 1.0);
        }

        return spring with { };
    }

    /// <summary>Resolves the effective spring parameters (strength-scaled when allowed).</summary>
    public SpringParameters ResolveParameters(MotionToken token)
    {
        var resolved = Resolve(token);
        return new SpringParameters(resolved.PeriodSeconds / NormalizedStrength, resolved.DampingRatio);
    }

    /// <summary>Scales a non-spring duration: reduced motion halves it, then the ceiling applies.</summary>
    public int ScaleDurationMs(int milliseconds, MotionDurations? durations = null)
    {
        var scaled = ReducedMotion ? milliseconds / 2 : milliseconds;
        var max = (durations ?? MotionDurations.Default).MaxAnimationMs;
        return Math.Clamp(scaled, 0, max);
    }

    /// <summary>Nudge amplitude: 0 under reduced motion (spec 2.9).</summary>
    public double NudgeAmplitude(double normalAmplitude) => ReducedMotion ? 0 : normalAmplitude;

    /// <summary>Whether accent bloom is allowed (disabled under reduced motion).</summary>
    public bool AllowsBloom => !ReducedMotion;

    /// <summary>Whether artwork spin is allowed (disabled under reduced motion).</summary>
    public bool AllowsSpin => !ReducedMotion;

    /// <summary>Whether press scale animation is allowed.</summary>
    public bool AllowsPressScale => !ReducedMotion;
}

/// <summary>
/// Content opacity derived from the shell height (never animated independently, spec 4.1):
/// <c>alpha = clamp((h − 0.62·h_page) / (0.35·h_page), 0, 1)</c>; compact content fades inversely.
/// </summary>
public static class ContentOpacityPolicy
{
    /// <summary>Height below which expanded content is fully transparent (0.62 × page height).</summary>
    public const double ExpandedStartFactor = 0.62;

    /// <summary>Height range over which expanded content fades in (0.35 × page height).</summary>
    public const double ExpandedRangeFactor = 0.35;

    /// <summary>Compact content inverse factor (1.9).</summary>
    public const double CompactInverseFactor = 1.9;

    /// <summary>Expanded content opacity for a current shell height and the page target height.</summary>
    public static double ForExpandedContent(double currentHeight, double pageHeight)
    {
        if (pageHeight <= 0)
        {
            return 0;
        }

        var alpha = (currentHeight - (ExpandedStartFactor * pageHeight)) / (ExpandedRangeFactor * pageHeight);
        return Math.Clamp(alpha, 0, 1);
    }

    /// <summary>Compact content opacity derived from the same expanded alpha (inverse fade).</summary>
    public static double ForCompactContent(double currentHeight, double pageHeight)
    {
        var expanded = ForExpandedContent(currentHeight, pageHeight);
        return Math.Clamp(1 - (CompactInverseFactor * expanded), 0, 1);
    }
}

/// <summary>Velocity-driven squash &amp; stretch factors (spec 4.1, derived from spring velocity).</summary>
/// <param name="WidthScale">Multiplier for width.</param>
/// <param name="HeightScale">Multiplier for height.</param>
public readonly record struct VelocityDeformation(double WidthScale, double HeightScale);

/// <summary>Squash/stretch derivation; no extra springs are allocated.</summary>
public static class SquashStretchPolicy
{
    /// <summary>Width velocity gain (spec: 0.000125).</summary>
    public const double WidthGain = 0.000125;

    /// <summary>Height velocity gain (spec: 0.00008).</summary>
    public const double HeightGain = 0.00008;

    /// <summary>Maximum width growth (spec: 0.062).</summary>
    public const double MaxWidthGrowth = 0.062;

    /// <summary>Maximum width shrink (spec: 0.05).</summary>
    public const double MaxWidthShrink = 0.05;

    /// <summary>Maximum height growth (spec: 0.05).</summary>
    public const double MaxHeightGrowth = 0.05;

    /// <summary>Maximum height shrink (spec: 0.04).</summary>
    public const double MaxHeightShrink = 0.04;

    /// <summary>Computes the deformation for the current width velocity (DIP/s).</summary>
    public static VelocityDeformation FromWidthVelocity(double widthVelocity)
    {
        var width = Math.Clamp(widthVelocity * WidthGain, -MaxWidthShrink, MaxWidthGrowth);
        var height = Math.Clamp(-widthVelocity * HeightGain, -MaxHeightShrink, MaxHeightGrowth);
        return new VelocityDeformation(1 + width, 1 + height);
    }
}
