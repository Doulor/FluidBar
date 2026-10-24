using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Activities;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Frame-clock gating for the Skia host. The clock must run for as long as something animates and stop
/// only when the island is at rest; a render attempt that painted nothing (the 50 ms deadline ticker)
/// is not a reason to stop it.
/// </summary>
public sealed class IslandFrameLoopPolicyTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, false, false)]
    public void ShouldRun_FollowsTheAnimationFlags(bool animating, bool agentAnimating, bool keepIdleFrames, bool expected)
        => Assert.Equal(expected, IslandFrameLoopPolicy.ShouldRun(animating, agentAnimating, keepIdleFrames));

    /// <summary>
    /// Regression (2026-10-24): an idle island stopped the clock, so a dark → light desktop change never
    /// repainted the liquid glass until some activity happened. A backdrop luminance change must request
    /// frames on its own.
    /// </summary>
    [Fact]
    public void ShouldRun_WhenOnlyTheBackdropChanged()
    {
        Assert.True(IslandFrameLoopPolicy.ShouldRun(false, false, false, backdropChanged: true));
        Assert.False(IslandFrameLoopPolicy.ShouldRun(false, false, false, backdropChanged: false));
    }

    /// <summary>
    /// Regression (2026-10-21): the host used to stop the frame clock when the last render attempt was
    /// empty. The deadline ticker renders without advancing motion, so the expand animation froze after
    /// two frames. The animation state must survive a deadline pump and keep asking for frames.
    /// </summary>
    [Fact]
    public void Expand_KeepsAnimating_AcrossADeadlinePump()
    {
        using var fixture = ControllerFixture.Create();
        fixture.Settle();
        Assert.False(fixture.Controller.IsAnimating, "a rested island requests no frames");

        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        fixture.Controller.HandlePointerPressed(shell.MidX, shell.MidY);
        fixture.Controller.HandlePointerReleased(shell.MidX, shell.MidY);
        Assert.True(fixture.Controller.IsAnimating, "the press must start the expand animation");

        // The deadline path (50 ms ticker): pumps timers, paints, never advances motion.
        fixture.Controller.PumpTimers();
        Assert.True(fixture.Controller.IsAnimating, "a deadline pump must not end the animation");
        Assert.True(
            IslandFrameLoopPolicy.ShouldRun(fixture.Controller.IsAnimating, fixture.Controller.IsAgentAnimating, keepIdleFrames: false),
            "the host must keep the clock while the shell is still opening");

        // And the very next frame still has damage to paint.
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16.7));
        Assert.True(fixture.Controller.TakeInvalidation(out _), "an animating frame must produce damage");
    }

    /// <summary>Once the shell has settled the pump must report "nothing to animate" so the clock stops.</summary>
    [Fact]
    public void SettledShell_RequestsNoFrames()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.Controller.PumpTimers();

        Assert.False(fixture.Controller.IsAnimating);
        Assert.False(IslandFrameLoopPolicy.ShouldRun(
            fixture.Controller.IsAnimating, fixture.Controller.IsAgentAnimating, keepIdleFrames: false));
    }
}
