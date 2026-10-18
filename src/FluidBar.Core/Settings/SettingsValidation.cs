using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace FluidBar.Core.Settings;

/// <summary>Severity of a settings validation issue.</summary>
public enum SettingsIssueSeverity
{
    /// <summary>Informational note (value normalized).</summary>
    Info,

    /// <summary>Value was corrected/clamped; the app still runs.</summary>
    Warning,

    /// <summary>Setting is unusable; the app must fall back.</summary>
    Error,
}

/// <summary>One validation finding with a stable JSON path and code.</summary>
/// <param name="Path">Dotted JSON path (e.g. <c>appearance.fillOpacity</c>).</param>
/// <param name="Code">Stable machine code (e.g. <c>range.clamped</c>).</param>
/// <param name="Message">Human-readable detail (English, log-only).</param>
/// <param name="Severity">Severity.</param>
public sealed record SettingsIssue(string Path, string Code, string Message, SettingsIssueSeverity Severity);

/// <summary>Aggregated validation report for one settings snapshot.</summary>
public sealed record SettingsValidationReport
{
    /// <summary>All findings.</summary>
    public ImmutableArray<SettingsIssue> Issues { get; init; } = [];

    /// <summary>Whether any finding is an error.</summary>
    public bool HasErrors => Issues.Any(static i => i.Severity == SettingsIssueSeverity.Error);

    /// <summary>Whether any finding is a warning.</summary>
    public bool HasWarnings => Issues.Any(static i => i.Severity == SettingsIssueSeverity.Warning);

    /// <summary>Whether the snapshot is usable (no errors).</summary>
    public bool IsValid => !HasErrors;

    /// <summary>Number of findings.</summary>
    public int Count => Issues.Length;

    /// <summary>Builds a report from findings.</summary>
    public static SettingsValidationReport From(ImmutableArray<SettingsIssue> issues) => new() { Issues = issues };
}

/// <summary>Modifier flags for parsed hotkey bindings.</summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>No modifiers.</summary>
    None = 0,

    /// <summary>Ctrl.</summary>
    Control = 1,

    /// <summary>Alt.</summary>
    Alt = 2,

    /// <summary>Shift.</summary>
    Shift = 4,

    /// <summary>Windows key.</summary>
    Win = 8,
}

/// <summary>Parsed keyboard binding.</summary>
/// <param name="Modifiers">Modifier flags.</param>
/// <param name="Key">Canonical key name (e.g. <c>D</c>, <c>F5</c>, <c>LeftAlt</c>).</param>
public readonly record struct HotkeyBinding(HotkeyModifiers Modifiers, string Key);

/// <summary>
/// Parses and validates hotkey strings such as <c>Ctrl+Alt+D</c> or <c>LeftAlt</c>.
/// The core only validates the notation; Platform.Windows registers the actual hotkey.
/// </summary>
public static partial class HotkeyParser
{
    private static readonly ImmutableArray<string> NamedKeys =
    [
        "LeftAlt", "RightAlt", "LeftCtrl", "RightCtrl", "LeftShift", "RightShift",
        "LeftWin", "RightWin", "Space", "Tab", "Enter", "Escape", "Backspace", "Delete",
        "Insert", "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right",
        "CapsLock", "NumLock", "ScrollLock", "PrintScreen", "Pause",
    ];

    /// <summary>Parses a binding; returns <see langword="false"/> with a reason on failure.</summary>
    /// <param name="text">Binding text (case-insensitive, '+'-separated).</param>
    /// <param name="binding">Parsed binding on success.</param>
    /// <param name="error">Failure reason on failure.</param>
    public static bool TryParse(string? text, out HotkeyBinding binding, out string? error)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey is empty.";
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "Hotkey is empty.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    if (key is not null)
                    {
                        error = $"Hotkey '{text}' has more than one non-modifier key.";
                        return false;
                    }

                    key = NormalizeKey(part);
                    if (key is null)
                    {
                        error = $"Hotkey '{text}' has an unknown key '{part}'.";
                        return false;
                    }

