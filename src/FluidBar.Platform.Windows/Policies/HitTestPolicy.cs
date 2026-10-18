using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Policies;

/// <summary>Result mapped by the App window's <c>WM_NCHITTEST</c> handler.</summary>
public enum HitTestResult
{
    /// <summary><c>HTCLIENT</c>: the click belongs to the island.</summary>
    Client,

    /// <summary><c>HTTRANSPARENT</c>: the click falls through to the window below.</summary>
    Transparent,

    /// <summary><c>HTNOWHERE</c>: no hit-test result (used while hidden).</summary>
    Nowhere,
}

/// <summary>Immutable description of the current island geometry for hit-testing.</summary>
/// <param name="LocalX">Point X in window client coordinates (DIPs).</param>
/// <param name="LocalY">Point Y in window client coordinates (DIPs).</param>
/// <param name="Width">Current shell width in DIPs (animating value, not a token).</param>
/// <param name="Height">Current shell height in DIPs (animating value, not a token).</param>
/// <param name="CornerExponent">Squircle exponent: 2 = ellipse-ish, 4 = iOS-style superellipse, ∞ = rectangle.</param>
/// <param name="HasInteractiveContent">False for collapsed/hidden/click-through modes.</param>
public readonly record struct HitTestGeometry(
    double LocalX,
    double LocalY,
    double Width,
    double Height,
    double CornerExponent = 4.0,
    bool HasInteractiveContent = true);

/// <summary>
/// Pure hit-test policy for <c>WM_NCHITTEST</c> and for the whole-window
/// <c>WS_EX_TRANSPARENT</c> decision.
/// </summary>
/// <remarks>
/// The contract is: collapsed/hidden or non-interactive states are entirely <c>HTTRANSPARENT</c>;
/// interactive states accept clicks only inside the current squircle shell. Never a topmost window
/// that swallows clicks outside the visible pill (audit finding F-05).
/// </remarks>
public static class HitTestPolicy
{
    /// <summary>Default squircle exponent used by the shell geometry.</summary>
    public const double DefaultCornerExponent = 4.0;

    /// <summary>
    /// Evaluates a pointer position against the current shell. The shell rectangle is centred on the
    /// window origin for simplicity: points are relative to the shell's top-left corner.
    /// </summary>
    public static HitTestResult Evaluate(in HitTestGeometry geometry)
    {
        if (!geometry.HasInteractiveContent || geometry.Width <= 0 || geometry.Height <= 0)
        {
            return HitTestResult.Transparent;
        }

        return IsPointInsideShell(geometry.LocalX, geometry.LocalY, geometry.Width, geometry.Height,
            geometry.CornerExponent)
            ? HitTestResult.Client
            : HitTestResult.Transparent;
    }

    /// <summary>
    /// Analytic squircle test: normalise the point into [-1, 1] on both axes relative to the shell
    /// centre, then test <c>|x|^n + |y|^n ≤ 1</c>. This matches the drawn shape closely enough for
    /// hit-testing and is cheap enough to run per <c>WM_NCHITTEST</c>.
    /// </summary>
    public static bool IsPointInsideShell(double x, double y, double width, double height, double exponent = DefaultCornerExponent)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (x < 0 || y < 0 || x > width || y > height)
        {
            return false;
        }

        var halfWidth = width / 2.0;
        var halfHeight = height / 2.0;
        var normalisedX = Math.Abs((x - halfWidth) / halfWidth);
        var normalisedY = Math.Abs((y - halfHeight) / halfHeight);

        // exponent <= 2 degrades to a proper ellipse; very large exponents approximate a rectangle
        // but must still reject the exact corners.
        var safeExponent = Math.Clamp(exponent, 2.0, 64.0);
        var sum = Math.Pow(normalisedX, safeExponent) + Math.Pow(normalisedY, safeExponent);
        return sum <= 1.0;
    }

    /// <summary>
    /// Whole-window <c>WS_EX_TRANSPARENT</c> decision. Only enable it when no part of the window can
    /// receive input (collapsed/hidden); while interactive the per-point <c>WM_NCHITTEST</c> policy
    /// keeps the corners click-through and the pill clickable.
    /// </summary>
    public static bool ShouldWindowBeClickThrough(bool isVisible, bool hasInteractiveContent)
        => !isVisible || !hasInteractiveContent;

    /// <summary>Maps a decision to the Win32 <c>HT*</c> value.</summary>
    public static int ToNativeHitTest(HitTestResult result) => result switch
    {
        HitTestResult.Client => NativeMethods.HTCLIENT,
        HitTestResult.Transparent => NativeMethods.HTTRANSPARENT,
        _ => NativeMethods.HTNOWHERE,
    };
}
