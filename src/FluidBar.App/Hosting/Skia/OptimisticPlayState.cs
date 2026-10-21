namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Optimistic play/pause state for the island.
/// </summary>
/// <remarks>
/// <para>A fallback player (KuGou through MSAA) only reports its play/pause glyph on the accessibility
/// poll, and that poll can still return the <em>old</em> text for a while after the tap. The previous rule
/// (a fixed 600 ms window) rolled the flip back on a single stale read, which is exactly what made the
/// island show "playing" after the user had already paused.</para>
/// <para>The rule is now: the flip holds until the source reports the <em>same</em> disagreement twice in a
/// row (one stale read is ignored, two consistent reads are the real state), for at most
/// <see cref="HoldWindow"/> — during which the user's intent is what they see. When the flip is dropped the
/// caller is told to re-read the source once instead of trusting its cached reading.</para>
/// </remarks>
public sealed class OptimisticPlayState
{
    /// <summary>Longest a flip may survive without two agreeing source reads (the "visible 2–3 s" window).</summary>
    public static readonly TimeSpan HoldWindow = TimeSpan.FromMilliseconds(6000);

    /// <summary>
    /// Reads that arrive sooner than this after the tap are ignored: the accessibility poll almost always
    /// still carries the pre-tap glyph, so counting it would roll the flip back immediately.
    /// </summary>
    // Raised from 150 ms: a fallback player repaints its transport glyph ~0.5-1 s after the tap, so the
    // first two polls after a toggle still report the PRE-tap state. Counting them rolled the flip back
    // about a second after the click and left the icon inverted (user report 2026-10-21).
    public static readonly TimeSpan StaleReadGuard = TimeSpan.FromMilliseconds(1200);

    /// <summary>Consecutive disagreeing reads required before the flip is dropped.</summary>
        // Four consistent contradicting reads (>= 1 s of polls after the guard) before the source wins.
    public const int RequiredDisagreements = 4;

    private bool? _pending;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _until;
    private int _disagreements;

    /// <summary>Whether a flip is currently pending.</summary>
    public bool IsPending => _pending is not null;

    /// <summary>State the flip assumes (null when settled).</summary>
    public bool? PendingValue => _pending;

    /// <summary>How many consecutive disagreeing reads have been seen.</summary>
    public int Disagreements => _disagreements;

    /// <summary>
    /// True when the last <see cref="Resolve"/> dropped a flip because the source never agreed; the caller
    /// should re-read the source once instead of trusting its cached reading.
    /// </summary>
    public bool LastResolveRolledBack { get; private set; }

    /// <summary>Records a flip (the tap); <paramref name="now"/> is the current UTC clock.</summary>
    /// <param name="playing">State the caller optimistically assumes.</param>
    /// <param name="now">Current UTC time.</param>
    /// <param name="window">How long the flip stays valid (default <see cref="HoldWindow"/>).</param>
    public void Set(bool playing, DateTimeOffset now, TimeSpan? window = null)
    {
        _pending = playing;
        _startedAt = now;
        _until = now + (window ?? HoldWindow);
        _disagreements = 0;
        LastResolveRolledBack = false;
    }

    /// <summary>Drops the flip (rollback after a refused command).</summary>
    public void Clear()
    {
        _pending = null;
        _disagreements = 0;
        LastResolveRolledBack = false;
    }

    /// <summary>Resolves the state to publish for the last source reading.</summary>
    /// <param name="actual">State reported by the source.</param>
    /// <param name="now">Current UTC time.</param>
    /// <returns>The state the island should paint.</returns>
    public bool Resolve(bool actual, DateTimeOffset now)
    {
        LastResolveRolledBack = false;
        if (_pending is not { } optimistic)
        {
            return actual;
        }

        // The source caught up: the truth wins and the overlay is gone (not a rollback).
        if (optimistic == actual)
        {
            _pending = null;
            _disagreements = 0;
            return actual;
        }

        // Anything inside the stale-read guard is the pre-tap glyph: it must not roll the flip back.
        if (now - _startedAt < StaleReadGuard)
        {
            return optimistic;
        }

        _disagreements++;

        // One stale-looking read is tolerated; two consistent ones are the real state.
        if (_disagreements < RequiredDisagreements && now <= _until)
        {
            return optimistic;
        }

        _pending = null;
        _disagreements = 0;
        LastResolveRolledBack = true;
        return actual;
    }
}
