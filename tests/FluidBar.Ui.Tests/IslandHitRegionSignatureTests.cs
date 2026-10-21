using FluidBar.App.Hosting.Skia;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The island's clickable region is rebuilt only when its signature changes. The signature must carry
/// the painted shell size: the expand transition grows the shell over 380 ms and a stale region leaves
/// the panel body click-through (2026-10-21: media card transport buttons were dead after expanding).
/// </summary>
public sealed class IslandHitRegionSignatureTests
{
    [Fact]
    public void Expanding_ChangesTheSignature()
    {
        using var fixture = ControllerFixture.Create();
        fixture.Settle();
        var compact = IslandHitRegionSignature.From(fixture.Controller.RenderState, fixture.Controller.IsAnimating);

        fixture.ExpandAndSettle();
        var expanded = IslandHitRegionSignature.From(fixture.Controller.RenderState, fixture.Controller.IsAnimating);

        Assert.NotEqual(compact, expanded);
        Assert.True(expanded.ShellWidthTenths > compact.ShellWidthTenths, "the panel is wider than the pill");
        Assert.Equal(2840, expanded.ShellHeightTenths);
    }

    [Fact]
    public void MidTransition_ChangesEveryFrame()
    {
        using var fixture = ControllerFixture.Create();
        fixture.Settle();
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        fixture.Controller.HandlePointerPressed(shell.MidX, shell.MidY);
        fixture.Controller.HandlePointerReleased(shell.MidX, shell.MidY);

        var first = IslandHitRegionSignature.From(fixture.Controller.RenderState, animating: true);
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16.7));
        var second = IslandHitRegionSignature.From(fixture.Controller.RenderState, animating: true);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SameState_ProducesTheSameSignature()
    {
        using var fixture = ControllerFixture.Create();
        fixture.Settle();
        var first = IslandHitRegionSignature.From(fixture.Controller.RenderState, animating: false);
        var second = IslandHitRegionSignature.From(fixture.Controller.RenderState, animating: false);

        Assert.Equal(first, second);
    }
}
