using System.Globalization;
using System.Text.Json;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>Shared HTTP behaviour for online lyric providers: injected <see cref="HttpClient"/>,
/// response-size cap and cancellable async-only requests (no synchronous HTTP anywhere).</summary>
public abstract class HttpLyricsProvider : ILyricsProvider
{
    /// <summary>Maximum accepted response size (lyrics payloads are small; protects memory).</summary>
    protected const int MaxResponseBytes = 512 * 1024;

    protected HttpLyricsProvider(HttpClient httpClient, IPlatformLogger logger)
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Logger = logger ?? NullPlatformLogger.Instance;
    }

    protected HttpClient HttpClient { get; }

    protected IPlatformLogger Logger { get; }

    public abstract string Name { get; }

    public abstract Task<LyricsDocument?> FetchAsync(LyricsQuery query, CancellationToken cancellationToken);

    /// <summary>GETs a JSON string with a hard size cap and caller cancellation.</summary>
    protected async Task<string?> TryGetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await HttpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogDebug(PlatformLogCategory.Media, $"{Name}.http-status",
                $"Lyrics provider '{Name}' returned HTTP {(int)response.StatusCode}.");
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaxResponseBytes)
            {
                Logger.LogWarning(PlatformLogCategory.Media, $"{Name}.response-too-large",
                    $"Lyrics provider '{Name}' response exceeded {MaxResponseBytes} bytes; discarded.");
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}

/// <summary>NetEase Cloud Music lyric provider (search then lyric API).</summary>
public sealed class NeteaseLyricsProvider : HttpLyricsProvider
{
    private const string SearchUrl = "https://music.163.com/api/search/get?s={0}&type=1&limit=5&offset=0";
    private const string LyricUrl = "https://music.163.com/api/song/lyric?id={0}&lv=1&tv=1";

    public NeteaseLyricsProvider(HttpClient httpClient, IPlatformLogger logger)
        : base(httpClient, logger)
    {
    }

    public override string Name => "netease";

    public override async Task<LyricsDocument?> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        var keyword = Uri.EscapeDataString(query.BuildSearchKeyword());
        var searchJson = await TryGetStringAsync(SearchUrl.Replace("{0}", keyword, StringComparison.Ordinal),
            cancellationToken).ConfigureAwait(false);
        if (searchJson is null || !TryParseNeteaseSongId(searchJson, query, out var songId))
        {
            return null;
        }

        var lyricJson = await TryGetStringAsync(
            LyricUrl.Replace("{0}", songId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
            cancellationToken).ConfigureAwait(false);
        return lyricJson is null ? null : ParseNeteaseLyrics(lyricJson);
    }

