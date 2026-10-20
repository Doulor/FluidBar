using FluidBar.App.Hosting.Pages;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Motion;

namespace FluidBar.App.Hosting.Skia;

/// <summary>Composition options for <see cref="SkiaIslandHost"/>; every seam is injectable for tests.</summary>
public sealed record SkiaIslandHostOptions
{
    /// <summary>Localization service.</summary>
    public required ILocalizer Localizer { get; init; }

    /// <summary>Settings controller (live apply + persistence).</summary>
    public required SettingsController Settings { get; init; }

    /// <summary>Non-activity content source.</summary>
    public required IIslandDataSource Data { get; init; }

    /// <summary>Structured logger.</summary>
    public IAppLogger? Logger { get; init; }

    /// <summary>
    /// Optional platform/App seam for expanded page controls (media, volume, brightness, toggles,
    /// list actions). When null, media transport falls back to <c>MediaCommandRequested</c> and every
    /// other control logs <c>island.skia.control-unsupported</c>.
    /// </summary>
    public IIslandPlatformActions? PlatformActions { get; init; }

    /// <summary>Optional plugin page registry; when present its pages are appended after the built-ins.</summary>
    public PageRegistry? PageRegistry { get; init; }

    /// <summary>Injected time source (deadlines, wheel cooldowns).</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>
    /// Resolved Windows appearance (theme / reduced motion / accent) used by the "follow system" settings.
    /// Defaults to the cached platform probe; tests inject a mutable snapshot.
    /// </summary>
    public Func<SystemAppearanceSnapshot>? AppearanceSnapshot { get; init; }

    /// <summary>Whether to reveal the island on startup (settings can disable it).</summary>
    public bool StartVisible { get; init; } = true;

    /// <summary>Target frame rate for motion (60 or 120; clamped to [30, 240]).</summary>
    public double TargetFrameRate { get; init; } = 120;

    /// <summary>Whether the detached Page Archipelago strip is enabled.</summary>
    public bool EnableArchipelago { get; init; } = true;

    /// <summary>Optional frame-clock factory (tests inject a manual clock).</summary>
    public Func<IFrameClock>? FrameClockFactory { get; init; }

    /// <summary>Resolved target frame rate, clamped to a sane range.</summary>
    public double ResolvedFrameRate => Math.Clamp(TargetFrameRate, 30, 240);
}
