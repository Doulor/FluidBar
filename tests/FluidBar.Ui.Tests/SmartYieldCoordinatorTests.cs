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

        for (var i = 0; i < 60; i++)
        {
            now += 20;
            coordinator.PointerMoved(200, 800);
        }

        Assert.False(applied[^1], "input must be restored after the pointer leaves");
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
