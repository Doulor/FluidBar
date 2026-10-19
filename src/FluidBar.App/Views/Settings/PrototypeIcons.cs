using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FluidBar.App.Views.Settings;

/// <summary>Prototype icon identities (docs/rewrite/prototype2/icons.js, 24×24 grid, 1.7 stroke).</summary>
internal enum SettingsIconKind
{
    Settings,
    Palette,
    Island,
    Puzzle,
    Storefront,
    Sliders,
    Info,
    Close,
    Check,
    Search,
    Folder,
    File,
    Download,
    Refresh,
    Trash,
    External,
    Github,
    Chat,
    Shield,
    Star,
    Alert,
}

/// <summary>
/// Vector icon drawn with the prototype's exact path data (icons.js is the single source of truth for
/// the settings chrome). Stroke width, caps and joins match the prototype's <c>stroke-width="1.7"</c>.
/// </summary>
internal sealed class SettingsIcon : Control
{
    private static readonly Pen FallbackPen = new(Brushes.White, 1);

    /// <summary>Icon identity.</summary>
    public SettingsIconKind Kind { get; set; } = SettingsIconKind.Settings;

    /// <summary>Stroke/fill brush (<c>currentColor</c> in the prototype).</summary>
    public IBrush Brush { get; set; } = Brushes.White;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        PrototypeIcons.Draw(context, Kind, new Rect(0, 0, size, size), Brush);
    }
}

/// <summary>Transcribed prototype icon paths (icons.js).</summary>
internal static class PrototypeIcons
{
    private const double Grid = 24;

    private sealed record Icon(string[] Stroke, string[] Fill, Matrix Transform = default);

