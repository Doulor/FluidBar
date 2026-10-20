using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Deezer Search API artwork source (second source). No key, one lookup per track, 500 px album covers.
/// </summary>
public sealed class DeezerArtworkSource : IArtworkSource
{
    private const int MaxResults = 8;
    private const int MaxArtworkBytes = ArtworkDiskCache.MaxArtworkBytes;

    private readonly HttpClient _http;
    private readonly double _threshold;

    /// <summary>Creates the source.</summary>
    /// <param name="http">HTTP client.</param>
    /// <param name="threshold">Acceptance threshold for <see cref="ArtworkTitleMatcher"/>.</param>
    public DeezerArtworkSource(HttpClient http, double threshold = ArtworkTitleMatcher.DefaultThreshold)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _threshold = threshold;
    }

    /// <inheritdoc />
    public string Name => "deezer";

    /// <inheritdoc />
    public async Task<ArtworkSourceResult> FindAsync(string? artist, string? title, string? album,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.NoResults);
        }

        var primary = ArtworkTitleMatcher.PrimaryArtist(artist);
        var query = primary.Length == 0 ? title : primary + " " + title;
        var url = "https://api.deezer.com/search?limit=" + MaxResults + "&q=" + Uri.EscapeDataString(query);
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

        if (response?.Data is not { Count: > 0 } results)
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.NoResults);
        }

        Track? best = null;
        var bestScore = 0.0;
        foreach (var track in results)
        {
            if (string.IsNullOrWhiteSpace(track.Album?.CoverBig))
            {
                continue;
            }

            var score = ArtworkTitleMatcher.Score(artist, title, track.Artist?.Name, track.Title);
            bestScore = Math.Max(bestScore, score);
            if (score < _threshold || (best is not null && score <= bestScore))
            {
                continue;
            }

            best = track;
        }

        if (best?.Album?.CoverBig is not { } coverUrl)
        {
            return new ArtworkSourceResult(null, ArtworkFetchFailure.BelowThreshold, bestScore);
        }

        byte[]? bytes;
        try
        {
            bytes = await _http.GetByteArrayAsync(coverUrl, cancellationToken).ConfigureAwait(false);
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

        var accepted = ArtworkTitleMatcher.Score(artist, title, best.Artist?.Name, best.Title);
        return new ArtworkSourceResult(new ArtworkCandidate(bytes, coverUrl, accepted,
            best.Artist?.Name ?? string.Empty, best.Title ?? string.Empty, best.Album?.Title ?? string.Empty),
            ArtworkFetchFailure.None, accepted);
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("data")]
        public List<Track>? Data { get; init; }
    }

    private sealed class Track
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("artist")]
        public Artist? Artist { get; init; }

        [JsonPropertyName("album")]
        public Album? Album { get; init; }
    }

    private sealed class Artist
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class Album
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("cover_big")]
        public string? CoverBig { get; init; }
    }
}
