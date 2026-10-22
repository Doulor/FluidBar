using System.Text;
using System.Text.RegularExpressions;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>
/// Reads the lyrics Soda Music (汽水音乐) already cached on disk while playing.
/// </summary>
/// <remarks>
/// <para>Soda never registers with SMTC and its <c>GetTrackV2</c> catalogue API needs signed device
/// parameters, so online lookups for the tracks it plays (Douyin-catalogue songs) are not reachable.
/// Like KuGou it caches what it plays: the Luna resource cache keeps one entry per played track whose
/// payload embeds the lyric exactly as the player shows it, under
/// <c>%APPDATA%\SodaMusic\LunaCacheV2\entries.db</c>. Reading that file is offline and exact.</para>
/// <para>The file is the LMDB store of the cache, but the lyric payload is plain UTF-8 text inside the
/// value bytes: consecutive timed lines in either Soda's word-timed shape
/// (<c>[startMs,durMs]&lt;startMs,durMs,0&gt;word …</c>) or plain LRC (<c>[mm:ss.xx]text</c>), preceded
/// by the track metadata of the same cache entry (title and artist appear within a few KiB of the
/// lyric). The reader therefore scans the file for lyric blocks and keeps, per block, the readable
/// strings of the preceding window; a query matches when the window mentions the title (and, when
/// possible, the artist). Entries are appended to the file as tracks are played, so among equal
/// candidates the latest offset wins — the same "the player rewrites the current song" heuristic the
/// KuGou provider uses via file write times.</para>
/// </remarks>
public sealed class QishuiLocalLyricsProvider : ILyricsProvider
{
    private static readonly TimeSpan IndexLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The cache grows for the lifetime of the player; anything past this is treated as corrupt input
    /// rather than something to scan (lyrics are a few KiB each, so even hundreds of tracks stay far
    /// below the cap).
    /// </summary>
    private const long MaxFileBytes = 256 * 1024 * 1024;

    /// <summary>Maximum indexed lyric blocks (defensive bound; the cache holds a few hundred at most).</summary>
    private const int MaxBlocks = 512;

    /// <summary>How far back from a lyric block the track metadata can sit (characters of the decoded
    /// payload; replacement characters from invalid UTF-8 sequences only make the window wider).</summary>
    private const int TitleWindowChars = 8 * 1024;

