using System.Collections.Immutable;
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
    /// <summary>Primary pill splits preview islets beside it (default).</summary>
    Split,

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
    public IReadOnlyList<string> Enabled { get; init; } = ["fluidbar-media", "fluidbar-clipboard", "fluidbar-notifications", "fluidbar-agent"];

    /// <summary>User page order (PageIds); unknown ids are kept at the end.</summary>
    public IReadOnlyList<string> PageOrder { get; init; } =
    [
        "builtin.home", "builtin.media", "builtin.notifications", "builtin.clipboard", "builtin.agent", "builtin.widgets",
    ];

    /// <summary>Marketplace update channel.</summary>
    public string Channel { get; init; } = "stable";

    /// <summary>Update check interval in hours.</summary>
    public int CheckIntervalHours { get; init; } = 24;
}

/// <summary>
/// Per-monitor persisted placement (spec 2.6): the anchor token and offsets written when the user
/// drops the island on a monitor. Absent monitors use the global <see cref="AppSettings.Anchor"/>.
/// </summary>
public sealed record MonitorPlacementSetting
{
    /// <summary>Anchor token (<c>top-left</c>, <c>top-center</c>, ... <c>bottom-right</c>).</summary>
    public string Anchor { get; init; } = "top-center";

    /// <summary>Horizontal offset from the anchored edge/centre in DIP.</summary>
    public double OffsetX { get; init; }

    /// <summary>Vertical offset from the anchored edge in DIP; null selects the mode default.</summary>
    public double? OffsetY { get; init; }
}

