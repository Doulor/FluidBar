using FluidBar.Core.Activities;
using FluidBar.Island;
using SkiaSharp;
using Xunit;

namespace FluidBar.Island.Tests;

/// <summary>
/// Task-39: the elapsed-time tick must not move any layout. The duration column is reserved and drawn
/// with tabular digits, so only the time glyphs themselves may differ between 0:09 and 1:00.
/// </summary>
public sealed class AgentTimeLayoutTests
{
    private const float DpiScale = 1.5f;

    [Fact]
    public void Expanded_agent_duration_only_changes_pixels_inside_the_reserved_time_column()
    {
        var state = IslandTestFixtures.Expanded(4);
        var page = islandPage(state);
        var shortDuration = WithDurations(state, "0:09");
        var longDuration = WithDurations(state, "1:00");
        var changed = DiffBounds(shortDuration, longDuration);
        Assert.NotNull(changed);
        Assert.True(changed.Value.Left > page.Right - 130,
            $"layout moved outside the reserved duration column: {changed.Value}; page right={page.Right}");
        Assert.True(changed.Value.Width <= 90, $"time glyphs changed {changed.Value.Width} DIP wide");
    }

    [Fact]
    public void Compact_agent_duration_only_changes_pixels_inside_the_meta_column()
    {
        var a = CompactState("0:09");
        var b = CompactState("1:00");
        var shell = IslandMetrics.ShellRect(a);
        var changed = DiffBounds(a, b);
        Assert.NotNull(changed);
        Assert.True(changed.Value.Left > shell.Width - 150,
            $"compact layout moved outside the meta column: {changed.Value}");
    }

    private static IslandRenderState CompactState(string duration) => IslandTestFixtures.Compact(
        new CompactActivity(ActivityKind.Agent, new CompactModel
        {
            Title = "Refactor the agent page",
            StatusText = "Running",
            DurationText = duration,
            Status = CompactStatus.Info,
        }));

    private static SKRect islandPage(in IslandRenderState state) => IslandMetrics.PageRect(state, 4);

    private static IslandRenderState WithDurations(IslandRenderState state, string duration)
    {
        var items = state.Pages.Agent.Items.Select(item => item with { DurationText = duration }).ToArray();
        return state with
        {
            Pages = state.Pages with { Agent = state.Pages.Agent with { Items = items } },
        };
    }

    private static SKRect? DiffBounds(IslandRenderState a, IslandRenderState b)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var left = renderer.RenderToBitmap(a, DpiScale);
        using var right = renderer.RenderToBitmap(b, DpiScale);
        var minX = int.MaxValue;
        var maxX = int.MinValue;
        var minY = int.MaxValue;
        var maxY = int.MinValue;
        for (var y = 0; y < left.Height; y++)
        {
            for (var x = 0; x < left.Width; x++)
            {
                if (left.GetPixel(x, y) == right.GetPixel(x, y))
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        if (minX == int.MaxValue)
        {
            return null;
        }

        return new SKRect(
            (float)(minX / DpiScale),
            (float)(minY / DpiScale),
            (float)((maxX + 1) / DpiScale),
            (float)((maxY + 1) / DpiScale));
    }
}
