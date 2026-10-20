namespace FluidBar.Core.Layout;

/// <summary>
/// Shared text-colour tokens for every FluidBar surface. The island, settings window and marketplace
/// must reference these constants instead of hard-coding their own tertiary greys: the island used
/// #80808A while settings used #86868F, which read as two different "hint" intensities on the same
/// desktop (task-36).
/// </summary>
public static class TextTokens
{
    /// <summary>Tertiary/hint text on dark surfaces (#86868F). Island shell 5.55:1, raised cards 5.09:1, settings window 5.41:1.</summary>
    public const string TertiaryDark = "#86868F";

    /// <summary>Tertiary/hint text on light surfaces (#666D78). Island light 4.83:1, settings light 5.0:1.</summary>
    public const string TertiaryLight = "#666D78";
}
