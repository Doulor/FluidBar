using FluidBar.App.State;
using FluidBar.Platform.Windows.Agent;
using Xunit;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Ring colours follow the TOOL, not the slot (user report 2026-10-24): three DeepSeek Harness sessions
/// must all paint the same colour; only a different tool changes a ring's colour.
/// </summary>
public sealed class AgentToolPaletteTests
{
    [Fact]
    public void Sessions_from_the_same_tool_share_one_colour()
    {
        var first = AgentToolPalette.ColorFor(AgentKind.DeepSeek);
        var second = AgentToolPalette.ColorFor(AgentKind.DeepSeek);
        var third = AgentToolPalette.ColorFor(AgentKind.DeepSeek);

        Assert.NotEqual(0u, first);
        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    [Fact]
    public void Different_tools_get_different_colours()
    {
        Assert.NotEqual(
            AgentToolPalette.ColorFor(AgentKind.DeepSeek),
            AgentToolPalette.ColorFor(AgentKind.Claude));
    }

    [Fact]
    public void Unknown_tools_fall_back_to_the_theme_accent()
    {
        const uint accent = 0xFF4CC9F0;
        Assert.Equal(accent, AgentToolPalette.ColorFor(AgentKind.Generic, accent));
    }
}
