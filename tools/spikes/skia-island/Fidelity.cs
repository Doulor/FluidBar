using SkiaSharp;

namespace FluidBar.SkiaIslandSpike;

internal sealed record FidelityResult(
    string Case,
    string EvidenceBounds,
    string OurBounds,
    double MaskIou,
    double MeanRgbDelta,
    string EvidenceFill,
    string OurFill,
    string Notes);

/// <summary>
/// Quantitative fidelity checks against the Avalonia evidence PNGs. The evidence predates the
/// no-rail A1 revision, so only the idle pill shares a layout; expanded scenes are compared on
/// shell bounds/fill/shape, not on content positions.
/// </summary>
internal static class Fidelity
{
    public static FidelityResult Compare(string name, string evidencePath, SKImage ours, float scale, string notes)
    {
        using var evidence = SKBitmap.Decode(evidencePath)
            ?? throw new InvalidOperationException($"Cannot decode {evidencePath}");
        using var ourBitmap = SKBitmap.FromImage(ours);
        var e = OpaqueBounds(evidence);
        var o = OpaqueBounds(ourBitmap);
        var width = Math.Min(e.Width, o.Width);
        var height = Math.Min(e.Height, o.Height);
        var union = 0;
        var intersection = 0;
        double deltaSum = 0;
        var deltaCount = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var ec = evidence.GetPixel(e.Left + x, e.Top + y);
                var oc = ourBitmap.GetPixel(o.Left + x, o.Top + y);
                var em = ec.Alpha > 128;
                var om = oc.Alpha > 128;
                if (em || om)
                {
                    union++;
                    if (em && om)
                    {
                        intersection++;
                    }
                }

                if (ec.Alpha >= 240 && oc.Alpha >= 240)
                {
                    deltaSum += (Math.Abs(ec.Red - oc.Red) + Math.Abs(ec.Green - oc.Green) + Math.Abs(ec.Blue - oc.Blue)) / 3.0;
                    deltaCount++;
                }
            }
        }

        // Sample the shell fill at top-centre, 10 px below the top edge (clear of content on every page).
        var eFill = evidence.GetPixel(e.Left + (e.Width / 2), e.Top + 10);
        var oFill = ourBitmap.GetPixel(o.Left + (o.Width / 2), o.Top + 10);
        return new FidelityResult(
            name,
            Describe(e, scale),
            Describe(o, scale),
            union == 0 ? 0 : intersection / (double)union,
            deltaCount == 0 ? 0 : deltaSum / deltaCount,
            Hex(eFill),
            Hex(oFill),
            notes);
    }

    private static (int Left, int Top, int Width, int Height) OpaqueBounds(SKBitmap bitmap)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = 0;
        var maxY = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Alpha >= 240 && c.Red < 60 && c.Green < 60 && c.Blue < 60)
                {
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        return minX == int.MaxValue ? (0, 0, 0, 0) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static string Describe((int Left, int Top, int Width, int Height) bounds, float scale)
        => $"{bounds.Left},{bounds.Top} {bounds.Width}×{bounds.Height}px ({bounds.Width / scale:0.#}×{bounds.Height / scale:0.#} DIP)";

    private static string Hex(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}@{c.Alpha}";
}
