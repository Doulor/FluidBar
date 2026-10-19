namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Reads the album art KuGou caches for the songs it plays.
/// </summary>
/// <remarks>
/// <para>KuGou registers no SMTC session, so Windows never hands the island a thumbnail. The player
/// does keep the cover it shows in <c>%APPDATA%\KuGou8\ImagesCache\AlbumImg\{title}_{songId}\{size}\</c>,
/// which is the same artwork the user sees in the player — real data, no guessing.</para>
/// <para>Matching is by song title because the folder only carries the title and an internal id. The
/// directory listing is cached for a couple of minutes: lookups only happen when the track changes.</para>
/// </remarks>
internal static class KugouArtworkCache
{
    /// <summary>Largest accepted cover file (the cache holds small thumbs plus a few larger ones).</summary>
    internal const int MaxArtworkBytes = 512 * 1024;

    private static readonly TimeSpan IndexLifetime = TimeSpan.FromMinutes(2);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CachedDirectoryIndex> DirectoryCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads cover bytes for a track title, or null when the player cached none.</summary>
    public static byte[]? TryRead(string? title)
    {
        var root = Root();
        if (root is null)
        {
            return null;
        }

        string? path;
        try
        {
            path = FindBestFile(root, title);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (path is null)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxArtworkBytes)
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            return ImageHeaderReader.TryReadDimensions(bytes) is null ? null : bytes;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Finds the largest cached cover for a track title (pure-ish; unit-tested).</summary>
    /// <remarks>
    /// The cache folder is named <c>{title}_{songId}</c>, but the player writes the title in its own
    /// typography: "Untamed（失 控）_203532254" for the track the window title reports as
    /// "Untamed (失 控)". Matching therefore folds full-width punctuation/space and falls back to a
    /// whitespace-free comparison, otherwise a perfectly cached cover is never found.
    /// </remarks>
    internal static string? FindBestFile(string albumImageRoot, string? title)
    {
        var target = Normalize(title);
        if (target.Length == 0)
        {
            return null;
        }

        var candidates = Directories(albumImageRoot);
        string? bestPath = null;
        long bestLength = -1;
        foreach (var directory in candidates)
        {
            var folderTitle = FolderTitle(Path.GetFileName(directory));
            if (!Matches(folderTitle, target))
            {
                continue;
            }

            foreach (var file in EnumerateImages(directory))
            {
                long length;
                try
                {
                    length = new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    continue;
                }

                if (length <= 0 || length > MaxArtworkBytes)
                {
                    continue;
                }

                if (length > bestLength)
                {
                    bestPath = file;
                    bestLength = length;
                }
            }
        }

        return bestPath;
    }

    /// <summary>True when a cache folder title denotes the queried track title.</summary>
    internal static bool Matches(string? folderTitle, string? normalizedTitle)
    {
        var foldedFolder = Fold(folderTitle);
        var foldedTitle = Fold(normalizedTitle);
        if (foldedFolder.Length == 0 || foldedTitle.Length == 0)
        {
            return false;
        }

        if (foldedFolder.Equals(foldedTitle, StringComparison.Ordinal)
            || foldedFolder.StartsWith(foldedTitle, StringComparison.Ordinal))
        {
            return true;
        }

        return Compact(foldedFolder).Equals(Compact(foldedTitle), StringComparison.Ordinal);
    }

    /// <summary>Drops the "_songId" suffix the player appends to the cache folder.</summary>
    private static string FolderTitle(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return string.Empty;
        }

        var separator = folderName.LastIndexOf('_');
        if (separator > 0)
        {
            var suffix = folderName[(separator + 1)..];
            if (suffix.Length > 0 && suffix.All(char.IsAsciiDigit))
            {
                return folderName[..separator];
            }
        }

        return folderName;
    }

    /// <summary>Case-insensitive width folding: full-width forms become their half-width equivalents.</summary>
    internal static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            switch (character)
            {
                case >= '\uFF01' and <= '\uFF5E':
                    builder.Append((char)(character - 0xFEE0));
                    break;
                case '\u3000':
                    builder.Append(' ');
                    break;
                case '\u3001':
                    builder.Append(',');
                    break;
                case '\u3002':
                    builder.Append('.');
                    break;
                case '\u2018' or '\u2019':
                    builder.Append('\'');
                    break;
                case '\u201C' or '\u201D':
                    builder.Append('"');
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString().ToLowerInvariant();
    }

    private static string Compact(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<string> EnumerateImages(string directory)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private readonly record struct CachedDirectoryIndex(string[] Directories, DateTimeOffset BuiltAt);

    private static string[] Directories(string albumImageRoot)
    {
        lock (Gate)
        {
            if (DirectoryCache.TryGetValue(albumImageRoot, out var cached)
                && DateTimeOffset.UtcNow - cached.BuiltAt < IndexLifetime)
            {
                return cached.Directories;
            }
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(albumImageRoot);
        }
        catch (DirectoryNotFoundException)
        {
            directories = [];
        }
        catch (UnauthorizedAccessException)
        {
            directories = [];
        }

        lock (Gate)
        {
            DirectoryCache[albumImageRoot] = new CachedDirectoryIndex(directories, DateTimeOffset.UtcNow);
        }

        return directories;
    }

    /// <summary>Clears the cached folder index (tests, or when the player's cache is rebuilt).</summary>
    internal static void InvalidateIndex()
    {
        lock (Gate)
        {
            DirectoryCache.Clear();
        }
    }

    private static string? Root()
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

        var root = Path.Combine(roaming, "KuGou8", "ImagesCache", "AlbumImg");
        return Directory.Exists(root) ? root : null;
    }

    private static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        // Windows file names cannot contain these; the player strips/replaces them when naming folders.
        var folded = Fold(title);
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(folded.Length);
        foreach (var character in folded)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }
}