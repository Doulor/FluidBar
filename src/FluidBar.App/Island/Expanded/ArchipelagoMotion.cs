using FluidBar.Core.Motion;

namespace FluidBar.App.Island.Expanded;

/// <summary>
/// Spring-driven motion for the detached Page Archipelago (user feedback 2026-10-18: the strip motion
/// must feel smooth and must use Core springs, never CSS-like transitions or per-frame layout).
/// Two core springs: reveal/hide (translateY 8→0, opacity 0→1, scale .96→1; T .32 s, ζ .85) and the
/// active-island follow (1:1 with the pager offset, only slight smoothing; T .32 s, ζ .85).
/// </summary>
public sealed class ArchipelagoMotion
{
    /// <summary>Reveal spring period (s).</summary>
    public const double RevealPeriodSeconds = 0.32;

    /// <summary>Reveal spring damping ratio.</summary>
    public const double RevealDampingRatio = 0.85;

    /// <summary>Active-island follow period (s); keeps the strip within the ≤30 ms lag budget.</summary>
    public const double FollowPeriodSeconds = 0.32;

    /// <summary>Active-island follow damping ratio.</summary>
    public const double FollowDampingRatio = 0.85;

    /// <summary>Hidden vertical offset (DIP) animated to 0 on reveal.</summary>
    public const double HiddenOffsetY = 8;

    /// <summary>Hidden scale animated to 1.0 on reveal.</summary>
    public const double HiddenScale = 0.96;

    private readonly Spring _reveal;
    private readonly Spring _follow;

    /// <summary>Creates the motion with the strip hidden at page 0.</summary>
    public ArchipelagoMotion()
    {
        _reveal = new Spring(new SpringParameters(RevealPeriodSeconds, RevealDampingRatio), 0);
        _follow = new Spring(new SpringParameters(FollowPeriodSeconds, FollowDampingRatio), 0);
    }

    /// <summary>Reveal progress 0..1 (drives opacity/translate/scale).</summary>
    public double Progress => Math.Clamp(_reveal.Value, 0, 1);

    /// <summary>Smoothed active-island position in pages (fractional).</summary>
    public double ActivePosition => _follow.Value;

    /// <summary>Whether both springs are at rest (frame loop may stop).</summary>
    public bool IsAtRest => _reveal.IsAtRest && _follow.IsAtRest;

    /// <summary>Reveal translate offset (8→0 DIP).</summary>
    public double TranslateY => HiddenOffsetY * (1 - Progress);

    /// <summary>Reveal opacity (0→1).</summary>
    public double Opacity => Progress;

    /// <summary>Reveal scale (.96→1).</summary>
    public double Scale => HiddenScale + ((1 - HiddenScale) * Progress);

    /// <summary>Animates the strip in.</summary>
    public void Reveal() => _reveal.Retarget(1, 1.0 / 120.0);

    /// <summary>Animates the strip out.</summary>
    public void Hide() => _reveal.Retarget(0, 1.0 / 120.0);

    /// <summary>Sets the active-island follow target to the current pager page position.</summary>
    public void Follow(double pagePosition, int pageCount)
        => _follow.Retarget(Math.Clamp(pagePosition, 0, Math.Max(0, pageCount - 1)), 1.0 / 120.0);

    /// <summary>Steps both springs.</summary>
    public void Step(double dtSeconds)
    {
        _reveal.Step(dtSeconds);
        _follow.Step(dtSeconds);
    }

    /// <summary>Snaps the springs (restore / hidden / reduce-motion handoff).</summary>
    public void Snap(double pagePosition, bool visible)
    {
        _reveal.SnapTo(visible ? 1 : 0);
        _follow.SnapTo(pagePosition);
    }
}
