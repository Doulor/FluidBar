using System.Collections.Immutable;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>Current and upcoming lyric line for a playback position.</summary>
/// <param name="Current">Line being sung right now, or null.</param>
/// <param name="Next">Following line (the secondary label), or null at the end of the track.</param>
public readonly record struct LyricPair(string? Current, string? Next)
{
    /// <summary>True when neither line is available.</summary>
    public bool IsEmpty => Current is null && Next is null;
}

/// <summary>
/// Picks the lyric line to highlight for a playback position.
/// </summary>
/// <remarks>
/// <para>Lyric files routinely start with timed credit lines ("作词: X", "Composer: X") that are not
/// sung; painting them made the island look like it was showing the wrong song, so credit lines are
/// skipped in both directions before a line is selected.</para>
/// <para>Pure by design: the media page calls this on every frame while a track is playing.</para>
/// </remarks>
public static class LyricsTimelineSelector
{
    /// <summary>How long after a line finishes the next one takes over (the singing restarts there).</summary>
    private static readonly TimeSpan HandOverWindow = TimeSpan.FromMilliseconds(1200);

    /// <summary>Selects the current and next line at <paramref name="position"/>.</summary>
    public static LyricPair Select(LyricsDocument? document, TimeSpan position)
        => Select(document, position, null, null);

    /// <summary>
    /// Current line, next line and the karaoke sweep of the current line (0..1; negative when the line
    /// carries no per-word timing).
    /// </summary>
    /// <param name="document">Parsed lyrics.</param>
    /// <param name="position">Playback position.</param>
    /// <param name="title">Track title (rejects "title - artist" header lines).</param>
    /// <param name="artist">Track artist.</param>
    public static LyricSelection SelectDetailed(LyricsDocument? document, TimeSpan position, string? title,
        string? artist)
    {
        if (document is null || document.IsEmpty)
        {
            return default;
        }

        var lines = document.Lines;
        var index = document.FindLineIndex(position);
        var currentIndex = FindLine(lines, index, forward: false, title, artist);
        if (currentIndex < 0)
        {
            var first = FindLine(lines, index + 1, forward: true, title, artist);
            return first < 0
                ? default
                : new LyricSelection(null, lines[first].Text, LyricKaraoke.NoWordTiming);
        }

        var current = lines[currentIndex];
        // Lyric files leave a gap between lines: holding the finished line until the next timestamp hid
        // the first words of every next line (user report 2026-10-21). Hand over as soon as the sweep
        // completes, so the next line is already fading in when the singing starts.
        if (LyricKaraoke.Progress(current, position) >= 1)
        {
            var following = FindLine(lines, currentIndex + 1, forward: true, title, artist);
            if (following >= 0 && lines[following].Time - position <= HandOverWindow)
            {
                currentIndex = following;
                current = lines[currentIndex];
            }
        }

        var nextIndex = FindLine(lines, currentIndex + 1, forward: true, title, artist);
        var next = nextIndex < 0 ? null : lines[nextIndex].Text;
        if (next is not null && string.Equals(next.Trim(), current.Text.Trim(), StringComparison.Ordinal))
        {
            next = null;
        }

        return new LyricSelection(
            current.Text,
            next,
            LyricKaraoke.Progress(current, position),
            current);
    }

    /// <summary>
    /// Selects the current and next line, ignoring lines that only repeat the track identity.
    /// </summary>
    /// <param name="document">Parsed lyrics.</param>
    /// <param name="position">Playback position.</param>
    /// <param name="title">Track title (used to reject "title - artist" header lines).</param>
    /// <param name="artist">Track artist (used together with <paramref name="title"/>).</param>
    public static LyricPair Select(LyricsDocument? document, TimeSpan position, string? title, string? artist)
    {
        if (document is null || document.IsEmpty)
        {
            return default;
        }

        var lines = document.Lines;
        var index = document.FindLineIndex(position);

        var currentIndex = FindLine(lines, index, forward: false, title, artist);
        if (currentIndex < 0)
        {
            // Before the first sung line: still offer the first line as "next" so the card is not empty.
            var first = FindLine(lines, index + 1, forward: true, title, artist);
            return first < 0 ? default : new LyricPair(null, lines[first].Text);
        }

        var nextIndex = FindLine(lines, currentIndex + 1, forward: true, title, artist);
        var current = lines[currentIndex].Text;
        var next = nextIndex < 0 ? null : lines[nextIndex].Text;

        // Lyric files repeat a line for emphasis; showing the same text twice looks like a stuck card.
        if (next is not null && string.Equals(next.Trim(), current.Trim(), StringComparison.Ordinal))
        {
            next = null;
        }

        return new LyricPair(current, next);
    }