    /// <summary>Picks the id of the hit that really is this track (null when no hit qualifies).</summary>
    /// <param name="json">NetEase search response.</param>
    /// <param name="query">Track being looked up.</param>
    /// <param name="songId">Accepted song id.</param>
    internal static bool TryParseNeteaseSongId(string json, LyricsQuery query, out long songId)
    {
        songId = 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("result", out var result)
                || !result.TryGetProperty("songs", out var songs)
                || songs.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var bestScore = double.MinValue;
            foreach (var song in songs.EnumerateArray())
            {
                if (!song.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
                {
                    continue;
                }

                var name = song.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                var artists = song.TryGetProperty("artists", out var artistArray)
                    && artistArray.ValueKind == JsonValueKind.Array
                        ? string.Join('/', artistArray.EnumerateArray()
                            .Select(artist => artist.TryGetProperty("name", out var artistName)
                                ? artistName.GetString()
                                : null)
                            .Where(artistName => !string.IsNullOrWhiteSpace(artistName)))
                        : null;
                var hitDuration = song.TryGetProperty("duration", out var durationElement)
                    && durationElement.ValueKind == JsonValueKind.Number
                    && durationElement.TryGetInt64(out var milliseconds)
                        ? TimeSpan.FromMilliseconds(milliseconds)
                        : TimeSpan.Zero;

                if (!LyricsHitMatcher.Accept(query.Title, query.Artist, query.Duration, name, artists, hitDuration))
                {
                    continue;
                }

                var score = LyricsHitMatcher.Score(query.Title, query.Artist, name, artists);
                if (score > bestScore)
                {
                    bestScore = score;
                    songId = id;
                }
            }

            return songId != 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Parses the NetEase lyric response (LRC + optional translation).</summary>
    internal static LyricsDocument? ParseNeteaseLyrics(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var main = TryReadLrc(document.RootElement, "lrc");
            if (main is null || main.IsEmpty)
            {
                return null;
            }

            var translation = TryReadLrc(document.RootElement, "tlyric");
            var merged = LrcParser.MergeTranslations(main, translation);
            return merged with { Source = "netease" };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LyricsDocument? TryReadLrc(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("lyric", out var lyric)
            || lyric.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return LrcParser.Parse(lyric.GetString(), "netease");
    }
}

/// <summary>Kugou lyric provider (search → candidate → download base64 LRC).</summary>
public sealed class KugouLyricsProvider : HttpLyricsProvider
{
    private const string SearchUrl =
        "https://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={0}&page=1&pagesize=5";
    private const string CandidateUrl =
        "https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword=&duration={0}&hash={1}";
    private const string DownloadUrl =
        "https://lyrics.kugou.com/download?ver=1&client=pc&id={0}&accesskey={1}&fmt=lrc&charset=utf8";

    public KugouLyricsProvider(HttpClient httpClient, IPlatformLogger logger)
        : base(httpClient, logger)
    {
    }

    public override string Name => "kugou";

    public override async Task<LyricsDocument?> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        var keyword = Uri.EscapeDataString(query.BuildSearchKeyword());
        var searchJson = await TryGetStringAsync(SearchUrl.Replace("{0}", keyword, StringComparison.Ordinal),
            cancellationToken).ConfigureAwait(false);
        if (searchJson is null || !TryParseKugouSong(searchJson, query, out var hash, out var durationSeconds))
        {
            return null;
        }

        var duration = (int)query.Duration.TotalSeconds > 0
            ? (int)query.Duration.TotalSeconds
            : durationSeconds;
        var candidateUrl = CandidateUrl
            .Replace("{0}", Math.Max(0, duration).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{1}", Uri.EscapeDataString(hash), StringComparison.Ordinal);
        var candidateJson = await TryGetStringAsync(candidateUrl, cancellationToken).ConfigureAwait(false);
        if (candidateJson is null || !TryParseKugouCandidate(candidateJson, out var id, out var accessKey))
        {
            return null;
        }

        var downloadUrl = DownloadUrl
            .Replace("{0}", Uri.EscapeDataString(id), StringComparison.Ordinal)
            .Replace("{1}", Uri.EscapeDataString(accessKey), StringComparison.Ordinal);
        var downloadJson = await TryGetStringAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
        return downloadJson is null ? null : ParseKugouDownload(downloadJson);
    }

    /// <summary>Picks the hit that really is this track (null when none qualifies).</summary>
    /// <param name="json">KuGou search response.</param>
    /// <param name="query">Track being looked up.</param>
    /// <param name="hash">Accepted song hash.</param>
    /// <param name="durationSeconds">Length reported for the accepted hit.</param>
    internal static bool TryParseKugouSong(string json, LyricsQuery query, out string hash, out int durationSeconds)
    {
        hash = string.Empty;
        durationSeconds = 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("info", out var info)
                || info.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var bestScore = double.MinValue;
            foreach (var hit in info.EnumerateArray())
            {
                if (!hit.TryGetProperty("hash", out var hashElement) || hashElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var candidateHash = hashElement.GetString();
                if (string.IsNullOrEmpty(candidateHash))
                {
                    continue;
                }

                var songName = hit.TryGetProperty("songname", out var songNameElement) ? songNameElement.GetString() : null;
                var singerName = hit.TryGetProperty("singername", out var singerElement) ? singerElement.GetString() : null;
                var seconds = hit.TryGetProperty("duration", out var durationElement)
                    && durationElement.ValueKind == JsonValueKind.Number
                    && durationElement.TryGetInt32(out var value)
                        ? value
                        : 0;

                if (!LyricsHitMatcher.Accept(query.Title, query.Artist, query.Duration, songName, singerName,
                        TimeSpan.FromSeconds(seconds)))
                {
                    continue;
                }

                var score = LyricsHitMatcher.Score(query.Title, query.Artist, songName, singerName);
                if (score > bestScore)
                {
                    bestScore = score;
                    hash = candidateHash;
                    durationSeconds = seconds;
                }
            }

            return hash.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Extracts id/accesskey from a Kugou candidate response.</summary>
    internal static bool TryParseKugouCandidate(string json, out string id, out string accessKey)
    {
        id = string.Empty;
        accessKey = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                return false;
            }

            var first = candidates[0];
            if (!first.TryGetProperty("id", out var idElement)
                || !first.TryGetProperty("accesskey", out var accessKeyElement))
            {
                return false;
            }

            id = idElement.ValueKind switch
            {
                JsonValueKind.String => idElement.GetString() ?? string.Empty,
                JsonValueKind.Number => idElement.GetRawText(),
                _ => string.Empty,
            };
            accessKey = accessKeyElement.GetString() ?? string.Empty;
            return id.Length > 0 && accessKey.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Parses a Kugou download response (base64 LRC content).</summary>
    internal static LyricsDocument? ParseKugouDownload(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var lrc = LrcParser.TryDecodeBase64(content.GetString());
            var parsed = LrcParser.Parse(lrc, "kugou");
            return parsed.IsEmpty ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
