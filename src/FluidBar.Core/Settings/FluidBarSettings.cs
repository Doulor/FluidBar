using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluidBar.Core.Layout;

namespace FluidBar.Core.Settings;

/// <summary>Desktop theme of the surrounding UI (the island itself always stays obsidian).</summary>
public enum AppTheme
{
    /// <summary>Follow Windows.</summary>
    [JsonStringEnumMemberName("system")]
    System,

    /// <summary>Dark desktop chrome (default).</summary>
    [JsonStringEnumMemberName("dark")]
    Dark,

    /// <summary>Light porcelain desktop chrome.</summary>
    [JsonStringEnumMemberName("light")]
    Light,
}

/// <summary>Island material tier (spec 3.4).</summary>
public enum IslandMaterial
{
    /// <summary>Tier 0: opaque obsidian fill (default).</summary>
    [JsonStringEnumMemberName("obsidian")]
    Obsidian,

    /// <summary>Tier 1: acrylic backdrop behind the rounded shell.</summary>
    [JsonStringEnumMemberName("acrylic")]
    Acrylic,

    /// <summary>Tier 2: liquid glass (lensing rim + specular edge, drawn; layered hosts have no backdrop blur).</summary>
    [JsonStringEnumMemberName("liquid-glass")]
    LiquidGlass,
}

/// <summary>Curated dark palettes + porcelain (spec 3.3).</summary>
public enum ColorPalette
{
    /// <summary>Default obsidian palette.</summary>
    [JsonStringEnumMemberName("obsidian")]
    Obsidian,

    /// <summary>Graphite palette.</summary>
    [JsonStringEnumMemberName("graphite")]
    Graphite,

    /// <summary>Slate palette.</summary>
    [JsonStringEnumMemberName("slate")]
    Slate,

    /// <summary>Nord palette.</summary>
    [JsonStringEnumMemberName("nord")]
    Nord,

    /// <summary>Light porcelain palette (desktop chrome only).</summary>
    [JsonStringEnumMemberName("porcelain")]
    Porcelain,

    /// <summary>User-provided colors.</summary>
    [JsonStringEnumMemberName("custom")]
    Custom,
}

/// <summary>Accent color source.</summary>
public enum AccentMode
{
    /// <summary>Extract the accent from album artwork.</summary>
    [JsonStringEnumMemberName("artwork")]
    Artwork,

    /// <summary>Use the Windows system accent color.</summary>
    [JsonStringEnumMemberName("system")]
    System,

    /// <summary>Use <see cref="AccentSettings.CustomHex"/>.</summary>
    [JsonStringEnumMemberName("custom")]
    Custom,
}

/// <summary>Reduced-motion preference tri-state (spec 2.9).</summary>
public enum MotionPreference
{
    /// <summary>Follow the Windows "show animations" setting.</summary>
    [JsonStringEnumMemberName("system")]
    System,

    /// <summary>Animations enabled.</summary>
    [JsonStringEnumMemberName("on")]
    Enabled,

    /// <summary>Reduced motion always on.</summary>
    [JsonStringEnumMemberName("off")]
    Off,
}

/// <summary>Stacking strategy setting (spec 2.5).</summary>
public enum StackingPreference
{
    /// <summary>Primary pill splits off up to two preview islets beside it (default).</summary>
    [JsonStringEnumMemberName("split")]
    Split,

    /// <summary>Primary + up to two ghost pills behind it.</summary>
    [JsonStringEnumMemberName("stack")]
    Stack,

    /// <summary>Only the latest/primary activity (no ghosts).</summary>
    [JsonStringEnumMemberName("latest")]
    Latest,
}

/// <summary>Which displays host the island (spec 2.6).</summary>
public enum DisplayTarget
{
    /// <summary>Primary monitor only (default).</summary>
    [JsonStringEnumMemberName("primary")]
    Primary,

    /// <summary>All monitors.</summary>
    [JsonStringEnumMemberName("all")]
    All,

    /// <summary>Monitor under the pointer.</summary>
    [JsonStringEnumMemberName("follow-mouse")]
    FollowMouse,

    /// <summary>One persisted monitor id.</summary>
    [JsonStringEnumMemberName("specific")]
    Specific,
}

/// <summary>Update check policy.</summary>
public enum AutoUpdatePreference
{
    /// <summary>Never check.</summary>
    [JsonStringEnumMemberName("off")]
    Off,

