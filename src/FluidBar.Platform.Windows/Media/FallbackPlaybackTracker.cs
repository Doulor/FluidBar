namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Infers play/pause for the window-title fallback from the position the player persists.
/// </summary>
/// <remarks>
/// <para>Players without SMTC (KuGou) keep their audio session "active" even while muted or paused, so
/// the session state cannot answer "is it playing". The position KuGou rewrites into its settings file
/// every ~30 s can: it advances while playing and stays put (in a freshly rewritten file) while paused.</para>
/// <para>Pure state machine — the media service owns one instance and the unit tests drive it
/// directly, so no timing or Win32 is involved in the decision.</para>
/// </remarks>
internal sealed class FallbackPlaybackTracker
{
    /// <summary>Movement below this tolerance does not count as progress.</summary>
    internal static readonly TimeSpan PositionTolerance = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// How long a frozen position must persist before it means "paused". It depends on how often the
    /// source refreshes: the accessibility timeline ticks every second, the player's settings file only
    /// every ~30 s.
    /// </summary>
    internal static readonly TimeSpan AccessibilityStaleAfter = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// Staleness window for the settings-file timeline. The player rewrites the file roughly every 30 s,
    /// so a rewrite that leaves the position untouched is the pause signal; the window sits just under
    /// that cadence so the poll never mistakes "no new write yet" for a pause.
    /// </summary>
    internal static readonly TimeSpan StateFileStaleAfter = TimeSpan.FromSeconds(25);

    private string _trackKey = string.Empty;
    private TimeSpan _position;
    private DateTimeOffset _positionChangedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _sampledAt = DateTimeOffset.MinValue;
    private bool _hadTimeline;

    /// <summary>Latest inferred state (optimistic until the player contradicts it).</summary>
    public bool IsPlaying { get; private set; } = true;

    /// <summary>Feeds one observation and returns the inferred playing state.</summary>
    /// <param name="trackKey">Stable track key; a change means a new track started.</param>
    /// <param name="hasTimeline">Whether <paramref name="position"/> came from the player state file.</param>
    /// <param name="position">Persisted playback position.</param>
    /// <param name="sampledAt">When the position was sampled.</param>
    /// <param name="staleAfter">
    /// How long the position may stay frozen while the source keeps refreshing before playback is
    /// considered paused (source-refresh dependent).
    /// </param>
    public bool Observe(string trackKey, bool hasTimeline, TimeSpan position, DateTimeOffset sampledAt,
        TimeSpan? staleAfter = null)
    {
        var trackChanged = !string.Equals(_trackKey, trackKey, StringComparison.Ordinal);
        if (trackChanged)
        {
            IsPlaying = true;
            _positionChangedAt = sampledAt;
        }
        else if (hasTimeline && _hadTimeline)
        {
            if (position - _position > PositionTolerance)
            {
                IsPlaying = true;
                _positionChangedAt = sampledAt;
            }
            else if (sampledAt - _positionChangedAt > (staleAfter ?? AccessibilityStaleAfter))
            {
                // The source kept refreshing without the position moving => paused.
                IsPlaying = false;
            }
        }

        _trackKey = trackKey;
        _position = position;
        _sampledAt = sampledAt;
        _hadTimeline = hasTimeline;
        return IsPlaying;
    }

    /// <summary>Forgets the previous observation (mode switch or no player at all).</summary>
    public void Reset()
    {
        _trackKey = string.Empty;
        _position = TimeSpan.Zero;
        _positionChangedAt = DateTimeOffset.MinValue;
        _sampledAt = DateTimeOffset.MinValue;
        _hadTimeline = false;
        IsPlaying = true;
    }
}