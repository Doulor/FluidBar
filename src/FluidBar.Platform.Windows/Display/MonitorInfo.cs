using System.Collections.Immutable;

namespace FluidBar.Platform.Windows.Display;

/// <summary>Rectangle in physical pixels (physical screen coordinates).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    public override string ToString() => $"({X},{Y} {Width}x{Height})";
}

/// <summary>Rectangle in device-independent pixels (DIPs, 1/96 inch).</summary>
public readonly record struct DipRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"({X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##})";
}

/// <summary>Where the island sits inside a monitor's work area.</summary>
public enum IslandAnchor
{
    TopCenter,
    TopLeft,
    TopRight,
    BottomCenter,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Immutable description of one display: physical bounds, work area, effective DPI and identity.
/// </summary>
/// <param name="Id">Stable device id from <c>MONITORINFOEX.szDevice</c>, e.g. <c>\\.\DISPLAY1</c>.</param>
/// <param name="Index">Enumeration index (0-based, stable within one enumeration).</param>
/// <param name="Bounds">Full monitor rectangle in physical pixels.</param>
/// <param name="WorkArea">Work area (excluding taskbar) in physical pixels.</param>
/// <param name="DpiX">Effective horizontal DPI.</param>
/// <param name="DpiY">Effective vertical DPI.</param>
/// <param name="IsPrimary">Whether this is the primary monitor.</param>
public sealed record MonitorInfo(
    string Id,
    int Index,
    PixelRect Bounds,
    PixelRect WorkArea,
    uint DpiX,
    uint DpiY,
    bool IsPrimary)
{
    /// <summary>DIP → physical scale factor (1.0 at 96 DPI).</summary>
    public double Scale => DpiX / 96.0;

    /// <summary>Creates a monitor description with a single DPI value (Y = X).</summary>
    public static MonitorInfo Create(string id, int index, PixelRect bounds, PixelRect workArea, uint dpi, bool isPrimary)
        => new(id, index, bounds, workArea, dpi, dpi, isPrimary);
}

/// <summary>Immutable, value-equal set of monitors (the monitor service snapshot type).</summary>
public sealed class MonitorSet : IEquatable<MonitorSet>
{
    public static readonly MonitorSet Empty = new(ImmutableArray<MonitorInfo>.Empty);

    public MonitorSet(ImmutableArray<MonitorInfo> monitors)
    {
        Monitors = monitors;
        Primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
    }

    /// <summary>Monitors ordered by enumeration index.</summary>
    public ImmutableArray<MonitorInfo> Monitors { get; }

    /// <summary>Primary monitor (falls back to the first monitor when none is flagged).</summary>
    public MonitorInfo? Primary { get; }

    public int Count => Monitors.Length;

    public bool Equals(MonitorSet? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Monitors.Length != other.Monitors.Length)
        {
            return false;
        }

        for (var i = 0; i < Monitors.Length; i++)
        {
            if (!Monitors[i].Equals(other.Monitors[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is MonitorSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var monitor in Monitors)
        {
            hash.Add(monitor);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => string.Join("; ", Monitors.Select(m => $"{m.Id} {m.Bounds} @{m.DpiX}dpi"));
}
