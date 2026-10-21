using FluidBar.App.Hosting.Skia;
using FluidBar.Core;
using FluidBar.Core.Interaction;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Prototype2 page-transition contract: the pager must follow the pointer/wheel immediately, queue
/// page steps instead of swallowing them, land on the page the finger left behind and never rebound
/// past the target. The expected numbers come from the prototype's own PAGER contract
/// (T 0.38 s / ζ 0.94, gain 0.85, notch 140 DIP, 180 ms window) measured frame by frame in
/// docs/rewrite/prototype2 via Playwright.
/// </summary>
public sealed class PageSwitchFeelTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(1000.0 / 60.0);

    private static (ControllerFixture Fixture, SkiaIslandController Controller) Create()
    {
        var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.Controller.HandlePageRequest(FluidBar.Core.Activities.PageRef.Home);
        fixture.RunToRest();
        return (fixture, fixture.Controller);
    }

    private static void Advance(ControllerFixture fixture, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            fixture.Time.Advance(Frame);
            fixture.Controller.Advance(Frame);
        }
    }

    private static double Position(SkiaIslandController controller) => controller.RenderState.Pager.PagePosition;

    [Fact]
    public void TwoWheelNotchesTurnExactlyOnePageAndSettleWithoutOvershoot()
    {
        var (fixture, controller) = Create();
        using var _fixture = fixture;
        var positions = new List<double> { Position(controller) };

        // One physical notch is one 140 DIP page step.
        Assert.True(controller.HandleWheel(PagerConstants.WheelDipPerNotch, horizontal: false, shift: false).Paged);
        for (var i = 0; i < 90; i++)
        {
            Advance(fixture, 1);
            positions.Add(Position(controller));
        }

        Assert.Equal(1, controller.Pager.SelectedIndex);
        Assert.Equal(1.0, positions[^1], 3);
        Assert.All(positions.Zip(positions.Skip(1)), pair => Assert.True(pair.Second >= pair.First - 1e-9,
            "the snap must never move backwards (no rebound)"));
        Assert.True(positions.Max() <= 1.0001, "the snap must never overshoot the target page");
    }

    [Fact]
    public void ContinuousWheelGestureQueuesPagesInsteadOfSwallowingThem()
    {
        var (fixture, controller) = Create();
        using var _fixture = fixture;

        // Six notches at ~15 Hz (a real wheel spin), 70 DIP each. The prototype commits a page at most
        // once per 180 ms window and keeps the residual; the previous build froze the whole chain for
        // the duration of the snap and turned six notches into a single page.
        for (var i = 0; i < 6; i++)
        {
            controller.HandleWheel(PagerConstants.WheelDipPerNotch, horizontal: false, shift: false);
            Advance(fixture, 4);
        }

        for (var i = 0; i < 90; i++)
        {
            Advance(fixture, 1);
        }

        Assert.True(controller.Pager.SelectedIndex >= 2,
            "a continuous six-notch gesture must queue at least two pages, got " + controller.Pager.SelectedIndex);
        Assert.Equal(controller.Pager.SelectedIndex, (int)Math.Round(Position(controller)));
    }

    [Fact]
    public void ContinuousWheelAtTenHertzTurnsThreePagesForSixNotches()
    {
        var (fixture, controller) = Create();
        using var _fixture = fixture;

        // Six notches at 10 Hz = 420 DIP of wheel = three page steps; the 180 ms cooldown still paces
        // them (prototype2 measured 2-3 pages for the same gesture).
        for (var i = 0; i < 6; i++)
        {
            controller.HandleWheel(PagerConstants.WheelDipPerNotch, horizontal: false, shift: false);
            Advance(fixture, 6);
        }

        for (var i = 0; i < 90; i++)
        {
            Advance(fixture, 1);
        }

        Assert.Equal(3, controller.Pager.SelectedIndex);
        Assert.Equal(3.0, Position(controller), 3);
    }

    [Fact]
    public void WheelDuringTheSnapQueuesFromTheTargetPage()
    {
        var (fixture, controller) = Create();
        using var _fixture = fixture;

        controller.HandleWheel(PagerConstants.WheelDipPerNotch, horizontal: false, shift: false);
        controller.HandleWheel(PagerConstants.WheelDipPerNotch, horizontal: false, shift: false);
        Advance(fixture, 4); // ~67 ms into the 0.38 s snap: the spring is still running
        Assert.True(controller.Pager.IsAnimating);

        fixture.Time.Advance(TimeSpan.FromMilliseconds(PagerConstants.WheelCooldownMs + 20));
        controller.HandleWheel(PagerConstants.WheelDipPerNotch, horizontal: false, shift: false);
        controller.HandleWheel(PagerConstants.WheelDipPerNotch, horizontal: false, shift: false);
        for (var i = 0; i < 120; i++)
        {
            Advance(fixture, 1);
        }

        Assert.Equal(2, controller.Pager.SelectedIndex);
        Assert.Equal(2.0, Position(controller), 3);
    }

    [Fact]
    public void SlowShortDragSnapsBackWithoutChangingPage()
    {
        var (fixture, controller) = Create();
        using var _fixture = fixture;
        var pager = controller.Pager;
        var x0 = 260.0;
        var t = 0L;

        pager.BeginDrag(x0, t);
        for (var i = 1; i <= 12; i++)
        {
            t += 33;
            pager.DragTo(x0 - (i * 10), t); // 120 DIP of travel over ~400 ms: a slow, deliberate drag
            Advance(fixture, 2);
        }

        pager.EndDrag(t);
        for (var i = 0; i < 90; i++)
        {
            Advance(fixture, 1);
        }

        Assert.Equal(0, pager.SelectedIndex);
        Assert.Equal(0.0, Position(controller), 3);
    }

    [Fact]
    public void SlowHalfPageDragCommitsTheNearestPage()
    {
        var (fixture, controller) = Create();
        using var _fixture = fixture;
        var pager = controller.Pager;
        var x0 = 260.0;
        var t = 0L;

        pager.BeginDrag(x0, t);
        for (var i = 1; i <= 20; i++)
        {
            t += 33;
            pager.DragTo(x0 - (i * 12), t); // 240 DIP raw -> 204 DIP damped = 0.51 page
            Advance(fixture, 2);
        }

        pager.EndDrag(t);
        for (var i = 0; i < 90; i++)
        {
            Advance(fixture, 1);
        }

        Assert.Equal(1, pager.SelectedIndex);
        Assert.Equal(1.0, Position(controller), 3);
    }

    [Fact]
    public void FastFlickTurnsExactlyOnePage()
    {
        var (fixture, controller) = Create();
        using var _fixture = fixture;
        var pager = controller.Pager;
        var x0 = 260.0;
        var t = 0L;

        pager.BeginDrag(x0, t);
        for (var i = 1; i <= 6; i++)
        {
            t += 16;
            pager.DragTo(x0 - (i * 12), t); // 0.75 DIP/ms: a flick
            Advance(fixture, 1);
        }

        pager.EndDrag(t);
        for (var i = 0; i < 90; i++)
        {
            Advance(fixture, 1);
        }

        Assert.Equal(1, pager.SelectedIndex);
        Assert.Equal(1.0, Position(controller), 3);
    }

    [Fact]
    public void DragThatReversesBelowHalfAPageSettlesBackWhereItStarted()
    {
        var (fixture, controller) = Create();
        using var _fixture = fixture;
        var pager = controller.Pager;
        var x0 = 260.0;
        var t = 0L;

        pager.BeginDrag(x0, t);
        for (var i = 1; i <= 13; i++)
        {
            t += 16;
            pager.DragTo(x0 - (i * 20), t); // out to 260 DIP (0.55 page damped)
            Advance(fixture, 1);
        }

        for (var i = 8; i >= 5; i--)
        {
            t += 50;
            pager.DragTo(x0 - (i * 20), t); // back to 100 DIP and pause: no release momentum
            Advance(fixture, 3);
        }

        Advance(fixture, 7);
        pager.EndDrag(t);
        for (var i = 0; i < 90; i++)
        {
            Advance(fixture, 1);
        }

        Assert.Equal(0, pager.SelectedIndex);
        Assert.Equal(0.0, Position(controller), 3);
    }
}

