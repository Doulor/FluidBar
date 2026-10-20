namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Optimistic play/pause state for the island.
/// </summary>
/// <remarks>
/// A fallback player (KuGou through MSAA) only reports its play/pause glyph on the accessibility poll, so
/// waiting for the truth made the pill's icon lag visibly behind the tap. This holds the flipped state
/// until the source agrees, it expires, or the transport command is refused (rollback) — the island then
/// always ends up showing the real state instead of a stale lie.
/// </remarks>
public sealed class OptimisticPlayState
{
    /// <summary>How long a flip may disagree with the source before it is dropped.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(2);

    private bool? _pending;
    private DateTimeOffset _until;

    /// <summary>Whether a flip is currently pending.</summary>
    public bool IsPending => _pending is not null;

    /// <summary>Records a flip (the tap); <paramref name="now"/> is the current UTC clock.</summary>
    /// <param name="playing">State the caller optimistically assumes.</param>
    /// <param name="now">Current UTC time.</param>
    /// <param name="ttl">How long the flip stays valid (default 2 s).</param>
    public void Set(bool playing, DateTimeOffset now, TimeSpan? ttl = null)
    {
        _pending = playing;
        _until = now + (ttl ?? DefaultTtl);
    }

    /// <summary>Drops the flip (rollback after a refused command).</summary>
    public void Clear() => _pending = null;

    /// <summary>Resolves the state to publish for the last source reading.</summary>
    /// <param name="actual">State reported by the source.</param>
    /// <param name="now">Current UTC time.</param>
    /// <returns>The state the island should paint.</returns>
    public bool Resolve(bool actual, DateTimeOffset now)
    {
        if (_pending is not { } optimistic)
        {
            return actual;
        }

        // The source caught up (or the flip expired): the truth wins and the overlay is gone.
        if (optimistic == actual || now > _until)
        {
            _pending = null;
            return actual;
        }

        return optimistic;
    }
}
