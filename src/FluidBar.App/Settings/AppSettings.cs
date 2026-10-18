using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluidBar.App.Theme;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;

namespace FluidBar.App.Settings;

/// <summary>Theme token for the settings/marketplace surfaces (the island itself stays dark).</summary>
public enum AppTheme
{
    /// <summary>Dark desktop surfaces.</summary>
    Dark,

    /// <summary>Light ("Porcelain") desktop surfaces.</summary>
    Light,

    /// <summary>Follow the Windows app theme.</summary>
    System,
}

/// <summary>Island material tier.</summary>
public enum IslandMaterial
{
    /// <summary>Tier 0: opaque Obsidian fill.</summary>
    Obsidian,

    /// <summary>Tier 1: compositor acrylic blur behind the island.</summary>
    Acrylic,
}

/// <summary>Stacking strategy for concurrent activities.</summary>
public enum StackingStrategySetting
{
    /// <summary>Main pill + up to two ghost pills.</summary>
    Stacked,

    /// <summary>Only the latest activity is shown.</summary>
    Latest,
}

/// <summary>Display targeting for island windows.</summary>
public enum DisplayTarget
{
    /// <summary>Primary display only.</summary>
    Primary,

    /// <summary>One window per display.</summary>
    All,

    /// <summary>Display containing the pointer.</summary>
    FollowMouse,
}

/// <summary>Reduced motion preference (tri-state: follow Windows / force on / force off).</summary>
public enum ReducedMotionPreference
{
    /// <summary>Follow the Windows client-area animation setting.</summary>
    System,

    /// <summary>Always reduce motion.</summary>
    On,

    /// <summary>Never reduce motion.</summary>
    Off,
}

/// <summary>Dock-specific settings (spec 2.2/8.2).</summary>
public sealed record DockSettings
{
    /// <summary>Whether FluidBar asks Windows to auto-hide its taskbar.</summary>
    public bool HideSystemTaskbar { get; init; }

    /// <summary>Strip height (spec 52).</summary>
    public int Height { get; init; } = (int)LayoutTokens.DockStripHeight;
}

/// <summary>Plugin settings surfaced by the settings window (persistence owned by the plugin host).</summary>
public sealed record PluginSettings
{
    /// <summary>Enabled plugin ids (built-in + community).</summary>
    public IReadOnlyList<string> Enabled { get; init; } = ["builtin.media", "builtin.clipboard", "builtin.notifications", "builtin.agent"];

    /// <summary>Marketplace update channel.</summary>
    public string Channel { get; init; } = "stable";

    /// <summary>Update check interval in hours.</summary>
    public int CheckIntervalHours { get; init; } = 24;
}

/// <summary>Advanced/diagnostics settings.</summary>
public sealed record AdvancedSettings
{
    /// <summary>Minimum log level token (<c>trace/debug/info/warn/error</c>).</summary>
    public string LogLevel { get; init; } = "warn";

    /// <summary>Crash guard enabled (2 crashes → safe mode).</summary>
    public bool CrashGuard { get; init; } = true;

    /// <summary>Show frame statistics in diagnostics.</summary>
    public bool FrameStats { get; init; }

    /// <summary>Safe mode (community plugins disabled).</summary>
    public bool SafeMode { get; init; }
}

