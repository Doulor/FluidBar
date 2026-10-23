using FluidBar.App.Diagnostics;
using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Interaction;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Smart-yield glue coverage: the coordinator must flip input pass-through when the engine yields and
/// restore it afterwards, and must not yield while the user interacts with the island.
/// </summary>
public sealed class SmartYieldCoordinatorTests
{
    private static readonly (int X, int Y, int Width, int Height) Shell = (1000, 80, 132, 36);

    private static SmartYieldCoordinator Create(
        List<bool> applied,
        Func<long> clock,
        bool dragging = false,
        YieldOptions? options = null)
        => new(
            options ?? new YieldOptions(),
            () => (Shell, dragging),
            applied.Add,
            new TraceAppLogger(),
            clock,
            (_, drag) => new YieldContext(ForegroundMaximized: true, IsDragging: drag));

    private static void Approach(SmartYieldCoordinator coordinator, Func<long> clock, Action<long> advance, double from = 600, double to = 120)
    {
        for (var y = from; y >= to; y -= 30)
        {
            advance(8);
            coordinator.PointerMoved(1066, (int)y);
        }
    }

    [Fact]
    public void Fast_approach_yields_and_the_pointer_leaving_restores_input()
    {
        var applied = new List<bool>();
        long now = 0;
        using var coordinator = Create(applied, () => now);

        Approach(coordinator, () => now, ms => now += ms);
        Assert.Contains(true, applied);

        // The island restores 3 s after the last activity near it.
        for (var i = 0; i < 40; i++)
        {
            now += 100;
            coordinator.PointerMoved(200, 800);
        }

        Assert.False(applied[^1], "input must be restored 3 s after the pointer leaves");
    }

    [Fact]
    public void Recent_island_click_prevents_a_yield()
    {
        var applied = new List<bool>();
        long now = 0;
        using var coordinator = Create(applied, () => now);

        coordinator.NoteInteraction();
        Approach(coordinator, () => now, ms => now += ms);

        Assert.DoesNotContain(true, applied);
    }

    [Fact]
    public void Dragging_the_island_prevents_a_yield()
    {
        var applied = new List<bool>();
        long now = 0;
        using var coordinator = Create(applied, () => now, dragging: true);

        Approach(coordinator, () => now, ms => now += ms);

        Assert.DoesNotContain(true, applied);
    }

    [Fact]
    public void Right_click_softens_and_restores_manually()
    {
        var applied = new List<bool>();
        long now = 1000;
        using var coordinator = Create(applied, () => now);

        coordinator.Toggle();
        Assert.True(applied[^1], "right-click must soften the island");
        Assert.True(coordinator.Progress < 0.5);

        now += 200;
        coordinator.Toggle();
        Assert.False(applied[^1], "a second right-click must restore it");
    }

    [Fact]
    public void Soften_animation_moves_the_progress_and_settles()
    {
        var applied = new List<bool>();
        long now = 1000;
        using var coordinator = Create(applied, () => now);

        Approach(coordinator, () => now, ms => now += ms);
        Assert.Contains(true, applied);

        // The engine is yielding; stepping the animation must ease the progress towards 1.
        Assert.True(coordinator.Step(now));
        Assert.True(coordinator.Step(now + 120) && coordinator.Progress > 0.2 && coordinator.Progress < 1);
        coordinator.Step(now + 400);
        Assert.Equal(1d, coordinator.Progress, 3);

        // While the island is yielded the host keeps ticking so the 3 s restore timer can fire.
        Assert.True(coordinator.Step(now + 500));

        // Restoring the input animates back to 1 → 0.
        coordinator.Restore();
        Assert.True(coordinator.Step(now + 520));
        coordinator.Step(now + 1200);
        Assert.Equal(0d, coordinator.Progress, 3);
        Assert.False(coordinator.Step(now + 1300));
    }

    [Fact]
    public void Hidden_island_restores_input()
    {
        var applied = new List<bool>();
        long now = 0;
        var visible = true;
        using var coordinator = new SmartYieldCoordinator(
            new YieldOptions(),
            () => visible ? (Shell, false) : null,
            applied.Add,
            new TraceAppLogger(),
            () => now,
            (_, _) => new YieldContext(ForegroundMaximized: true));

        Approach(coordinator, () => now, ms => now += ms);
        Assert.Contains(true, applied);

        visible = false;
        now += 40;
        coordinator.PointerMoved(200, 800);
        Assert.False(applied[^1]);
    }
}
