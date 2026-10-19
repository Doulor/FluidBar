using System.Collections.Immutable;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>One parsed lyric line (time is absolute from track start).</summary>
/// <param name="Time">Line start time.</param>
/// <param name="Text">Lyric text (already trimmed).</param>
/// <param name="Translation">Optional translation line (NetEase <c>tlyric</c>).</param>
public sealed record LyricsLine(TimeSpan Time, string Text, string? Translation = null);

/// <summary>Immutable parsed lyrics document.</summary>
/// <param name="Title">Track title reported by the lyric source (may be empty).</param>
/// <param name="Artist">Artist reported by the lyric source (may be empty).</param>
/// <param name="Source">Provider that produced the document.</param>
/// <param name="Lines">Timed lines, sorted by time.</param>
/// <param name="Duration">
/// Track length implied by the source (end of the last timed line, or the length reported by the
/// search result). Zero when the source carries none — the island then keeps hiding the seek row
/// instead of drawing a made-up duration.
/// </param>
public sealed record LyricsDocument(
    string Title,
    string Artist,
    string Source,
    ImmutableArray<LyricsLine> Lines,
    TimeSpan Duration = default)
{
    public static readonly LyricsDocument Empty = new(string.Empty, string.Empty, "none", ImmutableArray<LyricsLine>.Empty);

    public bool IsEmpty => Lines.IsEmpty;

    /// <summary>Index of the line active at <paramref name="position"/>; -1 before the first line.</summary>
    public int FindLineIndex(TimeSpan position)
    {
        if (Lines.IsEmpty)
        {
            return -1;
        }

        var low = 0;
        var high = Lines.Length - 1;
        var result = -1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (Lines[mid].Time <= position)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }

    /// <summary>Active line and its translation at a position, or null before the first line.</summary>
    public LyricsLine? LineAt(TimeSpan position)
    {
        var index = FindLineIndex(position);
        return index < 0 ? null : Lines[index];
    }

    /// <summary>Next line after the active one (for the secondary line in the expanded panel).</summary>
    public LyricsLine? NextLine(TimeSpan position)
    {
        var index = FindLineIndex(position) + 1;
        return index < Lines.Length ? Lines[index] : null;
    }
}

/// <summary>Query passed to lyric providers.</summary>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist.</param>
/// <param name="Duration">Track duration (helps disambiguate search results).</param>
/// <param name="TrackKey">Stable key for caches/cancellation (source aumid | title | artist | album).</param>
public sealed record LyricsQuery(string Title, string Artist, TimeSpan Duration, string TrackKey)
{
    /// <summary>True when there is enough metadata to look lyrics up.</summary>
    public bool IsSearchable => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Artist);

    /// <summary>Search phrase used by online providers.</summary>
    public string BuildSearchKeyword()
        => string.IsNullOrWhiteSpace(Artist) ? Title.Trim() : $"{Title.Trim()} {Artist.Trim()}";
}

/// <summary>Async lyric provider contract (online providers live behind this interface).</summary>
public interface ILyricsProvider
{
    /// <summary>Provider name for logs/cache metadata.</summary>
    string Name { get; }

    /// <summary>Fetches lyrics or null when the track has none. Must be cancellable and never block the caller.</summary>
    Task<LyricsDocument?> FetchAsync(LyricsQuery query, CancellationToken cancellationToken);
}