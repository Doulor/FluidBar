using SkiaSharp;

namespace FluidBar.Island;

/// <summary>Small bounded accent-colour parser cache (hex strings are stable per activity).</summary>
internal sealed class AccentPalette
{
    private readonly Dictionary<string, SKColor> _colors = new(StringComparer.Ordinal);
    private readonly int _capacity;

    public AccentPalette(int capacity = 32)
    {
        _capacity = capacity;
    }

    public int Count => _colors.Count;

    public SKColor Get(string? hex, SKColor fallback)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return fallback;
        }

        if (_colors.TryGetValue(hex, out var cached))
        {
            return cached;
        }

        var parsed = IslandTheme.ParseColor(hex, fallback);
        if (_colors.Count >= _capacity)
        {
            _colors.Clear();
        }

        _colors[hex] = parsed;
        return parsed;
    }

    public void Clear() => _colors.Clear();
}

/// <summary>
/// Bounded cache of decoded cover art keyed by track identity. Bytes arrive with the media snapshot;
/// decoding happens once per track (not per frame) and the cache is cleared when it overflows.
/// <para>
/// Covers are decoded at most <see cref="DefaultMaxDimension"/> pixels on their long edge: the
/// largest painted artwork is ~92 DIP (138 px at 150 % DPI), so keeping a 640×640 (1.6 MB) source in
/// the working set buys nothing but memory. Scaled decode happens in Skia (`SKCodec`) and falls back
/// to a full decode for formats that do not support it.
/// </para>
/// </summary>
internal sealed class ArtworkImageCache : IDisposable
{
    /// <summary>Longest edge kept in the cache (pixels); 3× the largest painted cover at 150 % DPI.</summary>
    public const int DefaultMaxDimension = 256;

    private readonly Dictionary<string, SKImage> _images = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private readonly int _maxDimension;
    private long _pixelBytes;

    public ArtworkImageCache(int capacity = 16, int maxDimension = DefaultMaxDimension)
    {
        // The clipboard page paints up to eight image rows and the media page adds cover + thumbnails.
        // A four-entry cache cleared every time it filled, so every later frame re-decoded rows 1..4.
        // 16 entries stay under ~4 MB at the 256 px cap and cover the whole visible page.
        _capacity = capacity;
        _maxDimension = maxDimension > 0 ? maxDimension : DefaultMaxDimension;
    }

    public int Count => _images.Count;

    /// <summary>Retained pixel bytes of the cached covers (BGRA, actual decoded size).</summary>
    public long PixelBytes => _pixelBytes;

    /// <summary>Covers decoded since construction (cache misses).</summary>
    public long Builds { get; private set; }

    /// <summary>Returns the decoded cover for a key, decoding <paramref name="data"/> on first use.</summary>
    public SKImage? Get(string? key, byte[]? data)
    {
        if (string.IsNullOrEmpty(key) || data is null || data.Length == 0)
        {
            return null;
        }

        if (_images.TryGetValue(key, out var cached))
        {
            return cached;
        }

        SKImage? image = null;
        try
        {
            image = DecodeImage(data);
        }
        catch (Exception)
        {
            // Undecodable cover: fall back to the gradient placeholder.
            return null;
        }

        if (image is null)
        {
            return null;
        }

        if (_images.Count >= _capacity)
        {
            foreach (var old in _images.Values)
            {
                old.Dispose();
            }

            _images.Clear();
            _pixelBytes = 0;
        }

        _images[key] = image;
        _pixelBytes += image.Info.Width * (long)image.Info.Height * 4;
        Builds++;
        return image;
    }

