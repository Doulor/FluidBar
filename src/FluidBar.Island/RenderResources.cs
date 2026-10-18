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
    private readonly Dictionary<int, string> _text = [];
    private readonly int _capacity;

    public NumberTextCache(int capacity = 128)
    {
        _capacity = capacity;
    }

    public string Percent(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (_text.TryGetValue(clamped, out var cached))
        {
            return cached;
        }

        if (_text.Count >= _capacity)
        {
            _text.Clear();
        }

        var text = clamped.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        _text[clamped] = text;
        return text;
    }

    public string Number(int value)
    {
        if (_text.TryGetValue(value, out var cached))
        {
            return cached;
        }

        if (_text.Count >= _capacity)
        {
            _text.Clear();
        }

        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _text[value] = text;
        return text;
    }
}
