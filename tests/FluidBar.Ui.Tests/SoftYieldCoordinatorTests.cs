using FluidBar.App.Diagnostics;
using FluidBar.App.Hosting.Skia;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Manual soften coverage: right-click toggles the island, the fade animates, and the 3 s safety timer
/// always brings the island back.
/// </summary>
public sealed class SoftYieldCoordinatorTests
{
    private static SoftYieldCoordinator Create(List<bool> applied, Func<long> clock, long restoreAfterMs = 3000)
        => new(restoreAfterMs, applied.Add, new TraceAppLogger(), clock);

    [Fact]
    public void Right_click_softens_and_restores_manually()
    {
        var applied = new List<bool>();
        long now = 1000;
        using var coordinator = Create(applied, () => now);

        coordinator.Toggle();
        Assert.True(applied[^1]);
        Assert.True(coordinator.IsSoftened);

        now += 200;
        coordinator.Toggle();
        Assert.False(applied[^1]);
        Assert.False(coordinator.IsSoftened);
    }

    [Fact]
    public void Soften_restores_itself_after_the_timeout()
    {
        var applied = new List<bool>();
        long now = 1000;
        using var coordinator = Create(applied, () => now);

        coordinator.Toggle();
        Assert.True(applied[^1]);

        Assert.True(coordinator.Step(now + 500));
        Assert.True(coordinator.IsSoftened);

        // The safety timer fires exactly once the 3 s window closes.
        coordinator.Step(now + 3100);
        Assert.False(coordinator.IsSoftened);
        Assert.False(applied[^1]);
    }

    [Fact]
    public void Soften_never_expires_when_the_timer_is_disabled()
    {
        var applied = new List<bool>();
        long now = 1000;
        using var coordinator = Create(applied, () => now, restoreAfterMs: 0);

        coordinator.Toggle();
        coordinator.Step(now + 60_000);
        Assert.True(coordinator.IsSoftened);
    }

    [Fact]
    public void Progress_fades_in_and_out()
    {
        var applied = new List<bool>();
        long now = 1000;
        using var coordinator = Create(applied, () => now);

        coordinator.Toggle();
        Assert.Equal(0d, coordinator.Progress, 3);

        coordinator.Step(now);
        coordinator.Step(now + 120);
        Assert.True(coordinator.Progress > 0.2 && coordinator.Progress < 1);
        coordinator.Step(now + 400);
        Assert.Equal(1d, coordinator.Progress, 3);

        coordinator.Toggle();
        coordinator.Step(now + 420);
        coordinator.Step(now + 900);
        Assert.Equal(0d, coordinator.Progress, 3);
        Assert.False(coordinator.Step(now + 1000));
    }
}