                    break;
            }
        }

        if (key is null)
        {
            error = $"Hotkey '{text}' has modifiers but no key.";
            return false;
        }

        binding = new HotkeyBinding(modifiers, key);
        error = null;
        return true;
    }

    /// <summary>Formats a binding back to canonical text.</summary>
    public static string Format(HotkeyBinding binding)
    {
        var parts = new List<string>(5);
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(binding.Key);
        return string.Join('+', parts);
    }

    private static string? NormalizeKey(string raw)
    {
        if (raw.Length == 1)
        {
            var c = char.ToUpperInvariant(raw[0]);
            return char.IsLetterOrDigit(c) ? c.ToString() : null;
        }

        if (raw.Length is >= 2 and <= 3 && raw[0] is 'F' or 'f' && int.TryParse(raw[1..], out var fn)
            && fn is >= 1 and <= 24)
        {
            return $"F{fn}";
        }

        foreach (var named in NamedKeys)
        {
            if (string.Equals(named, raw, StringComparison.OrdinalIgnoreCase))
            {
                return named;
            }
        }

        return null;
    }
}

/// <summary>
/// Validates and normalizes settings: clamps ranges, trims strings, parses hotkeys and
/// reports every correction so nothing fails silently (spec hard rule 9).
/// </summary>
public static partial class SettingsValidator
{
    /// <summary>Minimum shell fill opacity for text-bearing states.</summary>
    public const double MinFillOpacity = 0.85;

    /// <summary>Minimum font scale.</summary>
    public const double MinFontScale = 0.8;

    /// <summary>Maximum font scale.</summary>
    public const double MaxFontScale = 1.6;

    /// <summary>Minimum motion strength.</summary>
    public const double MinMotionStrength = 0.5;

    /// <summary>Maximum motion strength.</summary>
    public const double MaxMotionStrength = 1.5;

    /// <summary>Minimum dock strip height.</summary>
    public const int MinDockHeight = 40;

    /// <summary>Maximum dock strip height.</summary>
    public const int MaxDockHeight = 80;

    /// <summary>Minimum auto-collapse delay (0 disables).</summary>
    public const int MinAutoCollapseMs = 0;

    /// <summary>Maximum auto-collapse delay.</summary>
    public const int MaxAutoCollapseMs = 60_000;

    /// <summary>Maximum auto-hide delay.</summary>
    public const int MaxAutoHideMs = 3_600_000;

    private static readonly ImmutableArray<string> ValidLogLevels = ["trace", "debug", "info", "warn", "error"];

    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagRegex();

    /// <summary>
    /// Validates and normalizes a settings snapshot. The returned tree is safe to persist;
    /// the report lists every normalized value, warning or error.
    /// </summary>
    /// <param name="settings">Input snapshot.</param>
    /// <param name="report">Validation report.</param>
    public static FluidBarSettings Normalize(FluidBarSettings settings, out SettingsValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var issues = ImmutableArray.CreateBuilder<SettingsIssue>();
        var normalizedVersion = settings.SchemaVersion;
        if (normalizedVersion != FluidBarSettings.CurrentSchemaVersion)
        {
            issues.Add(new SettingsIssue("schemaVersion", "schema.normalized",
                $"schemaVersion {normalizedVersion} normalized to {FluidBarSettings.CurrentSchemaVersion}.",
                SettingsIssueSeverity.Warning));
            normalizedVersion = FluidBarSettings.CurrentSchemaVersion;
        }

        var appearance = settings.Appearance;
        var fillOpacity = Clamp(appearance.FillOpacity, MinFillOpacity, 1.0, "appearance.fillOpacity", issues);
        var fontScale = Clamp(appearance.FontScale, MinFontScale, MaxFontScale, "appearance.fontScale", issues);
        var motionStrength = Clamp(appearance.MotionStrength, MinMotionStrength, MaxMotionStrength, "appearance.motionStrength", issues);
        var bloom = Clamp(appearance.BloomStrength, 0, 2, "appearance.bloomStrength", issues);
        var accentFallback = NormalizeHex(appearance.Accent.FallbackHex, "#4CC9F0", "appearance.accent.fallbackHex", issues);
        var accentCustom = appearance.Accent.CustomHex is null
            ? null
            : NormalizeHex(appearance.Accent.CustomHex, accentFallback, "appearance.accent.customHex", issues);

        var island = settings.Island;
        var autoCollapse = ClampInt(island.AutoCollapseMs, MinAutoCollapseMs, MaxAutoCollapseMs, "island.autoCollapseMs", issues);
        var autoHide = ClampInt(island.AutoHideMs, 0, MaxAutoHideMs, "island.autoHideMs", issues);

        var dock = settings.Dock;
        var dockHeight = ClampInt(dock.Height, MinDockHeight, MaxDockHeight, "dock.height", issues);
        var tiles = NormalizeTokens(dock.Tiles, "dock.tiles", issues);

        var plugins = settings.Plugins;
        var channel = plugins.Channel is "stable" or "beta" ? plugins.Channel : "stable";
        if (!string.Equals(channel, plugins.Channel, StringComparison.Ordinal))
        {
            issues.Add(new SettingsIssue("plugins.channel", "enum.defaulted",
                $"plugins.channel '{plugins.Channel}' is not stable|beta; defaulted to stable.", SettingsIssueSeverity.Warning));
        }

        var checkInterval = ClampInt(plugins.CheckIntervalHours, 1, 168, "plugins.checkIntervalHours", issues);

        var i18n = NormalizeI18n(settings.I18n, issues);
        var market = NormalizeMarket(settings.Market, issues);
        var advanced = NormalizeAdvanced(settings.Advanced, issues);
        var hotkeys = NormalizeHotkeys(settings.Hotkeys, issues);
        var placement = NormalizePlacement(settings.Placement, issues);
        var activities = NormalizeActivities(settings.Activities, issues);

        var normalized = settings with
        {
            SchemaVersion = normalizedVersion,
            Appearance = appearance with
            {
                FillOpacity = fillOpacity,
                FontScale = fontScale,
                MotionStrength = motionStrength,
                BloomStrength = bloom,
                Accent = appearance.Accent with
                {
                    FallbackHex = accentFallback,
                    CustomHex = accentCustom,
                },
            },
            Island = island with
            {
                AutoCollapseMs = autoCollapse,
                AutoHideMs = autoHide,
                SpecificMonitorId = string.IsNullOrWhiteSpace(island.SpecificMonitorId) ? null : island.SpecificMonitorId.Trim(),
            },
            Dock = dock with { Height = dockHeight, Tiles = tiles },
            Plugins = plugins with
            {
                Channel = channel,
                CheckIntervalHours = checkInterval,
                Enabled = NormalizeIds(plugins.Enabled, "plugins.enabled", issues),
                Orphaned = NormalizeIds(plugins.Orphaned, "plugins.orphaned", issues),
            },
            I18n = i18n,
            Market = market,
            Advanced = advanced,
            Hotkeys = hotkeys,
            Placement = placement,
            Activities = activities,
        };

        report = new SettingsValidationReport { Issues = issues.ToImmutable() };
        return normalized;
    }

