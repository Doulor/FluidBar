using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Visual style of the split satellites (the small spheres that bud off the main island).
/// <para>
/// The satellites stay "pure circles": no accent tint, no labels, no badges. The polish is all
/// light modelling — a 1 DIP top rim highlight (white 6-10 %), a barely visible bottom shade for
/// volume, a soft contact shadow, and (media) a rounded-square cover well with a 1 DIP hairline and
/// a recessed inner shade. Geometry: the ball equals the compact shell height, the cover well is the
/// ball minus 8 DIP, its radius is 22.5 % of the well side, and the agent ring is a 2 DIP round-capped
/// 180° arc. Everything here is a token or a pure function so the renderer stays allocation free and
/// the values are testable without pixels.
/// </para>
/// <para>
/// Only the split satellites use these values; motion stays in <c>MotionTokens</c> (never touched).
/// </para>
/// </summary>
public static class SplitSatelliteStyle
{
    // ---------------------------------------------------------------- cover well

    /// <summary>
    /// Inset of the rounded-square cover well from the ball edge (DIP): side = diameter - 16, i.e.
    /// 24 DIP on a 40 DIP ball (60 %, the audited 20-24 band), so the pure-circular black ring around
    /// the cover reads unmistakably and the silhouette can never be mistaken for a squircle.
    /// </summary>
    public const float InnerFrameInsetDip = 8f;

    /// <summary>Corner radius of the cover well as a fraction of its side (22.5 %, the approved ratio).</summary>
    public const float InnerFrameRadiusFactor = 0.225f;

    /// <summary>Hairline stroke drawn on the inside of the cover well (DIP).</summary>
    public const float InnerFrameStrokeWidthDip = 1f;

    /// <summary>White alpha of that hairline (7 %: a hair lighter than the pure black ball).</summary>
    public const byte InnerFrameStrokeAlpha = 18;

    /// <summary>Black alpha of the recessed shade at the top of the cover (12 %, the "inset" read).</summary>
    public const byte CoverInnerShadeAlpha = 31;

    /// <summary>
    /// Black alpha of the bevel's bottom half. The 1 DIP inner stroke is a vertical gradient (white
    /// 7 % on the top edge, black 25 % on the bottom edge) so it reads on dark *and* bright artwork
    /// instead of disappearing into the cover.
    /// </summary>
    public const byte InnerFrameBevelShadeAlpha = 64;

    /// <summary>How far down the cover the inner shade fades out (DIP, absolute: top 2 DIP).</summary>
    public const float CoverInnerShadeDepthDip = 2f;

    /// <summary>Blur of the cover's contact shadow (DIP, CSS blur / 2).</summary>
    public const float CoverShadowBlurDip = 2f;

    /// <summary>Vertical offset of the cover's contact shadow (DIP).</summary>
    public const float CoverShadowOffsetYDip = 1f;

    /// <summary>Black alpha of the cover's contact shadow (47 % before blur).</summary>
    public const byte CoverShadowAlpha = 120;

    // ---------------------------------------------------------------- ball body

    /// <summary>Width of the top rim highlight (DIP).</summary>
    public const float BallRimWidthDip = 1f;

    /// <summary>White alpha of the rim highlight at the top edge (8.6 %).</summary>
    public const byte BallRimAlpha = 22;

    /// <summary>Fraction of the ball height where the rim highlight has faded out (a tight top edge).</summary>
    public const float BallRimFade = 0.45f;

    /// <summary>Black alpha of the bottom volume shade (23 %).</summary>
    public const byte BallShadeAlpha = 58;

    /// <summary>Fraction of the ball height where the bottom shade starts.</summary>
    public const float BallShadeStart = 0.5f;

    /// <summary>White alpha of the hover glow around the ball (6 %), size and layout unchanged.</summary>
    public const byte HoverGlowAlpha = 15;

    /// <summary>Blur of the hover glow (DIP, CSS blur / 2).</summary>
    public const float HoverGlowBlurDip = 4f;

    /// <summary>Width of the hover glow's ring, just outside the ball edge (DIP).</summary>
    public const float HoverGlowWidthDip = 1.5f;

    /// <summary>Blur of the ball's contact shadow (DIP, CSS blur / 2).</summary>
    public const float BallShadowBlurDip = 4f;

