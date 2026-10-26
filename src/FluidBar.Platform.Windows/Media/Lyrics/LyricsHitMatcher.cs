namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>
/// Decides whether a catalogue search hit really is the track that is playing.
/// </summary>
/// <remarks>
/// <para>
/// Both online providers used to take <c>songs[0]</c> / <c>info[0]</c> with no check at all. For
/// "Fade (English Version)" by Jeff Satur the island showed a lyric whose first line was "作词 : 小柯" — a Chinese
/// songwriter — i.e. a completely different song (report 2026-10-26). Whatever the search engine ranks first, an
/// accepted hit must agree on title <em>and</em> artist, or on the track length when the title carries a different
/// decoration (remix/edit suffixes).
/// </para>
/// </remarks>
internal static class LyricsHitMatcher
{
    /// <summary>Confidence required when only the text is compared.</summary>
    public const double Threshold = ArtworkTitleMatcher.DefaultThreshold;

    /// <summary>Lower confidence that still passes when the duration agrees as well.</summary>
    public const double DurationAssistedThreshold = 0.45;

    /// <summary>
    /// How far the hit's length may differ from the playing track. Generous because a player's own clock (KuGou
    /// reports its length through MSAA) is a whole second granular and rounded, while a different *edition* of the
    /// same song differs by tens of seconds.
    /// </summary>
    public const double DurationToleranceSeconds = 10;

    /// <summary>True when the hit may be used for this track.</summary>
    /// <param name="queryTitle">Title as reported by the player.</param>
    /// <param name="queryArtist">Artist as reported by the player.</param>
    /// <param name="queryDuration">Length of the playing track (0 when unknown).</param>
    /// <param name="hitTitle">Title reported by the catalogue.</param>
    /// <param name="hitArtist">Artist reported by the catalogue.</param>
    /// <param name="hitDuration">Length reported by the catalogue (0 when unknown).</param>
    public static bool Accept(string? queryTitle, string? queryArtist, TimeSpan queryDuration,
        string? hitTitle, string? hitArtist, TimeSpan hitDuration)
    {
        // A different edition keeps the artist and folds the title to a prefix, so the text score alone cannot tell
        // "Fade (English Version)" from "Fade" — the length can. When both lengths are known, disagreement is veto.
        if (queryDuration > TimeSpan.Zero && hitDuration > TimeSpan.Zero && !DurationAgrees(queryDuration, hitDuration))
        {
            return false;
        }

        var score = Score(queryTitle, queryArtist, hitTitle, hitArtist);
        return score >= Threshold
            || (score >= DurationAssistedThreshold && DurationAgrees(queryDuration, hitDuration));
    }

    /// <summary>Confidence that the hit is the same recording.</summary>
    /// <param name="queryTitle">Title as reported by the player.</param>
    /// <param name="queryArtist">Artist as reported by the player.</param>
    /// <param name="hitTitle">Title reported by the catalogue.</param>
    /// <param name="hitArtist">Artist reported by the catalogue.</param>
    public static double Score(string? queryTitle, string? queryArtist, string? hitTitle, string? hitArtist)
        => ArtworkTitleMatcher.Score(queryArtist, queryTitle, hitArtist, hitTitle);

    /// <summary>True when both lengths are known and within <see cref="DurationToleranceSeconds"/>.</summary>
    /// <param name="queryDuration">Length of the playing track.</param>
    /// <param name="hitDuration">Length reported by the catalogue.</param>
    public static bool DurationAgrees(TimeSpan queryDuration, TimeSpan hitDuration)
        => queryDuration > TimeSpan.Zero
           && hitDuration > TimeSpan.Zero
           && Math.Abs((queryDuration - hitDuration).TotalSeconds) <= DurationToleranceSeconds;
}
