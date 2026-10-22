using FluidBar.Platform.Windows.Agent;

namespace FluidBar.Platform.Tests;

/// <summary>Agent row focus falls back to matching the session/project against top-level window titles.</summary>
public sealed class AgentWindowFocusTests
{
    [Fact]
    public void Eligible_process_names_exclude_browsers_and_include_terminals()
    {
        Assert.True(AgentWindowFocus.IsEligibleProcessName("WindowsTerminal"));
        Assert.True(AgentWindowFocus.IsEligibleProcessName("code"));
        Assert.False(AgentWindowFocus.IsEligibleProcessName("chrome"));
        Assert.False(AgentWindowFocus.IsEligibleProcessName(null));
    }

    [Fact]
    public void Matches_SessionTitleOrProjectCaseInsensitively()
    {
        Assert.True(AgentWindowFocus.Matches("Claude Code — FluidBar", "claude code", null));
        Assert.True(AgentWindowFocus.Matches(@"D:\src\FluidBar — Windows Terminal", null, "fluidbar"));
        Assert.False(AgentWindowFocus.Matches("Notepad", "claude code", "FluidBar"));
        Assert.False(AgentWindowFocus.Matches("", "claude code", "FluidBar"));
    }
}
