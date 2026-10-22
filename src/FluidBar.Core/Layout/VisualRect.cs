namespace FluidBar.Core.Layout;

/// <summary>
/// Rectangle in DIP (device-independent pixels). The only visual coordinate system in the core;
/// layout rects are expressed relative to the target monitor's work-area origin.
/// </summary>
public readonly record struct VisualRect(double X, double Y, double Width, double Height)
{
    /// <summary>Empty rectangle at the origin.</summary>
    public static VisualRect Empty { get; } = new(0, 0, 0, 0);

    /// <summary>Right edge (<c>X + Width</c>).</summary>
    public double Right => X + Width;

    /// <summary>Bottom edge (<c>Y + Height</c>).</summary>
    public double Bottom => Y + Height;

    /// <summary>Horizontal centre.</summary>
    public double CenterX => X + (Width / 2);

    /// <summary>Vertical centre.</summary>
    public double CenterY => Y + (Height / 2);

    /// <summary>Whether the rectangle has no area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Translates the rectangle.</summary>
    public VisualRect Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>Grows the rectangle by <paramref name="left"/> etc. (negative shrinks).</summary>
    public VisualRect Inflate(double left, double top, double right, double bottom)
        => new(X - left, Y - top, Width + left + right, Height + top + bottom);

    /// <summary>Grows the rectangle uniformly on all sides.</summary>
    public VisualRect Inflate(double amount) => Inflate(amount, amount, amount, amount);

    /// <summary>Shrinks the rectangle uniformly on all sides.</summary>
    public VisualRect Deflate(double amount) => Inflate(-amount, -amount, -amount, -amount);

    /// <summary>Returns whether the point is inside (half-open matching).</summary>
    public bool Contains(double x, double y)
        => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Intersection of two rectangles; empty when disjoint.</summary>
    public VisualRect Intersect(VisualRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? Empty : new VisualRect(left, top, right - left, bottom - top);
    }

    /// <summary>Smallest rectangle containing both inputs (either side may be <see cref="Empty"/>).</summary>
    public VisualRect Union(in VisualRect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        var right = Math.Max(Right, other.Right);
        var bottom = Math.Max(Bottom, other.Bottom);
        return new VisualRect(left, top, right - left, bottom - top);
    }

    /// <summary>Whether two rectangles overlap.</summary>
    public bool IntersectsWith(VisualRect other) => !Intersect(other).IsEmpty;

    /// <summary>Clamps the rectangle inside <paramref name="container"/>, shrinking it when it is larger.</summary>
    public VisualRect ClampInside(VisualRect container)
    {
        var width = Math.Min(Width, container.Width);
        var height = Math.Min(Height, container.Height);
        var x = Math.Clamp(X, container.X, container.X + container.Width - width);
        var y = Math.Clamp(Y, container.Y, container.Y + container.Height - height);
        return new VisualRect(x, y, width, height);
    }

    /// <summary>Rounds to whole DIPs (diagnostics/snapshots).</summary>
    public VisualRect Round()
        => new(Math.Round(X), Math.Round(Y), Math.Round(Width), Math.Round(Height));

    /// <inheritdoc />
    public override string ToString() => $"({X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##})";
}

/// <summary>Rectangle in physical pixels; only produced by the single DIP-&gt;pixel conversion.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>Empty rectangle.</summary>
    public static PixelRect Empty { get; } = new(0, 0, 0, 0);

    /// <summary>Right edge (exclusive).</summary>
    public int Right => X + Width;

    /// <summary>Bottom edge (exclusive).</summary>
    public int Bottom => Y + Height;

    /// <summary>Whether the rectangle has no area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <inheritdoc />
    public override string ToString() => $"({X},{Y} {Width}x{Height})";
}
