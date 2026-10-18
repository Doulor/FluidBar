using System.Collections.Immutable;

namespace FluidBar.Core.Layout;

/// <summary>
/// Superellipse (squircle) shell geometry (spec 3.2), shared by drawing, content clipping and
/// hit-testing so the click target can never drift from the drawn shape.
/// </summary>
public sealed record ShellGeometry
{
    private const double MinHalfExtent = 1e-6;

    /// <summary>Shell bounds in work-area-relative DIP.</summary>
    public required VisualRect Bounds { get; init; }

    /// <summary>Per-corner radii.</summary>
    public required CornerRadii Radii { get; init; }

    /// <summary>Superellipse exponent (2 = true ellipse/stadium, ~2.77 at the 440x284 shell).</summary>
    public required double ExponentK { get; init; }

    /// <summary>Creates geometry for a rectangle with the given corner radii.</summary>
    public static ShellGeometry Create(in VisualRect bounds, CornerRadii radii)
    {
        var halfMin = Math.Min(bounds.Width, bounds.Height) / 2;
        var maxRadius = Math.Clamp(radii.Max, 0, Math.Max(0, halfMin));
        var exponent = halfMin <= MinHalfExtent
            ? 2.0
            : 2.0 + ((1.0 - (maxRadius / halfMin)) * 1.22);
        return new ShellGeometry { Bounds = bounds, Radii = radii, ExponentK = exponent };
    }

    /// <summary>Creates a plain rounded-rectangle geometry from the shell and radii.</summary>
    public static ShellGeometry ForShell(in VisualRect shell, CornerRadii radii) => Create(shell, radii);

    /// <summary>Whether the point lies inside the squircle (used by <c>WM_NCHITTEST</c>).</summary>
    public bool Contains(double x, double y)
    {
        if (Bounds.IsEmpty || !Bounds.Contains(x, y))
        {
            return false;
        }

        var radii = Radii;
        var k = ExponentK;

        // Top-left quadrant.
        if (radii.TopLeft > 0 && x < Bounds.X + radii.TopLeft && y < Bounds.Y + radii.TopLeft)
        {
            return CornerContains(Bounds.X + radii.TopLeft - x, Bounds.Y + radii.TopLeft - y, radii.TopLeft, k);
        }

        // Top-right quadrant.
        if (radii.TopRight > 0 && x > Bounds.Right - radii.TopRight && y < Bounds.Y + radii.TopRight)
        {
            return CornerContains(x - (Bounds.Right - radii.TopRight), Bounds.Y + radii.TopRight - y, radii.TopRight, k);
        }

        // Bottom-right quadrant.
        if (radii.BottomRight > 0 && x > Bounds.Right - radii.BottomRight && y > Bounds.Bottom - radii.BottomRight)
        {
            return CornerContains(x - (Bounds.Right - radii.BottomRight), y - (Bounds.Bottom - radii.BottomRight), radii.BottomRight, k);
        }

        // Bottom-left quadrant.
        if (radii.BottomLeft > 0 && x < Bounds.X + radii.BottomLeft && y > Bounds.Bottom - radii.BottomLeft)
        {
            return CornerContains(Bounds.X + radii.BottomLeft - x, y - (Bounds.Bottom - radii.BottomLeft), radii.BottomLeft, k);
        }

        return true;
    }

    /// <summary>Whether the point lies inside the squircle.</summary>
    public bool Contains(VisualPoint point) => Contains(point.X, point.Y);

    /// <summary>
    /// Samples the outline, clockwise from the top edge, <paramref name="samplesPerQuadrant"/>
    /// points per corner quadrant (spec 3.2 recommends 15).
    /// </summary>
    public ImmutableArray<VisualPoint> SampleOutline(int samplesPerQuadrant = 15)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerQuadrant, 1);
        if (Bounds.IsEmpty)
        {
            return [];
        }

        var radii = Radii;
        var k = ExponentK;
        var exponent = 2.0 / k;
        var builder = ImmutableArray.CreateBuilder<VisualPoint>(samplesPerQuadrant * 4);
        var steps = samplesPerQuadrant;

        // Top edge: left -> right (top-left corner then top-right corner).
        AddCorner(builder, steps, Bounds.X + radii.TopLeft, Bounds.Y + radii.TopLeft, radii.TopLeft, exponent, -1, -1, reverse: true);
        AddCorner(builder, steps, Bounds.Right - radii.TopRight, Bounds.Y + radii.TopRight, radii.TopRight, exponent, 1, -1, reverse: false);

        // Right edge -> bottom edge.
        AddCorner(builder, steps, Bounds.Right - radii.BottomRight, Bounds.Bottom - radii.BottomRight, radii.BottomRight, exponent, 1, 1, reverse: false);
        AddCorner(builder, steps, Bounds.X + radii.BottomLeft, Bounds.Bottom - radii.BottomLeft, radii.BottomLeft, exponent, -1, 1, reverse: false);

        return builder.ToImmutable();
    }

    private static void AddCorner(
        ImmutableArray<VisualPoint>.Builder builder,
        int steps,
        double centerX,
        double centerY,
        double radius,
        double exponent,
        int signX,
        int signY,
        bool reverse)
    {
        for (var i = 0; i < steps; i++)
        {
            var t = reverse
                ? (Math.PI / 2) * (1.0 - (i / (double)steps))
                : (Math.PI / 2) * (i / (double)steps);
            var x = centerX + (signX * radius * Math.Pow(Math.Abs(Math.Cos(t)), exponent));
            var y = centerY + (signY * radius * Math.Pow(Math.Abs(Math.Sin(t)), exponent));
            builder.Add(new VisualPoint(x, y));
        }
    }

    private static bool CornerContains(double offsetX, double offsetY, double radius, double k)
    {
        if (radius <= 0)
        {
            return true;
        }

        var u = offsetX / radius;
        var v = offsetY / radius;
        if (u <= 0 || v <= 0)
        {
            return true;
        }

        return Math.Pow(u, k) + Math.Pow(v, k) <= 1.0 + 1e-9;
    }
}
