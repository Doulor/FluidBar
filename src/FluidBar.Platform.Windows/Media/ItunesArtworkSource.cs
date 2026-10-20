using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FluidBar.Platform.Windows.Media;

/// <summary>A cover found by an artwork source.</summary>
/// <param name="Bytes">Encoded image bytes.</param>
/// <param name="SourceUrl">URL the bytes came from.</param>
/// <param name="Score">Match confidence (0..1) from <see cref="ArtworkTitleMatcher"/>.</param>
/// <param name="Artist">Artist reported by the source.</param>
/// <param name="Title">Title reported by the source.</param>
/// <param name="Album">Album reported by the source.</param>
public sealed record ArtworkCandidate(byte[] Bytes, string SourceUrl, double Score, string Artist, string Title,
    string Album);

/// <summary>
/// iTunes Search API artwork source: no key, fast, and it reports both the track and the artist, which is
/// what the confidence check needs to reject covers of other songs.
/// </summary>
public sealed class ItunesArtworkSource : IArtworkSource
{
    private const int MaxResults = 8;
    private const int MaxArtworkBytes = ArtworkDiskCache.MaxArtworkBytes;

    private readonly HttpClient _http;
    private readonly double _threshold;

    /// <summary>Creates the source.</summary>
    /// <param name="http">HTTP client (injected by the service graph and by tests).</param>
    /// <param name="threshold">Acceptance threshold for <see cref="ArtworkTitleMatcher"/>.</param>
    public ItunesArtworkSource(HttpClient http, double threshold = ArtworkTitleMatcher.DefaultThreshold)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _threshold = threshold;
    }

    /// <inheritdoc />
    public string Name => "itunes";

    /// <inheritdoc />
    public async Task<ArtworkSourceResult> FindAsync(string? artist, string? title, string? album,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.NoResults);
        }

        // Search with the primary artist (multi-artist credits return nothing) but validate against the
        // whole credit string, so a featured artist does not invalidate a correct cover.
        var primary = ArtworkTitleMatcher.PrimaryArtist(artist);
        var term = primary.Length == 0 ? title : primary + " " + title;
        var url = "https://itunes.apple.com/search?entity=song&limit=" + MaxResults
            + "&term=" + Uri.EscapeDataString(term);
        SearchResponse? response;
        try
        {
            response = await _http.GetFromJsonAsync<SearchResponse>(url, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.NetworkError);
        }

        if (response?.Results is not { Count: > 0 } results)
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.NoResults);
        }

        Candidate? best = null;
        var bestScore = 0.0;
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.ArtworkUrl100))
            {
                continue;
            }

            var score = ArtworkTitleMatcher.Score(artist, title, result.ArtistName, result.TrackName);
            bestScore = Math.Max(bestScore, score);
            if (score < _threshold || (best is not null && score <= best.Score))
            {
                continue;
            }

            best = new Candidate(result, score);
        }

        if (best is null)
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.BelowThreshold, bestScore);
        }

        var artworkUrl = UpgradeArtworkUrl(best.Result.ArtworkUrl100!);
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

        return new ArtworkSourceResult(new ArtworkCandidate(bytes, artworkUrl, best.Score,
            best.Result.ArtistName ?? string.Empty,
            best.Result.TrackName ?? string.Empty,
            best.Result.CollectionName ?? string.Empty), ArtworkFetchFailure.None, best.Score);
    }

    /// <summary>Swaps the 100 px thumb for the 600 px variant of the same asset.</summary>
    /// <param name="url">Artwork URL from the search result.</param>
    public static string UpgradeArtworkUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        return url.Contains("100x100bb", StringComparison.Ordinal)
            ? url.Replace("100x100bb", "600x600bb", StringComparison.Ordinal)
            : url;
    }

    private sealed record Candidate(ItunesResult Result, double Score);

    private sealed class SearchResponse
    {
        [JsonPropertyName("results")]
        public List<ItunesResult>? Results { get; init; }
    }

    private sealed class ItunesResult
    {
        [JsonPropertyName("trackName")]
        public string? TrackName { get; init; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; init; }

        [JsonPropertyName("collectionName")]
        public string? CollectionName { get; init; }

        [JsonPropertyName("artworkUrl100")]
        public string? ArtworkUrl100 { get; init; }
    }
}
