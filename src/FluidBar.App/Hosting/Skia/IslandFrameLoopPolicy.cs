namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Decides whether the island host must keep its frame clock alive.
/// </summary>
/// <remarks>
/// <para>
/// The clock exists to animate, so the animation flags are the only input: shell/peek/pager springs,
/// the split-agent ring, the media wave/progress and the karaoke sweep all report through
/// <see cref="SkiaIslandController.IsAnimating"/> / <see cref="SkiaIslandController.IsAgentAnimating"/>.
/// <see cref="KeepIdleFrames"/> is the power table's exception for a painted satellite ring.
/// </para>
/// <para>
/// Regression guard (2026-10-21): the clock used to stop whenever the previous render attempt painted
/// nothing. The 50 ms deadline ticker calls the same render path without advancing motion, so every
/// deadline tick cleared that flag and killed the animation clock about 50 ms after it started — shell,
/// hover and wave animations ran two or three frames and then froze. A no-op render attempt is not a
/// reason to stop an animation, so the render flag is no longer an input here.
/// </para>
/// </remarks>
public static class IslandFrameLoopPolicy
{
    /// <summary>Whether the host should keep requesting frames.</summary>
    /// <param name="animating">Any shell/pager/strip/content animation is in flight.</param>
    /// <param name="agentAnimating">The agent spinner needs continuous frames.</param>
    /// <param name="keepIdleFrames">The power table keeps frames alive (painted satellite ring).</param>
    /// <param name="backdropChanged">
    /// A desktop capture changed the backdrop luminance while the island was idle: liquid glass has to
    /// repaint so the material follows the desktop (regression 2026-10-24: an idle island only recoloured
    /// after the next activity).
    /// </param>
    /// <returns>True when the frame clock must run.</returns>
    public static bool ShouldRun(bool animating, bool agentAnimating, bool keepIdleFrames,
        bool backdropChanged = false)
        => animating || agentAnimating || keepIdleFrames || backdropChanged;
}
