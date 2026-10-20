using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Interaction;
using FluidBar.Island;
using FluidBar.Platform.Windows.Accessibility;

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

    [Fact]
    public void HoverProjectsThePrototypeHoverSlot()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);
        fixture.Controller.Archipelago.Reveal(fixture.Time.GetLocalNow());
        fixture.Settle();

        var strip = SkiaIslandHitTest.MeasureStrip(fixture.Controller.RenderState);
        AdvanceFrame(fixture);
        fixture.Controller.HandlePointerMoved(strip.Next.MidX, strip.Next.MidY);
        AdvanceFrame(fixture);
        Assert.Equal(MiniRailSlot.Next, fixture.Controller.RenderState.Archipelago.RailHover);

        fixture.Controller.HandlePointerMoved(strip.Previous.MidX, strip.Previous.MidY);
        AdvanceFrame(fixture);
        Assert.Equal(MiniRailSlot.Previous, fixture.Controller.RenderState.Archipelago.RailHover);

        // The current capsule is a span in the prototype: hovering it never lightens it.
        fixture.Controller.HandlePointerMoved(strip.Current.MidX, strip.Current.MidY);
        AdvanceFrame(fixture);
        Assert.Equal(MiniRailSlot.None, fixture.Controller.RenderState.Archipelago.RailHover);

        // Leaving the strip clears the hover state.
        fixture.Controller.HandlePointerMoved(strip.Strip.MidX, strip.Strip.Top - 40);
        AdvanceFrame(fixture);
        Assert.Equal(MiniRailSlot.None, fixture.Controller.RenderState.Archipelago.RailHover);
    }

    [Fact]
    public void PressingAGlass_PaintsActiveAndNavigatesOnRelease()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);
        fixture.Controller.Archipelago.Reveal(fixture.Time.GetLocalNow());
        fixture.Settle();

        var strip = SkiaIslandHitTest.MeasureStrip(fixture.Controller.RenderState);
        fixture.Controller.HandlePointerPressed(strip.Next.MidX, strip.Next.MidY);
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));

        // Prototype buttons navigate on click (release); the press only paints ':active'.
        Assert.Equal(2, fixture.Controller.Pager.SelectedIndex);
        Assert.Equal(MiniRailSlot.Next, fixture.Controller.RenderState.Archipelago.RailPressed);

        fixture.Controller.HandlePointerReleased(strip.Next.MidX, strip.Next.MidY);
        fixture.RunToRest();
        Assert.Equal(MiniRailSlot.None, fixture.Controller.RenderState.Archipelago.RailPressed);
        Assert.Equal(3, fixture.Controller.Pager.SelectedIndex);
    }

    [Fact]
    public void DraggingOffTheGlass_CancelsTheClick()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);
        fixture.Controller.Archipelago.Reveal(fixture.Time.GetLocalNow());
        fixture.Settle();

        var strip = SkiaIslandHitTest.MeasureStrip(fixture.Controller.RenderState);
        fixture.Controller.HandlePointerPressed(strip.Next.MidX, strip.Next.MidY);
        fixture.Controller.HandlePointerReleased(strip.Next.MidX + 40, strip.Next.MidY);
        fixture.RunToRest();

        Assert.Equal(2, fixture.Controller.Pager.SelectedIndex);
        Assert.Equal(MiniRailSlot.None, fixture.Controller.RenderState.Archipelago.RailPressed);
    }

    [Fact]
    public void LeavingTheWindow_ClearsTheRailInteraction()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);
        fixture.Controller.Archipelago.Reveal(fixture.Time.GetLocalNow());
        fixture.Settle();

        var strip = SkiaIslandHitTest.MeasureStrip(fixture.Controller.RenderState);
        fixture.Controller.HandlePointerMoved(strip.Next.MidX, strip.Next.MidY);
        fixture.Controller.HandlePointerPressed(strip.Next.MidX, strip.Next.MidY);
        fixture.Controller.HandlePointerLeft();
        fixture.Controller.HandlePointerReleased(strip.Next.MidX, strip.Next.MidY);
        AdvanceFrame(fixture);

        Assert.Equal(MiniRailSlot.None, fixture.Controller.RenderState.Archipelago.RailHover);
        Assert.Equal(MiniRailSlot.None, fixture.Controller.RenderState.Archipelago.RailPressed);
        Assert.Equal(2, fixture.Controller.Pager.SelectedIndex);
    }

    [Fact]
    public void KeyboardFocusOnAGlass_PaintsTheAccentOutlineInsteadOfTheGenericRing()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        using var source = new SkiaIslandUiaSource(fixture.Controller, new FakeLocalizer(), new SkiaHostDispatcher());
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);
        fixture.Controller.Archipelago.Reveal(fixture.Time.GetLocalNow());
        fixture.Settle();
        source.NotifyWindowFocus(true);

        // Tab order already exposes the rail glasses as focusable buttons.
        var strip = source.BuildSnapshot().Find(SkiaIslandUiaElementIds.Strip);
        Assert.NotNull(strip);
        Assert.Contains(strip!.Children, child => child.Id == "island.strip.next.3");

        source.Perform(new UiaElementActionRequest(UiaElementAction.SetFocus, "island.strip.next.3"));
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(MiniRailSlot.Next, fixture.Controller.RenderState.Archipelago.RailFocus);
        // The strip paints the prototype 'focus-visible' outline itself, so the generic ring stays off.
        Assert.Null(fixture.Controller.RenderState.FocusRing);

        // Moving focus to a chrome control hands the ring back to the generic renderer.
        source.Perform(new UiaElementActionRequest(UiaElementAction.SetFocus, SkiaIslandUiaElementIds.Collapse));
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(MiniRailSlot.None, fixture.Controller.RenderState.Archipelago.RailFocus);
        Assert.NotNull(fixture.Controller.RenderState.FocusRing);

        source.NotifyWindowFocus(false);
        source.BuildSnapshot();
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(MiniRailSlot.None, fixture.Controller.RenderState.Archipelago.RailFocus);
    }

    /// <summary>Rebuilds the render state for the current interaction fields.</summary>
    private static void AdvanceFrame(ControllerFixture fixture) => fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));

    private static void Press(ControllerFixture fixture, float x, float y)
    {
        fixture.Controller.HandlePointerPressed(x, y);
        fixture.Controller.HandlePointerReleased(x, y);
    }
}