/// <summary>Effective placement for one monitor: its override or the global anchor.</summary>
/// <param name="Anchor">Resolved anchor.</param>
/// <param name="OffsetX">Horizontal offset in DIP.</param>
/// <param name="OffsetY">Vertical offset in DIP; null selects the mode default.</param>
public readonly record struct ResolvedMonitorPlacement(IslandAnchor Anchor, double OffsetX, double? OffsetY);

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

    /// <summary>Global hotkey to toggle the island (Ctrl+Alt+D).</summary>
    public string HotkeyToggle { get; init; } = "Ctrl+Alt+D";

    /// <summary>Global hotkey to hide the island (Ctrl+Alt+H).</summary>
    public string HotkeyHide { get; init; } = "Ctrl+Alt+H";

    /// <summary>Global hotkey to open settings (Ctrl+Alt+S).</summary>
    public string HotkeySettings { get; init; } = "Ctrl+Alt+S";

    /// <summary>Island material tier.</summary>
    public IslandMaterial Material { get; init; } = IslandMaterial.Obsidian;

    /// <summary>Island palette token.</summary>
    public string Palette { get; init; } = "obsidian";

    /// <summary>Accent mode token (<c>artwork</c>, <c>system</c>, <c>custom</c>).</summary>
    public string AccentMode { get; init; } = "artwork";

    /// <summary>Fallback accent color (<c>#RRGGBB</c>).</summary>
    public string AccentFallback { get; init; } = "#4CC9F0";

    /// <summary>Shell fill opacity (0.85–1.0; text-bearing states must stay ≥0.85; prototype2 default 0.97).</summary>
    public double FillOpacity { get; init; } = 0.97;

    /// <summary>Expanded panel corner radius in DIP (16–36; compact pills stay stadiums; prototype2 default 30).</summary>
    public double PanelRadius { get; init; } = 30;

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

    /// <summary>
    /// Per-monitor placement overrides keyed by monitor id (<c>\\.\DISPLAYn</c>), written by
    /// drag-and-drop and restored on startup. Additive: an empty map keeps the global anchor
    /// behaviour, so settings files written before this field parse unchanged (schema stays 2).
    /// </summary>
    public ImmutableDictionary<string, MonitorPlacementSetting> Placement { get; init; }
        = ImmutableDictionary<string, MonitorPlacementSetting>.Empty;

    /// <summary>Stacking strategy.</summary>
    public StackingStrategySetting Stacking { get; init; } = StackingStrategySetting.Split;

    /// <summary>Hover peek enabled.</summary>
    public bool HoverPeek { get; init; } = true;

    /// <summary>Auto-collapse delay in ms; 0 disables auto-collapse.</summary>
    public int AutoCollapseMs { get; init; } = 2600;

    /// <summary>Auto-hide delay in ms; 0 keeps the pill visible.</summary>
    public int AutoHideMs { get; init; }

    /// <summary>Peek when the track changes (default off).</summary>
    public bool PeekOnTrackChange { get; init; }

    /// <summary>
    /// Weather city used by the widgets page ("Beijing"). Empty keeps the automatic IP-based location.
    /// The <c>FLUIDBAR_WEATHER_CITY</c> environment variable still overrides it at composition time.
    /// </summary>
    public string WeatherCity { get; init; } = string.Empty;

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
    public IslandAnchor IslandAnchor => ParseAnchorToken(Anchor);

    /// <summary>
    /// Resolves the effective placement for a monitor: its persisted override, or the global anchor
    /// with zero offsets. Unknown/blank monitor ids fall back to the global anchor.
    /// </summary>
    /// <param name="monitorId">Monitor id (<c>\\.\DISPLAYn</c>).</param>
    public ResolvedMonitorPlacement ResolveMonitorPlacement(string? monitorId)
    {
        if (monitorId is { Length: > 0 } && Placement.TryGetValue(monitorId, out var placement))
        {
            return new ResolvedMonitorPlacement(
                ParseAnchorToken(placement.Anchor),
                placement.OffsetX,
                placement.OffsetY);
        }

        return new ResolvedMonitorPlacement(IslandAnchor, 0, null);
    }

    /// <summary>Returns a copy with the per-monitor placement for <paramref name="monitorId"/> replaced.</summary>
    /// <param name="monitorId">Monitor id (<c>\\.\DISPLAYn</c>).</param>
    /// <param name="anchor">Anchor the drop resolved to.</param>
    /// <param name="offsetX">Horizontal offset in DIP.</param>
    /// <param name="offsetY">Vertical offset in DIP; null selects the mode default.</param>
    public AppSettings WithMonitorPlacement(string monitorId, IslandAnchor anchor, double offsetX, double? offsetY)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);
        return this with
        {
            Placement = Placement.SetItem(
                monitorId.Trim(),
                new MonitorPlacementSetting
                {
                    Anchor = FormatAnchorToken(anchor),
                    OffsetX = offsetX,
                    OffsetY = offsetY,
                }),
        };
    }

    /// <summary>Parses an anchor token; unknown/blank values fall back to <c>top-center</c>.</summary>
    /// <param name="token">Anchor token.</param>
    public static IslandAnchor ParseAnchorToken(string? token) => token switch
    {
        "top-left" => IslandAnchor.TopLeft,
        "top-right" => IslandAnchor.TopRight,
        "bottom-left" => IslandAnchor.BottomLeft,
        "bottom-center" => IslandAnchor.BottomCenter,
        "bottom-right" => IslandAnchor.BottomRight,
        _ => IslandAnchor.TopCenter,
    };

    /// <summary>Formats an anchor as its spec token.</summary>
    /// <param name="anchor">Anchor to format.</param>
    public static string FormatAnchorToken(IslandAnchor anchor) => anchor switch
    {
        IslandAnchor.TopLeft => "top-left",
        IslandAnchor.TopRight => "top-right",
        IslandAnchor.BottomLeft => "bottom-left",
        IslandAnchor.BottomCenter => "bottom-center",
        IslandAnchor.BottomRight => "bottom-right",
        _ => "top-center",
    };

    /// <summary>Core motion profile derived from the settings.</summary>
    public MotionProfile BuildMotionProfile(bool systemPrefersReducedMotion) => new()
    {
        ReducedMotion = ResolveReducedMotion(systemPrefersReducedMotion),
        Strength = Math.Clamp(MotionStrength, MotionProfile.MinStrength, MotionProfile.MaxStrength),
    };

    /// <summary>Largest accepted per-monitor offset in DIP (guards corrupted/imported files).</summary>
    public const double MaxPlacementOffsetDip = 100_000;

    /// <summary>Clamps and normalizes every value so a corrupted file can never break the UI.</summary>
    /// <summary>Design-time plugin ids written by early builds, mapped to the runtime ids.</summary>
    private static readonly Dictionary<string, string> LegacyPluginIds = new(StringComparer.Ordinal)
    {
        ["builtin.media"] = "fluidbar-media",
        ["builtin.clipboard"] = "fluidbar-clipboard",
        ["builtin.notifications"] = "fluidbar-notifications",
        ["builtin.agent"] = "fluidbar-agent",
        ["builtin.devices"] = "fluidbar-devices",
    };

    private static IReadOnlyList<string> NormalizePluginIds(IReadOnlyList<string> ids)
    {
        // Keep the same instance when nothing is legacy: record equality drives change detection.
        var legacy = false;
        foreach (var id in ids)
        {
            if (LegacyPluginIds.ContainsKey(id))
            {
                legacy = true;
                break;
            }
        }

        if (!legacy)
        {
            return ids;
        }

        return ids
            .Select(static id => LegacyPluginIds.TryGetValue(id, out var mapped) ? mapped : id)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public AppSettings Normalize() => this with
    {
        SchemaVersion = 2,
        Language = string.IsNullOrWhiteSpace(Language) ? "system" : Language.Trim(),
        Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.System,
        AutoUpdate = AutoUpdate is "notify" or "auto" or "off" ? AutoUpdate : "notify",
        HotkeyToggle = NormalizeHotkey(HotkeyToggle, "Ctrl+Alt+D"),
        HotkeyHide = NormalizeHotkey(HotkeyHide, "Ctrl+Alt+H"),
        HotkeySettings = NormalizeHotkey(HotkeySettings, "Ctrl+Alt+S"),
        Material = Enum.IsDefined(Material) ? Material : IslandMaterial.Obsidian,
        Palette = Palette is "obsidian" or "graphite" or "slate" or "nord" ? Palette : "obsidian",
        AccentMode = AccentMode is "artwork" or "system" or "custom" ? AccentMode : "artwork",
        AccentFallback = FluidBar.App.Theme.Accents.ParseOrDefault(AccentFallback, FluidBar.App.Theme.Accents.Fallback).ToString(),
        FillOpacity = Math.Clamp(FillOpacity, LayoutTokens.MinFillOpacity, 1.0),
        PanelRadius = Math.Clamp(PanelRadius, 16, 36),
        FontScale = Math.Clamp(FontScale, 0.8, 1.6),
        ReducedMotion = Enum.IsDefined(ReducedMotion) ? ReducedMotion : ReducedMotionPreference.System,
        MotionStrength = Math.Clamp(MotionStrength, MotionProfile.MinStrength, MotionProfile.MaxStrength),
        Mode = Mode is "float" or "flush" or "dock" ? Mode : "float",
        Anchor = Anchor is "top-left" or "top-center" or "top-right" or "bottom-left" or "bottom-center" or "bottom-right"
            ? Anchor
            : "top-center",
        Placement = NormalizePlacement(Placement),
        Stacking = Enum.IsDefined(Stacking) ? Stacking : StackingStrategySetting.Split,
        AutoCollapseMs = Math.Clamp(AutoCollapseMs, 0, 60000),
        AutoHideMs = Math.Clamp(AutoHideMs, 0, 3_600_000),
        WeatherCity = NormalizeWeatherCity(WeatherCity),
        DisplayOn = Enum.IsDefined(DisplayOn) ? DisplayOn : DisplayTarget.Primary,
        Plugins = Plugins with
        {
            // Early builds stored design-time ids ("builtin.media"); the runtime uses "fluidbar-media".
            Enabled = NormalizePluginIds(Plugins.Enabled),
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
                HotkeyToggle = HotkeyToggle,
                HotkeyHide = HotkeyHide,
                HotkeySettings = HotkeySettings,
                Telemetry = false,
            },
            Appearance = new AppearanceJson
            {
                Theme = Theme.ToString().ToLowerInvariant(),
                Material = Material.ToString().ToLowerInvariant(),
                Palette = Palette,
                Accent = new AccentJson { Mode = AccentMode, Fallback = AccentFallback },
                FillOpacity = FillOpacity,
                PanelRadius = PanelRadius,
                FontScale = FontScale,
                ReducedMotion = ReducedMotion.ToString().ToLowerInvariant(),
                MotionStrength = MotionStrength,
            },
            Island = new IslandJson
            {
                Mode = Mode,
                Anchor = Anchor,
                Stacking = Stacking switch
                {
                    StackingStrategySetting.Latest => "latest",
                    StackingStrategySetting.Stacked => "stack",
                    _ => "split",
                },
                HoverPeek = HoverPeek,
                AutoCollapseMs = AutoCollapseMs,
                AutoHideMs = AutoHideMs,
                PeekOnTrackChange = PeekOnTrackChange,
                WeatherCity = WeatherCity,
                DisplayOn = DisplayOn switch
                {
                    DisplayTarget.All => "all",
                    DisplayTarget.FollowMouse => "follow-mouse",
                    _ => "primary",
                },
            },
            Placement = ToPlacementJson(Placement),
            Dock = new DockJson { HideSystemTaskbar = Dock.HideSystemTaskbar, Height = Dock.Height },
            Plugins = new PluginsJson
            {
                Enabled = [.. Plugins.Enabled],
                PageOrder = [.. Plugins.PageOrder],
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
                HotkeyToggle = document.General?.HotkeyToggle ?? defaults.HotkeyToggle,
                HotkeyHide = document.General?.HotkeyHide ?? defaults.HotkeyHide,
                HotkeySettings = document.General?.HotkeySettings ?? defaults.HotkeySettings,
                Theme = ParseTheme(document.Appearance?.Theme),
                Material = document.Appearance?.Material == "acrylic" ? IslandMaterial.Acrylic : IslandMaterial.Obsidian,
                Palette = document.Appearance?.Palette ?? defaults.Palette,
                AccentMode = document.Appearance?.Accent?.Mode ?? defaults.AccentMode,
                AccentFallback = document.Appearance?.Accent?.Fallback ?? defaults.AccentFallback,
                FillOpacity = document.Appearance?.FillOpacity ?? defaults.FillOpacity,
                PanelRadius = document.Appearance?.PanelRadius ?? defaults.PanelRadius,
                FontScale = document.Appearance?.FontScale ?? defaults.FontScale,
                ReducedMotion = ParseReducedMotion(document.Appearance?.ReducedMotion),
                MotionStrength = document.Appearance?.MotionStrength ?? defaults.MotionStrength,
                Mode = document.Island?.Mode ?? defaults.Mode,
                Anchor = document.Island?.Anchor ?? defaults.Anchor,
                Stacking = document.Island?.Stacking switch
                {
                    "latest" => StackingStrategySetting.Latest,
                    "stack" => StackingStrategySetting.Stacked,
                    _ => StackingStrategySetting.Split,
                },
                HoverPeek = document.Island?.HoverPeek ?? defaults.HoverPeek,
                AutoCollapseMs = document.Island?.AutoCollapseMs ?? defaults.AutoCollapseMs,
                AutoHideMs = document.Island?.AutoHideMs ?? defaults.AutoHideMs,
                PeekOnTrackChange = document.Island?.PeekOnTrackChange ?? defaults.PeekOnTrackChange,
                WeatherCity = document.Island?.WeatherCity ?? defaults.WeatherCity,
                DisplayOn = document.Island?.DisplayOn switch
                {
                    "all" => DisplayTarget.All,
                    "follow-mouse" => DisplayTarget.FollowMouse,
                    _ => DisplayTarget.Primary,
                },
                Placement = ParsePlacement(document.Placement),
                Dock = new DockSettings
                {
                    HideSystemTaskbar = document.Dock?.HideSystemTaskbar ?? false,
                    Height = document.Dock?.Height ?? (int)LayoutTokens.DockStripHeight,
                },
                Plugins = new PluginSettings
                {
                    Enabled = document.Plugins?.Enabled ?? defaults.Plugins.Enabled,
                    PageOrder = document.Plugins?.PageOrder ?? defaults.Plugins.PageOrder,
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

    private static string NormalizeWeatherCity(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= MaxWeatherCityLength ? text : text[..MaxWeatherCityLength];
    }

    /// <summary>Safety cap for the weather-city setting (a place name, not free text).</summary>
    public const int MaxWeatherCityLength = 64;

    private static string NormalizeHotkey(string? value, string fallback)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
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

    private static PlacementJson? ToPlacementJson(ImmutableDictionary<string, MonitorPlacementSetting> placement)
    {
        if (placement.IsEmpty)
        {
            return null;
        }

        var monitors = new Dictionary<string, PlacementEntryJson>(placement.Count, StringComparer.Ordinal);
        foreach (var (id, value) in placement)
        {
            monitors[id] = new PlacementEntryJson
            {
                Anchor = value.Anchor,
                OffsetX = value.OffsetX,
                OffsetY = value.OffsetY,
            };
        }

        return new PlacementJson { Monitors = monitors };
    }

    private static ImmutableDictionary<string, MonitorPlacementSetting> ParsePlacement(PlacementJson? json)
    {
        if (json?.Monitors is not { Count: > 0 } monitors)
        {
            return ImmutableDictionary<string, MonitorPlacementSetting>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, MonitorPlacementSetting>(StringComparer.Ordinal);
        foreach (var (id, entry) in monitors)
        {
            if (string.IsNullOrWhiteSpace(id) || entry is null)
            {
                continue;
            }

            builder[id.Trim()] = new MonitorPlacementSetting
            {
                Anchor = entry.Anchor ?? "top-center",
                OffsetX = entry.OffsetX ?? 0,
                OffsetY = entry.OffsetY,
            };
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, MonitorPlacementSetting> NormalizePlacement(
        ImmutableDictionary<string, MonitorPlacementSetting>? placement)
    {
        if (placement is null || placement.IsEmpty)
        {
            return ImmutableDictionary<string, MonitorPlacementSetting>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, MonitorPlacementSetting>(StringComparer.Ordinal);
        foreach (var (id, value) in placement)
        {
            if (string.IsNullOrWhiteSpace(id) || value is null)
            {
                continue;
            }

            builder[id.Trim()] = value with
            {
                Anchor = FormatAnchorToken(ParseAnchorToken(value.Anchor)),
                OffsetX = double.IsFinite(value.OffsetX)
                    ? Math.Clamp(value.OffsetX, -MaxPlacementOffsetDip, MaxPlacementOffsetDip)
                    : 0,
                OffsetY = value.OffsetY is { } offsetY && double.IsFinite(offsetY)
                    ? Math.Clamp(offsetY, -MaxPlacementOffsetDip, MaxPlacementOffsetDip)
                    : null,
            };
        }

        return builder.ToImmutable();
    }
}

// ----- JSON DTOs (spec 8.2 field names; System.Text.Json source generation for trimming/single-file) -----

internal sealed class SettingsJson
{
    public int SchemaVersion { get; set; }
    public GeneralJson? General { get; set; }
    public AppearanceJson? Appearance { get; set; }
    public IslandJson? Island { get; set; }
    public PlacementJson? Placement { get; set; }
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

    public string? HotkeyToggle { get; set; }

    public string? HotkeyHide { get; set; }

    public string? HotkeySettings { get; set; }

    public bool? Telemetry { get; set; }
}

internal sealed class AppearanceJson
{
    public string? Theme { get; set; }
    public string? Material { get; set; }
    public string? Palette { get; set; }
    public AccentJson? Accent { get; set; }
    public double? FillOpacity { get; set; }
    public double? PanelRadius { get; set; }
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
    public string? WeatherCity { get; set; }
    public string? DisplayOn { get; set; }
}

internal sealed class PlacementJson
{
    public Dictionary<string, PlacementEntryJson>? Monitors { get; set; }
}

internal sealed class PlacementEntryJson
{
    public string? Anchor { get; set; }
    public double? OffsetX { get; set; }
    public double? OffsetY { get; set; }
}

internal sealed class DockJson
{
    public bool? HideSystemTaskbar { get; set; }
    public int? Height { get; set; }
}

internal sealed class PluginsJson
{
    public string[]? Enabled { get; set; }
    public string[]? PageOrder { get; set; }
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