/// <summary>
/// App-side strongly-typed settings (spec 8.2 shape) used by the UI until Core's
/// <c>SettingsV2</c>/<c>ISettingsStore</c> lands; the JSON payload keeps the spec field names so the
/// Core migrator can adopt it unchanged. Temporary file: <c>%APPDATA%\FluidBar\settings.ui.json</c>.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Settings schema version (2).</summary>
    public int SchemaVersion { get; init; } = 2;

    /// <summary>UI language tag (<c>system</c>, <c>en-US</c>, <c>zh-Hans</c> or <c>qps-ploc</c>).</summary>
    public string Language { get; init; } = "system";

    /// <summary>Desktop theme for the settings/marketplace surfaces.</summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>Start FluidBar with Windows.</summary>
    public bool StartWithWindows { get; init; } = true;

    /// <summary>Show the tray icon.</summary>
    public bool TrayIcon { get; init; } = true;

    /// <summary>Auto-update preference (<c>notify</c>/<c>auto</c>/<c>off</c>).</summary>
    public string AutoUpdate { get; init; } = "notify";

    /// <summary>Island material tier.</summary>
    public IslandMaterial Material { get; init; } = IslandMaterial.Obsidian;

    /// <summary>Island palette token.</summary>
    public string Palette { get; init; } = "obsidian";

    /// <summary>Accent mode token (<c>artwork</c>, <c>system</c>, <c>custom</c>).</summary>
    public string AccentMode { get; init; } = "artwork";

    /// <summary>Fallback accent color (<c>#RRGGBB</c>).</summary>
    public string AccentFallback { get; init; } = "#4CC9F0";

    /// <summary>Shell fill opacity (0.85–1.0; text-bearing states must stay ≥0.85).</summary>
    public double FillOpacity { get; init; } = LayoutTokens.FillOpacity;

    /// <summary>Text scale for island content (0.8–1.6).</summary>
    public double FontScale { get; init; } = 1.0;

    /// <summary>Reduced-motion preference.</summary>
    public ReducedMotionPreference ReducedMotion { get; init; } = ReducedMotionPreference.System;

    /// <summary>Motion strength (0.5–1.5).</summary>
    public double MotionStrength { get; init; } = 1.0;

    /// <summary>Island mode token (<c>float</c>, <c>flush</c>, <c>dock</c>).</summary>
    public string Mode { get; init; } = "float";

    /// <summary>Anchor token (<c>top-center</c>, ...).</summary>
    public string Anchor { get; init; } = "top-center";

    /// <summary>Stacking strategy.</summary>
    public StackingStrategySetting Stacking { get; init; } = StackingStrategySetting.Stacked;

    /// <summary>Hover peek enabled.</summary>
    public bool HoverPeek { get; init; } = true;

    /// <summary>Auto-collapse delay in ms; 0 disables auto-collapse.</summary>
    public int AutoCollapseMs { get; init; } = 2600;

    /// <summary>Auto-hide delay in ms; 0 keeps the pill visible.</summary>
    public int AutoHideMs { get; init; }

    /// <summary>Peek when the track changes (default off).</summary>
    public bool PeekOnTrackChange { get; init; }

    /// <summary>Display targeting.</summary>
    public DisplayTarget DisplayOn { get; init; } = DisplayTarget.Primary;

    /// <summary>Dock settings.</summary>
    public DockSettings Dock { get; init; } = new();

    /// <summary>Plugin settings.</summary>
    public PluginSettings Plugins { get; init; } = new();

    /// <summary>Advanced settings.</summary>
    public AdvancedSettings Advanced { get; init; } = new();

    /// <summary>Default settings.</summary>
    public static AppSettings Default { get; } = new();

    /// <summary>Whether the current OS prefers reduced motion (injected for testability).</summary>
    public bool ResolveReducedMotion(bool systemPrefersReducedMotion) => ReducedMotion switch
    {
        ReducedMotionPreference.On => true,
        ReducedMotionPreference.Off => false,
        _ => systemPrefersReducedMotion,
    };

    /// <summary>Island colors derived from the palette token.</summary>
    [JsonIgnore]
    public IslandColors Colors => IslandColors.ForPalette(Palette);

    /// <summary>Settings-window surfaces derived from the theme token.</summary>
    [JsonIgnore]
    public AppSurfaceColors Surfaces => Theme == AppTheme.Light ? AppSurfaceColors.Light : AppSurfaceColors.Dark;

    /// <summary>Presentation mode derived from the mode token.</summary>
    [JsonIgnore]
    public IslandMode IslandMode => Mode switch
    {
        "flush" => IslandMode.Flush,
        "dock" => IslandMode.Dock,
        _ => IslandMode.Float,
    };

    /// <summary>Anchor derived from the anchor token.</summary>
    [JsonIgnore]
    public IslandAnchor IslandAnchor => Anchor switch
    {
        "top-left" => IslandAnchor.TopLeft,
        "top-right" => IslandAnchor.TopRight,
        "bottom-left" => IslandAnchor.BottomLeft,
        "bottom-center" => IslandAnchor.BottomCenter,
        "bottom-right" => IslandAnchor.BottomRight,
        _ => IslandAnchor.TopCenter,
    };

    /// <summary>Core motion profile derived from the settings.</summary>
    public MotionProfile BuildMotionProfile(bool systemPrefersReducedMotion) => new()
    {
        ReducedMotion = ResolveReducedMotion(systemPrefersReducedMotion),
        Strength = Math.Clamp(MotionStrength, MotionProfile.MinStrength, MotionProfile.MaxStrength),
    };

    /// <summary>Clamps and normalizes every value so a corrupted file can never break the UI.</summary>
    public AppSettings Normalize() => this with
    {
        SchemaVersion = 2,
        Language = string.IsNullOrWhiteSpace(Language) ? "system" : Language.Trim(),
        Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.System,
        AutoUpdate = AutoUpdate is "notify" or "auto" or "off" ? AutoUpdate : "notify",
        Material = Enum.IsDefined(Material) ? Material : IslandMaterial.Obsidian,
        Palette = Palette is "obsidian" or "graphite" or "slate" or "nord" ? Palette : "obsidian",
        AccentMode = AccentMode is "artwork" or "system" or "custom" ? AccentMode : "artwork",
        AccentFallback = FluidBar.App.Theme.Accents.ParseOrDefault(AccentFallback, FluidBar.App.Theme.Accents.Fallback).ToString(),
        FillOpacity = Math.Clamp(FillOpacity, LayoutTokens.MinFillOpacity, 1.0),
        FontScale = Math.Clamp(FontScale, 0.8, 1.6),
        ReducedMotion = Enum.IsDefined(ReducedMotion) ? ReducedMotion : ReducedMotionPreference.System,
        MotionStrength = Math.Clamp(MotionStrength, MotionProfile.MinStrength, MotionProfile.MaxStrength),
        Mode = Mode is "float" or "flush" or "dock" ? Mode : "float",
        Anchor = Anchor is "top-left" or "top-center" or "top-right" or "bottom-left" or "bottom-center" or "bottom-right"
            ? Anchor
            : "top-center",
        Stacking = Enum.IsDefined(Stacking) ? Stacking : StackingStrategySetting.Stacked,
        AutoCollapseMs = Math.Clamp(AutoCollapseMs, 0, 60000),
        AutoHideMs = Math.Clamp(AutoHideMs, 0, 3_600_000),
        DisplayOn = Enum.IsDefined(DisplayOn) ? DisplayOn : DisplayTarget.Primary,
        Plugins = Plugins with
        {
            Channel = Plugins.Channel is "stable" or "beta" ? Plugins.Channel : "stable",
            CheckIntervalHours = Math.Clamp(Plugins.CheckIntervalHours, 1, 168),
        },
        Advanced = Advanced with
        {
            LogLevel = Advanced.LogLevel is "trace" or "debug" or "info" or "warn" or "error" ? Advanced.LogLevel : "warn",
        },
        Dock = Dock with { Height = Math.Clamp(Dock.Height, 40, 72) },
    };

    /// <summary>Serializes settings to the spec-compatible JSON shape.</summary>
    public string ToJson()
    {
        var document = new SettingsJson
        {
            SchemaVersion = SchemaVersion,
            General = new GeneralJson
            {
                Language = Language,
                StartWithWindows = StartWithWindows,
                TrayIcon = TrayIcon,
                AutoUpdate = AutoUpdate,
                Telemetry = false,
            },
            Appearance = new AppearanceJson
            {
                Theme = Theme.ToString().ToLowerInvariant(),
                Material = Material.ToString().ToLowerInvariant(),
                Palette = Palette,
                Accent = new AccentJson { Mode = AccentMode, Fallback = AccentFallback },
                FillOpacity = FillOpacity,
                FontScale = FontScale,
                ReducedMotion = ReducedMotion.ToString().ToLowerInvariant(),
                MotionStrength = MotionStrength,
            },
            Island = new IslandJson
            {
                Mode = Mode,
                Anchor = Anchor,
                Stacking = Stacking == StackingStrategySetting.Stacked ? "stack" : "latest",
                HoverPeek = HoverPeek,
                AutoCollapseMs = AutoCollapseMs,
                AutoHideMs = AutoHideMs,
                PeekOnTrackChange = PeekOnTrackChange,
                DisplayOn = DisplayOn switch
                {
                    DisplayTarget.All => "all",
                    DisplayTarget.FollowMouse => "follow-mouse",
                    _ => "primary",
                },
            },
            Dock = new DockJson { HideSystemTaskbar = Dock.HideSystemTaskbar, Height = Dock.Height },
            Plugins = new PluginsJson
            {
                Enabled = [.. Plugins.Enabled],
                Channel = Plugins.Channel,
                CheckIntervalHours = Plugins.CheckIntervalHours,
            },
            Advanced = new AdvancedJson
            {
                LogLevel = Advanced.LogLevel,
                CrashGuard = Advanced.CrashGuard,
                FrameStats = Advanced.FrameStats,
                SafeMode = Advanced.SafeMode,
            },
        };
        return JsonSerializer.Serialize(document, SettingsJsonContext.Default.SettingsJson);
    }

    /// <summary>Parses settings JSON; returns <see langword="null"/> when the payload is unusable.</summary>
    public static AppSettings? TryParse(string json, out string? error)
    {
        error = null;
        try
        {
            var document = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsJson);
            if (document is null)
            {
                error = "settings document is empty";
                return null;
            }

            var defaults = Default;
            return new AppSettings
            {
                SchemaVersion = document.SchemaVersion is 2 ? 2 : document.SchemaVersion,
                Language = document.General?.Language ?? defaults.Language,
                StartWithWindows = document.General?.StartWithWindows ?? defaults.StartWithWindows,
                TrayIcon = document.General?.TrayIcon ?? defaults.TrayIcon,
                AutoUpdate = document.General?.AutoUpdate ?? defaults.AutoUpdate,
                Theme = ParseTheme(document.Appearance?.Theme),
                Material = document.Appearance?.Material == "acrylic" ? IslandMaterial.Acrylic : IslandMaterial.Obsidian,
                Palette = document.Appearance?.Palette ?? defaults.Palette,
                AccentMode = document.Appearance?.Accent?.Mode ?? defaults.AccentMode,
                AccentFallback = document.Appearance?.Accent?.Fallback ?? defaults.AccentFallback,
                FillOpacity = document.Appearance?.FillOpacity ?? defaults.FillOpacity,
                FontScale = document.Appearance?.FontScale ?? defaults.FontScale,
                ReducedMotion = ParseReducedMotion(document.Appearance?.ReducedMotion),
                MotionStrength = document.Appearance?.MotionStrength ?? defaults.MotionStrength,
                Mode = document.Island?.Mode ?? defaults.Mode,
                Anchor = document.Island?.Anchor ?? defaults.Anchor,
                Stacking = document.Island?.Stacking == "latest" ? StackingStrategySetting.Latest : StackingStrategySetting.Stacked,
                HoverPeek = document.Island?.HoverPeek ?? defaults.HoverPeek,
                AutoCollapseMs = document.Island?.AutoCollapseMs ?? defaults.AutoCollapseMs,
                AutoHideMs = document.Island?.AutoHideMs ?? defaults.AutoHideMs,
                PeekOnTrackChange = document.Island?.PeekOnTrackChange ?? defaults.PeekOnTrackChange,
                DisplayOn = document.Island?.DisplayOn switch
                {
                    "all" => DisplayTarget.All,
                    "follow-mouse" => DisplayTarget.FollowMouse,
                    _ => DisplayTarget.Primary,
                },
                Dock = new DockSettings
                {
                    HideSystemTaskbar = document.Dock?.HideSystemTaskbar ?? false,
                    Height = document.Dock?.Height ?? (int)LayoutTokens.DockStripHeight,
                },
                Plugins = new PluginSettings
                {
                    Enabled = document.Plugins?.Enabled ?? defaults.Plugins.Enabled,
                    Channel = document.Plugins?.Channel ?? "stable",
                    CheckIntervalHours = document.Plugins?.CheckIntervalHours ?? 24,
                },
                Advanced = new AdvancedSettings
                {
                    LogLevel = document.Advanced?.LogLevel ?? "warn",
                    CrashGuard = document.Advanced?.CrashGuard ?? true,
                    FrameStats = document.Advanced?.FrameStats ?? false,
                    SafeMode = document.Advanced?.SafeMode ?? false,
                },
            }.Normalize();
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static AppTheme ParseTheme(string? theme) => theme switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => AppTheme.System,
    };

    private static ReducedMotionPreference ParseReducedMotion(string? value) => value switch
    {
        "on" => ReducedMotionPreference.On,
        "off" => ReducedMotionPreference.Off,
        _ => ReducedMotionPreference.System,
    };
}

