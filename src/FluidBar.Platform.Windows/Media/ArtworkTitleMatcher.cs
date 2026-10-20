using System.Text;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Normalises track metadata and scores how well a candidate result denotes the same song.
/// </summary>
/// <remarks>
/// <para>The island only accepts a cover when the candidate really is the same track: a wrong cover looks
/// worse than the gradient placeholder, so every network result has to pass
/// <see cref="IsConfident"/> before its artwork is used.</para>
/// <para>Similarity combines artist equality (folded, decoration-free) with title overlap, so
/// "Dragonhearted (Remix)" can answer "Dragonhearted" while "Dragonhearted Piano Cover" by another
/// artist cannot.</para>
/// </remarks>
public static class ArtworkTitleMatcher
{
    /// <summary>Default acceptance threshold (0..1).</summary>
    public const double DefaultThreshold = 0.72;

    /// <summary>True when the candidate scores at or above the threshold.</summary>
    /// <param name="queryArtist">Artist from the player.</param>
    /// <param name="queryTitle">Title from the player.</param>
    /// <param name="candidateArtist">Artist reported by the catalogue.</param>
    /// <param name="candidateTitle">Title reported by the catalogue.</param>
    /// <param name="threshold">Acceptance threshold (use <see cref="DefaultThreshold"/>).</param>
    public static bool IsConfident(string? queryArtist, string? queryTitle, string? candidateArtist,
        string? candidateTitle, double threshold = DefaultThreshold)
        => Score(queryArtist, queryTitle, candidateArtist, candidateTitle) >= threshold;

    /// <summary>Similarity in 0..1 between the query and a catalogue result.</summary>
    /// <param name="queryArtist">Artist from the player.</param>
    /// <param name="queryTitle">Title from the player.</param>
    /// <param name="candidateArtist">Artist reported by the catalogue.</param>
    /// <param name="candidateTitle">Title reported by the catalogue.</param>
    public static double Score(string? queryArtist, string? queryTitle, string? candidateArtist,
        string? candidateTitle)
    {
        var artistScore = ArtistScore(queryArtist, candidateArtist);
        var titleScore = TitleScore(queryTitle, candidateTitle);
        if (titleScore <= 0 || artistScore <= 0)
        {
            return 0;
        }

        // The title is what the user reads, the artist is the guard against karaoke/cover versions.
        return (titleScore * 0.65) + (artistScore * 0.35);
    }

    /// <summary>Artist similarity: 1 identical, 0.85 containment, 0.7 shared-token overlap.</summary>
    private static double ArtistScore(string? query, string? candidate)
    {
        var a = Normalize(query);
        var b = Normalize(candidate);
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a.Equals(b, StringComparison.Ordinal))
        {
            return 1;
        }

        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            return 0.85;
        }

        var tokens = TokenScore(a, b);
        return tokens >= 0.5 ? 0.7 + (tokens * 0.2) : 0;
    }

    /// <summary>Title similarity: 1 identical, 0.9 containment, 0.6..0.9 token overlap.</summary>
    private static double TitleScore(string? query, string? candidate)
    {
        var a = Normalize(query);
        var b = Normalize(candidate);
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a.Equals(b, StringComparison.Ordinal))
        {
            return 1;
        }

        // "Dragonhearted" vs "Dragonhearted (Remix)": the catalogue version is a superset. Requiring the
        // shorter side to be most of the longer one keeps "Love" from answering "Love Story".
        var shorter = a.Length <= b.Length ? a : b;
        var longer = a.Length <= b.Length ? b : a;
        if (longer.Contains(shorter, StringComparison.Ordinal) && shorter.Length * 10 >= longer.Length * 6)
        {
            return 0.9;
        }

        return TokenScore(a, b);
    }

    /// <summary>Jaccard-style token overlap (0..1).</summary>
    private static double TokenScore(string a, string b)
    {
        var left = Tokens(a);
        var right = Tokens(b);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var shared = left.Intersect(right, StringComparer.Ordinal).Count();
        var union = left.Union(right, StringComparer.Ordinal).Count();
        return union == 0 ? 0 : (double)shared / union;
    }

    private static HashSet<string> Tokens(string value)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        // `、` (U+3001) and `；` are the CJK list separators KuGou uses between artists; the catalogues use
        // `&`. Splitting on all of them lets "Alan Walker、Ali Gatie" match "Alan Walker & Ali Gatie".
        foreach (var token in value.Split(
                     [' ', '-', '/', ',', '.', '(', ')', '[', ']', '&', '+', '\u3001', '\uFF0C', '\uFF06', '\uFF1B', ';'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length > 1 && !StopWords.Contains(token))
            {
                set.Add(token);
            }
        }

        return set;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal) { "feat", "ft", "the", "a", "and" };

    /// <summary>
    /// First artist of a multi-artist credit (KuGou writes "A、B、C"): searching the first name finds the
    /// release, while the full credit string usually returns nothing.
    /// </summary>
    /// <param name="artist">Artist credit.</param>
    public static string PrimaryArtist(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return string.Empty;
        }

        var cut = artist.IndexOfAny(['\u3001', ',', '&', ';', '/', '\uFF0C', '\uFF06']);
        return cut > 0 ? artist[..cut].Trim() : artist.Trim();
    }

    /// <summary>
    /// Folds metadata for comparison: lowercase, decorations/featuring credits removed, punctuation and
    /// whitespace collapsed (the player and the catalogues spell "（" vs "(", "feat." vs "ft.").
    /// </summary>
    /// <param name="value">Raw metadata.</param>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '(' or '（' or '[' or '【':
                    depth++;
                    continue;
                case ')' or '）' or ']' or '】':
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }

                    break;
            }

            if (depth > 0)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsWhiteSpace(c)
                     || c is '-' or '–' or '—' or '_' or '/' or '&'
                     || c is '、' or '，' or '；' or ';' or '＆')
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }
            }
        }

        return builder.ToString().Trim();
    }
}
