using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FluidBar;

public sealed class KugouLyricsProvider : ILyricsProvider
{
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly Dictionary<string, KugouTrackMetadata?> _metadataCache = new();
    private readonly Dictionary<string, List<LrcLine>> _lyricsCache = new();
    private readonly Dictionary<string, DateTime> _lyricsMissUntil = new();
    private string? _lastKey;
    private List<LrcLine>? _lastLyrics;
    private DateTime _lastFetchTime = DateTime.MinValue;
    private static readonly TimeSpan LyricsMissTtl = TimeSpan.FromSeconds(90);

    public string? TryGetCurrentLine(MediaSnapshot snapshot, TimeSpan position)
    {
        var enriched = EnrichSnapshot(snapshot, position);
        return enriched.LyricLine;
    }

    public MediaSnapshot EnrichSnapshot(MediaSnapshot snapshot, TimeSpan position)
    {
        MediaSnapshot? best = null;
        foreach (var (title, artist) in BuildLookupCandidates(snapshot.Title, snapshot.Artist))
        {
            if (string.IsNullOrWhiteSpace(title) || IsKnownNonSongString(title))
                continue;

            var enriched = EnrichSnapshotForTrack(snapshot, position, title, artist);
            if (!string.IsNullOrWhiteSpace(enriched.LyricLine))
                return enriched;

            if (best is null ||
                (string.IsNullOrWhiteSpace(best.AlbumArtPath) &&
                 !string.IsNullOrWhiteSpace(enriched.AlbumArtPath)))
            {
                best = enriched;
            }
        }

        return best ?? snapshot;
    }

    private MediaSnapshot EnrichSnapshotForTrack(
        MediaSnapshot snapshot,
        TimeSpan position,
        string title,
        string artist)
    {
        var cacheKey = MakeCacheKey(title, artist);
        var duration = ResolveDuration(snapshot);
        var metadata = GetMetadata(title, artist, cacheKey);

        var albumArtPath = snapshot.AlbumArtPath;
        if (string.IsNullOrWhiteSpace(albumArtPath) && !string.IsNullOrWhiteSpace(metadata?.AlbumArtUrl))
            albumArtPath = DownloadAlbumArt(metadata.AlbumArtUrl, cacheKey);

        var lyricLine = snapshot.LyricLine;
        string? secondaryLyricLine = null;
        if (string.IsNullOrWhiteSpace(lyricLine))
        {
            var lines = GetLyrics(metadata, title, artist, duration, cacheKey);
            lyricLine = SelectCurrentLine(lines, position);
            secondaryLyricLine = SelectNextLine(lines, position);
        }

        return snapshot with
        {
            Title = title,
            Artist = artist,
            AlbumArtPath = albumArtPath,
            LyricLine = lyricLine,
            SecondaryLyricLine = secondaryLyricLine
        };
    }

    public static (string Title, string Artist) NormalizeTrackText(string? rawTitle, string? rawArtist)
    {
        var title = CleanSongText(rawTitle);
        var artist = CleanSongText(rawArtist);
        return NormalizeTitleAndArtist(title, artist);
    }

    public static KugouTrackMetadata? ParseSearchMetadata(string searchJson)
    {
        try
        {
            using var searchDoc = JsonDocument.Parse(searchJson);
            if (!searchDoc.RootElement.TryGetProperty("data", out var data))
                return null;
            if (!data.TryGetProperty("info", out var info) || info.GetArrayLength() == 0)
                return null;

            var first = info[0];
            var hash = ReadString(first, "hash");
            if (string.IsNullOrWhiteSpace(hash))
                return null;

            return new KugouTrackMetadata(
                hash,
                ReadString(first, "album_audio_id"),
                ReadString(first, "album_id"),
                NormalizeAlbumArtUrl(ReadAlbumArtCandidate(first)));
        }
        catch
        {
            return null;
        }
    }

    public static KugouLyricsCandidate? ParseLyricsCandidate(string searchJson) =>
        ParseLyricsCandidate(searchJson, null, null, 0);

