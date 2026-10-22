using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>Lyrics decoded from a KuGou <c>.krc</c> file.</summary>
/// <param name="Document">Timed lyric document.</param>
/// <param name="ApproximateDuration">End of the last timed line when the file carries no total.</param>
/// <param name="DeclaredTotal">
/// The <c>[total:ms]</c> header, i.e. the length of the audio file the lyric belongs to. Zero when the
/// header is missing or empty (many files ship <c>[total:0]</c>).
/// </param>
public sealed record KrcLyrics(LyricsDocument Document, TimeSpan ApproximateDuration, TimeSpan DeclaredTotal);

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
        var declaredTotal = TimeSpan.Zero;

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

                var (body, words) = ParseWords(line[(close + 1)..]);
                body = body.Trim();
                if (body.Length == 0)
                {
                    continue;
                }

                var start = TimeSpan.FromMilliseconds(startMs);
                builder.Add(new LyricsLine(start, body, null, words));
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
                case "total":
                    // Milliseconds, and the only field that is the SONG length rather than a lyric span.
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalMs)
                        && totalMs > 0)
                    {
                        declaredTotal = TimeSpan.FromMilliseconds(totalMs);
                    }
                    break;
            }
        }

        if (builder.Count == 0)
        {
            return null;
        }

        builder.Sort(static (left, right) => left.Time.CompareTo(right.Time));
        // A malformed last line can carry the whole song length in its span field (measured on 145 of the
        // 277 files that declare a total: start + span ends at twice the song length). The declared total
        // is authoritative, so it wins whenever the two disagree.
        var effective = declaredTotal > TimeSpan.Zero ? declaredTotal : duration;
        var document = new LyricsDocument(title, artist, "kugou-krc", builder.ToImmutable(), effective);
        return new KrcLyrics(document, duration, declaredTotal);
    }

    /// <summary>
    /// Splits a KRC line body into text and per-word spans (<c>&lt;offsetMs,length,extra&gt;word</c>).
    /// </summary>
    /// <remarks>
    /// The spans are what makes karaoke highlighting possible; the previous implementation threw them
    /// away and only kept the plain text. Defensive behaviour for real files: negative offsets are
    /// clamped to 0, lengths are clamped to the remaining text, out-of-order spans are sorted by offset
    /// (stable), and a line without tags returns an empty span list (callers fall back to the whole line).
    /// </remarks>
    internal static (string Text, ImmutableArray<LyricWord> Words) ParseWords(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (string.Empty, ImmutableArray<LyricWord>.Empty);
        }

        if (text.IndexOf('<') < 0)
        {
            return (text, ImmutableArray<LyricWord>.Empty);
        }

        var builder = new StringBuilder(text.Length);
        var spans = new List<(int Offset, int Length, string Text)>();
        var index = 0;
        var pendingOffset = -1;
        var pendingLength = 0;
        while (index < text.Length)
        {
            if (text[index] == '<')
            {
                var end = text.IndexOf('>', index + 1);
                if (end < 0)
                {
                    break;
                }

                var tag = text[(index + 1)..end];
                var parts = tag.Split(',');
                if (parts.Length >= 2
                    && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)
                    && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
                {
                    pendingOffset = Math.Max(0, offset);
                    pendingLength = Math.Max(0, length);
                }

                index = end + 1;
                continue;
            }

            var runStart = index;
            while (index < text.Length && text[index] != '<')
            {
                index++;
            }

            var run = text[runStart..index];
            if (run.Length == 0)
            {
                continue;
            }

            builder.Append(run);
            if (pendingOffset >= 0)
            {
                // The tag's length counts characters starting at the tag: keep the span inside its run.
                var length = Math.Min(pendingLength, run.Length);
                spans.Add((pendingOffset, length > 0 ? length : run.Length, run[..Math.Min(run.Length, Math.Max(1, length))]));
                pendingOffset = -1;
            }
        }

        var body = builder.ToString();
        if (spans.Count == 0)
        {
            return (body, ImmutableArray<LyricWord>.Empty);
        }

        // Real files occasionally list spans out of order: sort stably by offset, then clamp lengths.
        var words = spans
            .OrderBy(static span => span.Offset)
            .Select(span => new LyricWord(span.Offset, Math.Clamp(span.Length, 1, Math.Max(1, body.Length)), span.Text))
            .ToImmutableArray();
        return (body, words);
    }
}