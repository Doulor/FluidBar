using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Extracts a stable accent colour from album artwork so the progress bar, the audio wave and the
/// karaoke highlight share the cover's colour instead of a generic theme accent.
/// </summary>
/// <remarks>
/// The colour is a saturation-weighted average of the cover's downscaled pixels: grey/black/white
/// detail (which dominates most covers) barely contributes, while a saturated garment or backdrop does.
/// Results are cached per artwork key, so the same cover always yields the same colour (no jitter
/// between frames) and switching tracks immediately yields the new cover's colour.
/// </remarks>
public static class ArtworkAccent
{
    private const int SampleSize = 24;
    private const int MaxCachedKeys = 48;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    /// <summary>Accent hex for a cover, or null when there is no decodable artwork.</summary>
    /// <param name="artworkKey">Stable cache key (track identity); required for caching.</param>
    /// <param name="artwork">Encoded image bytes (PNG/JPEG/WebP).</param>
    public static string? For(string? artworkKey, byte[]? artwork)
    {
        if (artwork is null || artwork.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(artworkKey))
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(artworkKey, out var cached))
                {
                    return cached;
                }
            }
        }

        var hex = Extract(artwork);
        if (!string.IsNullOrEmpty(artworkKey))
        {
            lock (Gate)
            {
                if (Cache.Count >= MaxCachedKeys && Order.Count > 0)
                {
                    Cache.Remove(Order.Dequeue());
                }

                Cache[artworkKey] = hex;
                Order.Enqueue(artworkKey);
            }
        }

        return hex;
    }

    /// <summary>Extracts the accent from encoded artwork (pure; null when it cannot be decoded).</summary>
    /// <param name="artwork">Encoded image bytes.</param>
    public static string? Extract(byte[]? artwork)
    {
        if (artwork is null || artwork.Length == 0)
        {
            return null;
        }

        try
        {
            using var decoded = SKBitmap.Decode(artwork);
            if (decoded is null)
            {
                return null;
            }

            using var small = decoded.Resize(new SKImageInfo(SampleSize, SampleSize), new SKSamplingOptions(SKFilterMode.Nearest))
                               ?? decoded.Copy();
            if (small is null)
            {
                return null;
            }

            double sumR = 0, sumG = 0, sumB = 0, sumWeight = 0;
            for (var y = 0; y < small.Height; y++)
            {
                for (var x = 0; x < small.Width; x++)
                {
                    var pixel = small.GetPixel(x, y);
                    if (pixel.Alpha < 32)
                    {
                        continue;
                    }

                    var (_, saturation, value) = ToHsv(pixel.Red, pixel.Green, pixel.Blue);
                    // Mid-bright saturated pixels carry the identity; near-black/near-white detail does not.
                    if (value < 0.18 || value > 0.97)
                    {
                        continue;
                    }

                    var weight = (saturation * saturation) + 0.02;
                    sumR += pixel.Red * weight;
                    sumG += pixel.Green * weight;
                    sumB += pixel.Blue * weight;
                    sumWeight += weight;
                }
            }

            if (sumWeight <= 0)
            {
                return null;
            }

            var accent = new SKColor(
                (byte)Math.Clamp(sumR / sumWeight, 0, 255),
                (byte)Math.Clamp(sumG / sumWeight, 0, 255),
                (byte)Math.Clamp(sumB / sumWeight, 0, 255));
            return $"#{accent.Red:X2}{accent.Green:X2}{accent.Blue:X2}";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Clears the per-key cache (tests, settings changes).</summary>
    public static void ClearCache()
    {
        lock (Gate)
        {
            Cache.Clear();
            Order.Clear();
        }
    }

    private static (double Hue, double Saturation, double Value) ToHsv(byte r, byte g, byte b)
    {
        double red = r / 255.0, green = g / 255.0, blue = b / 255.0;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        var saturation = max <= 0 ? 0 : delta / max;
        return (0, saturation, max);
    }
}