    public static KugouLyricsCandidate? ParseLyricsCandidate(
        string searchJson,
        string? title,
        string? artist,
        int durationMilliseconds)
    {
        try
        {
            using var doc = JsonDocument.Parse(searchJson);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                return null;
            }

            KugouLyricsCandidate? best = null;
            var bestScore = int.MinValue;
            var index = 0;
            foreach (var candidate in candidates.EnumerateArray())
            {
                var id = ReadFirstString(candidate, "id", "download_id");
                var accessKey = ReadFirstString(candidate, "accesskey", "access_key");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(accessKey))
                {
                    index++;
                    continue;
                }

                var duration = 0;
                var rawDuration = ReadString(candidate, "duration");
                if (!string.IsNullOrWhiteSpace(rawDuration))
                    _ = int.TryParse(rawDuration, out duration);
                duration = NormalizeLyricsDuration(duration, durationMilliseconds);

                var current = new KugouLyricsCandidate(id, accessKey, duration);
                var score = ScoreLyricsCandidate(
                    candidate,
                    title,
                    artist,
                    durationMilliseconds,
                    duration,
                    index);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = current;
                }

                index++;
            }

            return MeetsMinimumCandidateScore(bestScore, title, artist, durationMilliseconds)
                ? best
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static string? SelectLineFromDownloadJson(string downloadJson, TimeSpan position)
    {
        var lines = ParseLyricsFromDownloadJson(downloadJson);
        return SelectCurrentLine(lines, position);
    }

    private KugouTrackMetadata? GetMetadata(string title, string? artist, string cacheKey)
    {
        if (_metadataCache.TryGetValue(cacheKey, out var cached))
            return cached;

        if (cacheKey == _lastKey && (DateTime.Now - _lastFetchTime).TotalSeconds <= 3)
            return null;

        _lastKey = cacheKey;
        _lastFetchTime = DateTime.Now;

        var keyword = string.IsNullOrWhiteSpace(artist) ? title : $"{title} {artist}";
        var encoded = WebUtility.UrlEncode(keyword);
        foreach (var host in new[]
        {
            "http://mobilecdn.kugou.com",
            "http://mobilecdnbj.kugou.com",
            "https://mobilecdn.kugou.com",
            "https://mobilecdnbj.kugou.com"
        })
        {
            var searchUrl = $"{host}/api/v3/search/song?format=json&keyword={encoded}&page=1&pagesize=5";
            var searchJson = HttpGet(searchUrl);
            if (searchJson is null)
                continue;

            var metadata = ParseSearchMetadata(searchJson);
            _metadataCache[cacheKey] = metadata;
            return metadata;
        }

        // Don't cache null for app-name-only titles — they may get real song info later
        if (!IsKnownNonSongString(title))
            _metadataCache[cacheKey] = null;
        return null;
    }

    private List<LrcLine>? GetLyrics(
        KugouTrackMetadata? metadata,
        string title,
        string? artist,
        int durationMilliseconds,
        string cacheKey)
    {
        if (_lyricsCache.TryGetValue(cacheKey, out var cached))
        {
            _lastLyrics = cached;
            return cached;
        }
        if (_lyricsMissUntil.TryGetValue(cacheKey, out var missUntil) &&
            missUntil > DateTime.Now)
        {
            return null;
        }

        try
        {
            var candidate = FindLyricsCandidate(metadata, title, artist, durationMilliseconds);
            if (candidate is null)
            {
                _lyricsMissUntil[cacheKey] = DateTime.Now.Add(LyricsMissTtl);
                return null;
            }

            var lrcDownloadPath = $"/download?ver=1&client=pc&id={WebUtility.UrlEncode(candidate.Id)}&accesskey={WebUtility.UrlEncode(candidate.AccessKey)}&fmt=lrc&charset=utf8";
            var parsed = ParseLyricsFromFirstValidDownload(
                $"http://lyrics.kugou.com{lrcDownloadPath}",
                $"https://lyrics.kugou.com{lrcDownloadPath}");
            if (parsed.Count == 0)
            {
                _lyricsMissUntil[cacheKey] = DateTime.Now.Add(LyricsMissTtl);
                return null;
            }

            _lyricsCache[cacheKey] = parsed;
            _lyricsMissUntil.Remove(cacheKey);
            _lastLyrics = parsed;
            return parsed;
        }
        catch
        {
            _lyricsMissUntil[cacheKey] = DateTime.Now.Add(LyricsMissTtl);
            return null;
        }
    }

    private static List<LrcLine> ParseLyricsFromFirstValidDownload(params string[] urls)
    {
        foreach (var url in urls)
        {
            var json = HttpGet(url);
            if (json is null)
                continue;

            var parsed = ParseLyricsFromDownloadJson(json);
            if (parsed.Count > 0)
                return parsed;
        }

        return [];
    }

    private static KugouLyricsCandidate? FindLyricsCandidate(
        KugouTrackMetadata? metadata,
        string title,
        string? artist,
        int durationMilliseconds)
    {
        foreach (var searchJson in EnumerateLyricsSearchResults(metadata, title, artist, durationMilliseconds))
        {
            var candidate = ParseLyricsCandidate(searchJson, title, artist, durationMilliseconds);
            if (candidate is not null)
                return candidate;
        }

        return null;
    }

    private static int ScoreLyricsCandidate(
        JsonElement candidate,
        string? title,
        string? artist,
        int expectedDurationMilliseconds,
        int candidateDurationMilliseconds,
        int index)
    {
        var score = Math.Max(0, 20 - index);
        var candidateTitle = ReadFirstString(candidate, "song", "songname", "name", "filename");
        var candidateArtist = ReadFirstString(candidate, "singer", "singername", "author");

        score += MatchTextScore(title, candidateTitle, strongScore: 120, partialScore: 70);
        score += MatchTextScore(artist, candidateArtist, strongScore: 60, partialScore: 35);

        if (expectedDurationMilliseconds > 0 && candidateDurationMilliseconds > 0)
        {
            var delta = Math.Abs(expectedDurationMilliseconds - candidateDurationMilliseconds);
            score += delta switch
            {
                <= 1500 => 45,
                <= 5000 => 24,
                <= 12000 => 8,
                _ => -20
            };
        }

        return score;
    }

    private static int MatchTextScore(
        string? expected,
        string? actual,
        int strongScore,
        int partialScore)
    {
        var left = NormalizeForMatch(expected);
        var right = NormalizeForMatch(actual);
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return 0;

        if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
            return strongScore;

        return left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
               right.Contains(left, StringComparison.OrdinalIgnoreCase)
            ? partialScore
            : 0;
    }

    private static int NormalizeLyricsDuration(
        int candidateDurationMilliseconds,
        int expectedDurationMilliseconds)
    {
        if (candidateDurationMilliseconds <= 0 || expectedDurationMilliseconds <= 0)
            return candidateDurationMilliseconds;

        if (expectedDurationMilliseconds >= 60_000 &&
            candidateDurationMilliseconds < expectedDurationMilliseconds / 3)
        {
            var centisecondsAsMilliseconds = candidateDurationMilliseconds * 10;
            if (Math.Abs(centisecondsAsMilliseconds - expectedDurationMilliseconds) <
                Math.Abs(candidateDurationMilliseconds - expectedDurationMilliseconds))
            {
                return centisecondsAsMilliseconds;
            }
        }

        return candidateDurationMilliseconds;
    }

    private static IReadOnlyList<string> BuildLyricsKeywords(string title, string? artist)
    {
        var keywords = new List<string>();
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            value = value.Trim();
            if (!keywords.Contains(value, StringComparer.OrdinalIgnoreCase))
                keywords.Add(value);
        }

        Add(string.IsNullOrWhiteSpace(artist) ? null : $"{artist} {title}");
        Add(string.IsNullOrWhiteSpace(artist) ? null : $"{title} {artist}");
        Add(string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}");
        Add(title);
        return keywords;
    }

    private static IEnumerable<string> EnumerateLyricsSearchResults(
        KugouTrackMetadata? metadata,
        string title,
        string? artist,
        int durationMilliseconds)
    {
        if (metadata is not null && !string.IsNullOrWhiteSpace(metadata.Hash))
        {
            var lrcSearchPath = $"/search?ver=1&man=yes&client=mobi&hash={WebUtility.UrlEncode(metadata.Hash)}&key=NVPh5oo715z5DIWAeQlhMDsWXXQV4hwt";
            foreach (var url in new[]
            {
                $"https://krcs.kugou.com{lrcSearchPath}",
                $"http://krcs.kugou.com{lrcSearchPath}"
            })
            {
                var json = HttpGet(url);
                if (json is not null)
                    yield return json;
            }
        }

        var duration = durationMilliseconds > 0 ? durationMilliseconds : metadata?.DurationMilliseconds ?? 0;
        foreach (var keyword in BuildLyricsKeywords(title, artist))
        {
            var keywordPath = $"/search?ver=1&man=yes&client=pc&keyword={WebUtility.UrlEncode(keyword)}";
            if (duration > 0)
                keywordPath += $"&duration={duration}";
            if (!string.IsNullOrWhiteSpace(metadata?.Hash))
                keywordPath += $"&hash={WebUtility.UrlEncode(metadata.Hash)}";

            foreach (var url in new[]
            {
                $"http://lyrics.kugou.com{keywordPath}",
                $"https://lyrics.kugou.com{keywordPath}",
                $"http://krcs.kugou.com{keywordPath}",
                $"https://krcs.kugou.com{keywordPath}"
            })
            {
                var json = HttpGet(url);
                if (json is not null)
                    yield return json;
            }
        }
    }

    private static List<LrcLine> ParseLyricsFromDownloadJson(string downloadJson)
    {
        try
        {
            using var dlDoc = JsonDocument.Parse(downloadJson);
            foreach (var payload in EnumerateLyricsPayloads(dlDoc.RootElement))
            {
                foreach (var lrcText in DecodeLyricsPayloadCandidates(payload))
                {
                    var parsed = ParseLrc(lrcText);
                    if (parsed.Count > 0)
                        return parsed;
                }
            }
        }
        catch
        {
        }

        return [];
    }

    private static IEnumerable<string> EnumerateLyricsPayloads(JsonElement root)
    {
        foreach (var payload in EnumerateLyricsPayloadsFromObject(root))
            yield return payload;

        if (!root.TryGetProperty("data", out var data))
            yield break;

        if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (var payload in EnumerateLyricsPayloadsFromObject(data))
                yield return payload;
        }
        else if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                foreach (var payload in EnumerateLyricsPayloadsFromObject(item))
                    yield return payload;
            }
        }
    }

    private static IEnumerable<string> EnumerateLyricsPayloadsFromObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var propertyName in new[] { "content", "lyrics", "lyric", "lrc" })
        {
            var value = ReadString(element, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }
    }

    private static IEnumerable<string> DecodeLyricsPayloadCandidates(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            yield break;

        var trimmed = payload.Trim();
        yield return trimmed;

        string? decoded = null;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(decoded) &&
            !decoded.Equals(trimmed, StringComparison.Ordinal))
        {
            yield return decoded;
        }
    }

    private static string? SelectCurrentLine(List<LrcLine>? lines, TimeSpan position)
    {
        if (lines is null || lines.Count == 0)
            return null;

        var elapsed = Math.Max(0, position.TotalSeconds);
        LrcLine? best = null;
        foreach (var line in lines)
        {
            if (line.Time.TotalSeconds <= elapsed)
                best = line;
            else
                break;
        }

        return best?.Text ?? lines[0].Text;
    }

    private static string? SelectNextLine(List<LrcLine>? lines, TimeSpan position)
    {
        if (lines is null || lines.Count == 0)
            return null;

        var elapsed = Math.Max(0, position.TotalSeconds);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Time.TotalSeconds > elapsed)
                return lines[i].Text;
        }

        return null; // At the end of the song
    }

    /// <summary>
    /// Re-select lyrics from cache based on current position. No HTTP requests.
    /// Returns updated snapshot with lyrics at the given position, or null if no cached lyrics.
    /// </summary>
    public MediaSnapshot? ReSelectLyrics(MediaSnapshot snapshot, TimeSpan position)
    {
        foreach (var (title, artist) in BuildLookupCandidates(snapshot.Title, snapshot.Artist))
        {
            if (string.IsNullOrWhiteSpace(title) || IsKnownNonSongString(title))
                continue;

            var cacheKey = MakeCacheKey(title, artist);
            if (_lyricsCache.TryGetValue(cacheKey, out var lines) && lines.Count > 0)
            {
                var current = SelectCurrentLine(lines, position);
                var next = SelectNextLine(lines, position);
                return snapshot with { LyricLine = current, SecondaryLyricLine = next };
            }
        }
        return null;
    }

    private static string? DownloadAlbumArt(string albumArtUrl, string cacheKey)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FluidBar", "art");
        Directory.CreateDirectory(tempDir);
        var outPath = Path.Combine(tempDir, $"kugou_{Math.Abs(cacheKey.GetHashCode())}.jpg");
        if (File.Exists(outPath))
            return outPath;

        foreach (var url in AlbumArtUrlCandidates(albumArtUrl))
        {
            try
            {
                var bytes = Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                if (bytes.Length == 0)
                    continue;

                File.WriteAllBytes(outPath, bytes);
                return outPath;
            }
            catch
            {
            }
        }

        return null;
    }

    private static string? HttpGet(string url)
    {
        try
        {
            return Http.GetStringAsync(url).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static string? HttpGetAny(params string[] urls)
    {
        foreach (var url in urls)
        {
            var text = HttpGet(url);
            if (text is not null)
                return text;
        }
        return null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(3500)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FluidBar/1.0");
        client.DefaultRequestHeaders.Referrer = new Uri("https://www.kugou.com/");
        return client;
    }

    private static string MakeCacheKey(string title, string? artist)
    {
        return string.IsNullOrWhiteSpace(artist) ? title : $"{title}|{artist}";
    }

    private static int ResolveDuration(MediaSnapshot snapshot)
    {
        var ticks = snapshot.EndTicks - snapshot.StartTimeTicks;
        if (ticks <= 0)
            ticks = snapshot.EndTicks;
        if (ticks <= 0)
            return 0;

        var milliseconds = TimeSpan.FromTicks(ticks).TotalMilliseconds;
        return milliseconds > int.MaxValue ? 0 : (int)Math.Round(milliseconds);
    }

    private static string CleanSongText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Replace(" - 桌面歌词", "", StringComparison.Ordinal)
            .Replace(" - Kugou Music", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" - 酷狗音乐", "", StringComparison.Ordinal)
            .Replace(" - 酷狗", "", StringComparison.Ordinal)
            .Replace("桌面歌词", "", StringComparison.Ordinal)
            .Trim();
    }

    private static string? NormalizeAlbumArtUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var url = value.Replace("{size}", "240", StringComparison.OrdinalIgnoreCase).Trim();
        if (url.StartsWith("//", StringComparison.Ordinal))
            url = "http:" + url;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? url
            : null;
    }

    private static (string Title, string Artist) NormalizeTitleAndArtist(string title, string? artist)
    {
        var parts = title.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && IsKnownAppName(parts[0]))
        {
            title = string.Join(" - ", parts.Skip(1));
            artist = IsKnownAppName(artist) ? string.Empty : artist;
        }

        if (!string.IsNullOrWhiteSpace(artist) && !IsKnownAppName(artist))
            return (title, artist);

        if (IsKnownAppName(artist))
            artist = string.Empty;

        var dashIndex = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex <= 0 || dashIndex >= title.Length - 3)
            return (title, artist ?? string.Empty);

        var possibleArtist = title[..dashIndex].Trim();
        var possibleTitle = title[(dashIndex + 3)..].Trim();
        return string.IsNullOrWhiteSpace(possibleArtist) || string.IsNullOrWhiteSpace(possibleTitle)
            ? (title, artist ?? string.Empty)
            : (possibleTitle, possibleArtist);
    }

    private static IReadOnlyList<(string Title, string Artist)> BuildLookupCandidates(
        string? rawTitle,
        string? rawArtist)
    {
        var candidates = new List<(string Title, string Artist)>();

        void Add(string? title, string? artist)
        {
            title = CleanSongText(title);
            artist = CleanSongText(artist);
            if (string.IsNullOrWhiteSpace(title))
                return;

            var normalized = NormalizeTitleAndArtist(title, artist);
            if (string.IsNullOrWhiteSpace(normalized.Title))
                return;

            if (!candidates.Any(candidate =>
                    candidate.Title.Equals(normalized.Title, StringComparison.OrdinalIgnoreCase) &&
                    candidate.Artist.Equals(normalized.Artist, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(normalized);
            }
        }

        Add(rawTitle, rawArtist);

        var cleanTitle = CleanSongText(rawTitle);
        var cleanArtist = CleanSongText(rawArtist);
        var parts = cleanTitle.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && (string.IsNullOrWhiteSpace(cleanArtist) || IsKnownAppName(cleanArtist)))
        {
            Add(parts[0], parts[1]);
            Add(parts[1], parts[0]);
        }
        else if (parts.Length >= 3 && IsKnownAppName(parts[0]))
        {
            Add(parts[1], parts[2]);
            Add(parts[2], parts[1]);
        }

        return candidates;
    }

    private static bool MeetsMinimumCandidateScore(
        int bestScore,
        string? title,
        string? artist,
        int durationMilliseconds)
    {
        var hasTextHint = !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(artist);
        if (!hasTextHint)
            return bestScore > int.MinValue;

        if (bestScore >= 45)
            return true;

        return durationMilliseconds > 0 && bestScore >= 38;
    }

    private static bool IsKnownAppName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Equals("酷狗音乐", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("KuGou", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Kugou Music", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("网易云音乐", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("QQ音乐", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("QQ 音乐", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("酷我音乐", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadAlbumArtCandidate(JsonElement element)
    {
        return ReadString(element, "img") ??
               ReadString(element, "sizable_cover") ??
               ReadString(element, "album_sizable_cover") ??
               ReadString(element, "cover") ??
               ReadNestedString(element, "trans_param", "union_cover", "album_cover", "cover") ??
               ReadGroupAlbumArtCandidate(element);
    }

    private static string? ReadNestedString(JsonElement element, string objectName, params string[] propertyNames)
    {
        if (!element.TryGetProperty(objectName, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var propertyName in propertyNames)
        {
            var value = ReadString(nested, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static string? ReadGroupAlbumArtCandidate(JsonElement element)
    {
        if (!element.TryGetProperty("group", out var group) ||
            group.ValueKind != JsonValueKind.Array ||
            group.GetArrayLength() == 0)
        {
            return null;
        }

        foreach (var item in group.EnumerateArray())
        {
            var candidate = ReadAlbumArtCandidate(item);
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;
        }
        return null;
    }

    private static IReadOnlyList<string> AlbumArtUrlCandidates(string albumArtUrl)
    {
        if (!Uri.TryCreate(albumArtUrl, UriKind.Absolute, out var uri))
            return [albumArtUrl];

        if (uri.Scheme == Uri.UriSchemeHttp)
            return [albumArtUrl, "https://" + albumArtUrl["http://".Length..]];
        if (uri.Scheme == Uri.UriSchemeHttps)
            return [albumArtUrl, "http://" + albumArtUrl["https://".Length..]];
        return [albumArtUrl];
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static string? ReadFirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = ReadString(element, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in CleanSongText(value))
        {
            if (char.IsLetterOrDigit(ch) || IsCjk(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static bool IsCjk(char ch) =>
        ch >= '\u4e00' && ch <= '\u9fff';

    private static bool LooksLikeLrc(string value)
    {
        return value.Contains("[00:", StringComparison.Ordinal) ||
               value.Contains("[0:", StringComparison.Ordinal);
    }

    private static List<LrcLine> ParseLrc(string lrcText)
    {
        var lines = new List<LrcLine>();
        foreach (var rawLine in lrcText.Split('\n'))
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.Length < 10 || trimmed[0] != '[') continue;
            var close = trimmed.IndexOf(']');
            if (close <= 0) continue;

            var timestamp = trimmed[1..close];
            var text = trimmed[(close + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            var parts = timestamp.Split(':');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var minutes)) continue;
            if (!double.TryParse(parts[1], out var seconds)) continue;

            lines.Add(new LrcLine(TimeSpan.FromSeconds(minutes * 60 + seconds), text));
        }
        return lines.OrderBy(line => line.Time).ToList();
    }

    private static bool IsKnownNonSongString(string title)
    {
        if (title.Equals("Media", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Edge", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("kugou", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Browser", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        ReadOnlySpan<string> known = [
            "酷狗音乐", "网易云音乐", "QQ音乐", "正在播放",
            "桌面歌词", "酷我音乐", "Spotify",
            "Microsoft Edge", "Chrome", "Firefox", "Media Player"];
        foreach (var knownText in known)
        {
            if (title.Contains(knownText, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private sealed record LrcLine(TimeSpan Time, string Text);
}

public sealed record KugouTrackMetadata(
    string Hash,
    string? AlbumAudioId,
    string? AlbumId,
    string? AlbumArtUrl,
    int DurationMilliseconds = 0);

public sealed record KugouLyricsCandidate(
    string Id,
    string AccessKey,
    int DurationMilliseconds);
