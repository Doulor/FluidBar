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
    private static string[]? _directories;
    private static DateTimeOffset _indexBuiltAt = DateTimeOffset.MinValue;

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

    /// <summary>Finds the largest cached cover whose folder starts with the track title (pure-ish; tested).</summary>
    internal static string? FindBestFile(string albumImageRoot, string? title)
    {
        var normalized = Normalize(title);
        if (normalized.Length == 0)
        {
            return null;
        }

        var prefix = normalized + "_";
        var candidates = Directories(albumImageRoot);
        string? bestPath = null;
        long bestLength = -1;
        foreach (var directory in candidates)
        {
            var name = Path.GetFileName(directory);
            if (name.Length == 0
                || (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && !name.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
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

    private static string[] Directories(string albumImageRoot)
    {
        lock (Gate)
        {
            if (_directories is not null && DateTimeOffset.UtcNow - _indexBuiltAt < IndexLifetime)
            {
                return _directories;
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
            _directories = directories;
            _indexBuiltAt = DateTimeOffset.UtcNow;
        }

        return directories;
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
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(title.Length);
        foreach (var character in title.Trim())
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }
}
