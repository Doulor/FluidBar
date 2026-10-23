using System.Collections.Immutable;
using FluidBar.App.State;
using FluidBar.Platform.Windows.Agent;
using Xunit;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Ring-count liveness (user report 2026-10-24): three Harness sessions must yield three rings even when
/// only one of them is mid-step — the other two are open tabs that simply emitted no event recently.
/// </summary>
public sealed class AgentLivenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 3, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Open_sessions_that_went_quiet_still_count_as_live()
    {
        var snapshot = new AgentEventSnapshot(ImmutableArray.Create(
            Entry("s1", AgentEventKind.Running, Now.AddSeconds(-5)),
            Entry("s2", AgentEventKind.Completed, Now.AddMinutes(-1)),
            Entry("s3", AgentEventKind.Completed, Now.AddMinutes(-3))));

        var rows = AgentContentMapper.Map(snapshot, Now);
        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => Assert.True(row.IsLive));
    }

    [Fact]
    public void Sessions_outside_the_liveness_window_are_not_live()
    {
        var snapshot = new AgentEventSnapshot(ImmutableArray.Create(
            Entry("old", AgentEventKind.Completed, Now.AddHours(-2)),
            Entry("new", AgentEventKind.Running, Now.AddSeconds(-10))));

        var rows = AgentContentMapper.Map(snapshot, Now);
        Assert.Equal(2, rows.Length);
        Assert.Equal(1, rows.Count(row => row.IsLive));
    }

    [Fact]
    public void Failed_sessions_never_count_as_live()
    {
        var snapshot = new AgentEventSnapshot(ImmutableArray.Create(
            Entry("bad", AgentEventKind.Failed, Now.AddSeconds(-5))));

        var rows = AgentContentMapper.Map(snapshot, Now);
        Assert.Single(rows);
        Assert.False(rows[0].IsLive);
    }

    private static AgentEventEntry Entry(string id, AgentEventKind kind, DateTimeOffset at)
        => new(id, AgentKind.DeepSeek, kind, "DeepSeek Harness", "detail", at, id + ".json", id);
}
