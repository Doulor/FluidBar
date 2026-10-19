namespace FluidBar.Platform.Windows.Media;

/// <summary>Image dimensions parsed from a header without decoding pixels.</summary>
public readonly record struct ImageDimensions(int Width, int Height)
{
    public long PixelCount => (long)Width * Height;
}

/// <summary>
/// Pure header reader for the image formats GSMTC artwork can return (PNG, JPEG, GIF, BMP).
/// The platform layer never decodes artwork (no UI/imaging dependency): it enforces a decoded-pixel
/// budget from the header and hands raw bytes to the App for a single <c>DecodePixelWidth</c> decode.
/// </summary>
public static class ImageHeaderReader
{
    /// <summary>Reads dimensions from PNG/JPEG/GIF/BMP headers; null when unknown or truncated.</summary>
    public static ImageDimensions? TryReadDimensions(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && IsPng(data))
        {
            return ReadPng(data);
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            return ReadJpeg(data);
        }

        if (data.Length >= 30 && IsWebP(data))
        {
            return ReadWebP(data);
        }

        if (data.Length >= 10 && IsGif(data))
        {
            return ReadGif(data);
        }

        if (data.Length >= 26 && data[0] == (byte)'B' && data[1] == (byte)'M')
        {
            return ReadBmp(data);
        }

        return null;
    }

    /// <summary>Returns the MIME type for the recognised header, or "application/octet-stream".</summary>
    public static string DetectContentType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && IsPng(data))
        {
            return "image/png";
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8)
        {
            return "image/jpeg";
        }

        if (data.Length >= 30 && IsWebP(data))
        {
            return "image/webp";
        }

        if (data.Length >= 3 && IsGif(data))
        {
            return "image/gif";
        }

        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
        {
            return "image/bmp";
        }

        return "application/octet-stream";
    }

    /// <summary>True for a RIFF/WEBP container (KuGou stores its cached cover art as WebP).</summary>
    private static bool IsWebP(ReadOnlySpan<byte> data)
        => data.Length >= 30
           && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
           && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P';

    /// <summary>
    /// Reads the canvas size from a WebP container: VP8X carries the canvas size explicitly, lossy VP8
    /// and lossless VP8L pack it into their first chunk.
    /// </summary>
    private static ImageDimensions? ReadWebP(ReadOnlySpan<byte> data)
    {
        var chunk = (char)data[12] + ((char)data[13]).ToString() + (char)data[14] + (char)data[15];
        switch (chunk)
        {
            case "VP8X":
                // 4-byte flags, then 3-byte canvas width-1 and 3-byte canvas height-1 (little-endian).
                var canvasWidth = 1 + (data[24] | (data[25] << 8) | (data[26] << 16));
                var canvasHeight = 1 + (data[27] | (data[28] << 8) | (data[29] << 16));
                return IsSane(canvasWidth, canvasHeight) ? new ImageDimensions(canvasWidth, canvasHeight) : null;

            case "VP8 ":
                // Chunk payload: 3-byte frame tag, 3-byte start code, then 14-bit width/height.
                if (data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A)
                {
                    return null;
                }

                var lossyWidth = (data[26] | (data[27] << 8)) & 0x3FFF;
                var lossyHeight = (data[28] | (data[29] << 8)) & 0x3FFF;
                return IsSane(lossyWidth, lossyHeight) ? new ImageDimensions(lossyWidth, lossyHeight) : null;

            case "VP8L":
                if (data[20] != 0x2F)
                {
                    return null;
                }

                var bits = (uint)(data[21] | (data[22] << 8) | (data[23] << 16) | (data[24] << 24));
                var losslessWidth = (int)(bits & 0x3FFF) + 1;
                var losslessHeight = (int)((bits >> 14) & 0x3FFF) + 1;
                return IsSane(losslessWidth, losslessHeight) ? new ImageDimensions(losslessWidth, losslessHeight) : null;

            default:
                return null;
        }
    }

    private static bool IsGif(ReadOnlySpan<byte> data)
        => data.Length >= 3 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F';

    private static bool IsPng(ReadOnlySpan<byte> data)
        => data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
           && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

    private static ImageDimensions? ReadPng(ReadOnlySpan<byte> data)
    {
        // IHDR must be the first chunk; width/height are big-endian at offsets 16 and 20.
        var width = ReadInt32BigEndian(data.Slice(16, 4));
        var height = ReadInt32BigEndian(data.Slice(20, 4));
        return IsSane(width, height) ? new ImageDimensions(width, height) : null;
    }

    private static ImageDimensions? ReadGif(ReadOnlySpan<byte> data)
    {
        // Logical screen descriptor: little-endian 16-bit width/height at offsets 6 and 8.
        var width = data[6] | (data[7] << 8);
        var height = data[8] | (data[9] << 8);
        return IsSane(width, height) ? new ImageDimensions(width, height) : null;
    }

    private static ImageDimensions? ReadBmp(ReadOnlySpan<byte> data)
    {
        // BITMAPINFOHEADER: little-endian int32 width at 18, height at 22 (height may be negative).
        var width = Math.Abs(BitConverter.ToInt32(data.Slice(18, 4)));
        var height = Math.Abs(BitConverter.ToInt32(data.Slice(22, 4)));
        return IsSane(width, height) ? new ImageDimensions(width, height) : null;
    }

    private static ImageDimensions? ReadJpeg(ReadOnlySpan<byte> data)
    {
        var offset = 2;
        while (offset + 9 < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = data[offset + 1];
            // SOF0..SOF15 excluding DHT(C4), JPG(C8), DAC(CC).
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var height = (data[offset + 5] << 8) | data[offset + 6];
                var width = (data[offset + 7] << 8) | data[offset + 8];
                return IsSane(width, height) ? new ImageDimensions(width, height) : null;
            }

            if (marker is 0xD8 or 0xD9 or 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                offset += 2;
                continue;
            }

            if (offset + 4 > data.Length)
            {
                return null;
            }

            var segmentLength = (data[offset + 2] << 8) | data[offset + 3];
            if (segmentLength < 2)
            {
                return null;
            }

            offset += 2 + segmentLength;
        }

        return null;
    }

    private static bool IsSane(int width, int height)
        => width > 0 && height > 0 && width <= 100_000 && height <= 100_000;

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> data)
        => (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
}

