using FluidBar.App.Island;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Island;
using FluidBar.Platform.Windows.Agent;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Agent animation gating: the ring spinner and the indeterminate progress bar must request 60 fps
/// frames while an agent is running and stop the frame loop (freezing the clock) as soon as it is not.
/// The phase math itself is covered by <c>FluidBar.Island.Tests.AgentMotionTests</c>.
/// </summary>
public sealed class AgentAnimationTests
{
    private static AgentItemData Row(StatusTone tone, string time = "0:09") => new()
    {
        Name = "Claude Code",
        PrimaryText = tone == StatusTone.Success ? "Done" : "读取 src/App.tsx",
        StatusText = tone == StatusTone.Success ? "已完成" : "运行中",
        Tone = tone,
        TimeText = time,
        Kind = AgentKind.Claude,
    };

    [Fact]
    public void Running_agent_page_advances_the_agent_clock()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(4);

        Assert.True(fixture.Controller.IsAgentAnimating);
        var start = fixture.Controller.RenderState.AgentAnimationSeconds;
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(start + 0.1, fixture.Controller.RenderState.AgentAnimationSeconds, 3);
    }

    [Fact]
    public void Elapsed_time_updates_keep_the_shell_size_and_only_dirty_the_time_strip()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(4);
        fixture.Data.Update(content => content with { Agents = [Row(StatusTone.Info, "0:09")] });
        fixture.Settle();
        Drain(fixture);

        var before = fixture.Controller.RenderState;
        fixture.Data.Update(content => content with { Agents = [Row(StatusTone.Info, "0:10")] });
        Assert.True(fixture.Controller.TakeInvalidation(out var first));
        Assert.False(first.FullFrame);
        Assert.Equal(before.ShellWidth, fixture.Controller.RenderState.ShellWidth, 6);
        Assert.Equal(before.ShellHeight, fixture.Controller.RenderState.ShellHeight, 6);

        // 0:59 -> 1:00 used to be the worst case (one extra digit): the reserved tabular column keeps
        // the shell and the left column stationary.
        fixture.Data.Update(content => content with { Agents = [Row(StatusTone.Info, "1:00")] });
        Assert.True(fixture.Controller.TakeInvalidation(out var second));
        Assert.False(second.FullFrame);
        Assert.Equal(before.ShellWidth, fixture.Controller.RenderState.ShellWidth, 6);
        Assert.Equal(before.ShellHeight, fixture.Controller.RenderState.ShellHeight, 6);
    }

    [Fact]
    public void Agent_frames_are_continuous_and_dirty_every_frame()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(4);
        fixture.Data.Update(content => content with { Agents = [Row(StatusTone.Info, "0:09")] });
        fixture.Settle();
        Drain(fixture);

        var shell = fixture.Controller.RenderState;
        var frames = new List<object>();
        var previousSeconds = shell.AgentAnimationSeconds;
        const double step = 1000.0 / 60.0;
        for (var i = 0; i < 300; i++)
        {
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(step));
            Assert.True(fixture.Controller.TakeInvalidation(out var change), $"frame {i} produced no damage");
            Assert.False(change.FullFrame, $"frame {i} repainted the whole surface");

            var state = fixture.Controller.RenderState;
            var deltaSeconds = state.AgentAnimationSeconds - previousSeconds;
            var deltaDegrees = deltaSeconds / AgentMotion.RingPeriodSeconds * 360.0;
            Assert.InRange(deltaDegrees, 6.6, 6.73);
            Assert.Equal(shell.ShellWidth, state.ShellWidth, 6);
            Assert.Equal(shell.ShellHeight, state.ShellHeight, 6);
            frames.Add(new
            {
                frame = i,
                milliseconds = Math.Round(i * step, 3),
                angleDegrees = Math.Round(AgentMotion.RingRotationDegrees(state.AgentAnimationSeconds), 4),
                deltaDegrees = Math.Round(deltaDegrees, 4),
                shellWidth = state.ShellWidth,
                shellHeight = state.ShellHeight,
                fullFrame = change.FullFrame,
            });
            previousSeconds = state.AgentAnimationSeconds;
        }

        var dir = Path.Combine(RepoRoot(), "build", "reports", "ui-audit", "agent-animation");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "frames-60fps.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                note = "300 frames at 16.667 ms (5 s); production SkiaIslandController.Advance and render-state clock",
                ringPeriodSeconds = AgentMotion.RingPeriodSeconds,
                frames,
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Drain(ControllerFixture fixture)
    {
        while (fixture.Controller.TakeInvalidation(out _))
        {
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FluidBar.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public void Completed_agent_stops_the_frame_loop_and_freezes_the_clock()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(4);
        Assert.True(fixture.Controller.IsAgentAnimating);

        fixture.Data.Update(content => content with { Agents = [Row(StatusTone.Success)] });
        fixture.Settle();

        Assert.False(fixture.Controller.IsAgentAnimating);
        var frozen = fixture.Controller.RenderState.AgentAnimationSeconds;
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(frozen, fixture.Controller.RenderState.AgentAnimationSeconds, 6);
    }

    [Fact]
    public void Idle_agent_page_requests_no_frames()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(4);
        fixture.Data.Update(content => content with { Agents = [Row(StatusTone.Success)] });
        fixture.Settle();
        Assert.False(fixture.Controller.IsAgentAnimating);

        var requests = 0;
        fixture.Controller.FrameRequested += () => requests++;
        var frozen = fixture.Controller.RenderState.AgentAnimationSeconds;
        for (var i = 0; i < 30; i++)
        {
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        }

        Assert.Equal(0, requests);
        Assert.Equal(frozen, fixture.Controller.RenderState.AgentAnimationSeconds, 6);
    }

    [Fact]
    public void Reduced_motion_keeps_the_agent_static_and_requests_no_frames()
    {
        using var fixture = ControllerFixture.Create(
            settings: AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0, ReducedMotion = ReducedMotionPreference.On });
        fixture.ExpandAndSettle();
        fixture.GoToPage(4);

        Assert.True(fixture.Controller.RenderState.ReducedMotion);
        Assert.False(fixture.Controller.IsAgentAnimating);
        var requests = 0;
        fixture.Controller.FrameRequested += () => requests++;
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(0, requests);
    }

    [Fact]
    public void A_new_run_requests_frames_immediately()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(4);
        fixture.Data.Update(content => content with { Agents = [Row(StatusTone.Success)] });
        fixture.Settle();
        Assert.False(fixture.Controller.IsAgentAnimating);

        var requests = 0;
        fixture.Controller.FrameRequested += () => requests++;
        fixture.Data.Update(content => content with { Agents = [Row(StatusTone.Info)] });

        Assert.True(requests > 0);
        Assert.True(fixture.Controller.IsAgentAnimating);
    }
}