    /// <summary>Vertical offset of the ball's contact shadow (DIP).</summary>
    public const float BallShadowOffsetYDip = 2f;

    /// <summary>Black alpha of the ball's contact shadow before blur (18 %).</summary>
    public const byte BallShadowAlpha = 46;

    /// <summary>Black alpha of the press inner shadow at full press (18 %, contract §4.4).</summary>
    public const float PressShadeAlpha = 0.18f * 255f;

    /// <summary>Extra white alpha added to the rim/hairline at <c>Highlight = 1</c> (hover lift).</summary>
    public const byte HoverRimBoost = 70;

    // ---------------------------------------------------------------- agent ring

    /// <summary>Ring stroke width (DIP).</summary>
    public const float RingWidthDip = 2f;

    /// <summary>Inset of the ring's centre line from the ball edge (DIP).</summary>
    public const float RingInsetDip = 6f;

    /// <summary>
    /// Clearance between the ring's outer edge and the ball edge (DIP): inset - width / 2 = 5, so the
    /// ring never touches the ball silhouette (requirement: >= 4 DIP).
    /// </summary>
    public const float RingClearanceDip = RingInsetDip - (RingWidthDip / 2);

    /// <summary>Visible sweep of the agent ring (degrees).</summary>
    public const float RingSweepDegrees = 180f;

    /// <summary>Rest angle of the ring (12 o'clock) before the first frame advances it.</summary>
    public const float RingStartDegrees = -90f;

    /// <summary>
    /// Constant rotation speed: exactly one third of a turn per second (120°/s), the value fixed by
    /// docs/satellite-render-contract.md §5 and by <c>SatelliteMotionTokens</c> so the style and the
    /// motion driver can never drift apart.
    /// </summary>
    public const double RingDegreesPerSecond = 120;

    /// <summary>Full turn duration implied by <see cref="RingDegreesPerSecond"/> (3 s).</summary>
    public const double RingPeriodSeconds = 360 / RingDegreesPerSecond;

    // ---------------------------------------------------------------- geometry

    /// <summary>Rounded-square cover well of a ball, centred, <see cref="InnerFrameInsetDip"/> inside.</summary>
    /// <param name="ball">Ball bounding square in DIP.</param>
    public static SKRect InnerFrameRect(SKRect ball)
    {
        var side = Math.Max(0f, ball.Width - (InnerFrameInsetDip * 2));
        return new SKRect(
            ball.MidX - (side / 2),
            ball.MidY - (side / 2),
            ball.MidX + (side / 2),
            ball.MidY + (side / 2));
    }

    /// <summary>Corner radius of the cover well (22.5 % of its side).</summary>
    /// <param name="frame">Cover well rectangle.</param>
    public static float InnerFrameRadius(SKRect frame) => frame.Width * InnerFrameRadiusFactor;

    /// <summary>Bounding square of the agent ring's centre line (inset by <see cref="RingInsetDip"/>).</summary>
    /// <param name="ball">Ball bounding square in DIP.</param>
    public static SKRect RingRect(SKRect ball)
    {
        var inset = Math.Min(RingInsetDip, ball.Width / 2);
        return new SKRect(
            ball.Left + inset,
            ball.Top + inset,
            ball.Right - inset,
            ball.Bottom - inset);
    }

    /// <summary>Rotation of the agent ring after <paramref name="seconds"/> of spin (degrees, wrapped).</summary>
    /// <param name="seconds">Seconds since the ring started spinning.</param>
    public static float RingAngleDegrees(double seconds)
    {
        if (!double.IsFinite(seconds))
        {
            return RingStartDegrees;
        }

        var angle = RingStartDegrees + (seconds * RingDegreesPerSecond % 360.0);
        angle %= 360.0;
        if (angle < 0)
        {
            angle += 360.0;
        }

        return (float)angle;
    }

    /// <summary>Scales a token alpha by the frame opacity.</summary>
    /// <param name="alpha">Token alpha (0-255).</param>
    /// <param name="opacity">Frame opacity (0-1).</param>
    public static byte Alpha(byte alpha, float opacity)
        => (byte)Math.Clamp(alpha * Math.Clamp(opacity, 0f, 1f), 0f, 255f);
}
