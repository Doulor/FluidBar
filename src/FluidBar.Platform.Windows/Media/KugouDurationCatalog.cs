using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Resolves a track's length from KuGou's own catalogue, in milliseconds.
/// </summary>
/// <remarks>
/// <para>KuGou publishes no SMTC timeline, and the "mm:ss / mm:ss" text its accessibility tree exposes
/// truncates to whole seconds. The length attached to the FILE is exact, though, and the player already
/// leaves the key on disk: its lyric files are named
/// <c>&lt;artist&gt; - &lt;title&gt;-&lt;32 hex file hash&gt;-&lt;songId&gt;-00000000.krc</c>.</para>
/// <para>The lyric service answers that hash with the song's <c>duration</c> in milliseconds — the same
/// number the player puts in its own progress row. Measured 2026-10-22: 30 of 30 local lyric hashes
/// resolved, and the answer stayed within 1 s of the real audio length (file size ÷ bitrate) on every
/// sampled track. A keyword search is the fallback for tracks whose lyric file is not cached.</para>
/// <para>Do not derive the length from the lyric's last line: it is short by up to 20 s on tracks with an
/// instrumental outro and, on 145 of the 277 files that declare a total, the last span field holds the
/// whole song length, which makes start + span end at twice the real end.</para>
/// </remarks>
internal sealed partial class KugouDurationCatalog
{
    /// <summary>Lyric lookup by file hash; answers with the song's length in milliseconds.</summary>
    internal const string LyricSearchUrl =
        "https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword=&duration=&hash={0}";

    /// <summary>Catalogue search used when no lyric file is cached; answers with whole seconds.</summary>
    internal const string SongSearchUrl =
        "https://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={0}&page=1&pagesize=8&showtype=1";

