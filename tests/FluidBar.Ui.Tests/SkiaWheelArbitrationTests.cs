using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Interaction;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Wheel arbitration on the expanded panel: the controller feeds the wheel into Core's
/// <see cref="PagerScrollChainPolicy"/> with the real page metrics, so scrollable lists own the wheel
/// until their edge, the edge needs overscroll (>= 40 DIP) plus dwell (>= 120 ms) before exactly one
/// page step, a direction reversal cancels the pending step, short pages page immediately and a page
/// step freezes the chain for the 180 ms cooldown. The pure policy is covered by
/// <c>FluidBar.Core.Tests.PagerScrollChainPolicyTests</c>; these tests lock the wiring.
/// </summary>
public sealed class SkiaWheelArbitrationTests
{
    /// <summary>Clipboard history page index (04 剪贴板历史).</summary>
    private const int ClipboardPage = 3;

    /// <summary>
    /// One page-step worth of wheel (140 DIP = prototype2's wheelStep). The host delivers this as two
    /// physical 70 DIP notches; these tests feed it as one event so each call is one arbitration unit.
    /// </summary>
    private const double Notch = PagerConstants.WheelNotchDip;

    [Fact]
    public void ScrollableClipboard_ScrollsTheListWithoutTurningThePage()
    {
        using var fixture = CreateClipboardFixture(items: 10);
        Assert.Equal(ClipboardPage, Page(fixture));
        Assert.True(ClipboardMaxScroll(fixture) > 0.5, "ten clipboard rows must overflow the 232 DIP body");

        var before = Offset(fixture);
        var outcome = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);

        Assert.True(outcome.Consumed);
        Assert.True(outcome.ContentScroll);
        Assert.False(outcome.Paged);
        Assert.Equal(ClipboardPage, Page(fixture));
        Assert.True(Offset(fixture) > before, "the scrollable page must consume the wheel as content scroll");
        Assert.Equal(Math.Min(ClipboardMaxScroll(fixture), before + Notch), Offset(fixture), 3);