    /// <summary>A lyric line in Soda's word-timed shape: [startMs,durationMs]&lt;…&gt;text.</summary>
    /// <remarks>
    /// Not anchored to the line start: the payload is embedded in binary framing, so a line picked up
    /// by the line splitter can start with stray control bytes (e.g. <c>0x1F</c>) right before the
    /// bracket. The match position decides where the clean line text begins.
    /// </remarks>
    private static readonly Regex WordTimedLine = new(
        @"\[(?<start>\d{2,7}),(?<dur>\d{2,7})\]<", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A lyric line in plain LRC shape: [mm:ss(.xx)]text.</summary>
    private static readonly Regex LrcLine = new(
        @"\[\d{1,3}:\d{2}(?:[.:]\d{1,3})?\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Characters that legitimately appear inside a lyric line (ASCII, CJK, CJK punctuation, fullwidth
    /// forms). Anything else is the cache's binary framing.
    /// </summary>
    private static readonly Regex NonLyricCharacter = new(
        @"[^\x20-\x7E\u4E00-\u9FFF\u3000-\u303F\uFF00-\uFFEF]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Run of printable text inside the binary payload (ASCII plus CJK ranges).</summary>
    private static readonly Regex ReadableRun = new(
        @"[\x20-\x7E\u4E00-\u9FFF\u3000-\u303F\uFF00-\uFFEF]{3,}",
        RegexOptions.Compiled);

    /// <summary>A timed lyric block found in the cache, with the searchable strings around it.</summary>
    /// <param name="Offset">Character offset in the decoded payload (ordering only: later = fresher).</param>
    private sealed record CachedBlock(string[] WindowStrings, long Offset, string Text);

    private readonly string? _fileOverride;
    private readonly TimeProvider _time;
    private readonly IPlatformLogger _logger;
    private readonly object _gate = new();
    private List<CachedBlock>? _blocks;
    private DateTimeOffset _indexBuiltAt = DateTimeOffset.MinValue;

    /// <summary>Creates the provider.</summary>
    /// <param name="filePath">Cache file override (tests); null resolves the player's cache.</param>
    /// <param name="timeProvider">Injected clock for index expiry.</param>
    /// <param name="logger">Platform logger.</param>
    public QishuiLocalLyricsProvider(string? filePath = null, TimeProvider? timeProvider = null,
        IPlatformLogger? logger = null)
    {
        _fileOverride = filePath;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullPlatformLogger.Instance;
    }

    /// <inheritdoc />
    public string Name => "qishui-local";

    /// <inheritdoc />
    public Task<LyricsDocument?> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
        => Task.Run(() => Load(query, cancellationToken), cancellationToken);

    private LyricsDocument? Load(LyricsQuery query, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var file = ResolveFile();
        if (file is null)
        {
            return null;
        }

        var blocks = EnsureIndex(file);
        if (blocks.Count == 0)
        {
            return null;
        }

        var titleKey = KugouLocalLyricsProvider.Normalize(query.Title);
        var artistKey = KugouLocalLyricsProvider.Normalize(query.Artist);
        if (titleKey.Length == 0)
        {
            return null;
        }

        CachedBlock? best = null;
        var bestScore = 0;
        foreach (var block in blocks)
        {
            var score = Score(block.WindowStrings, titleKey, artistKey);
            // The cache appends as tracks are played, so a later block is a fresher copy of the same
            // song; among equal scores the latest offset wins.
            if (score > bestScore || (score == bestScore && best is not null && score > 0 && block.Offset > best.Offset))
            {
                best = block;
                bestScore = score;
            }
        }

        if (best is null || bestScore == 0)
        {
            return null;
        }

        var document = ParseBlock(best.Text);
        if (document is null)
        {
            _logger.LogDebug(PlatformLogCategory.Media, "lyrics.qishui-local-parse-failed",
                "A Soda lyric block could not be parsed.", new Dictionary<string, object?>
                {
                    ["offset"] = best.Offset,
                });
            return null;
        }

        _logger.LogInfo(PlatformLogCategory.Media, "lyrics.qishui-local",
            "Lyrics served from Soda Music's own cache.", new Dictionary<string, object?>
            {
                ["lines"] = document.Lines.Length,
                ["titleOnlyMatch"] = bestScore <= 1,
                ["offset"] = best.Offset,
            });
        return document;
    }

    /// <summary>Scores one block against the query: 2 = title+artist, 1 = title only, 0 = no match.</summary>
    /// <remarks>
    /// The match tolerates spacing differences as a fallback: the queue cache writes
    /// <c>Just Like This （Remix）</c> while the cached media detail stores <c>Just Like This（Remix）</c>
    /// (measured), so a whitespace-folded comparison runs after the plain one.
    /// </remarks>
    internal static int Score(string[] windowStrings, string titleKey, string artistKey)
    {
        var titleMatch = false;
        var artistMatch = string.IsNullOrEmpty(artistKey);
        var titleFolded = titleKey.Replace(" ", string.Empty, StringComparison.Ordinal);
        var artistFolded = artistKey.Replace(" ", string.Empty, StringComparison.Ordinal);
        foreach (var candidate in windowStrings)
        {
            var normalized = KugouLocalLyricsProvider.Normalize(candidate);
            if (!titleMatch && (normalized.Contains(titleKey, StringComparison.Ordinal)
                || normalized.Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Contains(titleFolded, StringComparison.Ordinal)))
            {
                titleMatch = true;
            }

            if (!artistMatch && (normalized.Contains(artistKey, StringComparison.Ordinal)
                || normalized.Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Contains(artistFolded, StringComparison.Ordinal)))
            {
                artistMatch = true;
            }

            if (titleMatch && artistMatch)
            {
                break;
            }
        }

        if (!titleMatch)
        {
            return 0;
        }

        return artistMatch ? 2 : 1;
    }

    /// <summary>Parses one lyric block into a document (exposed for tests).</summary>
    public static LyricsDocument? ParseBlock(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Tolerate framing bytes before the first bracket (the scanner strips them, but this is public).
        var bracket = text.IndexOf('[');
        if (bracket < 0)
        {
            return null;
        }

        if (bracket > 0)
        {
            text = text[bracket..];
        }

        var firstLineEnd = text.IndexOf('\n');
        var firstLine = firstLineEnd < 0 ? text : text[..firstLineEnd];
        if (WordTimedLine.IsMatch(firstLine))
        {
            // The word-timed shape shares the KRC line grammar; the decoder only needs the [ti]/[ar]
            // headers for the reported title, which this source does not carry.
            var decoded = KrcDecoder.Parse(text);
            return decoded is null ? null : decoded.Document with { Source = "qishui-krc" };
        }

        if (LrcLine.IsMatch(firstLine))
        {
            var parsed = LrcParser.Parse(text, "qishui-lrc");
            return parsed.IsEmpty ? null : parsed;
        }

        return null;
    }

    private List<CachedBlock> EnsureIndex(string file)
    {
        lock (_gate)
        {
            if (_blocks is not null && _time.GetUtcNow() - _indexBuiltAt < IndexLifetime)
            {
                return _blocks;
            }
        }

        var blocks = new List<CachedBlock>();
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length == 0 || info.Length > MaxFileBytes)
            {
                return blocks;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);
            var bytes = reader.ReadBytes((int)stream.Length);
            // The payload is UTF-8 with binary framing around it; lossy decoding is exactly what the
            // scan wants — framing bytes become replacement characters, embedded text survives.
            var text = Encoding.UTF8.GetString(bytes);

            // Split into candidate blocks: runs of consecutive timed lines of either shape. Matching
            // line by line and grouping runs keeps the regex work linear in the file size.
            var blockStart = -1;
            var blockBuilder = new StringBuilder();
            var blockLines = 0;
            var blockOffsetChars = 0;
            foreach (var line in EnumerateLines(text))
            {
                // A lyric line may carry framing bytes before the bracket; the match position gives the
                // clean text the block keeps.
                var wordMatch = WordTimedLine.Match(line.Text);
                var lrcMatch = wordMatch.Success ? null : LrcLine.Match(line.Text);
                if (wordMatch.Success || lrcMatch is { Success: true })
                {
                    var clean = wordMatch.Success ? line.Text[wordMatch.Index..] : line.Text[lrcMatch!.Index..];
                    clean = CutBinaryFraming(clean);
                    if (blockStart < 0)
                    {
                        blockStart = line.Index;
                        blockBuilder.Clear();
                        blockLines = 0;
                        blockOffsetChars = line.Index;
                    }

                    blockBuilder.Append(clean).Append('\n');
                    blockLines++;
                }
                else if (blockStart >= 0)
                {
                    if (blockLines >= 3)
                    {
                        AddBlock(blocks, text, blockStart, blockOffsetChars, blockBuilder.ToString());
                    }

                    blockStart = -1;
                }
            }

            if (blockStart >= 0 && blockLines >= 3)
            {
                AddBlock(blocks, text, blockStart, blockOffsetChars, blockBuilder.ToString());
            }
        }
        catch (IOException)
        {
            return blocks;
        }
        catch (UnauthorizedAccessException)
        {
            return blocks;
        }

        lock (_gate)
        {
            _blocks = blocks;
            _indexBuiltAt = _time.GetUtcNow();
        }

        return blocks;
    }

    private void AddBlock(List<CachedBlock> blocks, string text, int blockStartChar, int blockOffsetChars,
        string blockText)
    {
        if (blocks.Count >= MaxBlocks)
        {
            return;
        }

        // The window before the block holds the same cache entry's metadata (the track object sits in
        // the value bytes ahead of the lyric). Slicing the decoded text keeps the offsets consistent:
        // invalid UTF-8 in the payload becomes replacement characters, so byte-based offsets would
        // drift and eventually leave the buffer.
        var windowStart = Math.Max(0, blockStartChar - TitleWindowChars);
        var window = text[windowStart..blockStartChar];
        var strings = ReadableRun.Matches(window)
            .Select(static match => match.Value)
            .Where(static value => !LooksLikeFraming(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        blocks.Add(new CachedBlock(strings, blockOffsetChars, blockText));
    }

    /// <summary>True for runs that are binary framing artefacts rather than searchable text.</summary>
    private static bool LooksLikeFraming(string value)
    {
        // URLs, field names and protocol noise dominate the window; they never match a query title and
        // only cost memory. Keep the list short and cheap.
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("tos-", StringComparison.OrdinalIgnoreCase)
               || value.All(char.IsDigit);
    }

    /// <summary>
    /// Cuts a lyric line where the cache's binary framing begins.
    /// </summary>
    /// <remarks>
    /// <para>The payload is embedded in an LMDB store with no newline before the entry's MessagePack
    /// trailer, so a song's LAST line runs straight into it (measured 2026-10-22, user screenshot):
    /// <c>…还能不能相见£krcij◆7566176614707430459◆Gentlem…</c> — the £/◆ are raw framing bytes that
    /// survive the lossy UTF-8 decode, then the type tag, the "not_luna_user" field, the track ids and
    /// the neighbouring entry's title. Without a cut, KrcDecoder folds that whole tail into the last
    /// word's text and the island renders it as part of the line.</para>
    /// <para>The cut point is the first character outside the lyric alphabet. A line's own text can
    /// never contain framing bytes, so this can only ever remove a tail that is not lyric.</para>
    /// </remarks>
    private static string CutBinaryFraming(string line)
    {
        var match = NonLyricCharacter.Match(line);
        if (!match.Success || match.Index == 0)
        {
            return line;
        }

        return line[..match.Index].TrimEnd();
    }

    private static IEnumerable<(string Text, int Index)> EnumerateLines(string text)
    {
        var charIndex = 0;
        while (charIndex < text.Length)
        {
            var newline = text.IndexOf('\n', charIndex);
            var end = newline < 0 ? text.Length : newline;
            var line = text[charIndex..end].TrimEnd('\r');
            yield return (line, charIndex);
            charIndex = end + 1;
        }
    }

    /// <summary>Full path of the player's lyric cache, or null when it cannot be resolved.</summary>
    private string? ResolveFile()
    {
        if (!string.IsNullOrWhiteSpace(_fileOverride))
        {
            return File.Exists(_fileOverride) ? _fileOverride : null;
        }

        string? roaming = null;
        try
        {
            roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        catch (Exception)
        {
            // Unresolvable roaming folder: no cache.
        }

        if (string.IsNullOrWhiteSpace(roaming))
        {
            return null;
        }

        var path = Path.Combine(roaming, "SodaMusic", "LunaCacheV2", "entries.db");
        return File.Exists(path) ? path : null;
    }
}
