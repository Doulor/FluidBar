using SkiaSharp;

namespace FluidBar.Island;

/// <summary>Small SKRect helpers kept in DIP.</summary>
internal static class RectExtensions
{
    /// <summary>Shrinks the rectangle uniformly (negative values grow it).</summary>
    public static SKRect Deflate(this SKRect rect, float amount) => new(
        rect.Left + amount,
        rect.Top + amount,
        rect.Right - amount,
        rect.Bottom - amount);

    /// <summary>Grows the rectangle uniformly.</summary>
    public static SKRect Inflate(this SKRect rect, float amount) => rect.Deflate(-amount);

    /// <summary>Grows the rectangle vertically only.</summary>
    public static SKRect InflateY(this SKRect rect, float amount) => new(
        rect.Left, rect.Top - amount, rect.Right, rect.Bottom + amount);

    /// <summary>Whether two rectangles overlap.</summary>
    public static bool Intersects(this SKRect a, SKRect b)
        => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
}
