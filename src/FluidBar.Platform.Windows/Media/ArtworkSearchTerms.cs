using System.Text.RegularExpressions;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Cleans the window-title scrape into a usable cover search query.
/// </summary>
/// <remarks>
/// Fallback players expose "正在播放: 歌名 - 歌手" (or "Now playing: ..."), sometimes with a bracketed
/// suffix such as "[官方MV]" or "(Live)". Searching that verbatim is why the catalogue lookup returned
/// nothing for perfectly well-known tracks.
/// </remarks>
internal static partial class ArtworkSearchTerms
{
    /// <summary>Splits a raw window title into a title and an artist.</summary>
    public static (string Title, string Artist) Split(string? rawTitle, string? rawArtist)
    {
        var title = (rawTitle ?? string.Empty).Trim();
        var artist = (rawArtist ?? string.Empty).Trim();

        title = PrefixRegex().Replace(title, string.Empty).Trim();
        title = SuffixRegex().Replace(title, string.Empty).Trim();

        if (artist.Length == 0)
        {
            var separator = title.LastIndexOf(" - ", StringComparison.Ordinal);
            if (separator > 0)
            {
                artist = title[(separator + 3)..].Trim();
                title = title[..separator].Trim();
            }
        }

        return (title.Length == 0 ? (rawTitle ?? string.Empty).Trim() : title, artist);
    }

    [GeneratedRegex(@"^\s*(正在播放|正在播放\s*[:：]|Now\s+playing\s*[:：]?)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex PrefixRegex();

    [GeneratedRegex(@"\s*[\[(（【][^\])）】]*(mv|live|官方|伴奏|remix|ver\.?|version)[^\])）】]*[\])）】]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SuffixRegex();
}