    /// <summary>
    /// Decodes a cover, scaling it down to <see cref="_maxDimension"/> on the long edge when needed.
    /// Uses Skia's scaled `SKCodec` decode when the format supports it; otherwise keeps the full decode.
    /// </summary>
    private SKImage? DecodeImage(byte[] data)
    {
        using var encoded = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(encoded);
        if (codec is not null)
        {
            var source = codec.Info;
            if (source.Width > 0 && source.Height > 0)
            {
                var longest = Math.Max(source.Width, source.Height);
                if (longest > _maxDimension)
                {
                    var scale = _maxDimension / (double)longest;
                    var target = new SKImageInfo(
                        Math.Max(1, (int)Math.Round(source.Width * scale)),
                        Math.Max(1, (int)Math.Round(source.Height * scale)),
                        SKColorType.Bgra8888,
                        SKAlphaType.Premul);
                    using var scaled = new SKBitmap(target);
                    if (codec.GetPixels(target, scaled.GetPixels()) is SKCodecResult.Success
                        or SKCodecResult.IncompleteInput)
                    {
                        return SKImage.FromBitmap(scaled);
                    }
                }
            }
        }

        using var bitmap = SKBitmap.Decode(data);
        if (bitmap is null)
        {
            return null;
        }

        var longestEdge = Math.Max(bitmap.Width, bitmap.Height);
        if (longestEdge <= _maxDimension)
        {
            return SKImage.FromBitmap(bitmap);
        }

        // Scaled codec decode was not available for this payload (or produced an error): fall back to
        // decoding then resizing so an oversized cover can never enter the cache at full size.
        var factor = _maxDimension / (double)longestEdge;
        var resizedInfo = new SKImageInfo(
            Math.Max(1, (int)Math.Round(bitmap.Width * factor)),
            Math.Max(1, (int)Math.Round(bitmap.Height * factor)),
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var resized = bitmap.Resize(resizedInfo, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return resized is null ? SKImage.FromBitmap(bitmap) : SKImage.FromBitmap(resized);
    }

    public void Dispose()
    {
        foreach (var image in _images.Values)
        {
            image.Dispose();
        }

        _images.Clear();
        _pixelBytes = 0;
    }
}

/// <summary>Bounded cache of artwork gradient shaders keyed by accent/end colour.</summary>
internal sealed class ArtShaderCache : IDisposable
{
    private readonly Dictionary<Key, SKShader> _shaders = [];
    private readonly int _capacity;

    public ArtShaderCache(int capacity = 8)
    {
        _capacity = capacity;
    }

    public int Count => _shaders.Count;

    public SKShader Get(SKColor accent, SKColor end, bool large)
    {
        var key = new Key(accent, end, large);
        if (_shaders.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var size = large ? 92f : 32f;
        var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(size, size),
            [accent, end],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        if (_shaders.Count >= _capacity)
        {
            foreach (var old in _shaders.Values)
            {
                old.Dispose();
            }

            _shaders.Clear();
        }

        _shaders[key] = shader;
        return shader;
    }

    public void Dispose()
    {
        foreach (var shader in _shaders.Values)
        {
            shader.Dispose();
        }

        _shaders.Clear();
    }

    private readonly record struct Key(SKColor Accent, SKColor End, bool Large);
}

/// <summary>Bounded 0..100 integer-to-string cache so percentage labels never allocate per frame.</summary>
internal sealed class NumberTextCache
{
    // Keyed by (value, isPercent): the old single int key let Percent(12) poison Number(12) so the
    // calendar day "12" could be painted as "12%" after the battery showed 12 % (renderer-wide cache).
    private readonly Dictionary<(int Value, bool Percent), string> _text = [];
    private readonly int _capacity;

    public NumberTextCache(int capacity = 128)
    {
        _capacity = capacity;
    }

    /// <summary>Cached entries.</summary>
    public int Count => _text.Count;

    public string Percent(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (_text.TryGetValue((clamped, true), out var cached))
        {
            return cached;
        }

        if (_text.Count >= _capacity)
        {
            _text.Clear();
        }

        var text = clamped.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        _text[(clamped, true)] = text;
        return text;
    }

    public string Number(int value)
    {
        if (_text.TryGetValue((value, false), out var cached))
        {
            return cached;
        }

        if (_text.Count >= _capacity)
        {
            _text.Clear();
        }

        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _text[(value, false)] = text;
        return text;
    }
}