    /// <summary>Anything shorter is a preview/ringtone, anything longer is a mis-matched candidate.</summary>
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromHours(1.5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, TimeSpan> _cache = new(StringComparer.OrdinalIgnoreCase);

    public KugouDurationCatalog(HttpClient httpClient)
        => HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    private HttpClient HttpClient { get; }

    /// <summary>A length resolved earlier in this session, or null.</summary>
    public TimeSpan? TryGetCached(string key)
        => _cache.TryGetValue(key, out var duration) ? duration : null;

    /// <summary>
    /// Resolves the length of a track: declared lyric total, then the lyric service (by file hash), then
    /// the catalogue search. Returns <see cref="TimeSpan.Zero"/> when every source fails.
    /// </summary>
    /// <param name="key">Cache key (the track key used by the media snapshot).</param>
    /// <param name="title">Track title as the player reports it.</param>
    /// <param name="artist">Track artist as the player reports it.</param>
    /// <param name="lyricPath">The player's lyric file for this track, when one is cached.</param>
    /// <param name="cancellationToken">Cancellation for the whole lookup.</param>
    public async Task<TimeSpan> ResolveAsync(string key, string title, string artist, string? lyricPath,
        CancellationToken cancellationToken)
    {
        if (TryGetCached(key) is { } cached)
        {
            return cached;
        }

        var resolved = TimeSpan.Zero;
        try
        {
            resolved = TryReadDeclaredTotal(lyricPath)
                       ?? await TryQueryLyricAsync(TryReadHash(lyricPath), title, cancellationToken)
                           .ConfigureAwait(false)
                       ?? await TryQuerySearchAsync(title, artist, cancellationToken).ConfigureAwait(false)
                       ?? TimeSpan.Zero;
        }
        catch (Exception)
        {
            // Offline, rate-limited or a schema change: the caller keeps its previous estimate.
        }

        if (resolved > TimeSpan.Zero)
        {
            if (_cache.Count >= 512 && !_cache.ContainsKey(key))
            {
                _cache.Clear();
            }

            _cache[key] = resolved;
        }

        return resolved;
    }

    /// <summary>Extracts the file hash KuGou puts in a lyric file name.</summary>
    public static string? TryReadHash(string? lyricPath)
    {
        if (string.IsNullOrEmpty(lyricPath))
        {
            return null;
        }

        var match = HashPattern().Match(lyricPath);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>Reads the <c>[total:ms]</c> header of a local lyric file (no network).</summary>
    internal static TimeSpan? TryReadDeclaredTotal(string? lyricPath)
    {
        if (string.IsNullOrEmpty(lyricPath))
        {
            return null;
        }

        try
        {
            var decoded = Lyrics.KrcDecoder.TryDecode(File.ReadAllBytes(lyricPath));
            return decoded is not null && decoded.DeclaredTotal > TimeSpan.Zero ? decoded.DeclaredTotal : null;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    /// <summary>Parses a lyric-search response, rejecting candidates that name another song.</summary>
    internal static TimeSpan? TryParseLyricDuration(string? json, string title)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var candidate in candidates.EnumerateArray())
            {
                if (!candidate.TryGetProperty("duration", out var durationElement)
                    || !durationElement.TryGetInt32(out var milliseconds)
                    || !IsPlausible(milliseconds))
                {
                    continue;
                }

                // Without a lyric for this hash the service still answers — with an unrelated song
                // (measured: the KuGou track resolved to "The Seasons Op. 37b"). Never accept that.
                var song = candidate.TryGetProperty("song", out var songElement)
                    ? songElement.GetString()
                    : null;
                if (!NamesMatch(song, title))
                {
                    continue;
                }

                return TimeSpan.FromMilliseconds(milliseconds);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>Parses a catalogue search response, scoring song and artist matches.</summary>
    internal static TimeSpan? TryParseSearchDuration(string? json, string title, string artist)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("info", out var info)
                || info.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var wantedTitle = Normalize(title);
            var wantedArtist = Normalize(artist);
            var bestScore = 0;
            var best = TimeSpan.Zero;
            foreach (var item in info.EnumerateArray())
            {
                if (!item.TryGetProperty("duration", out var durationElement)
                    || !durationElement.TryGetInt32(out var seconds)
                    || seconds <= 0
                    || !IsPlausible(seconds * 1000))
                {
                    continue;
                }

                var song = item.TryGetProperty("songname", out var songElement) ? songElement.GetString() : null;
                if (!NamesMatch(song, title))
                {
                    continue;
                }

                var score = 1;
                if (Normalize(song) == wantedTitle && wantedTitle.Length > 0)
                {
                    score += 1;
                }

                var singer = item.TryGetProperty("singername", out var singerElement) ? singerElement.GetString() : null;
                if (wantedArtist.Length > 0 && Normalize(singer).Contains(wantedArtist, StringComparison.Ordinal))
                {
                    score += 1;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = TimeSpan.FromSeconds(seconds);
                }
            }

            return bestScore > 0 ? best : null;
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private async Task<TimeSpan?> TryQueryLyricAsync(string? hash, string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return null;
        }

        var url = string.Format(CultureInfo.InvariantCulture, LyricSearchUrl, Uri.EscapeDataString(hash));
        return TryParseLyricDuration(await GetStringAsync(url, cancellationToken).ConfigureAwait(false), title);
    }

    private async Task<TimeSpan?> TryQuerySearchAsync(string title, string artist, CancellationToken cancellationToken)
    {
        var keyword = string.Join(' ', new[] { artist, title }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (keyword.Length == 0)
        {
            return null;
        }

        var url = string.Format(CultureInfo.InvariantCulture, SongSearchUrl, Uri.EscapeDataString(keyword));
        return TryParseSearchDuration(await GetStringAsync(url, cancellationToken).ConfigureAwait(false), title, artist);
    }

    private async Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            return await HttpClient.GetStringAsync(url, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsPlausible(int milliseconds)
        => milliseconds >= MinimumDuration.TotalMilliseconds && milliseconds <= MaximumDuration.TotalMilliseconds;

    /// <summary>
    /// Whether a catalogue name refers to the same song. CJK titles are often decorated with version
    /// suffixes ("(DJ版)", "(女生版)"), so containment in either direction counts.
    /// </summary>
    private static bool NamesMatch(string? candidate, string title)
    {
        var left = Normalize(candidate);
        var right = Normalize(title);
        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        return left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"-([0-9a-fA-F]{32})-\d+-\d+\.krc$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
