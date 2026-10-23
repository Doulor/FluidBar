using FluidBar.Platform.Windows.Agent;

namespace FluidBar.App.State;

/// <summary>
/// Ring colour per agent tool. Sessions from the SAME tool share one colour (the user's rule:
/// "都是 harness 的 agent 就是一样的颜色"); different tools are what makes the rings differ.
/// Returns 0 when the theme accent should be used (generic/unknown tools).
/// </summary>
public static class AgentToolPalette
{
    /// <summary>0xAARRGGBB ring colour for a tool, or 0 for "theme accent".</summary>
    /// <param name="kind">Agent tool.</param>
    public static uint ColorFor(AgentKind kind) => kind switch
    {
        AgentKind.Claude => 0xFFD97757,
        AgentKind.Codex => 0xFF10A37F,
        AgentKind.DeepSeek => 0xFF4D6BFE,
        AgentKind.Cursor => 0xFF7C5CFF,
        AgentKind.Windsurf => 0xFF12B5A5,
        AgentKind.Aider => 0xFFE0A030,
        AgentKind.Copilot => 0xFF8A8F98,
        AgentKind.Zcode => 0xFF3B82F6,
        AgentKind.Qoder => 0xFFEF5DA8,
        AgentKind.WorkBuddy => 0xFF22C55E,
        _ => 0,
    };

    /// <summary>Same colour for the same tool; falls back to the theme accent.</summary>
    /// <param name="kind">Agent tool.</param>
    /// <param name="accent">Theme accent used when the tool has no palette colour.</param>
    public static uint ColorFor(AgentKind kind, uint accent)
    {
        var color = ColorFor(kind);
        return color != 0 ? color : accent;
    }
}
