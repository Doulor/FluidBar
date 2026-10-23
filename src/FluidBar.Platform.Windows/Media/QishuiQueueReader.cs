using System.IO.Compression;
using System.Text.Json;

namespace FluidBar.Platform.Windows.Media;

/// <summary>The track Soda Music reports as last played, read from its own queue cache.</summary>
/// <param name="Key">Playable key ("track-…").</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">First artist.</param>
/// <param name="Duration">Track length.</param>
public sealed record QishuiTrack(string Key, string Title, string Artist, TimeSpan Duration);

/// <summary>
/// Reads Soda Music's persisted play queue to learn which track it is playing.
/// </summary>
/// <remarks>
/// <para>Soda's window title stays "汽水音乐" no matter what plays, so unlike KuGou there is no caption
/// to parse. What the player does keep on disk is its queue (<c>%APPDATA%\SodaMusic\LunaStorage\QueueCache</c>,
/// a "LUNA" magic followed by gzip'd JSON) with the metadata of every playable and a
/// <c>lastPlayedKey</c> that follows the song being played (verified against the live player:
/// switching tracks updates it within seconds). That pair — last played key + playable metadata — is
/// the title/artist/duration source for the fallback candidate.</para>
/// <para>The file is only re-read when its write time changes; a read costs one small gzip
/// decompression. Readers run on the media poll thread, so everything here is non-blocking file I/O.</para>
/// </remarks>
public static class QishuiQueueReader
{
    private const string Magic = "LUNA";

    /// <summary>Largest accepted payload (the queue holds a few hundred tracks at most).</summary>
    private const long MaxFileBytes = 16 * 1024 * 1024;

    private static readonly object Gate = new();
    private static DateTime _lastWriteUtc = DateTime.MinValue;
    private static long _lastLength = -1;
    private static QishuiTrack? _cached;

    /// <summary>The track the player last started, or null when the cache is absent/unreadable.</summary>
    public static QishuiTrack? TryRead()
    {
        var path = ResolvePath();
        if (path is null)
        {
            return null;
        }

        DateTime writeUtc;
        long length;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxFileBytes)
            {
                return null;
            }

            writeUtc = info.LastWriteTimeUtc;
            length = info.Length;
        }
        catch (IOException)
        {
            return null;
        }

        lock (Gate)
        {
            if (_cached is not null && writeUtc == _lastWriteUtc && length == _lastLength)
            {
                return _cached;
            }
        }

        QishuiTrack? track = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);
            var bytes = reader.ReadBytes((int)stream.Length);
            track = Parse(bytes);
        }
        catch (IOException)
        {
            return null;
        }

        lock (Gate)
        {
            _lastWriteUtc = writeUtc;
            _lastLength = length;
            _cached = track;
        }

        return track;
    }

    /// <summary>Parses a QueueCache payload (exposed for tests).</summary>
    public static QishuiTrack? Parse(byte[]? bytes)
    {
        if (bytes is null || bytes.Length <= Magic.Length)
        {
            return null;
        }

        for (var index = 0; index < Magic.Length; index++)
        {
            if (bytes[index] != (byte)Magic[index])
            {
                return null;
            }
        }

        string json;
        try
        {
            using var input = new MemoryStream(bytes, Magic.Length, bytes.Length - Magic.Length, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            json = System.Text.Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Shape: { "u_<uid>:feed": { "lastPlayedKey": "track-…", "playables": [ … ] } }
            foreach (var feedProperty in root.EnumerateObject())
            {
                if (feedProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var feed = feedProperty.Value;
                if (!feed.TryGetProperty("lastPlayedKey", out var lastPlayed)
                    || lastPlayed.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var key = lastPlayed.GetString();
                if (string.IsNullOrEmpty(key)
                    || !feed.TryGetProperty("playables", out var playables)
                    || playables.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var playable in playables.EnumerateArray())
                {
                    if (playable.ValueKind != JsonValueKind.Object
                        || !playable.TryGetProperty("key", out var playableKey)
                        || playableKey.ValueKind != JsonValueKind.String
                        || !string.Equals(playableKey.GetString(), key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var track = ReadTrack(playable, key);
                    if (track is not null)
                    {
                        return track;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static QishuiTrack? ReadTrack(JsonElement playable, string key)
    {
        // Metadata lives both on the playable (name/artists/duration) and under its "track" object;
        // prefer the track object, fall back to the playable itself.
        var source = playable.TryGetProperty("track", out var nested) && nested.ValueKind == JsonValueKind.Object
            ? nested
            : playable;

        var title = source.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var artist = string.Empty;
        if (source.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
            && artists.GetArrayLength() > 0)
        {
            var first = artists[0];
            if (first.ValueKind == JsonValueKind.Object
                && first.TryGetProperty("name", out var artistName)
                && artistName.ValueKind == JsonValueKind.String)
            {
                artist = artistName.GetString() ?? string.Empty;
            }
        }

        var durationMs = 0;
        if (source.TryGetProperty("duration", out var duration) && duration.ValueKind == JsonValueKind.Number
            && duration.TryGetInt32(out var value))
        {
            durationMs = value;
        }

        return new QishuiTrack(key, title, artist, TimeSpan.FromMilliseconds(Math.Max(0, durationMs)));
    }

    private static string? ResolvePath()
    {
        string? roaming;
        try
        {
            roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        catch (Exception)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(roaming))
        {
            return null;
        }

        return Path.Combine(roaming, "SodaMusic", "LunaStorage", "QueueCache");
    }
}