    /// <summary>Check and notify only (default).</summary>
    [JsonStringEnumMemberName("notify")]
    Notify,
}

/// <summary>Anchors serialized with the spec's kebab-case names.</summary>
public static class SettingsJsonNames
{
    /// <summary>Default marketplace index URL until the mirror decision (spec Q8) is made.</summary>
    public const string DefaultMarketSourceUrl = "https://fluidbar.app/market/index.json";

    /// <summary>Placement dictionary key used by the v1 migration (primary monitor fallback).</summary>
    public const string PrimaryPlacementKey = "primary";
}

/// <summary>General/application settings.</summary>
public sealed record GeneralSettings
{
    /// <summary>UI language; <c>system</c> follows Windows (spec 7.2).</summary>
    public string Language { get; init; } = "system";

    /// <summary>Start with Windows (HKCU Run entry).</summary>
    public bool StartWithWindows { get; init; } = true;

    /// <summary>Show the tray icon.</summary>
    public bool TrayIcon { get; init; } = true;

    /// <summary>Auto update behaviour.</summary>
    public AutoUpdatePreference AutoUpdate { get; init; } = AutoUpdatePreference.Notify;

    /// <summary>
    /// Anonymous usage data: launch count, session length, startup time, app/OS version, architecture and UI
    /// language. No song titles, lyrics, notification text, window titles, file paths or plugin content —
    /// see docs/privacy/telemetry.md. On by default; turning it off discards everything pending.
    /// </summary>
    public bool Telemetry { get; init; } = true;

    /// <summary>Safe-mode flag set by the crash guard.</summary>
    public bool SafeMode { get; init; }
}

/// <summary>Accent color configuration.</summary>
public sealed record AccentSettings
{
    /// <summary>Accent source.</summary>
    public AccentMode Mode { get; init; } = AccentMode.Artwork;

    /// <summary>Fallback accent hex used when artwork extraction fails (spec: <c>#4CC9F0</c>).</summary>
    public string FallbackHex { get; init; } = "#4CC9F0";

    /// <summary>Custom accent hex when <see cref="Mode"/> is <see cref="AccentMode.Custom"/>.</summary>
    public string? CustomHex { get; init; }
}

/// <summary>Appearance settings (spec 8.2).</summary>
public sealed record AppearanceSettings
{
    /// <summary>Desktop chrome theme.</summary>
    public AppTheme Theme { get; init; } = AppTheme.Dark;

    /// <summary>Island material tier.</summary>
    public IslandMaterial Material { get; init; } = IslandMaterial.Obsidian;

    /// <summary>Palette.</summary>
    public ColorPalette Palette { get; init; } = ColorPalette.Obsidian;

    /// <summary>Accent configuration.</summary>
    public AccentSettings Accent { get; init; } = new();

    /// <summary>Shell fill opacity; clamped to 0.85..1.0 for text-bearing states.</summary>
    public double FillOpacity { get; init; } = 0.96;

    /// <summary>Font scale; clamped to 0.8..1.6.</summary>
    public double FontScale { get; init; } = 1.0;

    /// <summary>Reduced-motion preference.</summary>
    public MotionPreference ReducedMotion { get; init; } = MotionPreference.System;

    /// <summary>Motion strength multiplier; clamped to 0.5..1.5.</summary>
    public double MotionStrength { get; init; } = 1.0;

    /// <summary>Accent bloom strength 0..2 (1.0 = 100 %).</summary>
    public double BloomStrength { get; init; } = 1.0;
}

/// <summary>Island behaviour settings (spec 8.2).</summary>
public sealed record IslandSettings
{
    /// <summary>Master enable.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Presentation mode.</summary>
    public IslandMode Mode { get; init; } = IslandMode.Float;

    /// <summary>Default anchor (per-monitor placement overrides it).</summary>
    public IslandAnchor Anchor { get; init; } = IslandAnchor.TopCenter;

    /// <summary>Stacking strategy.</summary>
    public StackingPreference Stacking { get; init; } = StackingPreference.Split;

    /// <summary>Hover peek enabled.</summary>
    public bool HoverPeek { get; init; } = true;

    /// <summary>Hovering the hidden 10 DIP strip reveals the island.</summary>
    public bool HoverReveal { get; init; } = true;

    /// <summary>Hidden-state hover strip enabled.</summary>
    public bool HiddenHoverStrip { get; init; } = true;

