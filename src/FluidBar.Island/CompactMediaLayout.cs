using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Geometry of the compact media pill: the artwork, the single text column (title/artist or the live
/// lyric) and the audio wave. The compact play/pause button was removed on request (transport lives on
/// the expanded media page only), so the trailing area is the wave plus the right padding.
/// </summary>
/// <remarks>
/// The pill width is <see cref="FixedWidth"/> plus the measured text width, which is what lets the lyric
/// form show a whole line: the shell is widened instead of ellipsising the line. Callers clamp the result
/// to the compact size contract (never narrower than the token) and to the host surface (never wider than
/// the expanded width), so an over-long line still falls back to the ellipsis.
/// </remarks>
public static class CompactMediaLayout
{
    /// <summary>Left padding before the artwork.</summary>
    public const float LeftPadding = 8f;

    /// <summary>Artwork edge length.</summary>
    public const float ArtworkSize = 28f;

    /// <summary>Gap between the artwork and the text column.</summary>
    public const float ArtworkGap = 9f;

    /// <summary>Audio wave width.</summary>
    public const float WaveWidth = 20f;

    /// <summary>Audio wave height.</summary>
    public const float WaveHeight = 15f;

    /// <summary>Gap between the text column and the wave.</summary>
    public const float GapBeforeWave = 9f;

    /// <summary>Right padding after the wave.</summary>
    public const float RightPadding = 10f;

    /// <summary>Left edge of the text column (8 + 28 + 9).</summary>
    public const float TextLeft = LeftPadding + ArtworkSize + ArtworkGap;

    /// <summary>Width from the text column's right edge to the pill's right edge (39).</summary>
    public const float TextToTrailingEdge = GapBeforeWave + WaveWidth + RightPadding;

    /// <summary>Pill width that carries no text at all (artwork + paddings + wave).</summary>
    public const float FixedWidth = TextLeft + TextToTrailingEdge;

    /// <summary>
    /// Breathing room added to the measured line. Laying the text out in exactly its advance width makes
    /// the shaper trim it to an ellipsis on rounding alone, so the pill is widened by this much.
    /// </summary>
    public const float TextSlack = 2f;

    /// <summary>Artwork rectangle, vertically centred.</summary>
    /// <param name="height">Pill height.</param>
    public static SKRect ArtworkRect(float height)
        => new(LeftPadding, (height - ArtworkSize) / 2, LeftPadding + ArtworkSize, (height + ArtworkSize) / 2);

    /// <summary>Left edge of the audio wave for a pill width.</summary>
    /// <param name="width">Pill width.</param>
    public static float WaveLeft(float width)
        => Math.Max(TextLeft + 1, width - RightPadding - WaveWidth);

    /// <summary>Audio wave rectangle, vertically centred.</summary>
    /// <param name="width">Pill width.</param>
    /// <param name="height">Pill height.</param>
    public static SKRect WaveRect(float width, float height)
    {
        var left = WaveLeft(width);
        return new SKRect(left, (height - WaveHeight) / 2, left + WaveWidth, (height + WaveHeight) / 2);
    }

    /// <summary>Right edge of the text column for a pill width.</summary>
    /// <param name="width">Pill width.</param>
    public static float TextRight(float width) => WaveLeft(width) - GapBeforeWave;

    /// <summary>Pill width needed to show <paramref name="textWidth"/> without ellipsis.</summary>
    /// <param name="textWidth">Measured text width in DIP.</param>
    public static float WidthForText(float textWidth)
        => FixedWidth + TextSlack + (float.IsFinite(textWidth) && textWidth > 0 ? textWidth : 0);

    /// <summary>Adaptive width clamped to the compact contract: never narrower than the token, never wider than the surface.</summary>
    /// <param name="textWidth">Measured text width in DIP.</param>
    /// <param name="minWidth">Token width for the compact media pill.</param>
    /// <param name="maxWidth">Upper bound (host surface / expanded width).</param>
    public static float AdaptiveWidth(float textWidth, float minWidth, float maxWidth)
    {
        var lower = float.IsFinite(minWidth) && minWidth > 0 ? minWidth : FixedWidth;
        var upper = float.IsFinite(maxWidth) && maxWidth >= lower ? maxWidth : lower;
        return Math.Clamp(WidthForText(textWidth), lower, upper);
    }
}
