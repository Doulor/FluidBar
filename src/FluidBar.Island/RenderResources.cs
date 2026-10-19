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
/// </summary>
internal sealed class ArtworkImageCache : IDisposable
{
    private readonly Dictionary<string, SKImage> _images = new(StringComparer.Ordinal);
    private readonly int _capacity;

    public ArtworkImageCache(int capacity = 4)
    {
        _capacity = capacity;
    }

    public int Count => _images.Count;

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
            using var bitmap = SKBitmap.Decode(data);
            if (bitmap is not null)
            {
                image = SKImage.FromBitmap(bitmap);
            }
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
        }

        _images[key] = image;
        return image;
    }

    public void Dispose()
    {
        foreach (var image in _images.Values)
        {
            image.Dispose();
        }

        _images.Clear();
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
