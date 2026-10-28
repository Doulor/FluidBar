namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Recognises a player's <em>generic</em> cover: an image it hands out for many different tracks.
/// </summary>
/// <remarks>
/// <para>
/// KuGou serves at least three "no cover" images in turn: a flat grey 120×120 tile (caught by
/// <see cref="ArtworkQualityInspector"/>), a mid-size logo tile (~18 KB) and — for tracks it has no art for at all —
/// whichever of those it happens to have ready. The flat tile is recognisable by its statistics; the others look like
/// real photos, so the only reliable signal is that <b>the same bytes show up for a different track</b>.
/// </para>
/// <para>
/// Measured 2026-10-28: the 18 KB image was accepted as the cover of "妈妈的话" while the player's real cover (27 KB,
/// 97 colours) only appeared a minute later — and nothing ever re-fetched it, so the island kept the generic image.
/// </para>
/// </remarks>
internal sealed class ArtworkThumbprintRegistry
{
    /// <summary>How many thumbprints to remember (bounded: a handful of tracks worth).</summary>
    public const int DefaultCapacity = 64;

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly int _capacity;

    /// <summary>Creates the registry.</summary>
    /// <param name="capacity">Number of thumbprints to remember.</param>
    public ArtworkThumbprintRegistry(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>Number of remembered thumbprints (diagnostics/tests).</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _owners.Count;
            }
        }
    }

    /// <summary>
    /// Records the thumbprint for a track and reports whether the same bytes were already used by a different track.
    /// </summary>
    /// <param name="thumbprint">Content thumbprint (see <see cref="Thumbprint"/>).</param>
    /// <param name="trackKey">Track the image arrived with.</param>
    /// <param name="otherTrackKey">The other track that used the same bytes, when it is generic.</param>
    public bool IsGeneric(string thumbprint, string trackKey, out string? otherTrackKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackKey);
        lock (_gate)
        {
            if (_owners.TryGetValue(thumbprint, out var owner)
                && !string.Equals(owner, trackKey, StringComparison.Ordinal))
            {
                otherTrackKey = owner;
                return true;
            }

            otherTrackKey = null;
            if (_owners.ContainsKey(thumbprint))
            {
                return false;
            }

            _owners[thumbprint] = trackKey;
            _order.Enqueue(thumbprint);
            while (_order.Count > _capacity)
            {
                _owners.Remove(_order.Dequeue());
            }

            return false;
        }
    }

    /// <summary>Cheap content thumbprint: length plus FNV-1a over every byte.</summary>
    /// <param name="bytes">Encoded image bytes.</param>
    public static string Thumbprint(ReadOnlySpan<byte> bytes)
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;
        var hash = Offset;
        foreach (var value in bytes)
        {
            hash = unchecked((hash ^ value) * Prime);
        }

        return bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }
}