/// <summary>One artwork blob kept in the cache.</summary>
/// <param name="Data">Raw encoded image bytes (PNG/JPEG/GIF/BMP).</param>
/// <param name="ContentType">Detected MIME type.</param>
/// <param name="Dimensions">Decoded dimensions from the header.</param>
public sealed record ArtworkEntry(byte[] Data, string ContentType, ImageDimensions Dimensions)
{
    /// <summary>Approximate memory cost of the entry (encoded bytes + object overhead).</summary>
    public long ApproximateBytes => Data.LongLength + 128;
}

/// <summary>Byte/entry bounded LRU artwork cache.</summary>
/// <remarks>
/// Budgets (spec §10.1 #14): decoded pixels per image and total encoded bytes. Entries are keyed by
/// track key; the cache never decodes pixels. Eviction is LRU and every removal is counted.
/// </remarks>
public sealed class ArtworkCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<CacheItem>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheItem> _lru = new();
    private long _totalBytes;
    private long _hits;
    private long _misses;
    private long _evictions;
    private long _rejected;
    private int _disposed;

    public ArtworkCache(long maxBytes = 32L * 1024 * 1024, int maxEntries = 64, int maxDecodedPixels = 16_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDecodedPixels);
        MaxBytes = maxBytes;
        MaxEntries = maxEntries;
        MaxDecodedPixels = maxDecodedPixels;
    }

    public long MaxBytes { get; }

    public int MaxEntries { get; }

    public long MaxDecodedPixels { get; }

    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                return _totalBytes;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public long Hits => Interlocked.Read(ref _hits);

    public long Misses => Interlocked.Read(ref _misses);

    public long Evictions => Interlocked.Read(ref _evictions);

    public long Rejected => Interlocked.Read(ref _rejected);

    /// <summary>Tries to get a cached artwork entry and marks it most-recently used.</summary>
    public bool TryGet(string key, out ArtworkEntry? entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(key) || Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Increment(ref _misses);
            return false;
        }

        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                Interlocked.Increment(ref _misses);
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            entry = node.Value.Entry;
            Interlocked.Increment(ref _hits);
            return true;
        }
    }

    /// <summary>
    /// Adds encoded artwork bytes. Returns null when rejected (unknown/oversized dimensions, too large)
    /// or when the cache is disposed; returns the parsed entry on success.
    /// </summary>
    public ArtworkEntry? Add(string key, ReadOnlySpan<byte> data)
    {
        if (string.IsNullOrEmpty(key) || data.IsEmpty || Volatile.Read(ref _disposed) != 0)
        {
            return null;
        }

        var dimensions = ImageHeaderReader.TryReadDimensions(data);
        if (dimensions is null || dimensions.Value.PixelCount > MaxDecodedPixels)
        {
            Interlocked.Increment(ref _rejected);
            return null;
        }

        var entry = new ArtworkEntry(data.ToArray(), ImageHeaderReader.DetectContentType(data), dimensions.Value);
        if (entry.ApproximateBytes > MaxBytes)
        {
            Interlocked.Increment(ref _rejected);
            return null;
        }

        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _totalBytes -= existing.Value.Entry.ApproximateBytes;
            }

            var node = _lru.AddFirst(new CacheItem(key, entry));
            _map[key] = node;
            _totalBytes += entry.ApproximateBytes;

            while (_totalBytes > MaxBytes || _map.Count > MaxEntries)
            {
                var last = _lru.Last;
                if (last is null)
                {
                    break;
                }

                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
                _totalBytes -= last.Value.Entry.ApproximateBytes;
                Interlocked.Increment(ref _evictions);
            }
        }

        return entry;
    }

    /// <summary>Removes one entry (e.g. a stale track).</summary>
    public bool Remove(string key)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                return false;
            }

            _map.Remove(key);
            _lru.Remove(node);
            _totalBytes -= node.Value.Entry.ApproximateBytes;
            return true;
        }
    }

    /// <summary>Removes every entry.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
            _totalBytes = 0;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Clear();
    }

    private sealed record CacheItem(string Key, ArtworkEntry Entry);
}