using System.Globalization;
using System.Text.RegularExpressions;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>
/// Reads the lyrics KuGou already downloaded to its own cache directory.
/// </summary>
/// <remarks>
/// <para>KuGou never registers with SMTC and its mobile search CDN is not always reachable from a
/// desktop network, so third-party lyric lookups for it are hit and miss. The player keeps a
/// <c>.krc</c> (or newer <c>.lrc</c>) file per played song named <c>{artist} - {title}-{hash}-{songid}-00000000.krc/.lrc</c>;
/// reading that file is both offline and exact — it is the same lyric the player shows.</para>
/// <para>The directory comes from the player's own settings (<c>LyricPath</c>) and is cached: lookups
/// run on the thread pool and never block the UI.</para>
/// </remarks>
public sealed class KugouLocalLyricsProvider : ILyricsProvider
{
    private static readonly TimeSpan IndexLifetime = TimeSpan.FromMinutes(5);

    /// <summary>File name shape: "{artist} - {title}-{32 hex}-{song id}-{suffix}.krc".</summary>
    private static readonly Regex FileNamePattern = new(
        @"^(?<stem>.+)-(?<hash>[0-9a-fA-F]{32})-(?<songid>\d+)-(?<tail>\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string? _directoryOverride;
    private readonly TimeProvider _time;
    private readonly IPlatformLogger _logger;
    private readonly object _gate = new();
    private Dictionary<string, string>? _byFullKey;
    private Dictionary<string, List<string>>? _byTitleKey;
    private DateTimeOffset _indexBuiltAt = DateTimeOffset.MinValue;

    /// <summary>Creates the provider.</summary>
    /// <param name="directory">Lyric directory override (tests); null resolves the player's cache.</param>
    /// <param name="timeProvider">Injected clock for index expiry.</param>
    /// <param name="logger">Platform logger.</param>
    public KugouLocalLyricsProvider(string? directory = null, TimeProvider? timeProvider = null,
        IPlatformLogger? logger = null)
    {
        _directoryOverride = directory;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullPlatformLogger.Instance;
    }

    /// <inheritdoc />
    public string Name => "kugou-local";

    /// <inheritdoc />
    public Task<LyricsDocument?> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
        => Task.Run(() => Load(query, cancellationToken), cancellationToken);

    private LyricsDocument? Load(LyricsQuery query, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var directory = ResolveDirectory();
        if (directory is null)
        {
            return null;
        }

        var path = FindLyricFile(directory, query.Artist, query.Title, out var matchedFromTitleOnly);
        if (path is null)
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        LyricsDocument? document;
        if (Path.GetExtension(path).Equals(".lrc", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(path, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            document = LrcParser.Parse(reader.ReadToEnd(), "kugou-lrc");
        }
        else
        {
            document = KrcDecoder.TryDecode(bytes)?.Document;
        }

        if (document is not { Lines.Length: > 0 })
        {
            _logger.LogDebug(PlatformLogCategory.Media, "lyrics.kugou-local-decode-failed",
                "A KuGou lyric file could not be decoded.", new Dictionary<string, object?>
                {
                    ["file"] = Path.GetFileName(path),
                });
            return null;
        }

        _logger.LogInfo(PlatformLogCategory.Media, "lyrics.kugou-local",
            "Lyrics served from the player's own cache.", new Dictionary<string, object?>
            {
                ["file"] = Path.GetFileName(path),
                ["lines"] = document.Lines.Length,
                ["titleOnlyMatch"] = matchedFromTitleOnly,
            });
        return document;
    }

    /// <summary>Finds the cached lyric file for a track (exposed for tests through <see cref="FindLyricFile"/>).</summary>
    public string? FindLyricFile(string directory, string? artist, string? title, out bool titleOnlyMatch)
    {
        titleOnlyMatch = false;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        EnsureIndex(directory);
        Dictionary<string, string> byFullKey;
        Dictionary<string, List<string>> byTitleKey;
        lock (_gate)
        {
            byFullKey = _byFullKey ?? [];
            byTitleKey = _byTitleKey ?? [];
        }

        var fullKey = BuildLookupKey(artist, title);
        if (byFullKey.TryGetValue(fullKey, out var exact))
        {
            return exact;
        }

        if (!byTitleKey.TryGetValue(Normalize(title), out var candidates) || candidates.Count == 0)
        {
            return null;
        }

        titleOnlyMatch = true;
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        // Several songs share the title: prefer the file whose artist line mentions the queried
        // artist, then the most recently written file (the player rewrites the current song's lyric).
        var artistTokens = Tokenize(artist);
        var best = candidates[0];
        var bestScore = int.MinValue;
        var bestWriteTime = DateTime.MinValue;
        foreach (var candidate in candidates)
        {
            var stem = Path.GetFileNameWithoutExtension(candidate);
            var match = FileNamePattern.Match(stem);
            var stemText = match.Success ? match.Groups["stem"].Value : stem;
            var separator = stemText.LastIndexOf(" - ", StringComparison.Ordinal);
            var fileArtist = separator > 0 ? stemText[..separator] : string.Empty;
            var score = 0;
            foreach (var token in artistTokens)
            {
                if (fileArtist.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    score++;
                }
            }

            DateTime writeTime;
            try
            {
                writeTime = File.GetLastWriteTimeUtc(candidate);
            }
            catch (IOException)
            {
                writeTime = DateTime.MinValue;
            }

            if (score > bestScore || (score == bestScore && writeTime > bestWriteTime))
            {
                best = candidate;
                bestScore = score;
                bestWriteTime = writeTime;
            }
        }

        return best;
    }

    private void EnsureIndex(string directory)
    {
        lock (_gate)
        {
            if (_byFullKey is not null && _time.GetUtcNow() - _indexBuiltAt < IndexLifetime)
            {
                return;
            }
        }

        Dictionary<string, string> byFull = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> byTitle = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var extension in new[] { "*.krc", "*.lrc" })
            {
                foreach (var path in Directory.EnumerateFiles(directory, extension, SearchOption.TopDirectoryOnly))
                {
                    var stem = Path.GetFileNameWithoutExtension(path);
                    var match = FileNamePattern.Match(stem);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var stemText = match.Groups["stem"].Value;
                    var separator = stemText.LastIndexOf(" - ", StringComparison.Ordinal);
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var artist = stemText[..separator].Trim();
                    var title = stemText[(separator + 3)..].Trim();
                    if (title.Length == 0)
                    {
                        continue;
                    }

                    byFull[BuildLookupKey(artist, title)] = path;
                    var titleKey = Normalize(title);
                    if (!byTitle.TryGetValue(titleKey, out var list))
                    {
                        byTitle[titleKey] = list = [];
                    }

                    list.Add(path);
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        lock (_gate)
        {
            _byFullKey = byFull;
            _byTitleKey = byTitle;
            _indexBuiltAt = _time.GetUtcNow();
        }
    }

    /// <summary>Directory that holds the player's lyric cache, or null when there is none.</summary>
    private string? ResolveDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_directoryOverride))
        {
            return Directory.Exists(_directoryOverride) ? _directoryOverride : null;
        }

        foreach (var directory in CandidateDirectories())
        {
            if (directory is not null && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return null;
    }

    private static IEnumerable<string?> CandidateDirectories()
    {
        var configured = PlayerPlaybackStateReader.TryReadKugouLyricDirectory();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
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

        if (!string.IsNullOrWhiteSpace(roaming))
        {
            yield return Path.Combine(roaming, "KuGou8", "Lyric");
        }
    }

    /// <summary>Lookup key shared by the index and the query ("artist - title", normalized).</summary>
    public static string BuildLookupKey(string? artist, string? title)
        => Normalize(artist) + " - " + Normalize(title);

    /// <summary>Trims, lower-cases and collapses whitespace so file names and tags compare equal.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
            lastWasSpace = false;
        }

        return builder.ToString();
    }

    private static List<string> Tokenize(string? artist)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(artist))
        {
            return tokens;
        }

        foreach (var token in artist.Split(['、', ',', '/', '&', ';', ' ', '·'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = token.Trim();
            if (trimmed.Length > 0)
            {
                tokens.Add(trimmed);
            }
        }

        return tokens;
    }
}
