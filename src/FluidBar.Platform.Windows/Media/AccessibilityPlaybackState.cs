namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Resolves the player's play/pause state from the accessibility transport row.
/// </summary>
/// <remarks>
/// Multi-media players render one widget that swaps between the play and pause glyphs. The accessibility
/// tree keeps both elements but only the shown one is visible (<c>STATE_SYSTEM_INVISIBLE</c> is cleared),
/// so the visible glyph is the live state — available even in player modes that hide the time text
/// (VIP/preview tracks), where the position-based inference would need up to a full settings write
/// (~30 s) to notice a pause.
/// </remarks>
public static class AccessibilityPlaybackState
{
    /// <summary>Resolves the state, or null when the tree cannot tell (both or neither glyph visible).</summary>
    /// <param name="playButtonVisible">The "play" glyph is shown (=> paused).</param>
    /// <param name="pauseButtonVisible">The "pause" glyph is shown (=> playing).</param>
    public static bool? Resolve(bool playButtonVisible, bool pauseButtonVisible)
        => playButtonVisible == pauseButtonVisible ? null : !playButtonVisible;
}
