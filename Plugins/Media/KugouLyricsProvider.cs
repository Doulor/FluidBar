using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FluidBar;

/// <summary>
/// Fetches lyrics from Kugou's public API by song name.
/// No user action needed — automatically searches, downloads, and caches lyrics.
/// </summary>
public sealed class KugouLyricsProvider : ILyricsProvider
{
    private readonly Dictionary<string, List<LrcLine>> _cache = new();
    private string? _lastTitle;
    private List<LrcLine>? _lastLyrics;
    private DateTime _lastFetchTime = DateTime.MinValue;

    public string? TryGetCurrentLine(MediaSnapshot snapshot, TimeSpan position)
    {
        var title = snapshot.Title;
        if (string.IsNullOrWhiteSpace(title))
            return null;

        // Only re-fetch if song changed and at least 3 seconds since last fetch
        if (title != _lastTitle && (DateTime.Now - _lastFetchTime).TotalSeconds > 3)
        {
            _lastTitle = title;
            _lastLyrics = FetchLyricsForSong(title, snapshot.Artist);
            _lastFetchTime = DateTime.Now;
        }

        if (_lastLyrics is null || _lastLyrics.Count == 0)
            return null;

        // Find the lyric line closest to the current position
        // Since Kugou doesn't provide position, just show the first line
        // (or cycle through lines based on elapsed time)
        var elapsed = position.TotalSeconds;
        if (elapsed <= 0)
            return _lastLyrics[0].Text;

        LrcLine? best = null;
        foreach (var line in _lastLyrics)
        {
            if (line.Time.TotalSeconds <= elapsed)
                best = line;
            else
                break;
        }
        return best?.Text ?? _lastLyrics[0].Text;
    }

    private List<LrcLine>? FetchLyricsForSong(string songName, string? artist)
    {
        try
        {
            var keyword = string.IsNullOrWhiteSpace(artist)
                ? songName
                : $"{songName} {artist}";

            // Step 1: Search for the song
            var searchUrl = $"https://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={WebUtility.UrlEncode(keyword)}&page=1&pagesize=5";
            var searchJson = HttpGet(searchUrl);
            if (searchJson is null) return null;

            using var searchDoc = JsonDocument.Parse(searchJson);
            var data = searchDoc.RootElement.GetProperty("data");
            if (!data.TryGetProperty("info", out var info) || info.GetArrayLength() == 0)
                return null;

            var hash = info[0].GetProperty("hash").GetString();
            if (string.IsNullOrWhiteSpace(hash)) return null;

            // Step 2: Search for lyrics candidates
            var lrcSearchUrl = $"https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&hash={hash}&key=NVPh5oo715z5DIWAeQlhMDsWXXQV4hwt";
            var lrcSearchJson = HttpGet(lrcSearchUrl);
            if (lrcSearchJson is null) return null;

            using var lrcDoc = JsonDocument.Parse(lrcSearchJson);
            if (!lrcDoc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return null;

            var firstCandidate = candidates[0];
            var id = firstCandidate.GetProperty("id").GetString();
            var accesskey = firstCandidate.GetProperty("accesskey").GetString();

            // Step 3: Download LRC format lyrics
            var lrcDownloadUrl = $"https://lyrics.kugou.com/download?ver=1&client=pc&id={id}&accesskey={accesskey}&fmt=lrc&charset=utf8";
            var lrcDownloadJson = HttpGet(lrcDownloadUrl);
            if (lrcDownloadJson is null) return null;

            using var dlDoc = JsonDocument.Parse(lrcDownloadJson);
            if (!dlDoc.RootElement.TryGetProperty("content", out var contentEl))
                return null;

            var base64Content = contentEl.GetString();
            if (string.IsNullOrWhiteSpace(base64Content)) return null;

            var lrcBytes = Convert.FromBase64String(base64Content);
            var lrcText = Encoding.UTF8.GetString(lrcBytes);

            return ParseLrc(lrcText);
        }
        catch
        {
            return null;
        }
    }

    private static string? HttpGet(string url)
    {
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
            request.Timeout = 3000;
            using var response = (HttpWebResponse)request.GetResponse();
            using var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
            return reader.ReadToEnd();
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

            // Parse [mm:ss.xx]text format
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

    private sealed record LrcLine(TimeSpan Time, string Text);
}