    private static readonly Dictionary<SettingsIconKind, Icon> Icons = new()
    {
        [SettingsIconKind.Settings] = new(
            [
                Circle(12, 12, 3.1),
                "M19.1 13.8a1.6 1.6 0 0 0 .3 1.8l.1.1-2.6 2.6-.1-.1a1.6 1.6 0 0 0-1.8-.3 1.6 1.6 0 0 0-1 1.5v.2h-3.7v-.2a1.6 1.6 0 0 0-1-1.5 1.6 1.6 0 0 0-1.8.3l-.1.1-2.6-2.6.1-.1a1.6 1.6 0 0 0 .3-1.8 1.6 1.6 0 0 0-1.5-1H4v-3.7h.2a1.6 1.6 0 0 0 1.5-1 1.6 1.6 0 0 0-.3-1.8l-.1-.1 2.6-2.6.1.1a1.6 1.6 0 0 0 1.8.3 1.6 1.6 0 0 0 1-1.5v-.2h3.7v.2a1.6 1.6 0 0 0 1 1.5 1.6 1.6 0 0 0 1.8-.3l.1-.1 2.6 2.6-.1.1a1.6 1.6 0 0 0-.3 1.8 1.6 1.6 0 0 0 1.5 1h.2v3.7h-.2a1.6 1.6 0 0 0-1.5 1z",
            ],
            []),
        [SettingsIconKind.Palette] = new(
            [
                "M12 3a9 9 0 1 0 0 18h1.4a2.1 2.1 0 0 0 1.2-3.8 2.1 2.1 0 0 1 1.2-3.8H18a3 3 0 0 0 3-3A9 9 0 0 0 12 3z",
            ],
            [Circle(7.8, 11.6, 1.1), Circle(9.8, 7.4, 1.1), Circle(14.2, 7.2, 1.1)]),
        [SettingsIconKind.Island] = new(
            [RoundRect(5.2, 3.2, 13.6, 17.6, 6.8), "M9.8 9.2h4.4M9.8 13.2h2.8"],
            []),
        [SettingsIconKind.Puzzle] = new(
            [
                "M9.2 4.2A2.2 2.2 0 0 1 13.6 4v1.4h2.8a1.6 1.6 0 0 1 1.6 1.6v3h1.4a2.2 2.2 0 1 1 0 4.4H18v3.4a1.6 1.6 0 0 1-1.6 1.6h-3.2v-1.4a2.2 2.2 0 0 0-4.4 0v1.4H5.4a1.6 1.6 0 0 1-1.6-1.6v-3.2H5a2.2 2.2 0 1 0 0-4.4H3.8V7a1.6 1.6 0 0 1 1.6-1.6h3.8z",
            ],
            []),
        [SettingsIconKind.Storefront] = new(
            [
                "M4 9.2V19a1.6 1.6 0 0 0 1.6 1.6h12.8A1.6 1.6 0 0 0 20 19V9.2",
                "M3.2 9.2 5 3.8h14l1.8 5.4a3.2 3.2 0 0 1-5.9 1.7 3.2 3.2 0 0 1-5.8 0 3.2 3.2 0 0 1-5.9-1.7z",
                "M9.2 20.6v-5.4h5.6v5.4",
            ],
            []),
        [SettingsIconKind.Sliders] = new(
            [
                "M4 7.2h8.4M17.2 7.2h2.8M4 16.8h2.8M11.6 16.8H20",
                Circle(15, 7.2, 2.2),
                Circle(9.4, 16.8, 2.2),
            ],
            []),
        [SettingsIconKind.Info] = new(
            [Circle(12, 12, 8.8), "M12 10.8v5.4"],
            [Circle(12, 7.9, 1)]),
        [SettingsIconKind.Close] = new(["M6 6l12 12M18 6 6 18"], []),
        [SettingsIconKind.Check] = new(["m4.5 12.5 5 5L19.5 7"], []),
        [SettingsIconKind.Search] = new([Circle(10.8, 10.8, 6.3), "m15.5 15.5 4.5 4.5"], []),
        [SettingsIconKind.Folder] = new(
            ["M3 6.4A2.4 2.4 0 0 1 5.4 4h3.7l2 2.4h7.5A2.4 2.4 0 0 1 21 8.8v8.8a2.4 2.4 0 0 1-2.4 2.4H5.4A2.4 2.4 0 0 1 3 17.6z"],
            []),
        [SettingsIconKind.File] = new(
            [
                "M13.4 2.8H6.8A1.8 1.8 0 0 0 5 4.6v14.8a1.8 1.8 0 0 0 1.8 1.8h10.4a1.8 1.8 0 0 0 1.8-1.8V8z",
                "M13.2 3v5.2H19",
            ],
            []),
        [SettingsIconKind.Download] = new(
            ["M12 3.4v11.2", "m7.4 10.2 4.6 4.6 4.6-4.6", "M4.4 19.6h15.2"],
            []),
        [SettingsIconKind.Refresh] = new(
            [
                "M20 11a8 8 0 0 0-14.8-4",
                "M4 4v5h5",
                "M4 13a8 8 0 0 0 14.8 4",
                "M20 20v-5h-5",
            ],
            []),
        [SettingsIconKind.Trash] = new(
            [
                "M4.4 7h15.2M9.4 3.4h5.2l.8 3.6H8.6z",
                "m6.4 7 .9 12.2a1.8 1.8 0 0 0 1.8 1.6h5.8a1.8 1.8 0 0 0 1.8-1.6L17.6 7",
                "M10.2 11v5.6M13.8 11v5.6",
            ],
            []),
        [SettingsIconKind.External] = new(
            [
                "M14 4h6v6",
                "M20 4 11 13",
                "M18 14v4.5A1.5 1.5 0 0 1 16.5 20h-11A1.5 1.5 0 0 1 4 18.5v-11A1.5 1.5 0 0 1 5.5 6H10",
            ],
            []),
        [SettingsIconKind.Github] = new(
            [
                "M9.2 20.4c-4.2 1.3-4.2-2.1-5.9-2.6m11.8 5.3v-3.3c0-1 .1-1.4-.5-2 2.4-.3 4.9-1.2 4.9-5.3a4.1 4.1 0 0 0-1.1-2.9 3.8 3.8 0 0 0-.1-2.8s-.9-.3-3 1.1a10.3 10.3 0 0 0-5.4 0C7.8 5.5 6.9 5.8 6.9 5.8a3.8 3.8 0 0 0-.1 2.8 4.1 4.1 0 0 0-1.1 2.9c0 4.1 2.5 5 4.9 5.3-.4.4-.6.9-.6 1.5v3.6",
            ],
            [],
            Matrix.CreateScale(0.9, 0.9) * Matrix.CreateTranslation(1.4, -1.4)),
        [SettingsIconKind.Chat] = new(
            ["M20.6 11.6a8.2 8.2 0 0 1-8.2 8.2 8.6 8.6 0 0 1-3.6-.8L4 20.4l1.4-4.4a8.2 8.2 0 1 1 15.2-4.4z"],
            [Circle(8.4, 10.8, 1.4), Circle(12, 10.8, 1.4), Circle(15.6, 10.8, 1.4)]),
        [SettingsIconKind.Shield] = new(
            [
                "M12 2.8 4.6 5.8v5.7c0 4.7 3.1 7.9 7.4 9.7 4.3-1.8 7.4-5 7.4-9.7V5.8z",
                "m8.6 11.8 2.3 2.3 4.5-4.6",
            ],
            []),
        [SettingsIconKind.Star] = new(
            ["m12 3.2 2.7 5.5 6.1.9-4.4 4.3 1 6.1-5.4-2.9-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z"],
            []),
        [SettingsIconKind.Alert] = new(["M12 3.6 2.8 20h18.4z", "M12 9.2v4.6"], [Circle(12, 16.8, 0.9)]),
    };