        // No page turn while the list still has room, however many notches arrive.
        for (var i = 0; i < 2; i++)
        {
            var notch = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);
            Assert.False(notch.Paged);
        }

        Assert.Equal(ClipboardPage, Page(fixture));
    }

    [Fact]
    public void ClipboardAtTheBottom_NeedsDwellBeforeTurningThePage()
    {
        using var fixture = CreateClipboardFixture(items: 10);
        ScrollToBottom(fixture);
        Assert.Equal(ClipboardPage, Page(fixture));
        var bottom = Offset(fixture);
        Assert.True(bottom > 0.5);

        // First notch at the edge only accumulates: the 120 ms dwell has not elapsed yet, so the event
        // moves neither the list nor the page.
        var first = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);
        Assert.False(first.Paged);
        Assert.Equal(bottom, Offset(fixture), 3);
        Assert.Equal(ClipboardPage, Page(fixture));

        // After the dwell, the next notch at the same edge commits exactly one page step.
        fixture.Time.Advance(TimeSpan.FromMilliseconds(PagerConstants.ScrollChainDwellMs + 20));
        var second = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);
        Assert.True(second.Paged);
        Assert.Equal(ClipboardPage + 1, Page(fixture));
    }

    [Fact]
    public void ReversingAtTheBoundary_CancelsThePendingPageStep()
    {
        using var fixture = CreateClipboardFixture(items: 10);
        ScrollToBottom(fixture);
        var bottom = Offset(fixture);

        // Arm the pending page step at the bottom edge, then scroll back up before it fires.
        var armed = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);
        Assert.False(armed.Paged);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(PagerConstants.ScrollChainDwellMs + 20));
        var reversed = fixture.Controller.HandleWheel(-Notch, horizontal: false, shift: false);

        Assert.False(reversed.Paged);
        Assert.Equal(ClipboardPage, Page(fixture));
        Assert.Equal(bottom, Offset(fixture), 3);

        // The reversal also cleared the accumulation: a fresh down notch does not page on its own.
        var fresh = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);
        Assert.False(fresh.Paged);
        Assert.Equal(ClipboardPage, Page(fixture));

        // ...but with a full dwell it does, which proves the step was still reachable after the cancel.
        fixture.Time.Advance(TimeSpan.FromMilliseconds(PagerConstants.ScrollChainDwellMs + 20));
        var paged = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);
        Assert.True(paged.Paged);
        Assert.Equal(ClipboardPage + 1, Page(fixture));
    }

    [Fact]
    public void ShiftOrHorizontalWheel_BypassesScrollableContentAndPages()
    {
        using var fixture = CreateClipboardFixture(items: 10);
        var page = Page(fixture);

        // Shift forces navigation even though the clipboard list could scroll.
        var shift = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: true);
        Assert.True(shift.Paged);
        Assert.Equal(page + 1, Page(fixture));
        Assert.Equal(0, Offset(fixture), 3);

        fixture.Time.Advance(TimeSpan.FromMilliseconds(PagerConstants.WheelCooldownMs + 30));
        fixture.RunToRest();

        // The horizontal wheel axis pages too.
        var horizontal = fixture.Controller.HandleWheel(Notch, horizontal: true, shift: false);
        Assert.True(horizontal.Paged);
        Assert.Equal(page + 2, Page(fixture));
    }

    [Fact]
    public void ShortPage_TurnsOnOneWheel()
    {
        using var fixture = CreateClipboardFixture(items: 1);
        Assert.Equal(ClipboardPage, Page(fixture));
        Assert.Equal(0, ClipboardMaxScroll(fixture), 3);

        var outcome = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);

        Assert.True(outcome.Paged);
        Assert.False(outcome.ContentScroll);
        Assert.Equal(ClipboardPage + 1, Page(fixture));
        Assert.Equal(0, Offset(fixture), 3);
    }

    [Fact]
    public void WheelWithinTheCooldownAfterAPageStep_IsFrozen()
    {
        using var fixture = CreateClipboardFixture(items: 1);
        var page = Page(fixture);

        var first = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);
        Assert.True(first.Paged);
        Assert.Equal(page + 1, Page(fixture));

        // Same notch immediately after the step: the 180 ms cooldown must swallow it.
        var blocked = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);
        Assert.False(blocked.Paged);
        Assert.Equal(page + 1, Page(fixture));

        // After the cooldown (and once the snap settled) the wheel turns pages again.
        fixture.Time.Advance(TimeSpan.FromMilliseconds(PagerConstants.WheelCooldownMs + 30));
        fixture.RunToRest();
        var third = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);
        Assert.True(third.Paged);
        Assert.Equal(page + 2, Page(fixture));
    }

    [Fact]
    public void TwoPhysicalNotches_TurnOnePage_AndOneNotchAloneDoesNot()
    {
        using var fixture = CreateClipboardFixture(items: 1);
        var page = Page(fixture);

        var half = fixture.Controller.HandleWheel(
            PagerConstants.WheelDipPerNotch, horizontal: false, shift: false);
        Assert.False(half.Paged);
        Assert.Equal(page, Page(fixture));

        var full = fixture.Controller.HandleWheel(
            PagerConstants.WheelDipPerNotch, horizontal: false, shift: false);
        Assert.True(full.Paged);
        Assert.Equal(page + 1, Page(fixture));
    }

    /// <summary>
    /// Expands the island on the clipboard page with a fixed history size. Agent rows are cleared so
    /// the following page (04 → 05) is not scrollable and a page step is observable.
    /// </summary>
    private static ControllerFixture CreateClipboardFixture(int items)
    {
        var fixture = ControllerFixture.Create(AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 });
        fixture.Data.Update(content => content with
        {
            Clipboard =
            [
                .. Enumerable.Range(1, items).Select(index => new ClipboardItemData
                {
                    Preview = $"clipboard row {index}",
                    Meta = "1 min",
                }),
            ],
        });
        fixture.ExpandAndSettle();
        fixture.GoToPage(ClipboardPage);
        return fixture;
    }

    /// <summary>Scrolls the clipboard list to its bottom edge with whole notches.</summary>
    private static void ScrollToBottom(ControllerFixture fixture)
    {
        var max = ClipboardMaxScroll(fixture);
        var notches = (int)Math.Ceiling(max / Notch);
        Assert.True(notches > 0, "the fixture page must be scrollable");
        for (var i = 0; i < notches; i++)
        {
            var outcome = fixture.Controller.HandleWheel(Notch, horizontal: false, shift: false);
            Assert.False(outcome.Paged, "scrolling to the edge must never turn the page");
        }

        Assert.Equal(max, Offset(fixture), 3);
    }

    /// <summary>Scroll extent the controller derives for the clipboard page (same formula, same metrics).</summary>
    private static double ClipboardMaxScroll(ControllerFixture fixture)
    {
        var state = fixture.Controller.RenderState;
        var count = state.Pages.Clipboard.Items?.Count ?? 0;
        var metrics = FontScaleMetrics.From(fixture.Settings.Current.FontScale);
        var row = PageMetricsLayout.ClipboardRowHeight(metrics);
        return Math.Max(0, (count * (row + 6)) - 6 - PageMetricsLayout.BodyHeight);
    }

    private static double Offset(ControllerFixture fixture)
        => fixture.Controller.RenderState.Pages.Clipboard.ScrollOffset;

    private static int Page(ControllerFixture fixture)
        => fixture.Controller.Pager.SelectedIndex;
}