    /// <summary>Validates without normalizing (read-only diagnostic).</summary>
    public static SettingsValidationReport Validate(FluidBarSettings settings)
    {
        Normalize(settings, out var report);
        return report;
    }

    private static I18nSettings NormalizeI18n(I18nSettings i18n, ImmutableArray<SettingsIssue>.Builder issues)
    {
        var language = NormalizeLanguage(i18n.Language, "i18n.language", issues);
        var fallback = NormalizeLanguage(i18n.FallbackLanguage, "i18n.fallbackLanguage", issues);
        return i18n with { Language = language, FallbackLanguage = fallback };
    }

    private static MarketSettings NormalizeMarket(MarketSettings market, ImmutableArray<SettingsIssue>.Builder issues)
    {
        var url = market.SourceUrl?.Trim() ?? string.Empty;
        if (url.Length == 0)
        {
            issues.Add(new SettingsIssue("market.sourceUrl", "url.empty",
                "marketplace source URL is empty; automatic checks will be skipped.", SettingsIssueSeverity.Warning));
        }
        else if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            issues.Add(new SettingsIssue("market.sourceUrl", "url.invalid",
                $"marketplace source URL '{url}' is not an absolute http(s) URL.", SettingsIssueSeverity.Error));
            url = SettingsJsonNames.DefaultMarketSourceUrl;
        }

