using FluidBar.Island;

namespace FluidBar.Island;

/// <summary>
/// Point-in-time accounting for the renderer's retained caches. Used by the host perf probe and by
/// tests to attribute working-set growth to a concrete cache instead of guessing.
/// </summary>
/// <param name="PathCacheCount">Cached squircle paths.</param>
/// <param name="PathBuilds">Squircle paths built since construction.</param>
/// <param name="ShadowCacheCount">Cached shadow images.</param>
/// <param name="ShadowPixelBytes">Retained shadow pixel bytes (BGRA, actual image size).</param>
/// <param name="ShadowBuilds">Shadow images baked since construction.</param>
/// <param name="LiquidGlassOverlayCount">Baked liquid-glass overlays retained.</param>
/// <param name="LiquidGlassOverlayBytes">Retained liquid-glass overlay pixel bytes (BGRA).</param>
/// <param name="ArtShaderCount">Cached gradient shaders.</param>
/// <param name="ArtworkCount">Cached decoded covers.</param>
/// <param name="ArtworkPixelBytes">Retained artwork pixel bytes (BGRA).</param>
/// <param name="ArtworkBuilds">Covers decoded since construction.</param>
/// <param name="TextCacheCount">Cached text layouts.</param>
/// <param name="TextCacheCapacity">Text layout cache capacity.</param>
/// <param name="TextApproxBytes">Approximate retained bytes of the text layout cache.</param>
/// <param name="NumberTextCount">Cached number/percent strings.</param>
public readonly record struct RenderResourceStats(
    int PathCacheCount,
    long PathBuilds,
    int ShadowCacheCount,
    long ShadowPixelBytes,
    long ShadowBuilds,
    int LiquidGlassOverlayCount,
    long LiquidGlassOverlayBytes,
    int ArtShaderCount,
    int ArtworkCount,
    long ArtworkPixelBytes,
    long ArtworkBuilds,
    int TextCacheCount,
    int TextCacheCapacity,
    long TextApproxBytes,
    int NumberTextCount)
{
    /// <summary>Pixel bytes attributed to cached images (shadow + liquid glass + artwork).</summary>
    public long CachedImageBytes => ShadowPixelBytes + LiquidGlassOverlayBytes + ArtworkPixelBytes;

    /// <summary>Approximate total retained bytes across all tracked caches.</summary>
    public long TotalApproxBytes => CachedImageBytes + TextApproxBytes;
}
