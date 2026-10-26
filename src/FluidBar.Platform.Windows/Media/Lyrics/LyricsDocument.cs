using System.Collections.Immutable;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>One word/character span inside a line (KuGou KRC <c>&lt;offset,length,0&gt;</c> tag).</summary>
/// <param name="OffsetMs">Start of the span relative to the line start.</param>
/// <param name="Length">Number of characters the span covers.</param>
/// <param name="Text">The span text (same characters the line carries at that position).</param>
public sealed record LyricWord(int OffsetMs, int Length, string Text);

/// <summary>One parsed lyric line (time is absolute from track start).</summary>
/// <param name="Time">Line start time.</param>
/// <param name="Text">Lyric text (already trimmed).</param>
/// <param name="Translation">Optional translation line (NetEase <c>tlyric</c>).</param>
/// <param name="Words">Per-word timing from the lyric source (empty = whole-line only).</param>
public sealed record LyricsLine(
    TimeSpan Time,
    string Text,
    string? Translation = null,
    System.Collections.Immutable.ImmutableArray<LyricWord> Words = default)
{
    /// <summary>True when the line carries a usable per-word timeline.</summary>
    public bool HasWordTiming => !Words.IsDefaultOrEmpty && Words.Length > 0 && Text.Length > 0;
}

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

/// <summary>
/// Karaoke sweep for a timed line: how much of the line has been sung at a playback position.
/// </summary>
/// <remarks>
/// The KRC word timeline gives each span its start offset and duration; inside a span the sweep is
/// interpolated by elapsed time, so the highlight advances smoothly instead of hopping word to word.
/// A line without word timing returns <see cref="NoWordTiming"/> so callers paint the whole line.
/// </remarks>
public static class LyricKaraoke
{
    /// <summary>Returned when the line has no per-word timeline (callers draw a plain line).</summary>
    public const double NoWordTiming = -1;

    /// <summary>Fraction of the line that has been sung at <paramref name="position"/> (0..1, or -1).</summary>
    /// <param name="line">Timed line (may carry word spans).</param>
    /// <param name="position">Playback position.</param>
    public static double Progress(LyricsLine? line, TimeSpan position)
    {
        if (line is null || !line.HasWordTiming)
        {
            return NoWordTiming;
        }

        var elapsedMs = (position - line.Time).TotalMilliseconds;
        if (elapsedMs <= 0)
        {
            return 0;
        }

        var words = line.Words;
        var totalChars = 0;
        foreach (var word in words)
        {
            totalChars += Math.Max(0, word.Length);
        }

        if (totalChars <= 0)
        {
            return NoWordTiming;
        }

        // Walk the spans in order; the first span that still has time left decides the sweep. Spans can
        // arrive slightly out of order in real files, so the next span's offset is the effective end.
        var consumed = 0;
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            var length = Math.Max(0, word.Length);
            var startMs = Math.Max(0, word.OffsetMs);
            var endMs = index + 1 < words.Length
                ? Math.Max(startMs, words[index + 1].OffsetMs)
                : Math.Max(startMs, (long)Math.Round(TotalLineMs(line)));

            if (elapsedMs >= endMs)
            {
                consumed += length;
                continue;
            }

            if (elapsedMs <= startMs)
            {
                break;
            }

            var span = Math.Max(1.0, endMs - startMs);
            var within = Math.Clamp((elapsedMs - startMs) / span, 0, 1);
            return Math.Clamp((consumed + (within * length)) / totalChars, 0, 1);
        }

        return Math.Clamp((double)consumed / totalChars, 0, 1);
    }

    private static double TotalLineMs(LyricsLine line)
    {
        var last = 0;
        foreach (var word in line.Words)
        {
            last = Math.Max(last, Math.Max(0, word.OffsetMs));
        }

        // The last span is the line's own duration in KRC; approximate it with the span start plus a
        // sane syllable duration when the file's line duration is not available here.
        return last + 400;
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

    /// <summary>Whether this provider reads files the player already downloaded (cheap enough to re-probe on every request).</summary>
    bool IsLocalCache => false;

    /// <summary>Fetches lyrics or null when the track has none. Must be cancellable and never block the caller.</summary>
    Task<LyricsDocument?> FetchAsync(LyricsQuery query, CancellationToken cancellationToken);
}