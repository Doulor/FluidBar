using Avalonia;
using Avalonia.Media;

namespace FluidBar.App.Controls;

/// <summary>Functional icon identities (spec 3.6: 24×24 grid, vector paths, no emoji).</summary>
public enum IconKind
{
    /// <summary>Clock (idle surface / timer).</summary>
    Clock,

    /// <summary>Play transport.</summary>
    Play,

    /// <summary>Pause transport.</summary>
    Pause,

    /// <summary>Previous track.</summary>
    Previous,

    /// <summary>Next track.</summary>
    Next,

    /// <summary>Volume: muted.</summary>
    VolumeMute,

    /// <summary>Volume: low.</summary>
    VolumeLow,

    /// <summary>Volume: medium.</summary>
    VolumeMedium,

    /// <summary>Volume: high.</summary>
    VolumeHigh,

    /// <summary>Brightness: dim.</summary>
    BrightnessLow,

    /// <summary>Brightness: bright.</summary>
    BrightnessHigh,

    /// <summary>Notification bell.</summary>
    Bell,

    /// <summary>Clipboard.</summary>
    Clipboard,

    /// <summary>Agent / sparkles.</summary>
    Agent,

    /// <summary>Battery outline.</summary>
    Battery,

    /// <summary>Charging bolt.</summary>
    Bolt,

    /// <summary>Caps/Num lock key.</summary>
    Lock,

    /// <summary>USB device.</summary>
    Usb,

    /// <summary>Bluetooth.</summary>
    Bluetooth,

    /// <summary>Focus assist moon.</summary>
    Focus,

    /// <summary>Download/progress.</summary>
    Download,

    /// <summary>Search / lookup.</summary>
    Search,

    /// <summary>Chevron pointing down.</summary>
    ChevronDown,

    /// <summary>Chevron pointing right.</summary>
    ChevronRight,

    /// <summary>Settings gear.</summary>
    Settings,

    /// <summary>Marketplace storefront.</summary>
    Store,

    /// <summary>Close.</summary>
    Close,

    /// <summary>Check.</summary>
    Check,

    /// <summary>Plus.</summary>
    Plus,

    /// <summary>Pin.</summary>
    Pin,

    /// <summary>Home.</summary>
    Home,

    /// <summary>Media / note.</summary>
    Media,

    /// <summary>Widgets / grid.</summary>
    Grid,

    /// <summary>Wi-Fi.</summary>
    Wifi,

    /// <summary>Monitor / display.</summary>
    Monitor,

    /// <summary>Calendar.</summary>
    Calendar,

    /// <summary>Task / checkbox.</summary>
    Task,

    /// <summary>Sun (appearance light theme).</summary>
    Sun,

    /// <summary>More (ellipsis dots).</summary>
    More,

    /// <summary>Rollback / undo arrow.</summary>
    Rollback,

    /// <summary>Warning triangle.</summary>
    Warning,

    /// <summary>External link.</summary>
    ExternalLink,

    /// <summary>Folder.</summary>
    Folder,

    /// <summary>Document/log.</summary>
    Document,
}

/// <summary>
/// Draws the vector icon set on a 24×24 grid with ~1.5 DIP strokes. Icons are procedural
/// <see cref="DrawingContext"/> primitives: no icon font, no images, no emoji (spec 3.6).
/// </summary>
public static class Icons
{
    private const double Grid = 24.0;

    /// <summary>Draws <paramref name="kind"/> inside <paramref name="bounds"/>.</summary>
    public static void Draw(DrawingContext context, IconKind kind, Rect bounds, IBrush brush)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(brush);
        var size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var scale = size / Grid;
        var stroke = Math.Clamp(1.6 * scale, 0.9, 2.4);
        var pen = new Pen(brush, stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var ox = bounds.X + ((bounds.Width - size) / 2);
        var oy = bounds.Y + ((bounds.Height - size) / 2);

        Point P(double x, double y) => new(ox + (x * scale), oy + (y * scale));

        Rect R(double x, double y, double w, double h) => new(ox + (x * scale), oy + (y * scale), w * scale, h * scale);

        void Line(double x1, double y1, double x2, double y2) => context.DrawLine(pen, P(x1, y1), P(x2, y2));

        void Rounded(double x, double y, double w, double h, double r)
            => context.DrawRectangle(null, pen, R(x, y, w, h), r * scale, r * scale);

        void Filled(double x, double y, double w, double h)
            => context.DrawRectangle(brush, null, R(x, y, w, h));

        void Arc(double cx, double cy, double radius, double startDeg, double sweepDeg)
            => context.DrawGeometry(null, pen, ArcGeometry(P(cx, cy), radius * scale, startDeg, sweepDeg));

        void Dot(double cx, double cy, double radius)
            => context.DrawEllipse(brush, null, P(cx, cy), radius * scale, radius * scale);

        void Rays(double inner, double outer)
        {
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Math.PI / 4;
                context.DrawLine(pen,
                    P(12 + (Math.Cos(angle) * inner), 12 + (Math.Sin(angle) * inner)),
                    P(12 + (Math.Cos(angle) * outer), 12 + (Math.Sin(angle) * outer)));
            }
        }

