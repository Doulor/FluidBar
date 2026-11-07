namespace FluidBar.Core.Configuration;

/// <summary>Shared policy for the media plugin's sung-lyric halo, independent of UI and storage.</summary>
public static class LyricGlowSettings
{
    /// <summary>Host-owned media plugin setting key; persisted as a percentage.</summary>
    public const string Key = "media.lyricGlowStrength";

    /// <summary>Reduced default; 100 retains the original halo intensity.</summary>
    public const double DefaultPercent = 70;

    /// <summary>Normalized rendering default.</summary>
    public const float DefaultStrength = (float)(DefaultPercent / 100);

    /// <summary>Clamps finite percentages and rejects nonfinite values.</summary>
    public static double NormalizePercent(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : DefaultPercent;

    /// <summary>Converts a stored percentage into a bounded rendering strength.</summary>
    public static float ToStrength(double value) => (float)(NormalizePercent(value) / 100);
}
