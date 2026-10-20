using FluidBar.App.Island;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Platform.Windows.Agent;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Agent animation gating: the ring spinner and the indeterminate progress bar must request 60 fps
/// frames while an agent is running and stop the frame loop (freezing the clock) as soon as it is not.
/// The phase math itself is covered by <c>FluidBar.Island.Tests.AgentMotionTests</c>.
/// </summary>
public sealed class AgentAnimationTests
{
    private static AgentItemData Row(StatusTone tone) => new()
    {
        Name = "Claude Code",
        PrimaryText = tone == StatusTone.Success ? "Done" : "读取 src/App.tsx",
        StatusText = tone == StatusTone.Success ? "已完成" : "运行中",
        Tone = tone,
        TimeText = "12s",
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
