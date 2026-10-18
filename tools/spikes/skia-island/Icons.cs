using SkiaSharp;

namespace FluidBar.SkiaIslandSpike;

/// <summary>Icon identities used by the spike (subset of App <c>Controls/Icons.cs</c>).</summary>
internal enum IconKind
{
    Play,
    Pause,
    Previous,
    Next,
    Battery,
    Bolt,
    Wifi,
    Focus,
    Settings,
    Store,
    Close,
    ChevronDown,
    ChevronRight,
    Media,
    Bell,
    Clipboard,
    Agent,
    Pin,
    Check,
    Document,
    Grid,
    Search,
    Clock,
    Download,
    Lock,
    Usb,
    Bluetooth,
}

/// <summary>
/// Path-based icon set on the 24×24 grid (spec 3.6, 1.6 grid stroke). Paths are built once and reused;
/// drawing only sets the paint colour, so the per-frame cost is one transform + two path draws.
/// </summary>
internal static class Icons
{
    private const float Grid = 24f;
    private const float Stroke = 1.6f;

    private static readonly Dictionary<IconKind, IconGeometry> Cache = [];
    private static readonly Lock Gate = new();

    public static void Draw(SKCanvas canvas, IconKind kind, SKRect bounds, SKColor color, SKPaint paint)
    {
        var size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var geometry = Get(kind);
        var scale = size / Grid;
        canvas.Save();
        canvas.Translate(
            bounds.Left + ((bounds.Width - size) / 2),
            bounds.Top + ((bounds.Height - size) / 2));
        canvas.Scale(scale);

        paint.IsAntialias = true;
        paint.Color = color;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = Stroke;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.StrokeJoin = SKStrokeJoin.Round;
        if (!geometry.Stroke.IsEmpty)
        {
            canvas.DrawPath(geometry.Stroke, paint);
        }

        paint.Style = SKPaintStyle.Fill;
        if (!geometry.Fill.IsEmpty)
        {
            canvas.DrawPath(geometry.Fill, paint);
        }

        canvas.Restore();
    }

