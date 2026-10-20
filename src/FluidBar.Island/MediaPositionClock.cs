namespace FluidBar.Island;

/// <summary>
/// Turns the sampled playback position into a monotonic wall-clock estimate so the karaoke sweep can be
/// evaluated once per rendered frame instead of once per poll.
/// </summary>
/// <remarks>
/// <para>Media sources publish a position every poll period (250 ms for the accessibility fallback), which
/// is what made the sweep advance in visible steps. Between samples the position is simply the last sample
/// plus the elapsed time while the track is playing; a paused track freezes it.</para>
/// <para>Every new sample re-anchors the clock. A sample that disagrees with the extrapolated position by
/// more than <see cref="ResyncToleranceMs"/> means the user seeked (or the track looped / changed), so the
/// clock snaps to the new sample instead of easing into it.</para>
/// </remarks>
public sealed class MediaPositionClock
{
    /// <summary>Deviation above which a sample is treated as a seek instead of drift.</summary>
    public const double ResyncToleranceMs = 750;

    private double _anchorPositionMs;
    private double _anchorAtMs;
    private bool _playing;
    private bool _hasAnchor;

    /// <summary>Whether the clock has a usable anchor.</summary>
    public bool HasAnchor => _hasAnchor;

    /// <summary>Whether the position advances with wall time (false while paused).</summary>
    public bool IsAdvancing => _hasAnchor && _playing;

    /// <summary>The last anchored position (milliseconds).</summary>
    public double AnchorPositionMs => _anchorPositionMs;

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
            return _anchorPositionMs;
        }

        var elapsed = nowMs - _anchorAtMs;
        if (double.IsNaN(elapsed) || elapsed <= 0)
        {
            return _anchorPositionMs;
        }

        // A clock jump (suspend/resume, clock change) must not fast-forward the sweep.
        if (elapsed > 60_000)
        {
            return _anchorPositionMs;
        }

        return _anchorPositionMs + elapsed;
    }

    /// <summary>(Re)anchors the clock on a source sample.</summary>
    /// <param name="positionMs">Sampled position in milliseconds.</param>
    /// <param name="nowMs">Monotonic "now" (milliseconds).</param>
    /// <param name="playing">Whether the track is currently playing.</param>
    public void Anchor(double positionMs, double nowMs, bool playing)
    {
        _anchorPositionMs = positionMs < 0 ? 0 : positionMs;
        _anchorAtMs = nowMs;
        _playing = playing;
        _hasAnchor = true;
    }

    /// <summary>Updates the playing flag without moving the anchor.</summary>
    /// <param name="playing">Whether the track is playing.</param>
    /// <param name="nowMs">Monotonic "now" (milliseconds); the current position is frozen into the anchor.</param>
    public void SetPlaying(bool playing, double nowMs)
    {
        if (!_hasAnchor)
        {
            return;
        }

        if (playing == _playing)
        {
            return;
        }

        // Freeze the extrapolated value first so a pause does not rewind and a resume does not jump.
        _anchorPositionMs = PositionAt(nowMs);
        _anchorAtMs = nowMs;
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
    }
}
