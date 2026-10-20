using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Approach reveal: moving the pointer into the hover band below the expanded shell shows the preview
/// rail immediately (no dwell, no wheel), leaving the band hides it after the usual 900 ms delay, and
/// boundary jitter neither reveals twice nor schedules repaints.
/// </summary>
public sealed class SkiaApproachRevealTests
{
    [Fact]
    public void EnteringTheBand_RevealsTheRailImmediately()
    {
        using var fixture = ExpandedFixture();
        Assert.False(fixture.Controller.Archipelago.IsVisible);

        var band = ArchipelagoHoverBand.For(fixture.Controller.RenderState);
        Assert.False(band.IsEmpty);

        fixture.Controller.HandlePointerMoved(band.CenterX, band.CenterY);
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));

        Assert.True(fixture.Controller.Archipelago.IsVisible);
        Assert.True(fixture.Controller.RenderState.Archipelago.Visible);
    }

    [Fact]
    public void LeavingTheBand_HidesAfterThePrototypeDelay()
    {
        using var fixture = ExpandedFixture();
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        var band = ArchipelagoHoverBand.For(fixture.Controller.RenderState);

        fixture.Controller.HandlePointerMoved(band.CenterX, band.CenterY);
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.True(fixture.Controller.Archipelago.IsVisible);

        // Back onto the panel: the leave edge arms the 900 ms hide delay.
        fixture.Controller.HandlePointerMoved(shell.MidX, shell.MidY + 60);
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        fixture.Time.Advance(TimeSpan.FromMilliseconds(SkiaArchipelago.HideDelayMs - 200));
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.True(fixture.Controller.Archipelago.IsVisible, "the rail must stay while the 900 ms delay runs");

        fixture.Time.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.False(fixture.Controller.Archipelago.IsVisible);
    }

    [Fact]
    public void BoundaryJitter_DoesNotRetriggerOrRepaint()
    {
        using var fixture = ExpandedFixture();
        var band = ArchipelagoHoverBand.For(fixture.Controller.RenderState);
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);

        // Reveal once and let the springs settle (draining the reveal damage: the host consumes it).
        fixture.Controller.HandlePointerMoved(band.CenterX, band.CenterY);
        fixture.RunToRest();
        Assert.True(fixture.Controller.Archipelago.IsVisible);
        var activityAt = fixture.Controller.Archipelago.LastActivityAt;
        while (fixture.Controller.TakeInvalidation(out _))
        {
        }

        var frames = 0;
        fixture.Controller.FrameRequested += () => frames++;

        // Jitter across the band's top edge: same y a hair inside/outside, one pixel apart.
        for (var i = 0; i < 40; i++)
        {
            var y = i % 2 == 0 ? band.Y + 0.5 : shell.Bottom - 0.5;
            fixture.Controller.HandlePointerMoved(band.CenterX + (i % 3), y);
        }

        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.True(fixture.Controller.Archipelago.IsVisible);
        Assert.True(fixture.Controller.Archipelago.LastActivityAt >= activityAt);
        Assert.Equal(0, frames);
        Assert.False(fixture.Controller.TakeInvalidation(out _), "no move may invalidate a frame while the rail stays up");
    }

    [Fact]
    public void MovesInsideTheBand_NeverInvalidateAFrame()
    {
        using var fixture = ExpandedFixture();
        var band = ArchipelagoHoverBand.For(fixture.Controller.RenderState);

        // Entry edge: one reveal, one damage box, one frame request.
        var frames = 0;
        fixture.Controller.FrameRequested += () => frames++;
        fixture.Controller.HandlePointerMoved(band.X + 20, band.Y + 10);
        Assert.True(fixture.Controller.Archipelago.IsVisible);
        Assert.Equal(1, frames);
        Assert.True(fixture.Controller.TakeInvalidation(out _), "the reveal itself must paint once");

        // Every further move inside the band is a rectangle test and nothing else: 200 moves, no
        // damage at all (the host would render zero extra frames because hovering costs nothing).
        for (var i = 0; i < 200; i++)
        {
            fixture.Controller.HandlePointerMoved(band.X + 20 + (i % 100), band.Y + 10 + (i % 4));
            Assert.False(fixture.Controller.TakeInvalidation(out _), $"move {i} must not schedule a repaint");
        }

        Assert.Equal(1, frames);
        Assert.True(fixture.Controller.Archipelago.IsVisible);
    }

    [Fact]
    public void CompactIsland_NeverShowsTheApproachBand()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 });
        fixture.Settle();

        var band = ArchipelagoHoverBand.For(fixture.Controller.RenderState);
        Assert.True(band.IsEmpty, "a collapsed island has no rail to approach");

        fixture.Controller.HandlePointerMoved(fixture.Controller.RenderState.ShellWidth / 2, 200);
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.False(fixture.Controller.Archipelago.IsVisible);
    }

    [Fact]
    public void BandIsHiddenWhenThePanelCollapses()
    {
        using var fixture = ExpandedFixture();
        var band = ArchipelagoHoverBand.For(fixture.Controller.RenderState);
        Assert.False(band.IsEmpty);

        // Collapsing must drop the band with the panel (the same state the hook watches).
        fixture.Controller.HandleEscape();
        fixture.RunToRest();
        Assert.True(ArchipelagoHoverBand.For(fixture.Controller.RenderState).IsEmpty);
    }

    private static ControllerFixture ExpandedFixture()
    {
        var fixture = ControllerFixture.Create(AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 });
        fixture.ExpandAndSettle();
        return fixture;
    }
}
