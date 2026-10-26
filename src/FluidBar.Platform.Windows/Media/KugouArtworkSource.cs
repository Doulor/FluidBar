using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// KuGou catalogue artwork source (highest priority for users whose player is KuGou).
/// </summary>
/// <remarks>
/// <para>
/// iTunes and Deezer are unreachable from mainland China — `itunes.apple.com` resolves to no address and
/// `api.deezer.com` refuses the connection — so for a KuGou user the two incumbent sources can never answer and
/// every track without a player-cached cover fell back to the gradient placeholder (report 2026-10-26). KuGou's
/// own endpooint answers in ~0.3 s with one unauthenticated GET and carries the track name, the artist and the
/// cover URL, which is exactly what <see cref="ArtworkTitleMatcher"/> needs.
/// </para>
/// <para>
/// The returned URL carries a `{size}` placeholder; replacing it with <c>400</c> yields a ~47 KB cover, small
/// enough for the cache budget and sharp enough for the island.
/// </para>
/// </remarks>
public sealed class KugouArtworkSource : IArtworkSource
{
    private const int MaxResults = 8;
    private const int MaxArtworkBytes = ArtworkDiskCache.MaxArtworkBytes;
    private const string SizePlaceholder = "{size}";
    private const string CoverSize = "400";

    private readonly HttpClient _http;
    private readonly double _threshold;

    /// <summary>Creates the source.</summary>
    /// <param name="http">HTTP client (injected by the service graph and by tests).</param>
    /// <param name="threshold">Acceptance threshold for <see cref="ArtworkTitleMatcher"/>.</param>
    public KugouArtworkSource(HttpClient http, double threshold = ArtworkTitleMatcher.DefaultThreshold)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _threshold = threshold;
    }

    /// <inheritdoc />
    public string Name => "kugou";

    /// <inheritdoc />
    public async Task<ArtworkSourceResult> FindAsync(string? artist, string? title, string? album,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.NoResults);
        }

        // Same query shape as the other sources: the primary artist only (multi-artist credits return nothing),
        // validated against the full credit string.
        var primary = ArtworkTitleMatcher.PrimaryArtist(artist);
        var query = primary.Length == 0 ? title : primary + " " + title;
        var url = "https://songsearch.kugou.com/song_search_v2?platform=WebFilter&page=1&pagesize=" + MaxResults
            + "&keyword=" + Uri.EscapeDataString(query);

        // The endpoint answers with `Content-Type: text/plain`, so GetFromJsonAsync (which insists on a JSON
        // media type) refuses the payload even though it is valid JSON — that silent refusal is why the first
        // wiring of this source produced nothing at all. Read the body and parse it ourselves.
        string payload;
        try
        {
            payload = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.NetworkError);
        }

        SearchResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<SearchResponse>(payload);
        }
        catch (JsonException)
        {
            // A block page or an error document is a network-level failure, not an empty catalogue.
            return new ArtworkSourceResult(null, ArtworkFetchFailure.NetworkError);
        }

        if (response?.Data?.Lists is not { Count: > 0 } hits)
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.NoResults);
        }

        Hit? best = null;
        var bestScore = 0.0;
        var bestIsPortrait = false;
        var topScore = 0.0;
        foreach (var hit in hits)
        {
            if (CoverUrl(hit.Image) is not { } cover)
            {
                continue;
            }

            var score = ArtworkTitleMatcher.Score(artist, title, hit.SingerName, hit.SongName);
            topScore = Math.Max(topScore, score);
            if (score < _threshold)
            {
                continue;
            }

            // KuGou answers with the singer portrait ("softhead") for tracks it has no album art for. A real
            // cover always wins; the portrait is only used when no hit carries album art.
            var portrait = cover.Contains("softhead", StringComparison.OrdinalIgnoreCase);
            var better = best is null
                || (!portrait && bestIsPortrait)
                || (portrait == bestIsPortrait && score > bestScore);
            if (!better)
            {
                continue;
            }

            best = hit;
            bestScore = score;
            bestIsPortrait = portrait;
        }

        if (best is null)
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.BelowThreshold, topScore);
        }

        var artworkUrl = CoverUrl(best.Image)!;
        byte[]? bytes;
        try
        {
            bytes = await _http.GetByteArrayAsync(artworkUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.NetworkError, bestScore);
        }

        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxArtworkBytes
            || ImageHeaderReader.TryReadDimensions(bytes) is null)
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.DecodeFailed, bestScore);
        }

        return new ArtworkSourceResult(new ArtworkCandidate(bytes, artworkUrl, bestScore,
            best.SingerName ?? string.Empty, best.SongName ?? string.Empty, best.AlbumName ?? string.Empty),
            ArtworkFetchFailure.None, bestScore);
    }

    /// <summary>Resolves the `{size}` placeholder of a KuGou cover URL (null when unusable).</summary>
    /// <param name="image">Cover URL reported by the catalogue.</param>
    public static string? CoverUrl(string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return null;
        }

        var url = image.Contains(SizePlaceholder, StringComparison.Ordinal)
            ? image.Replace(SizePlaceholder, CoverSize, StringComparison.Ordinal)
            : image;
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : null;
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("data")]
        public Payload? Data { get; init; }
    }

    private sealed class Payload
    {
        [JsonPropertyName("lists")]
        public List<Hit>? Lists { get; init; }
    }

    private sealed class Hit
    {
        [JsonPropertyName("SongName")]
        public string? SongName { get; init; }

        [JsonPropertyName("SingerName")]
        public string? SingerName { get; init; }

        [JsonPropertyName("AlbumName")]
        public string? AlbumName { get; init; }

        [JsonPropertyName("Image")]
        public string? Image { get; init; }
    }
}
