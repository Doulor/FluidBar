using System.Drawing;
using System.IO;

namespace FluidBar;

public readonly record struct MediaDominantColor(byte R, byte G, byte B);

public static class MediaArtworkColorAnalyzer
{
    public static MediaDominantColor? TryExtractDominantColor(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return null;

        try
        {
            using var bitmap = new Bitmap(imagePath);
            if (bitmap.Width <= 0 || bitmap.Height <= 0)
                return null;

            var step = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 48);
            var buckets = new Dictionary<int, ColorBucket>();
            for (var y = 0; y < bitmap.Height; y += step)
            {
                for (var x = 0; x < bitmap.Width; x += step)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.A < 128)
                        continue;

                    var saturation = pixel.GetSaturation();
                    var brightness = pixel.GetBrightness();
                    if (saturation < 0.18f || brightness < 0.14f || brightness > 0.94f)
                        continue;

                    var key = Quantize(pixel);
                    if (!buckets.TryGetValue(key, out var bucket))
                        bucket = new ColorBucket();

                    var weight = 0.7 + saturation * 1.6 + (1.0 - Math.Abs(brightness - 0.58)) * 0.45;
                    bucket.Add(pixel, weight);
                    buckets[key] = bucket;
                }
            }

            if (buckets.Count == 0)
                return null;

            var best = buckets.Values
                .OrderByDescending(bucket => bucket.Weight)
                .ThenByDescending(bucket => bucket.SaturationWeight)
                .First();

            return best.ToDominantColor();
        }
        catch
        {
            return null;
        }
    }

    private static int Quantize(Color color)
    {
        return (color.R >> 5) << 10 |
               (color.G >> 5) << 5 |
               (color.B >> 5);
    }

    private sealed class ColorBucket
    {
        private double _red;
        private double _green;
        private double _blue;

        public double Weight { get; private set; }
        public double SaturationWeight { get; private set; }

        public void Add(Color color, double weight)
        {
            _red += color.R * weight;
            _green += color.G * weight;
            _blue += color.B * weight;
            Weight += weight;
            SaturationWeight += color.GetSaturation() * weight;
        }

        public MediaDominantColor ToDominantColor()
        {
            if (Weight <= 0)
                return new MediaDominantColor(142, 142, 147);

            return new MediaDominantColor(
                ClampToByte(_red / Weight),
                ClampToByte(_green / Weight),
                ClampToByte(_blue / Weight));
        }

        private static byte ClampToByte(double value)
        {
            return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
        }
    }
}