// ----- JSON DTOs (spec 8.2 field names; System.Text.Json source generation for trimming/single-file) -----

internal sealed class SettingsJson
{
    public int SchemaVersion { get; set; }
    public GeneralJson? General { get; set; }
    public AppearanceJson? Appearance { get; set; }
    public IslandJson? Island { get; set; }
    public DockJson? Dock { get; set; }
    public PluginsJson? Plugins { get; set; }
    public AdvancedJson? Advanced { get; set; }
}

internal sealed class GeneralJson
{
    public string? Language { get; set; }

    public bool? StartWithWindows { get; set; }

    public bool? TrayIcon { get; set; }

    public string? AutoUpdate { get; set; }

    public bool? Telemetry { get; set; }
}

internal sealed class AppearanceJson
{
    public string? Theme { get; set; }
    public string? Material { get; set; }
    public string? Palette { get; set; }
    public AccentJson? Accent { get; set; }
    public double? FillOpacity { get; set; }
    public double? FontScale { get; set; }
    public string? ReducedMotion { get; set; }
    public double? MotionStrength { get; set; }
}

internal sealed class AccentJson
{
    public string? Mode { get; set; }
    public string? Fallback { get; set; }
}

internal sealed class IslandJson
{
    public string? Mode { get; set; }
    public string? Anchor { get; set; }
    public string? Stacking { get; set; }
    public bool? HoverPeek { get; set; }
    public int? AutoCollapseMs { get; set; }
    public int? AutoHideMs { get; set; }
    public bool? PeekOnTrackChange { get; set; }
    public string? DisplayOn { get; set; }
}

internal sealed class DockJson
{
    public bool? HideSystemTaskbar { get; set; }
    public int? Height { get; set; }
}

internal sealed class PluginsJson
{
    public string[]? Enabled { get; set; }
    public string? Channel { get; set; }
    public int? CheckIntervalHours { get; set; }
}

internal sealed class AdvancedJson
{
    public string? LogLevel { get; set; }
    public bool? CrashGuard { get; set; }
    public bool? FrameStats { get; set; }
    public bool? SafeMode { get; set; }
}

/// <summary>Source-generated JSON context for settings serialization.</summary>
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SettingsJson))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
