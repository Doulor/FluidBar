namespace FluidBar.App.Hosting.Skia;

/// <summary>What a drag gesture means for the Skia island host.</summary>
public enum SkiaDragKind
{
    /// <summary>Nothing draggable was hit (or dragging is disabled in this mode).</summary>
    None,

    /// <summary>Horizontal pager drag inside the expanded panel.</summary>
    Pager,

    /// <summary>Compact pill press: becomes a window move once the pointer passes the threshold.</summary>
    WindowCandidate,
}

/// <summary>
/// Pure click-vs-drag discrimination for the compact island shell.
/// <para>
/// The press alone no longer expands: a click is committed on release only when the pointer stayed
/// within <see cref="ThresholdDip"/>. Beyond that the gesture becomes a window move. The threshold is
/// measured in <b>screen</b> DIP, not client DIP: once the window starts following the pointer the
/// client coordinates stop changing, so a client-space threshold could never latch.
/// </para>
/// <para>
/// 6 DIP matches the spec's "&gt;6 DIP cancels the in-page click" rule (spec 2.4) and is comfortably
/// above the 4 px Win32 drag metric at every DPI.
/// </para>
/// </summary>
public static class SkiaDragThreshold
{
    /// <summary>Distance in DIP beyond which a press becomes a drag (6).</summary>
    public const double ThresholdDip = 6;

    /// <summary>Whether the pointer moved far enough from the press point to count as a drag.</summary>
    /// <param name="startXDip">Press point X in screen DIP.</param>
    /// <param name="startYDip">Press point Y in screen DIP.</param>
    /// <param name="currentXDip">Current pointer X in screen DIP.</param>
    /// <param name="currentYDip">Current pointer Y in screen DIP.</param>
    public static bool Exceeds(double startXDip, double startYDip, double currentXDip, double currentYDip)
    {
        var dx = currentXDip - startXDip;
        var dy = currentYDip - startYDip;
        return (dx * dx) + (dy * dy) >= ThresholdDip * ThresholdDip;
    }

    /// <summary>Straight-line distance in DIP between two screen points.</summary>
    /// <param name="startXDip">Start X in screen DIP.</param>
    /// <param name="startYDip">Start Y in screen DIP.</param>
    /// <param name="currentXDip">Current X in screen DIP.</param>
    /// <param name="currentYDip">Current Y in screen DIP.</param>
    public static double Distance(double startXDip, double startYDip, double currentXDip, double currentYDip)
    {
        var dx = currentXDip - startXDip;
        var dy = currentYDip - startYDip;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
