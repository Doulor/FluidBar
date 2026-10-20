using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Interaction;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Rail navigation semantics: the two neighbour pills turn to the adjacent page (left = previous,
/// right = next), while the far-page dots jump to the farthest page in their direction (left = 01,
/// right = 06). Both are prototype2 behaviours; the pills were made real buttons there in this task.
/// </summary>
public sealed class SkiaRailNavigationTests
{
    [Fact]
    public void NeighbourPills_TurnToTheAdjacentPage()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);
        fixture.Controller.Archipelago.Reveal(fixture.Time.GetLocalNow());
        fixture.Settle();

        var strip = SkiaIslandHitTest.MeasureStrip(fixture.Controller.RenderState);
        Assert.Equal(2, strip.Page);

        Press(fixture, strip.Next.MidX, strip.Next.MidY);
        fixture.RunToRest();
        Assert.Equal(3, fixture.Controller.Pager.SelectedIndex);

        strip = SkiaIslandHitTest.MeasureStrip(fixture.Controller.RenderState);
        Press(fixture, strip.Previous.MidX, strip.Previous.MidY);
        fixture.RunToRest();
        Assert.Equal(2, fixture.Controller.Pager.SelectedIndex);
    }

    [Fact]
    public void FarDots_JumpToTheFarthestPage()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.Controller.Archipelago.Reveal(fixture.Time.GetLocalNow());
        fixture.Settle();

        // Page 01: the right cluster owns the far pages, so any right dot jumps to 06.
        var strip = SkiaIslandHitTest.MeasureStrip(fixture.Controller.RenderState);
        Assert.Equal(0, strip.Page);
        Assert.NotEmpty(strip.DotsRight);
        Press(fixture, strip.DotsRight[^1].MidX, strip.DotsRight[^1].MidY);
        fixture.RunToRest();
        Assert.Equal(5, fixture.Controller.Pager.SelectedIndex);

        // Page 06: the left cluster jumps back to 01.
        strip = SkiaIslandHitTest.MeasureStrip(fixture.Controller.RenderState);
        Assert.Equal(5, strip.Page);
        Assert.NotEmpty(strip.DotsLeft);
        Press(fixture, strip.DotsLeft[^1].MidX, strip.DotsLeft[^1].MidY);
        fixture.RunToRest();
        Assert.Equal(0, fixture.Controller.Pager.SelectedIndex);
    }

    [Fact]
    public void PillClick_RetargetsTheRailSprings_TheSameFrame()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);
        fixture.Controller.Archipelago.Reveal(fixture.Time.GetLocalNow());
        fixture.Settle();

        var before = fixture.Controller.RenderState.Archipelago.Rail;
        Assert.True(before.Active);
        Assert.Equal(2, before.Page);

        var strip = SkiaIslandHitTest.MeasureStrip(fixture.Controller.RenderState);
        Press(fixture, strip.Next.MidX, strip.Next.MidY);

        // One frame later the rail page is the destination and the capsule is mid-spring.
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        var after = fixture.Controller.RenderState.Archipelago.Rail;
        Assert.Equal(3, after.Page);
        Assert.InRange(after.CapsuleWidth, 64f, 104f);

        // ...and it settles on the destination page's label width (the prototype capsule contract).
        fixture.RunToRest();
        var state = fixture.Controller.RenderState;
        var expected = MiniRailTargetsFactory.Create(
            3, state.Pages, fixture.Settings.Current.FontScale).CapsuleWidth;
        Assert.Equal(expected, state.Archipelago.Rail.CapsuleWidth, 0.5f);
        Assert.Equal(expected, SkiaIslandHitTest.MeasureStrip(state).Current.Width, 0.5f);
    }

    private static void Press(ControllerFixture fixture, float x, float y)
    {
        fixture.Controller.HandlePointerPressed(x, y);
        fixture.Controller.HandlePointerReleased(x, y);
    }
}