    /// <summary>Auto-collapse delay in ms (0 disables).</summary>
    public int AutoCollapseMs { get; init; } = 2600;

    /// <summary>Auto-hide delay in ms (0 keeps the pill visible).</summary>
    public int AutoHideMs { get; init; }

    /// <summary>Session pin (suppresses auto-collapse and preemption).</summary>
    public bool Pinned { get; init; }

    /// <summary>Peek on track change (default off due to browser sources).</summary>
    public bool PeekOnTrackChange { get; init; }

    /// <summary>Display hosting policy.</summary>
    public DisplayTarget DisplayOn { get; init; } = DisplayTarget.Primary;

    /// <summary>Specific monitor id when <see cref="DisplayOn"/> is <see cref="DisplayTarget.Specific"/>.</summary>
    public string? SpecificMonitorId { get; init; }
}

/// <summary>Dock mode settings (spec 2.2).</summary>
public sealed record DockSettings
{
    /// <summary>Ask Windows to auto-hide the system taskbar while dock mode is active.</summary>
    public bool HideSystemTaskbar { get; init; }

    /// <summary>Dock strip height in DIP; clamped to 40..80.</summary>
    public int Height { get; init; } = 52;

    /// <summary>Dock tile order (built-in page tokens).</summary>
    public ImmutableArray<string> Tiles { get; init; } = ["media", "notifications", "clipboard", "agent", "widgets"];
}

/// <summary>Per-monitor placement (spec 2.6).</summary>
public sealed record MonitorPlacement
{
    /// <summary>Anchor within the monitor work area.</summary>
    public IslandAnchor Anchor { get; init; } = IslandAnchor.TopCenter;

    /// <summary>Horizontal offset from the anchored edge/centre, DIP.</summary>
    public double OffsetX { get; init; }

    /// <summary>Vertical offset from the anchored edge, DIP; null selects the mode default.</summary>
    public double? OffsetY { get; init; }
}

/// <summary>Per-monitor persisted placement map (monitor id -&gt; placement).</summary>
public sealed record PlacementSettings
{
    /// <summary>Placements keyed by stable monitor id; the <c>primary</c> key is the fallback.</summary>
    public Dictionary<string, MonitorPlacement> Monitors { get; init; } = new(StringComparer.Ordinal)
    {
        [SettingsJsonNames.PrimaryPlacementKey] = new MonitorPlacement(),
    };

    /// <summary>Resolves the placement for a monitor, falling back to the <c>primary</c> entry.</summary>
    /// <param name="monitorId">Target monitor id.</param>
    /// <param name="isPrimary">Whether the target monitor is primary.</param>
    public MonitorPlacement Resolve(string? monitorId, bool isPrimary)
    {
        if (monitorId is { Length: > 0 } && Monitors.TryGetValue(monitorId, out var exact))
        {
            return exact;
        }

        if (Monitors.TryGetValue(SettingsJsonNames.PrimaryPlacementKey, out var primary))
        {
            return primary;
        }

        return new MonitorPlacement();
    }
}

/// <summary>Hotkey bindings (spec 2.4).</summary>
public sealed record HotkeySettings
{
    /// <summary>Toggle expand/collapse (default Ctrl+Alt+D).</summary>
    public string Toggle { get; init; } = "Ctrl+Alt+D";

    /// <summary>Hide/show (default Ctrl+Alt+H).</summary>
    public string Hide { get; init; } = "Ctrl+Alt+H";

    /// <summary>Open settings (default Ctrl+Alt+S).</summary>
    public string Settings { get; init; } = "Ctrl+Alt+S";

    /// <summary>Hold-to-hide key; null/empty disables it (default LeftAlt).</summary>
    public string? HoldToHide { get; init; } = "LeftAlt";
}

/// <summary>Per-activity settings (spec 8.2).</summary>
public sealed record ActivityFeatureSettings
{
    /// <summary>Whether this activity class is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Override TTL in ms; null uses the spec default.</summary>
    public int? DurationMs { get; init; }

    /// <summary>Notification verification-code detection.</summary>
    public bool DetectVerificationCode { get; init; }

    /// <summary>Show lyrics on the media surface.</summary>
    public bool ShowLyrics { get; init; }

    /// <summary>Application blocklist (lowercase process names).</summary>
    public ImmutableArray<string> Blocklist { get; init; } = [];
}

