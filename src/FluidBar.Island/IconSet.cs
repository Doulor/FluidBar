using SkiaSharp;

namespace FluidBar.Island;

/// <summary>Icon identities used by the island (subset of the App icon set, path-based).</summary>
public enum IconKind
{
    /// <summary>Play.</summary>
    Play,

    /// <summary>Pause.</summary>
    Pause,

    /// <summary>Previous track.</summary>
    Previous,

    /// <summary>Next track.</summary>
    Next,

    /// <summary>Battery outline.</summary>
    Battery,

    /// <summary>Charging bolt.</summary>
    Bolt,

    /// <summary>Wi-Fi.</summary>
    Wifi,

    /// <summary>Focus/do-not-disturb.</summary>
    Focus,

    /// <summary>Settings gear.</summary>
    Settings,

    /// <summary>Store/market.</summary>
    Store,

    /// <summary>Close.</summary>
    Close,

    /// <summary>Chevron down.</summary>
    ChevronDown,

    /// <summary>Chevron right.</summary>
    ChevronRight,

    /// <summary>Chevron left.</summary>
    ChevronLeft,

    /// <summary>Music note.</summary>
    Media,

    /// <summary>Notification bell.</summary>
    Bell,

    /// <summary>Clipboard.</summary>
    Clipboard,

    /// <summary>Agent sparkle.</summary>
    Agent,

    /// <summary>Pin.</summary>
    Pin,

    /// <summary>Check mark.</summary>
    Check,

    /// <summary>Document.</summary>
    Document,

    /// <summary>Grid/widgets.</summary>
    Grid,

    /// <summary>Search.</summary>
    Search,

    /// <summary>Clock.</summary>
    Clock,

    /// <summary>Download/progress.</summary>
    Download,

    /// <summary>Lock.</summary>
    Lock,

    /// <summary>USB device.</summary>
    Usb,

    /// <summary>Bluetooth.</summary>
    Bluetooth,

    /// <summary>Home.</summary>
    Home,

    /// <summary>Volume (generic).</summary>
    Volume,

    /// <summary>Muted volume.</summary>
    VolumeMute,

    /// <summary>Low volume.</summary>
    VolumeLow,

    /// <summary>Medium volume.</summary>
    VolumeMedium,

    /// <summary>High volume.</summary>
    VolumeHigh,

    /// <summary>Brightness (generic).</summary>
    Brightness,

    /// <summary>Low brightness.</summary>
    BrightnessLow,

    /// <summary>High brightness.</summary>
    BrightnessHigh,

    /// <summary>Alert/warning.</summary>
    Alert,

    /// <summary>Moon (dark/focus).</summary>
    Moon,

    /// <summary>Sun (light).</summary>
    Sun,

    /// <summary>Cloud + sun (weather).</summary>
    CloudSun,

    /// <summary>Speaker/output.</summary>
    Speaker,

    /// <summary>Play queue.</summary>
    Queue,

    /// <summary>Timer.</summary>
    Timer,

    /// <summary>Copy to clipboard.</summary>
    Copy,

    /// <summary>Code snippet.</summary>
    Code,

    /// <summary>Web link.</summary>
    Link,

    /// <summary>Image preview.</summary>
    Image,

    /// <summary>Calendar.</summary>
    Calendar,

    /// <summary>Chat/message.</summary>
    Chat,
}

/// <summary>
/// Path-based icon set drawn on a 20×20 grid with one 1.5 DIP hairline stroke and round caps/joins.
/// its ink is centred on the 20 unit viewbox centre (10,10) and scaled to the optical size of its
/// weight class, so a filled transport glyph, a stroke glyph and a small affordance never drift apart.
/// Paths are built once and reused; drawing only sets the paint colour, so per-frame cost is one
/// transform plus two path draws.
/// </summary>
public static class IconSet
{
    /// <summary>Coordinate grid the final paths live in (20 units = the 20 DIP viewbox).</summary>
    private const float Grid = 20f;

    /// <summary>The single stroke weight used by every stroked glyph (DIP at the nominal 20 DIP size).</summary>
    private const float Stroke = 1.5f;

    /// <summary>Centre of the viewbox in grid units.</summary>
    private const float Center = Grid / 2f;

    /// <summary>Optical size class: what fraction of the viewbox a glyph may occupy.</summary>
    private enum OpticalClass
    {
        /// <summary>Solid glyphs (transport, bolt, sparkle, alert): ~62 % of the box, they read heavier.</summary>
        Glyph,

