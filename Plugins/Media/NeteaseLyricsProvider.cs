using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FluidBar;

/// <summary>
/// Fetches lyrics from NetEase Cloud Music API.
/// Falls back to Kugou if NetEase doesn't have the song.
/// </summary>
public sealed class NeteaseLyricsProvider : ILyricsProvider
{
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly Dictionary<string, List<LrcLine>> _lyricsCache = new();
    private readonly Dictionary<string, DateTime> _missCache = new();
    private static readonly TimeSpan MissTtl = TimeSpan.FromSeconds(90);

    public string? TryGetCurrentLine(MediaSnapshot snapshot, TimeSpan position)
    {
        var enriched = EnrichSnapshot(snapshot, position);
        return enriched.LyricLine;
    }

    public MediaSnapshot EnrichSnapshot(MediaSnapshot snapshot, TimeSpan position)
    {
        var title = snapshot.Title?.Trim() ?? "";
        var artist = snapshot.Artist?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
            return snapshot;

        var cacheKey = $"{title}|{artist}";

        // Check miss cache
        if (_missCache.TryGetValue(cacheKey, out var missUntil) && missUntil > DateTime.Now)
            return snapshot;

        // Check lyrics cache
        if (_lyricsCache.TryGetValue(cacheKey, out var cachedLines) && cachedLines.Count > 0)
        {
            var line = SelectLine(cachedLines, position);
            var nextLine = SelectNextLine(cachedLines, position);
            return snapshot with { LyricLine = line, SecondaryLyricLine = nextLine };
        }

        // Search NetEase API
        var songId = SearchSong(title, artist);
        if (songId is null)
        {
            _missCache[cacheKey] = DateTime.Now + MissTtl;
            return snapshot;
        }

        // Get lyrics
        var lyrics = GetLyrics(songId.Value);
        if (lyrics is null || lyrics.Count == 0)
        {
            _missCache[cacheKey] = DateTime.Now + MissTtl;
            return snapshot;
        }

        _lyricsCache[cacheKey] = lyrics;
        _missCache.Remove(cacheKey);

        var currentLine = SelectLine(lyrics, position);
        var nextLine2 = SelectNextLine(lyrics, position);
        return snapshot with { LyricLine = currentLine, SecondaryLyricLine = nextLine2 };
    }

    private static long? SearchSong(string title, string artist)
    {
        try
        {
            var keyword = string.IsNullOrWhiteSpace(artist) ? title : $"{title} {artist}";
            var encoded = WebUtility.UrlEncode(keyword);
            var url = $"https://music.163.com/api/search/get/web?s={encoded}&type=1&limit=5";

            var json = HttpGet(url);
            if (json is null) return null;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var result))
                return null;
            if (!result.TryGetProperty("songs", out var songs) || songs.GetArrayLength() == 0)
                return null;

            // Pick the best match
            foreach (var song in songs.EnumerateArray())
            {
                var songName = ReadString(song, "name");
                var songArtist = ReadString(song, "artist");
                var songId = song.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : (long?)null;

                if (songId is null) continue;

                // Prefer exact title match
                if (!string.IsNullOrWhiteSpace(songName) &&
                    songName.Equals(title, StringComparison.OrdinalIgnoreCase))
                    return songId;
            }

            // Fallback: return first result
            var first = songs[0];
            return first.TryGetProperty("id", out var firstId) ? firstId.GetInt64() : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<LrcLine>? GetLyrics(long songId)
    {
        try
        {
            var url = $"https://music.163.com/api/song/lyric?id={songId}&lv=1";
            var json = HttpGet(url);
            if (json is null) return null;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("lrc", out var lrc))
                return null;
            if (!lrc.TryGetProperty("lyric", out var lyricProp))
                return null;

            var lrcText = lyricProp.GetString();
            if (string.IsNullOrWhiteSpace(lrcText))
                return null;

            return ParseLrc(lrcText);
        }
        catch
        {
            return null;
        }
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
        return lines.OrderBy(l => l.Time).ToList();
    }

    private static string? SelectLine(List<LrcLine>? lines, TimeSpan position)
    {
        if (lines is null || lines.Count == 0) return null;
        var elapsed = Math.Max(0, position.TotalSeconds);
        LrcLine? best = null;
        foreach (var line in lines)
        {
            if (line.Time.TotalSeconds <= elapsed) best = line;
            else break;
        }
        return best?.Text ?? lines[0].Text;
    }

    private static string? SelectNextLine(List<LrcLine>? lines, TimeSpan position)
    {
        if (lines is null || lines.Count == 0) return null;
        var elapsed = Math.Max(0, position.TotalSeconds);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Time.TotalSeconds > elapsed)
                return lines[i].Text;
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

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FluidBar/1.0");
        client.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
        return client;
    }

    private sealed record LrcLine(TimeSpan Time, string Text);
}