/// <summary>All activity-class settings.</summary>
public sealed record ActivitySettings
{
    /// <summary>Lookup/search activity settings.</summary>
    public ActivityFeatureSettings Lookup { get; init; } = new();

    /// <summary>Clipboard activity settings.</summary>
    public ActivityFeatureSettings Clipboard { get; init; } = new();

    /// <summary>Lock-key activity settings.</summary>
    public ActivityFeatureSettings LockKeys { get; init; } = new() { DurationMs = 1600 };

    /// <summary>USB device activity settings.</summary>
    public ActivityFeatureSettings Device { get; init; } = new() { DurationMs = 3000 };

    /// <summary>Bluetooth activity settings.</summary>
    public ActivityFeatureSettings Bluetooth { get; init; } = new() { DurationMs = 3000 };

    /// <summary>Focus assist activity settings.</summary>
    public ActivityFeatureSettings Focus { get; init; } = new();

    /// <summary>Volume HUD settings.</summary>
    public ActivityFeatureSettings Volume { get; init; } = new() { DurationMs = 1600 };

    /// <summary>Brightness HUD settings.</summary>
    public ActivityFeatureSettings Brightness { get; init; } = new() { DurationMs = 1600 };

    /// <summary>Notification settings.</summary>
    public ActivityFeatureSettings Notification { get; init; } = new() { DurationMs = 5000, DetectVerificationCode = true };

    /// <summary>Agent completion/failure settings.</summary>
    public ActivityFeatureSettings Agent { get; init; } = new() { DurationMs = 5000 };

    /// <summary>Low-battery settings.</summary>
    public ActivityFeatureSettings BatteryLow { get; init; } = new();

    /// <summary>Long-task progress settings.</summary>
    public ActivityFeatureSettings Progress { get; init; } = new();

    /// <summary>Timer settings.</summary>
    public ActivityFeatureSettings Timer { get; init; } = new();

    /// <summary>Media settings.</summary>
    public ActivityFeatureSettings Media { get; init; } = new()
    {
        ShowLyrics = true,
        Blocklist = ["chrome", "youtube"],
    };

    /// <summary>Returns the feature settings for an activity kind.</summary>
    public ActivityFeatureSettings For(Activities.ActivityKind kind) => kind switch
    {
        Activities.ActivityKind.Lookup => Lookup,
        Activities.ActivityKind.Clipboard => Clipboard,
        Activities.ActivityKind.LockKeys => LockKeys,
        Activities.ActivityKind.Device => Device,
        Activities.ActivityKind.Bluetooth => Bluetooth,
        Activities.ActivityKind.Focus => Focus,
        Activities.ActivityKind.Volume => Volume,
        Activities.ActivityKind.Brightness => Brightness,
        Activities.ActivityKind.Notification => Notification,
        Activities.ActivityKind.Agent => Agent,
        Activities.ActivityKind.BatteryLow => BatteryLow,
        Activities.ActivityKind.Progress => Progress,
        Activities.ActivityKind.Timer => Timer,
        Activities.ActivityKind.Media => Media,
        _ => new ActivityFeatureSettings(),
    };
}

/// <summary>Built-in plugin ids.</summary>
public static class BuiltinPlugins
{
    /// <summary>Media plugin id.</summary>
    public const string Media = "builtin.media";

    /// <summary>Clipboard plugin id.</summary>
    public const string Clipboard = "builtin.clipboard";

    /// <summary>Notifications plugin id.</summary>
    public const string Notifications = "builtin.notifications";

    /// <summary>Agent plugin id.</summary>
    public const string Agent = "builtin.agent";

    /// <summary>All built-in ids.</summary>
    public static ImmutableArray<string> All { get; } = [Media, Clipboard, Notifications, Agent];
}

/// <summary>Plugin enablement and update channel settings.</summary>
public sealed record PluginsSettings
{
    /// <summary>Enabled plugin ids.</summary>
    public ImmutableArray<string> Enabled { get; init; } = [BuiltinPlugins.Media, BuiltinPlugins.Clipboard, BuiltinPlugins.Notifications];

    /// <summary>Update channel (<c>stable</c> or <c>beta</c>).</summary>
    public string Channel { get; init; } = "stable";

    /// <summary>Update check interval in hours.</summary>
    public int CheckIntervalHours { get; init; } = 24;

    /// <summary>Previously enabled ids that are no longer installed (migration/report only).</summary>
    public ImmutableArray<string> Orphaned { get; init; } = [];
}

