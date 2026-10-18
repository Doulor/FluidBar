using FluidBar.Core.Activities;
using FluidBar.Core.Tests.TestSupport;

namespace FluidBar.Core.Tests;

public class IslandPoliciesTests
{
    private static readonly IslandBehaviorOptions Defaults = IslandBehaviorOptions.Default;

    private static IslandState Compact() => IslandState.Hidden with
    {
        Kind = IslandStateKind.Compact,
        ActivityCount = 1,
        IsHovering = true,
    };

    [Fact]
    public void Peek_CanEnterGuards()
    {
        Assert.True(PeekPolicy.CanEnterPeek(Compact(), Defaults));
        Assert.False(PeekPolicy.CanEnterPeek(Compact() with { IsPinned = true }, Defaults));
        Assert.False(PeekPolicy.CanEnterPeek(Compact() with { IsDragging = true }, Defaults));
        Assert.False(PeekPolicy.CanEnterPeek(Compact() with { IsHovering = false }, Defaults));
        Assert.False(PeekPolicy.CanEnterPeek(Compact(), Defaults with { HoverPeek = false }));
        Assert.False(PeekPolicy.CanEnterPeek(Compact() with { Kind = IslandStateKind.Hidden }, Defaults));
    }

    [Fact]
    public void Peek_HoverIntentDeadlineAndElapsed()
    {
        var now = TestData.Now;
        var deadline = PeekPolicy.HoverIntentDeadline(now, Defaults);
        Assert.Equal(now.AddMilliseconds(90), deadline);

        var armed = Compact() with { HoverIntentDeadline = deadline };
        Assert.False(PeekPolicy.HoverIntentElapsed(armed, Defaults, now.AddMilliseconds(89)));
        Assert.True(PeekPolicy.HoverIntentElapsed(armed, Defaults, now.AddMilliseconds(90)));
        Assert.False(PeekPolicy.HoverIntentElapsed(armed with { IsPinned = true }, Defaults, now.AddMilliseconds(200)));
    }

    [Fact]
    public void Peek_LeaveDebounceAndDwell()
    {
        var now = TestData.Now;
        Assert.Equal(now.AddMilliseconds(120), PeekPolicy.LeaveDeadline(now, Defaults));

        var peek = IslandState.Hidden with
        {
            Kind = IslandStateKind.Peek,
            IsHovering = false,
            LeaveDeadline = now.AddMilliseconds(120),
        };
        Assert.False(PeekPolicy.ShouldExitPeek(peek, now.AddMilliseconds(119)));
        Assert.True(PeekPolicy.ShouldExitPeek(peek, now.AddMilliseconds(120)));
        Assert.False(PeekPolicy.ShouldExitPeek(peek with { IsHovering = true }, now.AddMilliseconds(500)));
        Assert.False(PeekPolicy.ShouldAutoRetractPeek(peek, now));

        var dwell = peek with { PeekDwellDeadline = now };
        Assert.True(PeekPolicy.ShouldAutoRetractPeek(dwell, now.AddSeconds(1)));
        Assert.False(PeekPolicy.ShouldAutoRetractPeek(dwell with { IsHovering = true }, now.AddSeconds(1)));
    }

    [Fact]
    public void Collapse_AutoCollapseGuards()
    {
        var now = TestData.Now;
        var expanded = IslandState.Hidden with
        {
            Kind = IslandStateKind.Expanded,
            AutoCollapseDeadline = now,
        };

        Assert.False(CollapsePolicy.ShouldAutoCollapse(expanded, now.AddMilliseconds(-1)));
        Assert.True(CollapsePolicy.ShouldAutoCollapse(expanded, now));
        Assert.False(CollapsePolicy.ShouldAutoCollapse(expanded with { IsPinned = true }, now));
        Assert.False(CollapsePolicy.ShouldAutoCollapse(expanded with { Kind = IslandStateKind.Compact }, now));
        Assert.True(CollapsePolicy.IsAutoCollapseBlockedByPin(expanded with { IsPinned = true }));
    }

    [Fact]
    public void Collapse_AutoHideGuards()
    {
        var compact = IslandState.Hidden with
        {
            Kind = IslandStateKind.Compact,
            AutoHideDeadline = TestData.Now,
        };
        var options = Defaults with { AutoHideMs = 100 };

        Assert.True(CollapsePolicy.ShouldAutoHide(compact, options));
        Assert.False(CollapsePolicy.ShouldAutoHide(compact, options with { AutoHideMs = 0 }));
        Assert.False(CollapsePolicy.ShouldAutoHide(compact with { IsHovering = true }, options));
        Assert.False(CollapsePolicy.ShouldAutoHide(compact with { IsPinned = true }, options));
        Assert.False(CollapsePolicy.ShouldAutoHide(compact with { AutoHideDeadline = null }, options));
    }

    [Fact]
    public void Collapse_RevealAndOutsideClickGuards()
    {
        var hidden = IslandState.Hidden;
        Assert.True(CollapsePolicy.ShouldRevealOnActivity(hidden, Defaults, ActivityKind.Notification));
        Assert.False(CollapsePolicy.ShouldRevealOnActivity(hidden with { IsSuppressed = true }, Defaults, ActivityKind.Notification));
        Assert.False(CollapsePolicy.ShouldRevealOnActivity(hidden, Defaults with { Enabled = false }, ActivityKind.Notification));

        Assert.False(CollapsePolicy.BlocksOutsideClickClose(hidden));
        Assert.True(CollapsePolicy.BlocksOutsideClickClose(hidden with { IsPinned = true }));
    }
}
