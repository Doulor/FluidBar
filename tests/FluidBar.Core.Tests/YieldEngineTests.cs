using FluidBar.Core.Interaction;

namespace FluidBar.Core.Tests;

/// <summary>
/// Smart-yield engine coverage (design doc §4–§5): the state machine must yield only on evidence,
/// never while the user is dragging or clicking the island, hold a yield long enough to be useful and
/// always come back to Interactive.
/// </summary>
public sealed class YieldEngineTests
{
    private static readonly YieldShell Shell = new(1000, 80, 132, 36);

    private static YieldSample Sample(double x, double y, long ms) => new(x, y, ms);

    private static YieldTransition Feed(YieldEngine engine, YieldSample sample, YieldContext context = default)
        => engine.Update(sample, Shell, context, sample.TimestampMs);

    private static YieldTransition Approach(YieldEngine engine, double startY, double endY, double step, long stepMs, YieldContext context = default)
    {
        var x = Shell.X + (Shell.Width / 2);
        var transition = default(YieldTransition);
        for (var y = startY; y >= endY; y -= step)
        {
            transition = Feed(engine, Sample(x, y, (long)((startY - y) / step * stepMs)), context);
        }

        return transition;
    }

    [Fact]
    public void Idle_pointer_stays_interactive()
    {
        var engine = new YieldEngine();
        for (var i = 0; i < 20; i++)
        {
            Feed(engine, Sample(100, 600, i * 40));
        }

        Assert.Equal(YieldState.Interactive, engine.State);
        Assert.False(engine.ShouldPassThrough);
    }

    [Fact]
    public void Fast_approach_towards_the_shell_yields()
    {
        var engine = new YieldEngine();
        var transition = Approach(engine, startY: 600, endY: 120, step: 30, stepMs: 8,
            new YieldContext(ForegroundMaximized: true));

        Assert.Equal(YieldState.Yielding, engine.State);
        Assert.True(transition.ShouldPassThrough);
    }

    [Fact]
    public void Slow_pointer_next_to_the_shell_does_not_yield()
    {
        var engine = new YieldEngine();
        for (var i = 0; i < 30; i++)
        {
            // 2 px per 40 ms = 0.05 px/ms, far below the fast-approach threshold.
            Feed(engine, Sample(900 + i, 300, i * 40));
        }

        Assert.Equal(YieldState.Interactive, engine.State);
    }

    [Fact]
    public void Dwelling_inside_the_shell_suppresses_the_yield()
    {
        var engine = new YieldEngine();
        var x = Shell.X + (Shell.Width / 2);
        var y = Shell.Y + (Shell.Height / 2);
        for (var i = 0; i < 40; i++)
        {
            Feed(engine, Sample(x, y, i * 30));
        }

        Assert.Equal(YieldState.Interactive, engine.State);
        Assert.True(engine.Score < 0, $"dwell should score negative, got {engine.Score}");
    }

    [Fact]
    public void Dragging_the_island_never_yields()
    {
        var engine = new YieldEngine();
        var transition = Approach(engine, startY: 600, endY: 120, step: 30, stepMs: 8, new YieldContext(IsDragging: true));

        Assert.Equal(YieldState.Interactive, engine.State);
        Assert.True(transition.Score <= -100);
    }

    [Fact]
    public void Recent_island_interaction_suppresses_the_yield()
    {
        var engine = new YieldEngine();
        engine.NoteInteraction(0);
        var transition = Approach(engine, startY: 600, endY: 120, step: 30, stepMs: 8,
            new YieldContext(ForegroundMaximized: true));

        Assert.Equal(YieldState.Interactive, engine.State);
        Assert.True(transition.Score < engine.Score + 1 && transition.Score < 20);
    }

    [Fact]
    public void Yield_is_held_for_the_minimum_time_before_cooling_down()
    {
        var engine = new YieldEngine(new YieldOptions { MinYieldMs = 600, CooldownMs = 900 });
        Approach(engine, startY: 600, endY: 120, step: 30, stepMs: 8, new YieldContext(ForegroundMaximized: true));
        Assert.Equal(YieldState.Yielding, engine.State);

        // Pointer leaves: the yield must still be held for MinYieldMs after it started.
        var held = Feed(engine, Sample(200, 800, 200));
        Assert.Equal(YieldState.Yielding, held.Current);

        var stillHeld = Feed(engine, Sample(200, 800, 400));
        Assert.Equal(YieldState.Yielding, stillHeld.Current);

        // After the minimum hold (and the leave delay) it falls back to Cooldown.
        var after = Feed(engine, Sample(200, 800, 900));
        Assert.Equal(YieldState.Cooldown, after.Current);
        Assert.False(after.ShouldPassThrough);
    }

    [Fact]
    public void Cooldown_blocks_an_immediate_re_yield()
    {
        var engine = new YieldEngine(new YieldOptions { MinYieldMs = 100, CooldownMs = 1000 });
        Approach(engine, startY: 600, endY: 120, step: 30, stepMs: 8, new YieldContext(ForegroundMaximized: true));
        var t = Feed(engine, Sample(200, 800, 5000));
        Assert.Equal(YieldState.Cooldown, t.Current);

        // A new approach during the cooldown must not pass through again.
        var again = Approach(engine, startY: 600, endY: 120, step: 30, stepMs: 8, new YieldContext(ForegroundMaximized: true));
        Assert.NotEqual(YieldState.Yielding, again.Current);
    }

    [Fact]
    public void Manual_restore_returns_to_interactive_immediately()
    {
        var engine = new YieldEngine();
        Approach(engine, startY: 600, endY: 120, step: 30, stepMs: 8, new YieldContext(ForegroundMaximized: true));
        Assert.Equal(YieldState.Yielding, engine.State);

        var restored = Feed(engine, Sample(1100, 300, 2000), new YieldContext(ManualRestoreRequested: true));
        Assert.Equal(YieldState.Interactive, restored.Current);
        Assert.False(restored.ShouldPassThrough);
    }

    [Fact]
    public void History_stays_bounded()
    {
        var engine = new YieldEngine(new YieldOptions { HistorySize = 8 });
        for (var i = 0; i < 500; i++)
        {
            Feed(engine, Sample(100 + i, 600, i * 10));
        }

        Assert.True(engine.HistoryCount <= 8, $"history grew to {engine.HistoryCount}");
    }

    [Fact]
    public void Aggressive_sensitivity_yields_earlier_than_conservative()
    {
        static YieldState Run(YieldSensitivity sensitivity)
        {
            var engine = new YieldEngine(new YieldOptions { Sensitivity = sensitivity });
            Approach(engine, startY: 500, endY: 200, step: 25, stepMs: 10, new YieldContext(ForegroundMaximized: true));
            return engine.State;
        }

        var aggressive = Run(YieldSensitivity.Aggressive);
        var conservative = Run(YieldSensitivity.Conservative);
        Assert.True(aggressive == YieldState.Yielding || aggressive == YieldState.Candidate);
        Assert.True(conservative != YieldState.Yielding, "conservative should not yield on the same weak approach");
    }
}
