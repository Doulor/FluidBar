using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>
/// Pure LRC parser (no I/O). Supports metadata tags, multiple timestamps per line, 2- or 3-digit
/// fractions and the <c>[offset:]</c> tag. Malformed lines are skipped, never thrown.
/// </summary>
public static class LrcParser
{
    /// <summary>Parses LRC text. Returns a document with an empty line list when nothing valid is found.</summary>
    public static LyricsDocument Parse(string? lrc, string source = "lrc")
    {
        var builder = ImmutableArray.CreateBuilder<LyricsLine>();
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return LyricsDocument.Empty with { Source = source };
        }

        var title = string.Empty;
        var artist = string.Empty;
        var offsetMs = 0;
        var pending = new List<(TimeSpan Time, string Text)>();

        foreach (var rawLine in lrc.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var timestamps = new List<TimeSpan>();
            var index = 0;
            var metadataOnly = true;
            while (index < line.Length && line[index] == '[')
            {
                var close = line.IndexOf(']', index + 1);
                if (close < 0)
                {
                    break;
                }

                var tag = line.Substring(index + 1, close - index - 1);
                if (TryParseTimestamp(tag, out var time))
                {
                    timestamps.Add(time);
                    metadataOnly = false;
                }
                else if (TryParseMetadata(tag, out var key, out var value))
                {
                    switch (key)
                    {
                        case "ti":
                            title = value;
                            break;
                        case "ar":
                            artist = value;
                            break;
                        case "offset":
                            offsetMs = ParseOffset(value);
                            break;
                    }
                }

                index = close + 1;
            }

            if (metadataOnly)
            {
                continue;
            }

            if (timestamps.Count == 0)
            {
                continue;
            }

            var text = line[index..].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            foreach (var timestamp in timestamps)
            {
                pending.Add((timestamp, text));
            }
        }

        if (pending.Count == 0)
        {
            return LyricsDocument.Empty with { Title = title, Artist = artist, Source = source };
        }

        var offset = TimeSpan.FromMilliseconds(offsetMs);
        foreach (var (time, text) in pending)
        {
            var adjusted = time - offset;
            if (adjusted < TimeSpan.Zero)
            {
                adjusted = TimeSpan.Zero;
            }

            builder.Add(new LyricsLine(adjusted, text));
        }

        builder.Sort(static (left, right) => left.Time.CompareTo(right.Time));
        return new LyricsDocument(title, artist, source, builder.ToImmutable());
    }

    /// <summary>Merges translation lines (same timestamps) into the main document.</summary>
    public static LyricsDocument MergeTranslations(LyricsDocument? main, LyricsDocument? translations)
    {
        if (main is null || main.IsEmpty || translations is null || translations.IsEmpty)
        {
            return main ?? LyricsDocument.Empty;
        }

        var translationByTime = new Dictionary<long, string>();
        foreach (var line in translations.Lines)
        {
            translationByTime[(long)line.Time.TotalMilliseconds] = line.Text;
        }

        var merged = ImmutableArray.CreateBuilder<LyricsLine>(main.Lines.Length);
        foreach (var line in main.Lines)
        {
            merged.Add(translationByTime.TryGetValue((long)line.Time.TotalMilliseconds, out var translation)
                ? line with { Translation = translation }
                : line);
        }

        return main with { Lines = merged.ToImmutable() };
    }

    private static bool TryParseTimestamp(string tag, out TimeSpan time)
    {
        time = default;
        var colon = tag.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        var minutesText = tag[..colon].Trim();
        if (!int.TryParse(minutesText, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes < 0)
        {
            return false;
        }

        var rest = tag[(colon + 1)..].Trim();
        var dot = rest.IndexOf('.');
        string secondsText;
        string fractionText;
        if (dot < 0)
        {
            secondsText = rest;
            fractionText = string.Empty;
        }
        else
        {
            secondsText = rest[..dot];
            fractionText = rest[(dot + 1)..];
        }

        if (!int.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds is < 0 or > 59)
        {
            return false;
        }

        var milliseconds = 0;
        if (fractionText.Length > 0)
        {
            if (fractionText.Length > 3
                || !int.TryParse(fractionText, NumberStyles.None, CultureInfo.InvariantCulture, out var fraction))
            {
                return false;
            }

            milliseconds = fractionText.Length switch
            {
                1 => fraction * 100,
                2 => fraction * 10,
                _ => fraction,
            };
        }

        time = new TimeSpan(0, 0, minutes, seconds, milliseconds);
        return true;
    }

    private static bool TryParseMetadata(string tag, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        var colon = tag.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        key = tag[..colon].Trim().ToLowerInvariant();
        value = tag[(colon + 1)..].Trim();
        return key is "ti" or "ar" or "al" or "offset" or "by" or "length";
    }

    private static int ParseOffset(string value)
    {
        var text = value.Trim();
        var sign = 1;
        if (text.StartsWith('+'))
        {
            text = text[1..];
        }
        else if (text.StartsWith('-'))
        {
            sign = -1;
            text = text[1..];
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)
            ? sign * offset
            : 0;
    }

    /// <summary>Decodes a base64-encoded lyric payload (Kugou returns base64 content).</summary>
    public static string? TryDecodeBase64(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(encoded.Trim());
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
