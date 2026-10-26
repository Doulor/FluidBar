using System.Drawing;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Recognises the "no cover" tile a player hands out instead of real artwork.
/// </summary>
/// <remarks>
/// <para>
/// KuGou publishes an SMTC thumbnail for every track, but for tracks it has no art for the thumbnail is a flat
/// grey 120×120 tile (measured 2026-10-26: 4.6 KB, luminance spread 26, mean 216, achromatic — byte-identical
/// across four different songs). Accepting it looked like "the cover is still missing", because the island painted
/// a plain grey square, and it also stopped the player-cache and catalogue fallbacks from ever running.
/// </para>
/// <para>
/// The discriminator is the luminance spread of an 8×8 sample grid. Measured on this machine: the placeholder tile
/// spans 27 levels (a barely visible grey gradient, five quantised colours), while every real cover in the same
/// folder spanned 71–253. A flat <em>dark</em> cover stays below the brightness floor and is kept, and if the tile
/// is ever a real low-contrast cover the catalogue fallback simply returns the same image again.
/// </para>
/// </remarks>
internal static class ArtworkQualityInspector
{
    /// <summary>Luminance spread (0..255) below which the image counts as flat.</summary>
    internal const int FlatSpreadThreshold = 32;

    /// <summary>Minimum brightness for a flat image to be considered a placeholder tile (dark covers are kept).</summary>
    internal const int PlaceholderMinMeanLuminance = 100;

    /// <summary>Grid resolution used for sampling.</summary>
    internal const int GridSize = 8;

    /// <summary>True when the bytes decode to a flat placeholder tile rather than real artwork.</summary>
    /// <param name="bytes">Encoded image bytes as handed out by the player.</param>
    public static bool IsLikelyPlaceholder(byte[] bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var bitmap = new Bitmap(stream);
            if (bitmap.Width < 2 || bitmap.Height < 2)
            {
                return false;
            }

            var min = 255;
            var max = 0;
            long sum = 0;
            var samples = 0;
            for (var cellY = 0; cellY < GridSize; cellY++)
            {
                var y = Math.Min(bitmap.Height - 1, ((cellY * 2) + 1) * bitmap.Height / (GridSize * 2));
                for (var cellX = 0; cellX < GridSize; cellX++)
                {
                    var x = Math.Min(bitmap.Width - 1, ((cellX * 2) + 1) * bitmap.Width / (GridSize * 2));
                    var pixel = bitmap.GetPixel(x, y);
                    var luminance = (int)((0.2126 * pixel.R) + (0.7152 * pixel.G) + (0.0722 * pixel.B));
                    min = Math.Min(min, luminance);
                    max = Math.Max(max, luminance);
                    sum += luminance;
                    samples++;
                }
            }

            if (samples == 0)
            {
                return false;
            }

            var mean = sum / samples;
            return max - min <= FlatSpreadThreshold && mean >= PlaceholderMinMeanLuminance;
        }
        catch (Exception)
        {
            // Undecodable bytes are somebody else's problem (the cache rejects them); never throw from here.
            return false;
        }
    }
}