        return market with { SourceUrl = url };
    }

    private static AdvancedSettings NormalizeAdvanced(AdvancedSettings advanced, ImmutableArray<SettingsIssue>.Builder issues)
    {
        var level = (advanced.LogLevel ?? string.Empty).Trim().ToLowerInvariant();
        if (!ValidLogLevels.Contains(level))
        {
            issues.Add(new SettingsIssue("advanced.logLevel", "enum.defaulted",
                $"log level '{advanced.LogLevel}' is invalid; defaulted to warn.", SettingsIssueSeverity.Warning));
            level = "warn";
        }

        return advanced with { LogLevel = level };
    }

    private static HotkeySettings NormalizeHotkeys(HotkeySettings hotkeys, ImmutableArray<SettingsIssue>.Builder issues)
    {
        var toggle = NormalizeHotkey(hotkeys.Toggle, "hotkeys.toggle", issues);
        var hide = NormalizeHotkey(hotkeys.Hide, "hotkeys.hide", issues);
        var settingsHotkey = NormalizeHotkey(hotkeys.Settings, "hotkeys.settings", issues);
        var hold = string.IsNullOrWhiteSpace(hotkeys.HoldToHide) ? null : hotkeys.HoldToHide.Trim();
        if (hold is not null && !HotkeyParser.TryParse(hold, out _, out var holdError))
        {
            issues.Add(new SettingsIssue("hotkeys.holdToHide", "hotkey.invalid",
                $"hold-to-hide key '{hold}' is invalid: {holdError}", SettingsIssueSeverity.Warning));
            hold = null;
        }

        // Duplicate global hotkeys are a registration conflict; surface it, do not auto-change.
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddConflict(toggle, "hotkeys.toggle", parsed, issues);
        AddConflict(hide, "hotkeys.hide", parsed, issues);
        AddConflict(settingsHotkey, "hotkeys.settings", parsed, issues);

        return hotkeys with { Toggle = toggle, Hide = hide, Settings = settingsHotkey, HoldToHide = hold };
    }

    private static void AddConflict(
        string value,
        string path,
        Dictionary<string, string> parsed,
        ImmutableArray<SettingsIssue>.Builder issues)
    {
        if (!HotkeyParser.TryParse(value, out var binding, out _))
        {
            return;
        }

        var canonical = HotkeyParser.Format(binding);
        if (parsed.TryGetValue(canonical, out var otherPath))
        {
            issues.Add(new SettingsIssue(path, "hotkey.conflict",
                $"hotkey '{canonical}' is also used by {otherPath}.", SettingsIssueSeverity.Warning));
        }
        else
        {
            parsed[canonical] = path;
        }
    }

    private static string NormalizeHotkey(string value, string path, ImmutableArray<SettingsIssue>.Builder issues)
    {
        if (!HotkeyParser.TryParse(value, out var binding, out var error))
        {
            issues.Add(new SettingsIssue(path, "hotkey.invalid",
                $"hotkey '{value}' is invalid: {error}", SettingsIssueSeverity.Warning));
            return string.Empty;
        }

        return HotkeyParser.Format(binding);
    }

    private static PlacementSettings NormalizePlacement(PlacementSettings placement, ImmutableArray<SettingsIssue>.Builder issues)
    {
        var monitors = new Dictionary<string, MonitorPlacement>(StringComparer.Ordinal);
        foreach (var (id, value) in placement.Monitors)
        {
            var key = id.Trim();
            if (key.Length == 0)
            {
                issues.Add(new SettingsIssue("placement.monitors", "monitor.emptyId",
                    "A placement entry has an empty monitor id and was dropped.", SettingsIssueSeverity.Warning));
                continue;
            }

            if (!double.IsFinite(value.OffsetX) || !double.IsFinite(value.OffsetY ?? 0))
            {
                issues.Add(new SettingsIssue($"placement.monitors.{key}", "value.nonFinite",
                    "Placement offsets must be finite numbers; entry was dropped.", SettingsIssueSeverity.Warning));
                continue;
            }

            monitors[key] = value;
        }

        if (!monitors.ContainsKey(SettingsJsonNames.PrimaryPlacementKey))
        {
            monitors[SettingsJsonNames.PrimaryPlacementKey] = new MonitorPlacement();
        }

        return placement with { Monitors = monitors };
    }

    private static ActivitySettings NormalizeActivities(ActivitySettings activities, ImmutableArray<SettingsIssue>.Builder issues)
    {
        ActivityFeatureSettings Fix(ActivityFeatureSettings feature, string path)
        {
            var duration = feature.DurationMs;
            if (duration is < 0)
            {
                issues.Add(new SettingsIssue($"{path}.durationMs", "range.clamped",
                    "Duration must be >= 0 ms; defaulted.", SettingsIssueSeverity.Warning));
                duration = null;
            }
            else if (duration is > 0 and < 100)
            {
                issues.Add(new SettingsIssue($"{path}.durationMs", "range.clamped",
                    "Duration below 100 ms is unreadable; clamped to 100 ms.", SettingsIssueSeverity.Warning));
                duration = 100;
            }

            var blocklist = feature.Blocklist.IsDefault
                ? []
                : feature.Blocklist
                    .Where(static s => !string.IsNullOrWhiteSpace(s))
                    .Select(static s => s.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .ToImmutableArray();
            return feature with { DurationMs = duration, Blocklist = blocklist };
        }

        return activities with
        {
            Lookup = Fix(activities.Lookup, "activities.lookup"),
            Clipboard = Fix(activities.Clipboard, "activities.clipboard"),
            LockKeys = Fix(activities.LockKeys, "activities.lockKeys"),
            Device = Fix(activities.Device, "activities.device"),
            Bluetooth = Fix(activities.Bluetooth, "activities.bluetooth"),
            Focus = Fix(activities.Focus, "activities.focus"),
            Volume = Fix(activities.Volume, "activities.volume"),
            Brightness = Fix(activities.Brightness, "activities.brightness"),
            Notification = Fix(activities.Notification, "activities.notification"),
            Agent = Fix(activities.Agent, "activities.agent"),
            BatteryLow = Fix(activities.BatteryLow, "activities.batteryLow"),
            Progress = Fix(activities.Progress, "activities.progress"),
            Timer = Fix(activities.Timer, "activities.timer"),
            Media = Fix(activities.Media, "activities.media"),
        };
    }

    private static ImmutableArray<string> NormalizeIds(
        ImmutableArray<string> ids,
        string path,
        ImmutableArray<SettingsIssue>.Builder issues)
    {
        if (ids.IsDefaultOrEmpty)
        {
            return [];
        }

        var normalized = ids
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        if (normalized.Length != ids.Length)
        {
            issues.Add(new SettingsIssue(path, "list.normalized",
                "Duplicate or empty plugin ids were removed.", SettingsIssueSeverity.Info));
        }

        return normalized;
    }

    private static ImmutableArray<string> NormalizeTokens(
        ImmutableArray<string> values,
        string path,
        ImmutableArray<SettingsIssue>.Builder issues)
    {
        if (values.IsDefaultOrEmpty)
        {
            return ["media", "notifications", "clipboard", "agent", "widgets"];
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "home", "media", "notifications", "clipboard", "agent", "widgets", "settings", "market",
        };
        var tokens = values
            .Where(static v => !string.IsNullOrWhiteSpace(v))
            .Select(static v => v.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        var filtered = tokens.Where(allowed.Contains).ToImmutableArray();
        if (filtered.Length != tokens.Length)
        {
            issues.Add(new SettingsIssue(path, "list.unknownToken",
                "Unknown dock tile tokens were removed.", SettingsIssueSeverity.Warning));
        }

        return filtered.IsDefaultOrEmpty ? ["media"] : filtered;
    }

    private static string NormalizeLanguage(string value, string path, ImmutableArray<SettingsIssue>.Builder issues)
    {
        var language = (value ?? string.Empty).Trim();
        if (string.Equals(language, "system", StringComparison.OrdinalIgnoreCase))
        {
            return "system";
        }

        if (!LanguageTagRegex().IsMatch(language))
        {
            issues.Add(new SettingsIssue(path, "language.invalid",
                $"'{value}' is not a valid BCP-47 language tag; defaulted to system.", SettingsIssueSeverity.Warning));
            return "system";
        }

        return language;
    }

    private static string NormalizeHex(string? value, string fallback, string path, ImmutableArray<SettingsIssue>.Builder issues)
    {
        var hex = (value ?? string.Empty).Trim();
        if (hex.Length is 7 or 9 && hex[0] == '#' && hex[1..].All(Uri.IsHexDigit))
        {
            return hex.ToUpperInvariant();
        }

        if (hex.Length != 0)
        {
            issues.Add(new SettingsIssue(path, "color.invalid",
                $"'{value}' is not a #RRGGBB/#AARRGGBB color.", SettingsIssueSeverity.Warning));
        }

        return fallback;
    }

    private static double Clamp(double value, double min, double max, string path, ImmutableArray<SettingsIssue>.Builder issues)
    {
        if (!double.IsFinite(value))
        {
            issues.Add(new SettingsIssue(path, "value.nonFinite",
                "Value is not finite; defaulted to the nearest bound.", SettingsIssueSeverity.Warning));
            return min;
        }

        var clamped = Math.Clamp(value, min, max);
        if (Math.Abs(clamped - value) > 1e-9)
        {
            issues.Add(new SettingsIssue(path, "range.clamped",
                $"Value {value} clamped to {clamped} (allowed {min}..{max}).", SettingsIssueSeverity.Warning));
        }

        return clamped;
    }

    private static int ClampInt(int value, int min, int max, string path, ImmutableArray<SettingsIssue>.Builder issues)
    {
        var clamped = Math.Clamp(value, min, max);
        if (clamped != value)
        {
            issues.Add(new SettingsIssue(path, "range.clamped",
                $"Value {value} clamped to {clamped} (allowed {min}..{max}).", SettingsIssueSeverity.Warning));
        }

        return clamped;
    }
}
