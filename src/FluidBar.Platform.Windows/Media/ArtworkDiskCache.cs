using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluidBar.Platform.Windows.Media;

/// <summary>Where an artwork file came from (recorded next to the bytes).</summary>
public enum ArtworkSourceKind
{
    /// <summary>Fetched from the iTunes Search API catalogue.</summary>
    ITunes,

    /// <summary>Read from the player's own on-disk cache.</summary>
    PlayerCache,
}

/// <summary>Metadata recorded next to a cached artwork file.</summary>
/// <param name="Key">Normalised cache key.</param>
/// <param name="SourceKind">Which source produced the bytes.</param>
/// <param name="SourceUrl">URL the bytes came from (empty for local files).</param>
/// <param name="Artist">Artist that was looked up.</param>
/// <param name="Title">Title that was looked up.</param>
/// <param name="Album">Album that was looked up (may be empty).</param>
/// <param name="Score">Match confidence of the accepted result (0..1).</param>
/// <param name="FetchedAtUtc">When the bytes were fetched.</param>
public sealed record ArtworkCacheMetadata(
    string Key,
    ArtworkSourceKind SourceKind,
    string SourceUrl,
    string Artist,
    string Title,
    string Album,
    double Score,
    DateTimeOffset FetchedAtUtc);

/// <summary>An artwork file read back from the disk cache.</summary>
/// <param name="Bytes">Encoded image bytes.</param>
/// <param name="Metadata">Sidecar metadata.</param>
/// <param name="Path">Path of the cached image file.</param>
public sealed record ArtworkCacheEntry(byte[] Bytes, ArtworkCacheMetadata Metadata, string Path);

/// <summary>Options for <see cref="ArtworkDiskCache"/>.</summary>
public sealed class ArtworkDiskCacheOptions
{
    /// <summary>Directory that receives the cached artwork (e.g. <c>%APPDATA%\FluidBar\artwork</c>).</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>Maximum bytes kept on disk.</summary>
    public long MaxBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Maximum number of cache entries.</summary>
    public int MaxEntries { get; init; } = 512;
}

/// <summary>
/// Persistent artwork cache: image bytes plus a JSON sidecar recording the source URL and fetch time.
/// </summary>
/// <remarks>
/// <para>The key is a hash of the normalised artist + title + album, so the same song hits the cache no
/// matter how the player spells it and no matter which source produced the file.</para>
/// <para>Only bytes are cached (never decoded pixels) and every entry is bounded: oversized files are
/// rejected and the store sweeps the oldest entries when it exceeds its budget.</para>
/// </remarks>
public sealed class ArtworkDiskCache
{
    /// <summary>Largest accepted image (larger covers are rejected instead of bloating the cache).</summary>
    public const int MaxArtworkBytes = 4 * 1024 * 1024;

    private readonly ArtworkDiskCacheOptions _options;
    private readonly object _gate = new();
    private DateTime _lastSweepUtc = DateTime.MinValue;

    /// <summary>Creates the cache over a directory (created on demand).</summary>
    /// <param name="options">Cache options.</param>
    public ArtworkDiskCache(ArtworkDiskCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DirectoryPath);
        _options = options;
    }

    /// <summary>Directory holding cached artwork.</summary>
    public string DirectoryPath => _options.DirectoryPath;

    /// <summary>Builds the cache key for a track (normalised, case/decoration insensitive).</summary>
    /// <param name="artist">Artist.</param>
    /// <param name="title">Title.</param>
    /// <param name="album">Album (optional).</param>
    public static string KeyFor(string? artist, string? title, string? album)
    {
        var normalized = string.Join('|',
            ArtworkTitleMatcher.Normalize(artist),
            ArtworkTitleMatcher.Normalize(title),
            ArtworkTitleMatcher.Normalize(album));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash, 0, 10).ToLowerInvariant();
    }

    /// <summary>Reads a cached cover, or null when the track was never fetched.</summary>
    /// <param name="key">Cache key.</param>
    public ArtworkCacheEntry? TryRead(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        var imagePath = ImagePath(key);
        var metadataPath = MetadataPath(key);
        try
        {
            if (!File.Exists(imagePath) || !File.Exists(metadataPath))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(imagePath);
            if (bytes.Length == 0 || bytes.Length > MaxArtworkBytes || ImageHeaderReader.TryReadDimensions(bytes) is null)
            {
                return null;
            }

            var metadata = JsonSerializer.Deserialize<ArtworkCacheMetadata>(File.ReadAllText(metadataPath));
            return metadata is null ? null : new ArtworkCacheEntry(bytes, metadata, imagePath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes bytes plus their metadata; returns the stored entry, or null when rejected.</summary>
    /// <param name="key">Cache key.</param>
    /// <param name="bytes">Encoded image bytes.</param>
    /// <param name="metadata">Source metadata.</param>
    public ArtworkCacheEntry? Write(string key, byte[] bytes, ArtworkCacheMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrEmpty(key) || bytes.Length == 0 || bytes.Length > MaxArtworkBytes
            || ImageHeaderReader.TryReadDimensions(bytes) is null)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_options.DirectoryPath);
            var imagePath = ImagePath(key);
            File.WriteAllBytes(imagePath, bytes);
            File.WriteAllText(MetadataPath(key), JsonSerializer.Serialize(metadata));
            SweepIfDue();
            return new ArtworkCacheEntry(bytes, metadata, imagePath);
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

    private string ImagePath(string key) => Path.Combine(_options.DirectoryPath, key + ".img");

    private string MetadataPath(string key) => Path.Combine(_options.DirectoryPath, key + ".json");

    /// <summary>Drops the oldest entries once the store outgrows its budget (at most once a minute).</summary>
    private void SweepIfDue()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastSweepUtc).TotalSeconds < 60)
            {
                return;
            }

            _lastSweepUtc = now;
            try
            {
                var files = new DirectoryInfo(_options.DirectoryPath)
                    .GetFiles("*.img")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToArray();
                var total = files.Sum(file => file.Length);
                for (var i = _options.MaxEntries; i < files.Length; i++)
                {
                    total -= files[i].Length;
                    files[i].Delete();
                    File.Delete(MetadataPath(Path.GetFileNameWithoutExtension(files[i].Name)));
                }

                for (var i = _options.MaxEntries - 1; i >= 0 && total > _options.MaxBytes; i--)
                {
                    total -= files[i].Length;
                    files[i].Delete();
                    File.Delete(MetadataPath(Path.GetFileNameWithoutExtension(files[i].Name)));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
