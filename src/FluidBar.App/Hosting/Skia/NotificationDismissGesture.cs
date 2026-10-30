namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Pure drag/resistance rules for the compact notification card. Kept outside the controller so the
/// feel (resistance, threshold and flick) is testable without a window or a real pointer.
/// </summary>
internal static class NotificationDismissGesture
{
    /// <summary>Upward travel that commits the dismissal: two 36-DIP compact rows.</summary>
    public const double DismissThresholdDip = 72;

    /// <summary>Resistance radius: movement is 1:1 at the start and logarithmically heavier further out.</summary>
    public const double DampingRadiusDip = 64;

    /// <summary>Destination of the exit spring (fully above the host surface).</summary>
    public const double DismissTargetDip = -260;

    /// <summary>Offset at which the card is off-screen and the notification may be removed.</summary>
    public const double DismissAtDip = -150;

    /// <summary>Upward release speed that dismisses even before the full two-row travel.</summary>
    public const double FlickVelocityDipPerSecond = 700;

    /// <summary>Maximum downward rubber-band offset while dragging the card away from the edge.</summary>
    public const double MaxDownwardOffsetDip = 32;

    /// <summary>
    /// Converts raw pointer travel (positive = up) into the painted offset. The logarithmic curve
    /// starts at 1:1 and gets progressively heavier, so a short drag feels direct while a long one
    /// cannot outrun the finger. Downward travel is heavily damped and capped.
    /// </summary>
    public static double OffsetYDip(double rawDeltaY)
    {
        if (!double.IsFinite(rawDeltaY) || rawDeltaY == 0)
        {
            return 0;
        }

        if (rawDeltaY > 0)
        {
            return -DampingRadiusDip * Math.Log(1 + (rawDeltaY / DampingRadiusDip));
        }

        return Math.Min(MaxDownwardOffsetDip, -rawDeltaY * 0.22);
    }

    /// <summary>
    /// Whether releasing at this travel/velocity should dismiss. A deliberate two-row pull always
    /// commits; a shorter pull commits only with a clear upward flick.
    /// </summary>
    public static bool ShouldDismiss(double rawDeltaY, double velocityDipPerSecond)
        => rawDeltaY >= DismissThresholdDip
           || (rawDeltaY >= DismissThresholdDip * 0.4 && velocityDipPerSecond <= -FlickVelocityDipPerSecond);
}
