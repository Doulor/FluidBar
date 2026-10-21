using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Single agent spinner primitive shared by the expanded agent list row and the split-satellite ball.
/// The two used to draw their own arcs with different sweeps, stroke widths, caps and speeds, which
/// made the small satellite look nothing like the big island. One implementation, one set of numbers.
/// </summary>
public static class AgentSpinner
{
    /// <summary>Sweep of the painted arc (180 deg: CSS top+right borders).</summary>
    public const float SweepDegrees = AgentMotion.ArcSweepDegrees;

    /// <summary>Rest angle of the arc before the rotation is applied (-135 deg, top-left diagonal).</summary>
    public const float StartDegrees = AgentMotion.ArcStartDegrees;

    /// <summary>One revolution (0.9 s, linear, clockwise) — identical in both hosts.</summary>
    public const double PeriodSeconds = AgentMotion.RingPeriodSeconds;

    /// <summary>Cap style of the arc (the big island's CSS-border look uses butt caps).</summary>
    public const SKStrokeCap Cap = SKStrokeCap.Butt;

    /// <summary>
    /// Stroke : radius ratio of the reference spinner: 1.5 DIP stroke on a 4.75 DIP radius
    /// (the 11 DIP agent-state ring of the expanded row). Callers scale the stroke with the radius so
    /// the weight stays identical at any size.
    /// </summary>
    public const float StrokeToRadiusRatio = AgentMotion.RingStrokeWidth / ((11f - AgentMotion.RingStrokeWidth) / 2f);

    /// <summary>Stroke width for a given centre-line radius (DIP).</summary>
    /// <param name="radius">Centre-line radius in DIP.</param>
    public static float StrokeForRadius(float radius) => radius * StrokeToRadiusRatio;

    /// <summary>Rotation in degrees for an elapsed animation time (linear, 0.9 s per turn).</summary>
    /// <param name="seconds">Elapsed animation seconds.</param>
    public static double RotationDegrees(double seconds)
        => double.IsFinite(seconds) ? (seconds / PeriodSeconds) * 360.0 % 360.0 : 0;

    /// <summary>Draws the arc of the spinner.</summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="paint">Reusable stroke paint (its style/width/cap are set here).</param>
    /// <param name="centre">Centre of the spinner in canvas coordinates.</param>
    /// <param name="radius">Centre-line radius in DIP.</param>
    /// <param name="rotationDegrees">Current rotation from <see cref="RotationDegrees"/>.</param>
    /// <param name="color">Arc colour (the theme accent/Info in both hosts).</param>
    public static void Draw(SKCanvas canvas, SKPaint paint, SKPoint centre, float radius, double rotationDegrees, SKColor color)
    {
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = StrokeForRadius(radius);
        paint.StrokeCap = Cap;
        paint.Shader = null;
        paint.Color = color;
        canvas.DrawArc(
            new SKRect(centre.X - radius, centre.Y - radius, centre.X + radius, centre.Y + radius),
            StartDegrees + (float)rotationDegrees,
            SweepDegrees,
            false,
            paint);
    }
}