/// <summary>Localization settings (spec 7).</summary>
public sealed record I18nSettings
{
    /// <summary>UI language; <c>system</c> follows Windows.</summary>
    public string Language { get; init; } = "system";

    /// <summary>Fallback language.</summary>
    public string FallbackLanguage { get; init; } = "en-US";

    /// <summary>Pseudo-localization (development only).</summary>
    public bool PseudoLocalization { get; init; }
}

/// <summary>Marketplace client settings.</summary>
public sealed record MarketSettings
{
    /// <summary>Index URL.</summary>
    public string SourceUrl { get; init; } = SettingsJsonNames.DefaultMarketSourceUrl;

    /// <summary>Automatically check for index updates.</summary>
    public bool AutoCheck { get; init; } = true;

    /// <summary>Allow beta channel listings.</summary>
    public bool AllowBeta { get; init; }
}

/// <summary>Advanced/diagnostics settings.</summary>
public sealed record AdvancedSettings
{
    /// <summary>Minimum log level (<c>trace</c>/<c>debug</c>/<c>info</c>/<c>warn</c>/<c>error</c>).</summary>
    public string LogLevel { get; init; } = "warn";

    /// <summary>Crash-guard/safe-mode behaviour.</summary>
    public bool CrashGuard { get; init; } = true;

    /// <summary>Record frame statistics.</summary>
    public bool FrameStats { get; init; }

    /// <summary>Developer mode (pseudo-loc, verbose logs, diagnostics overlay).</summary>
    public bool DeveloperMode { get; init; }
}

/// <summary>
/// Root of the v2 settings model (spec 8.2). Immutable record tree; mutate with <c>with</c>.
/// <see cref="SchemaVersion"/> is 3; v1 files are migrated by <see cref="SettingsMigrator"/> and v2 files are
/// upgraded in place (an inert telemetry switch from v2 is reset to the shipped default).
/// </summary>
public sealed record FluidBarSettings
{
    /// <summary>Current schema version.</summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>Schema version of this document.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>General settings.</summary>
    public GeneralSettings General { get; init; } = new();

    /// <summary>Appearance settings.</summary>
    public AppearanceSettings Appearance { get; init; } = new();

    /// <summary>Island behaviour settings.</summary>
    public IslandSettings Island { get; init; } = new();

    /// <summary>Dock settings.</summary>
    public DockSettings Dock { get; init; } = new();

    /// <summary>Per-monitor placement.</summary>
    public PlacementSettings Placement { get; init; } = new();

    /// <summary>Hotkeys.</summary>
    public HotkeySettings Hotkeys { get; init; } = new();

    /// <summary>Per-activity settings.</summary>
    public ActivitySettings Activities { get; init; } = new();

    /// <summary>Plugin settings.</summary>
    public PluginsSettings Plugins { get; init; } = new();

    /// <summary>Localization settings.</summary>
    public I18nSettings I18n { get; init; } = new();

    /// <summary>Marketplace settings.</summary>
    public MarketSettings Market { get; init; } = new();

    /// <summary>Advanced/diagnostics settings.</summary>
    public AdvancedSettings Advanced { get; init; } = new();

    /// <summary>Creates a fully defaulted settings tree.</summary>
    public static FluidBarSettings CreateDefault() => new();
}

/// <summary>Serialization contract for settings JSON (camelCase, string enums, atomic-write friendly).</summary>
public static class SettingsJson
{
    /// <summary>Shared serializer options; do not mutate.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Serializes settings to compact JSON.</summary>
    public static string Serialize(FluidBarSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, Options);
    }

    /// <summary>Tries to deserialize settings JSON without throwing.</summary>
    /// <param name="json">JSON text.</param>
    /// <param name="settings">Deserialized settings on success.</param>
    /// <param name="error">Failure reason on failure.</param>
    public static bool TryDeserialize(string json, out FluidBarSettings? settings, out string? error)
    {
        try
        {
            settings = JsonSerializer.Deserialize<FluidBarSettings>(json, Options);
            if (settings is null)
            {
                error = "settings JSON deserialized to null.";
                return false;
            }

            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            settings = null;
            error = ex.Message;
            return false;
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        options.Converters.Add(new IslandModeJsonConverter());
        options.Converters.Add(new IslandAnchorJsonConverter());
        options.Converters.Add(new IslandMaterialJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
