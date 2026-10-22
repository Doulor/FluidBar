namespace FluidBar.Core.Media;

/// <summary>
/// The single source of truth for "where is the song now".
/// </summary>
/// <remarks>
/// <para>One anchor and one rate. The anchor is a position plus the clock reading when it was taken; the
/// clock reading is the player's own rendered audio time whenever it is available (it cannot drift and it
/// stops exactly when the player pauses), and wall time otherwise.</para>
/// <para>Everything that used to guess - extrapolating a 30-second state file, learning an offset, gating on
/// an audio peak - is gone: those compensations only existed because there was no exact clock.</para>
/// </remarks>
public sealed class PlaybackClock
{
    private TimeSpan _anchorPosition;
    private TimeSpan _anchorAudio;
    private TimeSpan _anchorWall;
    private bool _anchored;

    /// <summary>Current position, never negative.</summary>
    public TimeSpan Position { get; private set; }

    /// <summary>True once an anchor exists.</summary>
    public bool IsAnchored => _anchored;

    /// <summary>
    /// Places the anchor. <paramref name="audio"/> is the player's rendered audio time (or anything that
    /// advances at playback rate), <paramref name="wall"/> the same instant on the wall clock.
    /// </summary>
    public void Anchor(TimeSpan position, TimeSpan audio, TimeSpan wall)
    {
        _anchorPosition = position;
        _anchorAudio = audio;
        _anchorWall = wall;
        _anchored = true;
        Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    /// <summary>Anchors at the start of a song (the player always begins at zero).</summary>
    public void AnchorToTrackStart(TimeSpan audio, TimeSpan wall) => Anchor(TimeSpan.Zero, audio, wall);

    /// <summary>
    /// Re-anchors from a reading the player itself displayed. Returns true when the correction was applied.
    /// </summary>
    /// <remarks>
    /// A reading is authoritative at the instant it appears, so adopting it clears every accumulated error -
    /// this is the automatic version of "pause, wait, play", which the user found to be exact.
    /// A reading that disagrees by more than <paramref name="maximumJump"/> is a seek and is adopted too;
    /// a reading that disagrees by less than <paramref name="deadband"/> is ignored as text rounding.
    /// </remarks>
    public bool TryReanchor(TimeSpan reading, TimeSpan audio, TimeSpan wall, TimeSpan deadband, TimeSpan maximumJump)
    {
        if (!_anchored || reading < TimeSpan.Zero)
        {
            return false;
        }

        var difference = (reading - Position).Duration();
        if (difference < deadband || difference > maximumJump)
        {
            return false;
        }

        Anchor(reading, audio, wall);
        return true;
    }

    /// <summary>Advances the position.</summary>
    /// <param name="audio">Rendered audio time now.</param>
    /// <param name="wall">Wall time now.</param>
    /// <param name="audioIsReliable">False when the capture is missing frames: fall back to wall time.</param>
    public TimeSpan Advance(TimeSpan audio, TimeSpan wall, bool audioIsReliable)
    {
        if (!_anchored)
        {
            return TimeSpan.Zero;
        }

        var elapsed = audioIsReliable ? audio - _anchorAudio : wall - _anchorWall;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var position = _anchorPosition + elapsed;
        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        Position = position;
        return position;
    }

    /// <summary>Freezes at the current position (used while the player is paused).</summary>
    public TimeSpan Freeze() => Position;

    /// <summary>Forgets the anchor, e.g. when the player disappears.</summary>
    public void Reset()
    {
        _anchored = false;
        Position = TimeSpan.Zero;
    }
}
