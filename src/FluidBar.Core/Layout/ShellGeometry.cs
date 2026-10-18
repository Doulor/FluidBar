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
    /// Samples the outline clockwise, <paramref name="samplesPerQuadrant"/> points per corner
    /// quadrant (spec 3.2 recommends 15). Consecutive samples are directly connectable with straight
    /// segments (including the closing segment from the last sample back to the first), so callers can
    /// build a closed path from the result without reordering. The traversal starts on the left edge
    /// at the top-left corner and runs clockwise: top-left → top-right → bottom-right → bottom-left.
    /// </summary>
    public ImmutableArray<VisualPoint> SampleOutline(int samplesPerQuadrant = 15)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerQuadrant, 1);
        if (Bounds.IsEmpty)
        {
            return [];
        }

        var radii = Radii;
        var exponent = 2.0 / ExponentK;
        var builder = ImmutableArray.CreateBuilder<VisualPoint>(samplesPerQuadrant * 4);

        // Clockwise visual order. Each corner starts on the edge the previous corner ended on:
        // left edge -> top edge -> right edge -> bottom edge -> left edge (close).
        AddCorner(builder, samplesPerQuadrant, Bounds.X + radii.TopLeft, Bounds.Y + radii.TopLeft, radii.TopLeft, exponent, OutlineCorner.TopLeft);
        AddCorner(builder, samplesPerQuadrant, Bounds.Right - radii.TopRight, Bounds.Y + radii.TopRight, radii.TopRight, exponent, OutlineCorner.TopRight);
        AddCorner(builder, samplesPerQuadrant, Bounds.Right - radii.BottomRight, Bounds.Bottom - radii.BottomRight, radii.BottomRight, exponent, OutlineCorner.BottomRight);
        AddCorner(builder, samplesPerQuadrant, Bounds.X + radii.BottomLeft, Bounds.Bottom - radii.BottomLeft, radii.BottomLeft, exponent, OutlineCorner.BottomLeft);

        return builder.ToImmutable();
    }

    private static void AddCorner(
        ImmutableArray<VisualPoint>.Builder builder,
        int steps,
        double centerX,
        double centerY,
        double radius,
        double exponent,
        OutlineCorner corner)
    {
        for (var i = 0; i < steps; i++)
        {
            var t = (Math.PI / 2) * (i / (double)steps);
            var cos = Math.Pow(Math.Abs(Math.Cos(t)), exponent);
            var sin = Math.Pow(Math.Abs(Math.Sin(t)), exponent);
            var (x, y) = corner switch
            {
                // Left edge -> top edge.
                OutlineCorner.TopLeft => (centerX - (radius * cos), centerY - (radius * sin)),
                // Top edge -> right edge.
                OutlineCorner.TopRight => (centerX + (radius * sin), centerY - (radius * cos)),
                // Right edge -> bottom edge.
                OutlineCorner.BottomRight => (centerX + (radius * cos), centerY + (radius * sin)),
                // Bottom edge -> left edge.
                _ => (centerX - (radius * sin), centerY + (radius * cos)),
            };
            builder.Add(new VisualPoint(x, y));
        }
    }

    private enum OutlineCorner
    {
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft,
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
