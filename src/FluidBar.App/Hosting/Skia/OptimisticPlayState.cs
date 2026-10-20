namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Optimistic play/pause state for the island.
/// </summary>
/// <remarks>
/// <para>A fallback player (KuGou through MSAA) only reports its play/pause glyph on the accessibility
/// poll, so waiting for the truth made the pill's icon lag visibly behind the tap. This holds the flipped
/// state until the source agrees, it expires, or the transport command is refused (rollback) — the island
/// then always ends up showing the real state instead of a stale lie.</para>
/// <para>The window is deliberately short (600 ms ≈ two 250 ms polls): a flip that the source has not
/// confirmed by then is dropped and the caller is told to re-read the source once, which is what stops the
/// icon from disagreeing with the audio for a whole track.</para>
/// </remarks>
public sealed class OptimisticPlayState
{
    /// <summary>How long a flip may disagree with the source before it is dropped.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMilliseconds(600);

    private bool? _pending;
    private DateTimeOffset _until;

    /// <summary>Whether a flip is currently pending.</summary>
    public bool IsPending => _pending is not null;

    /// <summary>
    /// True when the last <see cref="Resolve"/> dropped a flip because the source never agreed; the caller
    /// should re-read the source once instead of trusting its cached reading.
    /// </summary>
    public bool LastResolveRolledBack { get; private set; }

    /// <summary>Records a flip (the tap); <paramref name="now"/> is the current UTC clock.</summary>
    /// <param name="playing">State the caller optimistically assumes.</param>
    /// <param name="now">Current UTC time.</param>
    /// <param name="ttl">How long the flip stays valid (default 600 ms).</param>
    public void Set(bool playing, DateTimeOffset now, TimeSpan? ttl = null)
    {
        _pending = playing;
        _until = now + (ttl ?? DefaultTtl);
        LastResolveRolledBack = false;
    }

    /// <summary>Drops the flip (rollback after a refused command).</summary>
    public void Clear()
    {
        _pending = null;
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
            return actual;
        }

        // Still waiting, but only inside the short window.
        if (now <= _until)
        {
            return optimistic;
        }

        // The source never agreed: drop the lie and tell the caller to re-read.
        _pending = null;
        LastResolveRolledBack = true;
        return actual;
    }
}
