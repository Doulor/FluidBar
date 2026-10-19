using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>Lyrics decoded from a KuGou <c>.krc</c> file.</summary>
/// <param name="Document">Timed lyric document.</param>
/// <param name="ApproximateDuration">End of the last timed line (the file carries no total duration).</param>
public sealed record KrcLyrics(LyricsDocument Document, TimeSpan ApproximateDuration);

/// <summary>
/// Decodes KuGou's local <c>.krc</c> lyric files into timed lines.
/// </summary>
/// <remarks>
/// <para>The format is a <c>krc1</c> magic followed by the payload XOR-ed with a fixed 16-byte key and
/// zlib-compressed UTF-8 text. Timed lines look like <c>[startMs,durationMs]&lt;wordOffset,wordLength,0&gt;word …</c>;
/// the word-level tags are stripped because the island renders whole lines.</para>
/// <para>Reading the player's own cache matters for correctness: the file is exactly the lyric the
/// player is showing, so no fuzzy title/artist matching against a third-party catalogue is involved.</para>
/// </remarks>
public static class KrcDecoder
{
    private static readonly byte[] KugouKey =
    [
        0x40, 0x47, 0x61, 0x77, 0x5E, 0x32, 0x74, 0x47,
        0x51, 0x36, 0x31, 0x2D, 0xCE, 0xD2, 0x6E, 0x69,
    ];

    private const string Magic = "krc1";

    /// <summary>Largest accepted encrypted file (lyric files are a few KiB).</summary>
    public const int MaxEncodedBytes = 4 * 1024 * 1024;

    /// <summary>Largest accepted decoded payload.</summary>
    public const int MaxDecodedBytes = 8 * 1024 * 1024;

    /// <summary>Decodes an encrypted <c>.krc</c> blob, or null when it is not a valid file.</summary>
    public static KrcLyrics? TryDecode(byte[]? encrypted)
    {
        var text = TryDecodeText(encrypted);
        return text is null ? null : Parse(text);
    }

    /// <summary>Decrypts and inflates the payload (exposed for tests).</summary>
    public static string? TryDecodeText(byte[]? encrypted)
    {
        if (encrypted is null || encrypted.Length <= Magic.Length || encrypted.Length > MaxEncodedBytes)
        {
            return null;
        }

        for (var index = 0; index < Magic.Length; index++)
        {
            if (encrypted[index] != (byte)Magic[index])
            {
                return null;
            }
        }

        var payload = new byte[encrypted.Length - Magic.Length];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(encrypted[index + Magic.Length] ^ KugouKey[index % KugouKey.Length]);
        }

        try
        {
            using var input = new MemoryStream(payload, writable: false);
            using var inflater = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = inflater.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > MaxDecodedBytes)
                {
                    return null;
                }

                output.Write(buffer, 0, read);
            }

            return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Parses decoded KRC text into a timed document (pure; exposed for tests).</summary>
    public static KrcLyrics? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var title = string.Empty;
        var artist = string.Empty;
        var builder = ImmutableArray.CreateBuilder<LyricsLine>();
        var duration = TimeSpan.Zero;

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length < 4 || line[0] != '[')
            {
                continue;
            }

            var close = line.IndexOf(']');
            if (close < 0)
            {
                continue;
            }

            var header = line[1..close];
            var comma = header.IndexOf(',');
            if (comma > 0)
            {
                if (!int.TryParse(header[..comma], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startMs)
                    || !int.TryParse(header[(comma + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var lineMs)
                    || startMs < 0)
                {
                    continue;
                }

                var body = StripWordTags(line[(close + 1)..]).Trim();
                if (body.Length == 0)
                {
                    continue;
                }

                var start = TimeSpan.FromMilliseconds(startMs);
                builder.Add(new LyricsLine(start, body));
                var end = TimeSpan.FromMilliseconds(startMs + Math.Max(0, lineMs));
                if (end > duration)
                {
                    duration = end;
                }

                continue;
            }

            var colon = header.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = header[..colon].Trim().ToLowerInvariant();
            var value = header[(colon + 1)..].Trim();
            switch (key)
            {
                case "ti":
                    title = value;
                    break;
                case "ar":
                    artist = value;
                    break;
            }
        }

        if (builder.Count == 0)
        {
            return null;
        }

        builder.Sort(static (left, right) => left.Time.CompareTo(right.Time));
        var document = new LyricsDocument(title, artist, "kugou-krc", builder.ToImmutable(), duration);
        return new KrcLyrics(document, duration);
    }

    /// <summary>Removes the per-word timing tags (<c>&lt;offset,length,extra&gt;</c>) from a lyric line.</summary>
    private static string StripWordTags(string text)
    {
        if (text.IndexOf('<') < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var insideTag = false;
        foreach (var character in text)
        {
            if (character == '<')
            {
                insideTag = true;
                continue;
            }

            if (character == '>' && insideTag)
            {
                insideTag = false;
                continue;
            }

            if (!insideTag)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