    private static IconGeometry Get(IconKind kind)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(kind, out var cached))
            {
                return cached;
            }

            var geometry = Build(kind);
            Cache[kind] = geometry;
            return geometry;
        }
    }

    private static IconGeometry Build(IconKind kind)
    {
        var stroke = new SKPath();
        var fill = new SKPath();
        var b = new Builder(stroke, fill);

        switch (kind)
        {
            case IconKind.Play:
                b.FillPolygon((8, 5), (19, 12), (8, 19));
                break;
            case IconKind.Pause:
                b.FillRect(7, 5, 3, 14);
                b.FillRect(14, 5, 3, 14);
                break;
            case IconKind.Previous:
                b.FillRect(6, 5, 2.5f, 14);
                b.FillPolygon((19, 5), (19, 19), (9.5f, 12));
                break;
            case IconKind.Next:
                b.FillPolygon((5, 5), (14.5f, 12), (5, 19));
                b.FillRect(15.5f, 5, 2.5f, 14);
                break;
            case IconKind.Battery:
                b.Rounded(2.5f, 8, 16, 8, 2);
                b.FillRect(20, 10.5f, 2, 3);
                break;
            case IconKind.Bolt:
                b.FillPolygon((13.5f, 2.5f), (6.5f, 13.5f), (11.8f, 13.5f));
                b.FillPolygon((11.5f, 10.5f), (17.5f, 10.5f), (10.5f, 21.5f));
                break;
            case IconKind.Wifi:
                b.Dot(12, 18.5f, 1.5f);
                b.Arc(12, 22, 6.5f, 215, 110);
                b.Arc(12, 22, 11.5f, 220, 100);
                break;
            case IconKind.Focus:
                b.Arc(14, 12, 8.2f, 65, 230);
                b.Ellipse(9.5f, 12, 6.5f, 8.2f);
                break;
            case IconKind.Settings:
                b.Ellipse(12, 12, 3.2f, 3.2f);
                b.Arc(12, 12, 8.2f, 0, 360);
                for (var i = 0; i < 8; i++)
                {
                    var angle = (i * Math.PI / 4) + (Math.PI / 8);
                    b.Line(
                        12 + (Math.Cos(angle) * 8.4), 12 + (Math.Sin(angle) * 8.4),
                        12 + (Math.Cos(angle) * 10.6), 12 + (Math.Sin(angle) * 10.6));
                }

                break;
            case IconKind.Store:
                b.Line(4, 9, 20, 9);
                b.Line(5.5f, 9, 6.5f, 4.5f);
                b.Line(18.5f, 9, 17.5f, 4.5f);
                b.Rounded(4.5f, 9, 15, 11, 2);
                b.Line(9.5f, 13.5f, 9.5f, 16.5f);
                b.Line(14.5f, 13.5f, 14.5f, 16.5f);
                break;
            case IconKind.Close:
                b.Line(6.5f, 6.5f, 17.5f, 17.5f);
                b.Line(17.5f, 6.5f, 6.5f, 17.5f);
                break;
            case IconKind.ChevronDown:
                b.Line(6.5f, 9.5f, 12, 15);
                b.Line(17.5f, 9.5f, 12, 15);
                break;
            case IconKind.ChevronRight:
                b.Line(9.5f, 6.5f, 15, 12);
                b.Line(9.5f, 17.5f, 15, 12);
                break;
            case IconKind.Media:
                b.Ellipse(8, 18, 3, 3);
                b.Ellipse(18, 15.5f, 3, 3);
                b.Line(11, 18, 11, 8);
                b.Line(21, 15.5f, 21, 5.5f);
                b.Line(11, 8, 21, 5.5f);
                break;
            case IconKind.Bell:
                b.Arc(12, 10.5f, 6.5f, 190, 160);
                b.Line(5.5f, 16.5f, 18.5f, 16.5f);
                b.Line(5.5f, 16.5f, 7, 13.5f);
                b.Line(18.5f, 16.5f, 17, 13.5f);
                b.Line(10, 19.5f, 14, 19.5f);
                break;
            case IconKind.Clipboard:
                b.Rounded(5, 4.5f, 14, 16, 2.5f);
                b.Rounded(9, 2.5f, 6, 4, 1.5f);
                b.Line(8.5f, 11, 15.5f, 11);
                b.Line(8.5f, 15, 13.5f, 15);
                break;
            case IconKind.Agent:
                b.FillEllipse(12, 8.5f, 4.4f, 1.6f);
                b.FillEllipse(12, 8.5f, 1.6f, 4.4f);
                b.FillEllipse(18.5f, 15, 2.6f, 1.0f);
                b.FillEllipse(18.5f, 15, 1.0f, 2.6f);
                b.FillEllipse(7, 16.5f, 2.0f, 0.8f);
                b.FillEllipse(7, 16.5f, 0.8f, 2.0f);
                break;
            case IconKind.Pin:
                b.Line(12, 21, 12, 13);
                b.Line(8, 4, 16, 4);
                b.Line(16, 4, 15, 9);
                b.Line(15, 9, 18, 13);
                b.Line(18, 13, 6, 13);
                b.Line(6, 13, 9, 9);
                b.Line(9, 9, 8, 4);
                break;
            case IconKind.Check:
                b.Line(5, 12.5f, 10, 17.5f);
                b.Line(10, 17.5f, 19.5f, 6.5f);
                break;
            case IconKind.Document:
                b.Rounded(5, 3.5f, 14, 17, 2);
                b.Line(8, 8.5f, 16, 8.5f);
                b.Line(8, 12.5f, 16, 12.5f);
                b.Line(8, 16.5f, 13, 16.5f);
                break;
            case IconKind.Grid:
                b.Rounded(4.5f, 4.5f, 6, 6, 2);
                b.Rounded(13.5f, 4.5f, 6, 6, 2);
                b.Rounded(4.5f, 13.5f, 6, 6, 2);
                b.Rounded(13.5f, 13.5f, 6, 6, 2);
                break;
            case IconKind.Search:
                b.Ellipse(10.5f, 10.5f, 6, 6);
                b.Line(15, 15, 20.5f, 20.5f);
                break;
            case IconKind.Clock:
                b.Ellipse(12, 12, 8.5f, 8.5f);
                b.Line(12, 7.5f, 12, 12);
                b.Line(12, 12, 15.5f, 14);
                break;
            case IconKind.Download:
                b.Line(12, 4, 12, 15);
                b.Line(7.5f, 11, 12, 15.5f);
                b.Line(16.5f, 11, 12, 15.5f);
                b.Line(5, 20, 19, 20);
                break;
            case IconKind.Lock:
                b.Rounded(5, 10, 14, 10, 2.5f);
                b.Arc(12, 10, 4.5f, 180, 180);
                break;
            case IconKind.Usb:
                b.Line(12, 21, 12, 6);
                b.Dot(12, 4, 2);
                b.Line(12, 10, 7, 14);
                b.Line(12, 13, 17, 17);
                b.FillRect(5.5f, 13, 3, 2);
                b.Dot(17, 17.5f, 1.4f);
                break;
            case IconKind.Bluetooth:
                b.Line(8, 7, 16, 17);
                b.Line(16, 7, 8, 17);
                b.Line(12, 3.5f, 12, 20.5f);
                b.Line(12, 3.5f, 16, 7);
                b.Line(12, 20.5f, 16, 17);
                break;
            default:
                b.Dot(12, 12, 3);
                break;
        }

        return new IconGeometry(stroke, fill);
    }

    private readonly record struct IconGeometry(SKPath Stroke, SKPath Fill);

    /// <summary>Builds stroke/fill paths on the 24×24 grid.</summary>
    private sealed class Builder(SKPath stroke, SKPath fill)
    {
        public void Line(double x1, double y1, double x2, double y2)
        {
            stroke.MoveTo((float)x1, (float)y1);
            stroke.LineTo((float)x2, (float)y2);
        }

        public void Rounded(double x, double y, double w, double h, double r)
            => stroke.AddRoundRect(new SKRect((float)x, (float)y, (float)(x + w), (float)(y + h)), (float)r, (float)r);

        public void Ellipse(double cx, double cy, double rx, double ry)
            => stroke.AddOval(new SKRect((float)(cx - rx), (float)(cy - ry), (float)(cx + rx), (float)(cy + ry)));

        public void Dot(double cx, double cy, double r)
            => fill.AddCircle((float)cx, (float)cy, (float)r);

        public void FillRect(double x, double y, double w, double h)
            => fill.AddRect(new SKRect((float)x, (float)y, (float)(x + w), (float)(y + h)));

        public void FillEllipse(double cx, double cy, double rx, double ry)
            => fill.AddOval(new SKRect((float)(cx - rx), (float)(cy - ry), (float)(cx + rx), (float)(cy + ry)));

        public void FillPolygon(params (double X, double Y)[] points)
        {
            fill.MoveTo((float)points[0].X, (float)points[0].Y);
            for (var i = 1; i < points.Length; i++)
            {
                fill.LineTo((float)points[i].X, (float)points[i].Y);
            }

            fill.Close();
        }

        public void Arc(double cx, double cy, double radius, double startDeg, double sweepDeg)
        {
            if (Math.Abs(sweepDeg) >= 359.9)
            {
                stroke.AddCircle((float)cx, (float)cy, (float)radius);
                return;
            }

            var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweepDeg) / 8.0));
            for (var i = 0; i <= steps; i++)
            {
                var angle = (startDeg + (sweepDeg * i / steps)) * Math.PI / 180.0;
                var x = (float)(cx + (Math.Cos(angle) * radius));
                var y = (float)(cy + (Math.Sin(angle) * radius));
                if (i == 0)
                {
                    stroke.MoveTo(x, y);
                }
                else
                {
                    stroke.LineTo(x, y);
                }
            }
        }
    }
}
