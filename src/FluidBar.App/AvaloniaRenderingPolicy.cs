using Avalonia;

namespace FluidBar.App;

/// <summary>
/// Chooses the Avalonia Win32 rendering backend. In skia-host mode the Avalonia UI is only the
/// settings/market window (opened rarely), so the GPU stack (ANGLE/D3D/NVIDIA user-mode driver) would
/// otherwise be loaded and kept resident for the whole session for nothing. Software rendering uses
/// Avalonia's built-in CPU rasterizer: same Skia output, no GPU modules, ~29 MB less working set and
/// ~54 MB less private memory (measured with SkiaHostPerfProbe).
/// </summary>
public static class AvaloniaRenderingPolicy
{
    /// <summary>Environment variable that overrides the choice (`gpu` or `software`).</summary>
    public const string OverrideVariable = "FLUIDBAR_AVALONIA_RENDERING";

    /// <summary>Legacy env var kept for the perf-audit scripts (equivalent to the software override).</summary>
    public const string LegacySoftwareVariable = "FLUIDBAR_AVALONIA_SOFTWARE";

    /// <summary>
    /// Whether the process should force <see cref="Win32RenderingMode.Software"/>.
    /// The default skia host path (no FLUIDBAR_HOST or FLUIDBAR_HOST=skia) uses software; the legacy
    /// Avalonia island host keeps the platform default so its full-window rendering stays unchanged.
    /// </summary>
    /// <param name="hostSelection">The `FLUIDBAR_HOST` value (null/empty/unknown = skia-first default).</param>
    /// <param name="overrideMode">An explicit rendering mode override (`gpu`/`software`), or null.</param>
    public static bool ShouldUseSoftwareRendering(string? hostSelection, string? overrideMode)
    {
        if (string.Equals(overrideMode, "software", StringComparison.OrdinalIgnoreCase)
            || string.Equals(overrideMode, "1", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(overrideMode, "gpu", StringComparison.OrdinalIgnoreCase)
            || string.Equals(overrideMode, "0", StringComparison.Ordinal))
        {
            return false;
        }

        // GPU is the default: only a per-pixel-alpha surface (which the software/composition path does
        // not deliver) lets the settings window paint a true rounded shape. Software stays available
        // through the explicit override for memory-constrained runs.
        _ = hostSelection;
        return false;
    }

    /// <summary>Resolves the override from the environment (`FLUIDBAR_AVALONIA_RENDERING`, then the legacy flag).</summary>
    public static bool ShouldUseSoftwareRendering()
    {
        var overrideMode = Environment.GetEnvironmentVariable(OverrideVariable);
        if (string.IsNullOrWhiteSpace(overrideMode)
            && string.Equals(Environment.GetEnvironmentVariable(LegacySoftwareVariable), "1", StringComparison.Ordinal))
        {
            overrideMode = "software";
        }

        return ShouldUseSoftwareRendering(Environment.GetEnvironmentVariable("FLUIDBAR_HOST"), overrideMode);
    }

    /// <summary>Returns the Win32 platform options for the resolved policy.</summary>
    public static Win32PlatformOptions CreatePlatformOptions()
        => ShouldUseSoftwareRendering()
            ? new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] }
            : new Win32PlatformOptions();
}
