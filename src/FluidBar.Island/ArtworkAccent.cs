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

    /// <summary>Number of hue bins used to find the dominant colour cluster.</summary>
    private const int HueBins = 12;

    /// <summary>Sample edge used for the colour histogram.</summary>
    private const int HistogramSample = 32;

    /// <summary>
    /// Extracts the dominant colour from encoded artwork (pure; null when it cannot be decoded).
    /// </summary>
    /// <remarks>
    /// Pixels are clustered by hue and the heaviest cluster wins, then the colour is normalised for a
    /// dark glass surface (saturation floor, value window). Averaging every pixel instead — the previous
    /// implementation — mixes complementary hues into a beige/grey wash, which is why many covers produced
    /// an accent that read as "fixed white-grey" (wave bars) and pushed the karaoke highlight back to the
    /// theme's pink fallback.
    /// </remarks>
    /// <param name="artwork">Encoded image bytes (PNG/JPEG/WebP).</param>
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

            using var small = decoded.Resize(new SKImageInfo(HistogramSample, HistogramSample),
                                 new SKSamplingOptions(SKFilterMode.Nearest))
                               ?? decoded.Copy();
            if (small is null)
            {
                return null;
            }

            var weights = new double[HueBins];
            var sums = new double[HueBins * 3];
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
                    var hue = HueOf(pixel.Red, pixel.Green, pixel.Blue);

                    // Near-black and near-white detail carries no identity; a little saturation is
                    // enough to vote, but grey pixels must not win by sheer count.
                    if (value < 0.15 || value > 0.98 || saturation < 0.04)
                    {
                        continue;
                    }

                    var weight = (saturation * saturation) * (0.35 + (0.65 * value));
                    sumR += pixel.Red * weight;
                    sumG += pixel.Green * weight;
                    sumB += pixel.Blue * weight;
                    sumWeight += weight;

                    var bin = (int)(hue * HueBins) % HueBins;
                    if (bin < 0)
                    {
                        bin += HueBins;
                    }

                    weights[bin] += weight;
                    sums[(bin * 3) + 0] += pixel.Red * weight;
                    sums[(bin * 3) + 1] += pixel.Green * weight;
                    sums[(bin * 3) + 2] += pixel.Blue * weight;
                }
            }

            if (sumWeight <= 0)
            {
                return null;
            }

            // Dominant cluster; when no hue stands out (a truly grey cover) keep the neutral average so
            // monochrome art still yields an honest grey instead of an invented colour.
            var best = 0;
            for (var i = 1; i < HueBins; i++)
            {
                if (weights[i] > weights[best])
                {
                    best = i;
                }
            }

            double r, g, b;
            if (weights[best] > 0 && weights[best] >= sumWeight * 0.15)
            {
                r = sums[(best * 3) + 0] / weights[best];
                g = sums[(best * 3) + 1] / weights[best];
                b = sums[(best * 3) + 2] / weights[best];
            }
            else
            {
                r = sumR / sumWeight;
                g = sumG / sumWeight;
                b = sumB / sumWeight;
            }

            var rgb = new SKColor(
                (byte)Math.Clamp(r, 0, 255),
                (byte)Math.Clamp(g, 0, 255),
                (byte)Math.Clamp(b, 0, 255));
            var h = HueOf(rgb.Red, rgb.Green, rgb.Blue);
            var (_, s, v) = ToHsv(rgb.Red, rgb.Green, rgb.Blue);

            // Normalise for a dark glass surface: enough saturation to read as a colour, and a value
            // window that keeps text/wave bars legible without glowing.
            var vividSaturation = Math.Max(s, 0.55);
            var vividValue = Math.Clamp(v, 0.62, 0.92);
            var accent = FromHsv(h, vividSaturation, vividValue);
            return $"#{accent.Red:X2}{accent.Green:X2}{accent.Blue:X2}";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Converts HSV (0..1) back to RGB.</summary>
    private static SKColor FromHsv(double hue, double saturation, double value)
    {
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        hue = ((hue % 1.0) + 1.0) % 1.0;

        var sector = hue * 6.0;
        var index = (int)Math.Floor(sector);
        var fraction = sector - index;
        var p = value * (1 - saturation);
        var q = value * (1 - (saturation * fraction));
        var t = value * (1 - (saturation * (1 - fraction)));

        var (r, g, b) = index switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };

        return new SKColor(
            (byte)Math.Clamp(r * 255, 0, 255),
            (byte)Math.Clamp(g * 255, 0, 255),
            (byte)Math.Clamp(b * 255, 0, 255));
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

    /// <summary>Hue of a colour in 0..1 (0 = red), 0 for greys.</summary>
    private static double HueOf(byte r, byte g, byte b)
    {
        double red = r / 255.0, green = g / 255.0, blue = b / 255.0;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        if (delta <= 1e-6)
        {
            return 0;
        }

        double hue;
        if (max == red)
        {
            hue = ((green - blue) / delta) % 6;
        }
        else if (max == green)
        {
            hue = ((blue - red) / delta) + 2;
        }
        else
        {
            hue = ((red - green) / delta) + 4;
        }

        hue /= 6.0;
        return hue < 0 ? hue + 1.0 : hue;
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
