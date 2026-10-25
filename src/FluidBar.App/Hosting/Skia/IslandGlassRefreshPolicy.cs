namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Decides whether a landed desktop capture has to wake the island frame loop while the island sits idle.
/// <para>
/// Waking on the mean-luminance delta alone is not enough: a different window, a video, scrolling or a colour
/// change behind the island can leave the average brightness identical, and the glass then kept showing the old
/// capture until some unrelated activity repainted a frame (report 2026-10-25: "背景变化不刷新，要等产生过一个
/// 活动后才刷新"). The content signature closes that hole while staying quiet on a static desktop, so the idle
/// memory trim is not starved by needless repaints.
/// </para>
/// </summary>
internal static class IslandGlassRefreshPolicy
{
    /// <summary>Mean-luminance delta that always counts as a change (drives the glass tint).</summary>
    public const double LuminanceThreshold = 0.02;

    /// <summary>
    /// True when the newly captured material differs from what is on screen and the loop must paint a frame.
    /// </summary>
    /// <param name="previousSignature">Signature of the material currently on screen (-1 when unknown).</param>
    /// <param name="signature">Signature of the capture that just landed (-1 when the provider has none yet).</param>
    /// <param name="previousLuminance">Raw luminance of the material on screen (-1 when unknown).</param>
    /// <param name="luminance">Raw luminance of the capture that just landed.</param>
    public static bool ShouldWake(long previousSignature, long signature, double previousLuminance, double luminance)
    {
        if (signature < 0)
        {
            return false;
        }

        if (previousSignature < 0 || signature != previousSignature)
        {
            return true;
        }

        return previousLuminance < 0 || Math.Abs(luminance - previousLuminance) > LuminanceThreshold;
    }
}