        void Speaker()
        {
            Line(4, 9.5, 7.5, 9.5);
            Line(7.5, 9.5, 11.5, 5.5);
            Line(11.5, 5.5, 11.5, 18.5);
            Line(11.5, 18.5, 7.5, 14.5);
            Line(7.5, 14.5, 4, 14.5);
            Line(4, 9.5, 4, 14.5);
        }

        void Sparkle(double cx, double cy, double rx, double ry)
        {
            context.DrawEllipse(brush, null, P(cx, cy), rx * scale, ry * scale);
            context.DrawEllipse(brush, null, P(cx, cy), ry * scale, rx * scale);
        }

        switch (kind)
        {
            case IconKind.Play:
                context.DrawGeometry(brush, null, Polygon(P(8, 5), P(19, 12), P(8, 19)));
                break;
            case IconKind.Pause:
                Filled(7, 5, 3, 14);
                Filled(14, 5, 3, 14);
                break;
            case IconKind.Previous:
                Filled(6, 5, 2.5, 14);
                context.DrawGeometry(brush, null, Polygon(P(19, 5), P(19, 19), P(9.5, 12)));
                break;
            case IconKind.Next:
                context.DrawGeometry(brush, null, Polygon(P(5, 5), P(14.5, 12), P(5, 19)));
                Filled(15.5, 5, 2.5, 14);
                break;
            case IconKind.Clock:
                context.DrawEllipse(null, pen, P(12, 12), 8.5 * scale, 8.5 * scale);
                Line(12, 7.5, 12, 12);
                Line(12, 12, 15.5, 14);
                break;
            case IconKind.VolumeMute:
                Speaker();
                Line(16, 9.5, 21, 14.5);
                Line(21, 9.5, 16, 14.5);
                break;
            case IconKind.VolumeLow:
                Speaker();
                Arc(16.2, 12, 4.6, -50, 100);
                break;
            case IconKind.VolumeMedium:
                Speaker();
                Arc(16.2, 12, 4.6, -50, 100);
                Arc(16.2, 12, 7.6, -55, 110);
                break;
            case IconKind.VolumeHigh:
                Speaker();
                Arc(16.2, 12, 4.6, -50, 100);
                Arc(16.2, 12, 7.6, -55, 110);
                Arc(16.2, 12, 10.6, -55, 110);
                break;
            case IconKind.BrightnessLow:
                context.DrawEllipse(null, pen, P(12, 12), 4.2 * scale, 4.2 * scale);
                Rays(7.2, 9.2);
                break;
            case IconKind.BrightnessHigh:
                context.DrawEllipse(null, pen, P(12, 12), 4.8 * scale, 4.8 * scale);
                Rays(7.6, 11.4);
                break;
            case IconKind.Bell:
                Arc(12, 10.5, 6.5, 190, 160);
                Line(5.5, 16.5, 18.5, 16.5);
                Line(5.5, 16.5, 7, 13.5);
                Line(18.5, 16.5, 17, 13.5);
                Line(10, 19.5, 14, 19.5);
                break;
            case IconKind.Clipboard:
                Rounded(5, 4.5, 14, 16, 2.5);
                Rounded(9, 2.5, 6, 4, 1.5);
                Line(8.5, 11, 15.5, 11);
                Line(8.5, 15, 13.5, 15);
                break;
            case IconKind.Agent:
                Sparkle(12, 8.5, 4.4, 1.6);
                Sparkle(18.5, 15, 2.6, 1.0);
                Sparkle(7, 16.5, 2.0, 0.8);
                break;
            case IconKind.Battery:
                Rounded(2.5, 8, 16, 8, 2);
                Filled(20, 10.5, 2, 3);
                break;
            case IconKind.Bolt:
                context.DrawGeometry(brush, null, Polygon(P(13.5, 2.5), P(6.5, 13.5), P(11.8, 13.5)));
                context.DrawGeometry(brush, null, Polygon(P(11.5, 10.5), P(17.5, 10.5), P(10.5, 21.5)));
                break;
            case IconKind.Lock:
                Rounded(5, 10, 14, 10, 2.5);
                Arc(12, 10, 4.5, 180, 180);
                break;
            case IconKind.Usb:
                Line(12, 21, 12, 6);
                Dot(12, 4, 2);
                Line(12, 10, 7, 14);
                Line(12, 13, 17, 17);
                context.DrawRectangle(brush, null, R(5.5, 13, 3, 2));
                Dot(17, 17.5, 1.4);
                break;
            case IconKind.Bluetooth:
                Line(8, 7, 16, 17);
                Line(16, 7, 8, 17);
                Line(12, 3.5, 12, 20.5);
                Line(12, 3.5, 16, 7);
                Line(12, 20.5, 16, 17);
                break;
            case IconKind.Focus:
                Arc(14, 12, 8.2, 65, 230);
                context.DrawEllipse(null, pen, P(9.5, 12), 6.5 * scale, 8.2 * scale);
                break;
            case IconKind.Download:
                Line(12, 4, 12, 15);
                Line(7.5, 11, 12, 15.5);
                Line(16.5, 11, 12, 15.5);
                Line(5, 20, 19, 20);
                break;
            case IconKind.Search:
                context.DrawEllipse(null, pen, P(10.5, 10.5), 6 * scale, 6 * scale);
                Line(15, 15, 20.5, 20.5);
                break;
            case IconKind.ChevronDown:
                Line(6.5, 9.5, 12, 15);
                Line(17.5, 9.5, 12, 15);
                break;
            case IconKind.ChevronRight:
                Line(9.5, 6.5, 15, 12);
                Line(9.5, 17.5, 15, 12);
                break;
            case IconKind.Settings:
                context.DrawEllipse(null, pen, P(12, 12), 3.2 * scale, 3.2 * scale);
                Arc(12, 12, 8.2, 0, 360);
                for (var i = 0; i < 8; i++)
                {
                    var angle = (i * Math.PI / 4) + (Math.PI / 8);
                    Line(12 + (Math.Cos(angle) * 8.4), 12 + (Math.Sin(angle) * 8.4),
                        12 + (Math.Cos(angle) * 10.6), 12 + (Math.Sin(angle) * 10.6));
                }

                break;
            case IconKind.Store:
                Line(4, 9, 20, 9);
                Line(5.5, 9, 6.5, 4.5);
                Line(18.5, 9, 17.5, 4.5);
                Rounded(4.5, 9, 15, 11, 2);
                Line(9.5, 13.5, 9.5, 16.5);
                Line(14.5, 13.5, 14.5, 16.5);
                break;
            case IconKind.Close:
                Line(6.5, 6.5, 17.5, 17.5);
                Line(17.5, 6.5, 6.5, 17.5);
                break;
            case IconKind.Check:
                Line(5, 12.5, 10, 17.5);
                Line(10, 17.5, 19.5, 6.5);
                break;
            case IconKind.Plus:
                Line(12, 5, 12, 19);
                Line(5, 12, 19, 12);
                break;
            case IconKind.Pin:
                Line(12, 21, 12, 13);
                Line(8, 4, 16, 4);
                Line(16, 4, 15, 9);
                Line(15, 9, 18, 13);
                Line(18, 13, 6, 13);
                Line(6, 13, 9, 9);
                Line(9, 9, 8, 4);
                break;
            case IconKind.Home:
                Line(4, 11, 12, 4);
                Line(12, 4, 20, 11);
                Line(6.5, 10, 6.5, 20);
                Line(17.5, 10, 17.5, 20);
                Line(6.5, 20, 17.5, 20);
                break;
            case IconKind.Media:
                context.DrawEllipse(null, pen, P(8, 18), 3 * scale, 3 * scale);
                context.DrawEllipse(null, pen, P(18, 15.5), 3 * scale, 3 * scale);
                Line(11, 18, 11, 8);
                Line(21, 15.5, 21, 5.5);
                Line(11, 8, 21, 5.5);
                break;
            case IconKind.Grid:
                Rounded(4.5, 4.5, 6, 6, 2);
                Rounded(13.5, 4.5, 6, 6, 2);
                Rounded(4.5, 13.5, 6, 6, 2);
                Rounded(13.5, 13.5, 6, 6, 2);
                break;
            case IconKind.Wifi:
                Dot(12, 18.5, 1.5);
                Arc(12, 22, 6.5, 215, 110);
                Arc(12, 22, 11.5, 220, 100);
                break;
            case IconKind.Monitor:
                Rounded(3.5, 5, 17, 11.5, 2);
                Line(12, 16.5, 12, 20);
                Line(8, 20.5, 16, 20.5);
                break;
            case IconKind.Calendar:
                Rounded(4, 6, 16, 14, 2.5);
                Line(4, 10.5, 20, 10.5);
                Line(8.5, 3.5, 8.5, 7.5);
                Line(15.5, 3.5, 15.5, 7.5);
                break;
            case IconKind.Task:
                Rounded(4.5, 4.5, 15, 15, 3);
                Line(8.5, 12.5, 11.5, 15.5);
                Line(11.5, 15.5, 16, 9);
                break;
            case IconKind.Sun:
                context.DrawEllipse(brush, null, P(12, 12), 4.2 * scale, 4.2 * scale);
                Rays(7.6, 11.4);
                break;
            case IconKind.More:
                Dot(6, 12, 1.6);
                Dot(12, 12, 1.6);
                Dot(18, 12, 1.6);
                break;
            case IconKind.Rollback:
                Arc(12.5, 13, 5.8, 200, 250);
                Line(7, 6.5, 7, 11.5);
                Line(7, 11.5, 12, 11.5);
                break;
            case IconKind.Warning:
                context.DrawGeometry(null, pen, Polygon(P(12, 3.5), P(21, 20), P(3, 20)));
                Line(12, 9.5, 12, 14);
                Dot(12, 17, 1.1);
                break;
            case IconKind.ExternalLink:
                Rounded(4.5, 8, 11.5, 11.5, 2);
                Line(13, 4.5, 19.5, 4.5);
                Line(19.5, 4.5, 19.5, 11);
                Line(11, 13, 19, 5);
                break;
            case IconKind.Folder:
                Line(3.5, 19, 3.5, 5.5);
                Line(3.5, 5.5, 9.5, 5.5);
                Line(9.5, 5.5, 11.5, 9);
                Line(11.5, 9, 20.5, 9);
                Line(20.5, 9, 20.5, 19);
                Line(20.5, 19, 3.5, 19);
                break;
            case IconKind.Document:
                Rounded(5, 3.5, 14, 17, 2);
                Line(8, 8.5, 16, 8.5);
                Line(8, 12.5, 16, 12.5);
                Line(8, 16.5, 13, 16.5);
                break;
            default:
                Dot(12, 12, 3);
                break;
        }
    }

    /// <summary>Draws an icon at the given opacity (used for ghost layers/disabled states).</summary>
    public static void DrawScaled(DrawingContext context, IconKind kind, Rect bounds, IBrush brush, double opacity)
    {
        using var state = context.PushOpacity(opacity);
        Draw(context, kind, bounds, brush);
    }

    private static StreamGeometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(points[0], isFilled: true);
        for (var i = 1; i < points.Length; i++)
        {
            ctx.LineTo(points[i]);
        }

        ctx.EndFigure(true);
        return geometry;
    }

    private static StreamGeometry ArcGeometry(Point center, double radius, double startDeg, double sweepDeg)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var start = PointOnCircle(center, radius, startDeg);
        var end = PointOnCircle(center, radius, startDeg + sweepDeg);
        ctx.BeginFigure(start, isFilled: false);
        var large = Math.Abs(sweepDeg) > 180;
        var sweep = sweepDeg >= 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
        var size = new Size(radius, radius);
        if (Math.Abs(sweepDeg) >= 359.9)
        {
            // Full circle: two half arcs (an arc to itself is degenerate).
            var mid = PointOnCircle(center, radius, startDeg + 180);
            ctx.ArcTo(mid, size, 0, false, sweep);
            ctx.ArcTo(start, size, 0, false, sweep);
        }
        else
        {
            ctx.ArcTo(end, size, 0, large, sweep);
        }

        ctx.EndFigure(false);
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(center.X + (Math.Cos(radians) * radius), center.Y + (Math.Sin(radians) * radius));
    }
}