    /// <summary>Selected line pair plus the karaoke sweep of the current line.</summary>
    /// <param name="Current">Current line text.</param>
    /// <param name="Next">Next line text.</param>
    /// <param name="Highlight">Swept fraction 0..1; negative when the line has no word timing.</param>
    /// <param name="Line">
    /// The timed line the sweep belongs to (null when there is none). Callers use it to keep sweeping the
    /// line between position samples instead of only when a sample arrives.
    /// </param>
    public readonly record struct LyricSelection(string? Current, string? Next, double Highlight,
        LyricsLine? Line = null);

    /// <summary>Walks from <paramref name="start"/> towards the start/end until a real lyric line.</summary>
    public static int FindLine(ImmutableArray<LyricsLine> lines, int start, bool forward)
        => FindLine(lines, start, forward, null, null);

    /// <summary>Walks towards the start/end until a line that is neither a credit nor the track identity.</summary>
    public static int FindLine(ImmutableArray<LyricsLine> lines, int start, bool forward, string? title,
        string? artist)
    {
        if (lines.IsDefaultOrEmpty)
        {
            return -1;
        }

        if (forward)
        {
            for (var index = Math.Max(0, start); index < lines.Length; index++)
            {
                if (!IsCreditLine(lines[index].Text) && !IsTrackIdentityLine(lines[index].Text, title, artist))
                {
                    return index;
                }
            }

            return -1;
        }

        for (var index = Math.Min(start, lines.Length - 1); index >= 0; index--)
        {
            if (!IsCreditLine(lines[index].Text) && !IsTrackIdentityLine(lines[index].Text, title, artist))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// True when the line is just the track identity ("Whiplash - William Black"). Some catalogues store
    /// the header that way as a timed line; painting it looked like the island had the wrong lyric.
    /// </summary>
    public static bool IsTrackIdentityLine(string? text, string? title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var folded = FoldForCompare(text);
        var foldedTitle = FoldForCompare(title);
        var foldedArtist = FoldForCompare(artist);
        if (foldedTitle.Length == 0 || folded.Length < foldedTitle.Length)
        {
            return false;
        }

        if (foldedArtist.Length == 0)
        {
            return folded.Equals(foldedTitle, StringComparison.Ordinal);
        }

        return folded.Equals(foldedTitle + "-" + foldedArtist, StringComparison.Ordinal)
               || folded.Equals(foldedArtist + "-" + foldedTitle, StringComparison.Ordinal);
    }

    private static string FoldForCompare(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            builder.Append(character switch
            {
                >= '！' and <= '～' => (char)(character - 0xFEE0),
                '、' => ',',
                '。' => '.',
                _ => character,
            });
        }

        return builder.ToString().ToLowerInvariant();
    }

    /// <summary>Credit/metadata tokens that lyric catalogues append as timed lines (zh and en).</summary>
    private static readonly string[] CreditTokens =
    [
        "作词", "作曲", "编曲", "演唱", "原唱", "翻唱", "歌手", "制作人", "制作", "监制", "出品",
        "混音", "录音", "母带", "和声", "合音", "伴奏", "后期", "封面", "海报", "设计", "策划", "统筹",
        "发行", "版权", "词曲", "编配",
        "lyric", "lyrics", "composer", "music", "arranger", "producer", "vocals", "vocal",
        "artist", "mix", "mastering", "written", "words", "cover", "recorded", "recording",
        "engineer", "studio", "credits", "lrc",
    ];

    /// <summary>Separators that join several credit roles in one prefix ("混音/录音: ...").</summary>
    private static readonly char[] CreditSeparators = ['/', '、', '&', ',', ';', ' ', '·', '|', '-', '+'];

    /// <summary>Whether a lyric line is a credit/metadata line instead of sung text.</summary>
    public static bool IsCreditLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var trimmed = text.Trim();
        var colon = trimmed.IndexOfAny([':', '：']);
        if (colon <= 0 || colon > 24)
        {
            return false;
        }

        var prefix = trimmed[..colon].Trim().ToLowerInvariant();
        if (prefix.Length == 0)
        {
            return false;
        }

        // Roles are often concatenated ("混音/录音", "作词 & 作曲"), so match token by token.
        foreach (var token in prefix.Split(CreditSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = token.Trim();
            if (candidate.Length == 0)
            {
                continue;
            }

            foreach (var credit in CreditTokens)
            {
                if (candidate.StartsWith(credit, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}