        /// <summary>Every stroke glyph: ~80 % of the box.</summary>
        Standard,

        /// <summary>Small affordances (chevrons, close, check, pin): ~60 %, they sit inside buttons.</summary>
        Affordance,
    }

    private static readonly Dictionary<IconKind, IconGeometry> Cache = [];
    private static readonly Lock Gate = new();

    /// <summary>Draws an icon centred in <paramref name="bounds"/>.</summary>
    public static void Draw(SKCanvas canvas, IconKind kind, SKRect bounds, SKColor color, SKPaint paint)
    {
        var size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 0 || color.Alpha == 0)
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
                // Do-not-disturb: circle + slash. The old crescent was indistinguishable from Moon,
                // which made Focus and the dark-theme toggle look like the same button.
                b.Ellipse(12, 12, 8, 8);
                b.Line(6.8f, 17.2f, 17.2f, 6.8f);
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
                // Marketplace = shopping bag: rounded body plus a handle arc (the old glyph had two
                // unexplained vertical bars inside the body and read as a basket).
                b.Rounded(4.2f, 8.2f, 15.6f, 11.6f, 2.4f);
                b.Arc(12, 8.2f, 3.6f, 180, 180);
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
            case IconKind.ChevronLeft:
                b.Line(14.5f, 6.5f, 9, 12);
                b.Line(14.5f, 17.5f, 9, 12);
                break;
            case IconKind.Media:
                b.Ellipse(8, 18, 3, 3);
                b.Ellipse(18, 15.5f, 3, 3);
                b.Line(11, 18, 11, 8);
                b.Line(21, 15.5f, 21, 5.5f);
                b.Line(11, 8, 21, 5.5f);
                break;
            case IconKind.Bell:
                b.Arc(12, 11, 6.4f, 185, 170);
                b.Line(5.75f, 10.45f, 5.2f, 16.4f);
                b.Line(18.25f, 10.45f, 18.8f, 16.4f);
                b.Line(5.2f, 16.4f, 18.8f, 16.4f);
                b.Arc(12, 16.4f, 2.2f, 20, 140);
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
            case IconKind.Home:
                b.Line(4, 11, 12, 4);
                b.Line(20, 11, 12, 4);
                b.Line(6, 9.5f, 6, 20);
                b.Line(18, 9.5f, 18, 20);
                b.Line(6, 20, 18, 20);
                b.Line(10, 20, 10, 14);
                b.Line(14, 20, 14, 14);
                b.Line(10, 14, 14, 14);
                break;
            case IconKind.Volume:
                b.FillPolygon((4, 9.5f), (8, 9.5f), (12.5f, 5.5f), (12.5f, 18.5f), (8, 14.5f), (4, 14.5f));
                b.Arc(12.5f, 12, 4.5f, -55, 110);
                b.Arc(12.5f, 12, 8.0f, -55, 110);
                break;
            case IconKind.VolumeMute:
                b.FillPolygon((4, 9.5f), (8, 9.5f), (12.5f, 5.5f), (12.5f, 18.5f), (8, 14.5f), (4, 14.5f));
                b.Line(16, 9.5f, 20, 14.5f);
                b.Line(20, 9.5f, 16, 14.5f);
                break;
            case IconKind.VolumeLow:
                b.FillPolygon((4, 9.5f), (8, 9.5f), (12.5f, 5.5f), (12.5f, 18.5f), (8, 14.5f), (4, 14.5f));
                b.Arc(12.5f, 12, 4.5f, -55, 110);
                break;
            case IconKind.VolumeMedium:
                b.FillPolygon((4, 9.5f), (8, 9.5f), (12.5f, 5.5f), (12.5f, 18.5f), (8, 14.5f), (4, 14.5f));
                b.Arc(12.5f, 12, 4.5f, -55, 110);
                b.Arc(12.5f, 12, 8.0f, -55, 110);
                break;
            case IconKind.VolumeHigh:
                b.FillPolygon((4, 9.5f), (8, 9.5f), (12.5f, 5.5f), (12.5f, 18.5f), (8, 14.5f), (4, 14.5f));
                b.Arc(12.5f, 12, 4.5f, -55, 110);
                b.Arc(12.5f, 12, 8.0f, -55, 110);
                b.Arc(12.5f, 12, 11.5f, -55, 110);
                break;
            case IconKind.Brightness:
                b.Dot(12, 12, 3.9f);
                b.Rays(12, 12, 7.6f, 10.0f, 8);
                break;
            case IconKind.BrightnessLow:
                b.Dot(12, 12, 2.9f);
                b.Rays(12, 12, 7.6f, 10.0f, 8);
                break;
            case IconKind.BrightnessHigh:
                b.Dot(12, 12, 5.0f);
                b.Rays(12, 12, 7.6f, 10.0f, 8);
                break;
            case IconKind.Alert:
                // Outline triangle + solid mark: the previous filled triangle hid its own exclamation
                // (both paths were painted in the same colour, so the glyph read as a blank triangle).
                b.Line(12, 4, 20.4f, 19);
                b.Line(20.4f, 19, 3.6f, 19);
                b.Line(3.6f, 19, 12, 4);
                b.FillRect(11.1f, 9.6f, 1.8f, 4.9f);
                b.Dot(12, 16.9f, 1);
                break;
            case IconKind.Moon:
                // Crescent with joined ends: left half-circle plus a shallow inner arc that bulges right.
                b.Arc(12, 12, 8, 90, 180);
                b.Arc(2, 12, 12.806f, -38.66f, 77.32f);
                break;
            case IconKind.Sun:
                // Light theme: an outline core with long rays. BrightnessHigh used to be pixel-identical,
                // which made the theme toggle and the brightness slider read as the same control.
                b.Ellipse(12, 12, 4.0f, 4.0f);
                b.Rays(12, 12, 7.2f, 10.2f, 8);
                break;
            case IconKind.CloudSun:
                // Cloud silhouette: a main dome, a smaller right bump and a flat base; the sun sits in
                // the top-right corner with short rays so the two shapes never overlap.
                b.Arc(9.4f, 14.6f, 4.2f, 180, 180);
                b.Arc(13.6f, 14.2f, 3.2f, 270, 90);
                b.Line(5.2f, 14.6f, 5.2f, 17.0f);
                b.Line(16.8f, 14.2f, 16.8f, 17.0f);
                b.Line(5.2f, 17.0f, 16.8f, 17.0f);
                b.Dot(17.2f, 7.0f, 1.9f);
                b.Rays(17.2f, 7.0f, 2.9f, 3.9f, 6);
                break;
            case IconKind.Speaker:
                b.FillPolygon((4, 9.5f), (8, 9.5f), (12.5f, 5.5f), (12.5f, 18.5f), (8, 14.5f), (4, 14.5f));
                b.Arc(12.5f, 12, 4.5f, -55, 110);
                break;
            case IconKind.Queue:
                b.Line(4, 7, 16, 7);
                b.Line(4, 12, 16, 12);
                b.Line(4, 17, 12, 17);
                b.FillPolygon((16, 15), (21, 18), (16, 21));
                break;
            case IconKind.Timer:
                b.Ellipse(12, 13, 7.5f, 7.5f);
                b.Line(12, 13, 12, 9);
                b.Line(12, 13, 15, 14.5f);
                b.Line(9, 3.5f, 15, 3.5f);
                b.Line(12, 3.5f, 12, 5.5f);
                break;
            case IconKind.Copy:
                // Front card plus only the silhouette of the sheet behind it: its top edge, the rounded
                // top-left corner, the left edge and a stub that stops on the front card, so the back
                // outline never runs through the front one (the old pair of full rectangles did).
                b.Rounded(7.4f, 7.4f, 12.5f, 12.5f, 2.2f);
                b.Line(15.4f, 4.2f, 5.8f, 4.2f);
                b.Arc(5.8f, 5.8f, 1.6f, 180, 90);
                b.Line(4.2f, 5.8f, 4.2f, 14.4f);
                b.Arc(5.8f, 14.4f, 1.6f, 180, -90);
                b.Line(5.8f, 16, 7.4f, 16);
                break;
            case IconKind.Code:
                b.Line(8, 8, 4, 12);
                b.Line(4, 12, 8, 16);
                b.Line(16, 8, 20, 12);
                b.Line(20, 12, 16, 16);
                b.Line(13.5f, 6, 10.5f, 18);
                break;
            case IconKind.Link:
                b.Arc(9, 12, 5.5f, 45, 225);
                b.Arc(15, 12, 5.5f, 225, 225);
                b.Line(9, 12, 15, 12);
                break;
            case IconKind.Image:
                b.Rounded(3.5f, 5, 17, 14, 2.5f);
                b.Ellipse(9, 10, 2, 2);
                b.Line(5, 17, 10, 12.5f);
                b.Line(10, 12.5f, 14, 16);
                b.Line(14, 16, 16.5f, 13.5f);
                b.Line(16.5f, 13.5f, 19.5f, 16.5f);
                break;
            case IconKind.Calendar:
                b.Rounded(3.5f, 5.5f, 17, 15, 2.5f);
                b.Line(3.5f, 10, 20.5f, 10);
                b.Line(8, 3.5f, 8, 7);
                b.Line(16, 3.5f, 16, 7);
                b.Dot(8, 14, 1);
                b.Dot(12, 14, 1);
                b.Dot(16, 14, 1);
                break;
            case IconKind.Chat:
                b.Rounded(3.5f, 5, 17, 12, 3);
                b.Line(8, 17, 7, 20.5f);
                b.Line(7, 20.5f, 11.5f, 17);
                b.Dot(8, 11, 1);
                b.Dot(12, 11, 1);
                b.Dot(16, 11, 1);
                break;
            default:
                b.Dot(12, 12, 3);
                break;
        }

        return Normalize(kind, new IconGeometry(stroke, fill));
    }

    /// <summary>
    /// Optical normalisation: centres the glyph's ink on the viewbox centre and scales its longest
    /// side to the target of its weight class. The stroke is painted in grid units, so it stays 1.5 DIP
    /// regardless of how much the path itself is scaled (that is the optical-size, not weight, knob).
    /// </summary>
    private static IconGeometry Normalize(IconKind kind, IconGeometry geometry)
    {
        var bounds = InkBounds(geometry);
        if (bounds.Width <= 0.01f || bounds.Height <= 0.01f)
        {
            return geometry;
        }

        var longest = Math.Max(bounds.Width, bounds.Height);
        var target = TargetSize(kind);
        var scale = target / longest;
        var toOrigin = SKMatrix.CreateTranslation(-bounds.MidX, -bounds.MidY);
        var toCenter = SKMatrix.CreateTranslation(Center, Center);
        var scaleMatrix = SKMatrix.CreateScale(scale, scale);
        Transform(geometry.Stroke, toOrigin, scaleMatrix, toCenter);
        Transform(geometry.Fill, toOrigin, scaleMatrix, toCenter);
        return geometry;

        static void Transform(SKPath path, SKMatrix a, SKMatrix b, SKMatrix c)
        {
            if (path.IsEmpty)
            {
                return;
            }

            path.Transform(a);
            path.Transform(b);
            path.Transform(c);
        }
    }

    /// <summary>Union of the stroke and fill geometry bounds (no stroke width).</summary>
    private static SKRect InkBounds(IconGeometry geometry)
    {
        var stroke = geometry.Stroke.IsEmpty ? SKRect.Empty : geometry.Stroke.Bounds;
        var fill = geometry.Fill.IsEmpty ? SKRect.Empty : geometry.Fill.Bounds;
        if (stroke.IsEmpty)
        {
            return fill;
        }

        if (fill.IsEmpty)
        {
            return stroke;
        }

        return SKRect.Union(stroke, fill);
    }

    /// <summary>Longest side a glyph may occupy, in grid units, for its optical class.</summary>
    private static float TargetSize(IconKind kind) => ClassOf(kind) switch
    {
        OpticalClass.Glyph => 12.4f,      // 62 % of the 20 unit box
        OpticalClass.Affordance => 12.0f, // 60 %
        _ => 16.0f,                       // 80 %
    };

    /// <summary>Optical weight class of an icon.</summary>
    private static OpticalClass ClassOf(IconKind kind) => kind switch
    {
        IconKind.Play or IconKind.Pause or IconKind.Previous or IconKind.Next or IconKind.Bolt
            or IconKind.Agent or IconKind.Alert => OpticalClass.Glyph,
        IconKind.Close or IconKind.ChevronDown or IconKind.ChevronRight or IconKind.ChevronLeft
            or IconKind.Check or IconKind.Pin => OpticalClass.Affordance,
        _ => OpticalClass.Standard,
    };

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

        public void Rays(double cx, double cy, double inner, double outer, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var angle = i * (Math.PI * 2 / count);
                Line(
                    cx + (Math.Cos(angle) * inner), cy + (Math.Sin(angle) * inner),
                    cx + (Math.Cos(angle) * outer), cy + (Math.Sin(angle) * outer));
            }
        }
    }
}