    private static readonly Dictionary<string, Geometry> GeometryCache = new(StringComparer.Ordinal);

    /// <summary>Draws one prototype icon inside <paramref name="bounds"/>.</summary>
    public static void Draw(DrawingContext context, SettingsIconKind kind, Rect bounds, IBrush brush)
    {
        if (!Icons.TryGetValue(kind, out var icon))
        {
            return;
        }

        var scale = Math.Min(bounds.Width, bounds.Height) / Grid;
        if (scale <= 0)
        {
            return;
        }

        var at = new Point(
            bounds.X + ((bounds.Width - (Grid * scale)) / 2),
            bounds.Y + ((bounds.Height - (Grid * scale)) / 2));
        var pen = new Pen(brush, 1.7 * scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(at.X, at.Y)))
        {
            foreach (var data in icon.Stroke)
            {
                context.DrawGeometry(null, pen, Parse(data));
            }

            foreach (var data in icon.Fill)
            {
                context.DrawGeometry(brush, null, Parse(data));
            }
        }
    }

    private static Geometry Parse(string data)
    {
        if (GeometryCache.TryGetValue(data, out var cached))
        {
            return cached;
        }

        var geometry = StreamGeometry.Parse(data);
        GeometryCache[data] = geometry;
        return geometry;
    }

    private static string Circle(double cx, double cy, double r)
    {
        var l = Num(cx - r);
        var rr = Num(cx + r);
        var y = Num(cy);
        var radius = Num(r);
        return "M" + l + " " + y + "A" + radius + " " + radius + " 0 1 0 " + rr + " " + y +
               "A" + radius + " " + radius + " 0 1 0 " + l + " " + y + "Z";
    }

    private static string RoundRect(double x, double y, double w, double h, double r)
    {
        var x1 = Num(x + r);
        var x2 = Num(x + w - r);
        var y1 = Num(y + r);
        var y2 = Num(y + h - r);
        var xr = Num(x + w);
        var yb = Num(y + h);
        var xs = Num(x);
        var ys = Num(y);
        var radius = Num(r);
        return "M" + x1 + " " + ys +
               "H" + x2 +
               "A" + radius + " " + radius + " 0 0 1 " + xr + " " + y1 +
               "V" + y2 +
               "A" + radius + " " + radius + " 0 0 1 " + x2 + " " + yb +
               "H" + x1 +
               "A" + radius + " " + radius + " 0 0 1 " + xs + " " + y2 +
               "V" + y1 +
               "A" + radius + " " + radius + " 0 0 1 " + x1 + " " + ys + "Z";
    }

    private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
