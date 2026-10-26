namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Decides where a cover may come from when the media session hands out no usable thumbnail.
/// <para>
/// KuGou 8.x started registering an SMTC session (older builds did not), but its thumbnail reference yields a
/// zero-length stream, so the session path silently produced no cover at all — while the player's own cover cache
/// was only consulted on the window-title fallback, which never runs while a session exists (report 2026-10-26:
/// every KuGou track showed the coloured placeholder). The rule below keeps that ordering explicit.
/// </para>
/// </summary>
internal static class MediaArtworkFallbackPolicy
{
    /// <summary>
    /// True when the player's own on-disk cover cache should be consulted: no usable thumbnail and the player is
    /// KuGou (the only player whose cache layout this build knows).
    /// </summary>
    /// <param name="sourceAppUserModelId">Session AUMID reported by GSMTC.</param>
    /// <param name="thumbnailBytes">Length of the thumbnail stream (0 when the player advertises none).</param>
    public static bool ShouldUsePlayerCoverCache(string? sourceAppUserModelId, int thumbnailBytes)
        => thumbnailBytes <= 0 && IsKugou(sourceAppUserModelId);

    /// <summary>True when the catalogue lookup is the last remaining source.</summary>
    /// <param name="availableBytes">Cover bytes obtained so far (0 when nothing was found).</param>
    public static bool ShouldTryCatalogues(int availableBytes) => availableBytes <= 0;

    /// <summary>True when the AUMID looks like KuGou.</summary>
    /// <param name="sourceAppUserModelId">Session AUMID reported by GSMTC.</param>
    public static bool IsKugou(string? sourceAppUserModelId)
        => sourceAppUserModelId is not null
           && sourceAppUserModelId.Contains("kugou", StringComparison.OrdinalIgnoreCase);
}
