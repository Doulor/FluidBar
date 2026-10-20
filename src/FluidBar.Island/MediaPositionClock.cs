namespace FluidBar.Island;

/// <summary>
/// Turns the sampled playback position into a monotonic wall-clock estimate so the karaoke sweep can be
/// evaluated once per rendered frame instead of once per poll.
/// </summary>
/// <remarks>
/// <para>Media sources publish a position every poll period (250 ms for the accessibility fallback), which
/// is what made the sweep advance in visible steps. Between samples the position is simply the last sample
/// plus the elapsed time while the track is playing; a paused track freezes it — the sweep must stop on the
/// very frame the pause arrives.</para>
/// <para>Extrapolation is bounded: it never runs past the track length and never runs more than
/// <see cref="MaxExtrapolationMs"/> past the last sample, so a player that stops publishing a position can
/// not walk the progress bar past the end.</para>
/// <para>Every new sample re-anchors. A sample that disagrees by more than
/// <see cref="ResyncToleranceMs"/> is a seek and snaps; a smaller disagreement is drift and is eased out
/// over <see cref="DriftDecayMs"/> instead of jumping the picture backwards.</para>
/// </remarks>
public sealed class MediaPositionClock
{
    /// <summary>Deviation above which a sample is treated as a seek instead of drift.</summary>
    public const double ResyncToleranceMs = 750;

    /// <summary>
    /// Longest silence that is still treated as "the source stopped updating" (rather than a machine
    /// suspend / clock jump): one full track length.
    /// </summary>
    public const double MaxExtrapolationMs = 600_000;

    /// <summary>Time over which a small sample disagreement is eased out (no visible jump).</summary>
    public const double DriftDecayMs = 400;

    private double _anchorPositionMs;
    private double _anchorAtMs;
    private bool _playing;
    private bool _hasAnchor;
    private double _driftMs;
    private double _driftAtMs;
    private double _limitMs;

    /// <summary>Whether the clock has a usable anchor.</summary>
    public bool HasAnchor => _hasAnchor;

    /// <summary>Whether the position advances with wall time (false while paused).</summary>
    public bool IsAdvancing => _hasAnchor && _playing;

    /// <summary>The last anchored position (milliseconds).</summary>
    public double AnchorPositionMs => _anchorPositionMs;

    /// <summary>Upper bound for the position (track length); &lt;= 0 means unbounded.</summary>
    public double LimitMs => _limitMs;

    /// <summary>Bounds the position (track length); &lt;= 0 removes the bound.</summary>
    /// <param name="limitMs">Track length in milliseconds.</param>
    public void SetLimit(double limitMs) => _limitMs = limitMs > 0 ? limitMs : 0;

    /// <summary>The position at <paramref name="nowMs"/>.</summary>
    /// <param name="nowMs">Monotonic "now" on the same clock as the anchors (milliseconds).</param>
    public double PositionAt(double nowMs)
    {
        if (!_hasAnchor)
        {
            return 0;
        }

        if (!_playing)
        {
            return Clamp(_anchorPositionMs);
        }

        var elapsed = nowMs - _anchorAtMs;
        if (double.IsNaN(elapsed) || elapsed <= 0)
        {
            return Clamp(_anchorPositionMs);
        }

        // A clock jump (suspend/resume) must not fast-forward the picture: hold the last sample.
        if (elapsed > MaxExtrapolationMs)
        {
            return Clamp(_anchorPositionMs);
        }

        return Clamp(_anchorPositionMs + elapsed + DriftAt(nowMs));
    }

    /// <summary>(Re)anchors the clock on a source sample.</summary>
    /// <param name="positionMs">Sampled position in milliseconds.</param>
    /// <param name="nowMs">Monotonic "now" (milliseconds).</param>
    /// <param name="playing">Whether the track is currently playing.</param>
    public void Anchor(double positionMs, double nowMs, bool playing)
    {
        _anchorPositionMs = Math.Max(0, positionMs);
        _anchorAtMs = nowMs;
        _playing = playing;
        _hasAnchor = true;
        _driftMs = 0;
        _driftAtMs = nowMs;
    }

    /// <summary>
    /// Applies a new sample: snaps on a seek, eases out a small drift otherwise. Returns whether it snapped.
    /// </summary>
    /// <param name="positionMs">Sampled position.</param>
    /// <param name="nowMs">Monotonic "now" (milliseconds).</param>
    /// <param name="playing">Play state reported with the sample.</param>
    public bool Reconcile(double positionMs, double nowMs, bool playing)
    {
        if (!_hasAnchor)
        {
            Anchor(positionMs, nowMs, playing);
            return true;
        }

        var expected = PositionAt(nowMs);
        var diff = Math.Max(0, positionMs) - expected;
        if (Math.Abs(diff) > ResyncToleranceMs)
        {
            Anchor(positionMs, nowMs, playing);
            return true;
        }

        // Keep the anchor and remember the drift so it can be eased out instead of jumping backwards.
        _driftMs = diff;
        _driftAtMs = nowMs;
        _playing = playing;
        return false;
    }

    /// <summary>Updates the playing flag without moving the anchor (freezes the current position).</summary>
    /// <param name="playing">Whether the track is playing.</param>
    /// <param name="nowMs">Monotonic "now" (milliseconds); the current position is frozen into the anchor.</param>
    public void SetPlaying(bool playing, double nowMs)
    {
        if (!_hasAnchor || playing == _playing)
        {
            return;
        }

        // Freeze the extrapolated value first so a pause does not rewind and a resume does not jump.
        _anchorPositionMs = PositionAt(nowMs);
        _anchorAtMs = nowMs;
        _driftMs = 0;
        _driftAtMs = nowMs;
        _playing = playing;
    }

    /// <summary>Whether a fresh sample should be treated as a jump (seek / loop / track change).</summary>
    /// <param name="sampledPositionMs">Sampled position in milliseconds.</param>
    /// <param name="nowMs">Monotonic "now" (milliseconds).</param>
    public bool IsJump(double sampledPositionMs, double nowMs)
        => _hasAnchor && Math.Abs(sampledPositionMs - PositionAt(nowMs)) > ResyncToleranceMs;

    /// <summary>Drops the anchor (no track / no timeline).</summary>
    public void Reset()
    {
        _hasAnchor = false;
        _playing = false;
        _anchorPositionMs = 0;
        _anchorAtMs = 0;
        _driftMs = 0;
        _driftAtMs = 0;
    }

    private double DriftAt(double nowMs)
    {
        if (_driftMs == 0)
        {
            return 0;
        }

        var age = nowMs - _driftAtMs;
        if (age >= DriftDecayMs)
        {
            _driftMs = 0;
            return 0;
        }

        return _driftMs * (1 - (age / DriftDecayMs));
    }

    private double Clamp(double positionMs)
    {
        if (positionMs < 0)
        {
            return 0;
        }

        return _limitMs > 0 && positionMs > _limitMs ? _limitMs : positionMs;
    }
}
