using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Interaction;
using Xunit;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Wheel feel contract (2026-10-21): a light scroll (60 DIP notch) already turns one page, the snap
/// spring overshoots slightly (ζ 0.75 / T 0.35 s) and a gesture lock makes sure one physical flick can
/// never chain several pages: notches inside the same gesture (gap &lt; 120 ms) after a step are
/// dropped for the 260 ms transition window, while deliberate repeat scrolls (&gt; 150 ms apart) chain.
/// </summary>
public sealed class WheelGestureFeelTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(1000.0 / 60.0);

    [Fact]
    public void Contract_values_are_the_feel_pass_ones()
    {
        Assert.InRange(PagerConstants.WheelDipPerNotch, 55, 70);
        Assert.Equal(120, PagerConstants.WheelGestureIdleMs);
        Assert.Equal(260, PagerConstants.WheelTransitionLockMs);
        Assert.Equal(0.75, PagerConstants.SnapSpringDampingRatio, 0.001);
        Assert.InRange(PagerConstants.SnapSpringPeriodSeconds, 0.32, 0.38);
    }

    [Fact]
    public void One_light_notch_turns_exactly_one_page()
    {
        var policy = new PagerWheelPolicy(pageCount: 6);
        var now = DateTimeOffset.UnixEpoch;
        var result = policy.Accumulate(PagerConstants.WheelDipPerNotch, now);
        Assert.Equal(1, result.PageDelta);
    }

    [Fact]
    public void Eight_rapid_notches_turn_at_most_two_pages()
    {
        var policy = new PagerWheelPolicy(pageCount: 6);
        var now = DateTimeOffset.UnixEpoch;
        var pages = 0;
        var ignored = 0;
        for (var i = 0; i < 8; i++)
        {
            var result = policy.Accumulate(PagerConstants.WheelDipPerNotch, now);
            if (result.PageDelta != 0)
            {
                pages += Math.Abs(result.PageDelta);
            }
            else
            {
                ignored++;
            }

            now = now.AddMilliseconds(40);
        }

        Assert.InRange(pages, 1, 2);   // the contract cap: never more than two pages per flick
        Assert.True(ignored >= 6, "the gesture lock must drop the queued notches, dropped " + ignored);
    }

    [Fact]
    public void Three_light_scrolls_200ms_apart_turn_three_pages()
    {
        var policy = new PagerWheelPolicy(pageCount: 6);
        var now = DateTimeOffset.UnixEpoch;
        var pages = 0;
        for (var i = 0; i < 3; i++)
        {
            pages += Math.Abs(policy.Accumulate(PagerConstants.WheelDipPerNotch, now).PageDelta);
            now = now.AddMilliseconds(200);
        }

        Assert.Equal(3, pages);
    }

    [Fact]
    public void Two_deliberate_scrolls_160ms_apart_turn_two_pages()
    {
        var policy = new PagerWheelPolicy(pageCount: 6);
        var now = DateTimeOffset.UnixEpoch;
        var first = policy.Accumulate(PagerConstants.WheelDipPerNotch, now).PageDelta;
        now = now.AddMilliseconds(160);
        var second = policy.Accumulate(PagerConstants.WheelDipPerNotch, now).PageDelta;
        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

}
