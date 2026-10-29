using System.Collections.Immutable;
using System.Text.Json;
using FluidBar.Core.Layout;

namespace FluidBar.Core.Settings;

/// <summary>One mapped/dropped/failed settings path during migration.</summary>
/// <param name="SourcePath">Path in the source document.</param>
/// <param name="TargetPath">Path in the v2 document; null when dropped.</param>
/// <param name="Note">Human-readable explanation (goes into the migration report).</param>
public sealed record MigrationEntry(string SourcePath, string? TargetPath, string? Note = null);

/// <summary>Migration report (spec 8.2: never silent; every dropped field is recorded).</summary>
public sealed record MigrationReport
{
    /// <summary>Source schema version (0/1 = legacy).</summary>
    public int FromVersion { get; init; } = 1;

    /// <summary>Target schema version.</summary>
    public int ToVersion { get; init; } = FluidBarSettings.CurrentSchemaVersion;

    /// <summary>Fields successfully mapped.</summary>
    public ImmutableArray<MigrationEntry> Mapped { get; init; } = [];

    /// <summary>Fields deliberately dropped.</summary>
    public ImmutableArray<MigrationEntry> Dropped { get; init; } = [];

    /// <summary>Fields that could not be mapped / structural failures.</summary>
    public ImmutableArray<MigrationEntry> Failed { get; init; } = [];

    /// <summary>Whether migration produced a usable v2 document.</summary>
    public bool Success { get; init; } = true;

    /// <summary>Failure detail when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>Number of mapped fields.</summary>
    public int MappedCount => Mapped.Length;

    /// <summary>Number of dropped fields.</summary>
    public int DroppedCount => Dropped.Length;

    /// <summary>Number of failed fields.</summary>
    public int FailedCount => Failed.Length;
}

/// <summary>Migrated settings plus the audit report.</summary>
/// <param name="Settings">Migrated v2 settings (normalized).</param>
/// <param name="Report">Migration audit report.</param>
public sealed record MigrationResult(FluidBarSettings Settings, MigrationReport Report);

/// <summary>
/// v1 -&gt; v2 settings migration (spec 8.2 mapping table). Pure: takes JSON text, returns a v2
/// settings tree + report. The store owns backups/atomic writes.
/// </summary>
public static class SettingsMigrator
{
    private static readonly ImmutableDictionary<string, string> KnownV1PluginIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["media"] = BuiltinPlugins.Media,
            ["clipboard"] = BuiltinPlugins.Clipboard,
            ["notifications"] = BuiltinPlugins.Notifications,
            ["notification"] = BuiltinPlugins.Notifications,
            ["agent"] = BuiltinPlugins.Agent,
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly ImmutableDictionary<string, Activities.ActivityKind> KnownV1ActivityIds =
        new Dictionary<string, Activities.ActivityKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["volume"] = Activities.ActivityKind.Volume,
            ["brightness"] = Activities.ActivityKind.Brightness,
            ["clipboard"] = Activities.ActivityKind.Clipboard,
            ["notification"] = Activities.ActivityKind.Notification,
            ["notifications"] = Activities.ActivityKind.Notification,
            ["media"] = Activities.ActivityKind.Media,
            ["battery"] = Activities.ActivityKind.BatteryLow,
            ["lockkeys"] = Activities.ActivityKind.LockKeys,
            ["lockKeys"] = Activities.ActivityKind.LockKeys,
            ["device"] = Activities.ActivityKind.Device,
            ["usb"] = Activities.ActivityKind.Device,
            ["bluetooth"] = Activities.ActivityKind.Bluetooth,
            ["focus"] = Activities.ActivityKind.Focus,
            ["agent"] = Activities.ActivityKind.Agent,
            ["timer"] = Activities.ActivityKind.Timer,
            ["progress"] = Activities.ActivityKind.Progress,
            ["lookup"] = Activities.ActivityKind.Lookup,
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Detects the schema version of a settings JSON document.
    /// Returns <see langword="null"/> for a legacy (v1) document; sets <paramref name="error"/>
    /// and returns 0 when the document is not a JSON object.
    /// </summary>
    public static int? DetectVersion(string json, out string? error)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Settings root must be a JSON object.";
                return 0;
            }

            if (TryGetProperty(doc.RootElement, "schemaVersion", out var versionElement)
                && versionElement.ValueKind == JsonValueKind.Number
                && versionElement.TryGetInt32(out var version))
            {
                error = null;
                return version;
            }

            error = null;
            return null;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return 0;
        }
    }

    /// <summary>Migrates a document to v2 based on its detected version.</summary>
    public static MigrationResult Migrate(string json)
    {
        var version = DetectVersion(json, out var error);
        if (error is not null)
        {
            return Failure(error, fromVersion: 0);
        }

        if (version is null)
        {
            return MigrateV1(json);
        }

        if (version > FluidBarSettings.CurrentSchemaVersion)
        {
            return Failure($"Unsupported future schemaVersion {version}.", version.Value);
        }

        if (!SettingsJson.TryDeserialize(json, out var parsed, out var deserializeError) || parsed is null)
        {
            return Failure(deserializeError ?? "Settings JSON is invalid.", version.Value);
        }

        var upgrade = ImmutableArray<MigrationEntry>.Empty;
        if (version.Value == 2)
        {
            // v2 documents stored general.telemetry from a switch that could not be toggled (the UI forced
            // false), so that value was never a user choice: v3 resets it to the shipped default instead.
            var shippedTelemetry = new FluidBarSettings().General.Telemetry;
            parsed = parsed with { General = parsed.General with { Telemetry = shippedTelemetry } };
            upgrade =
            [
                new MigrationEntry("general.telemetry", "general.telemetry",
                    "Pre-v3 builds wrote this from an inert switch; reset to the shipped default."),
            ];
        }

        var normalized = SettingsValidator.Normalize(parsed, out var validation);
        var report = new MigrationReport
        {
            FromVersion = version.Value,
            ToVersion = FluidBarSettings.CurrentSchemaVersion,
            Mapped = upgrade,
            Success = !validation.HasErrors,
            Failed = validation.HasErrors
                ? [.. validation.Issues
                    .Where(static i => i.Severity == SettingsIssueSeverity.Error)
                    .Select(static i => new MigrationEntry(i.Path, null, i.Message))]
                : [],
            Error = validation.HasErrors ? "Settings validation failed." : null,
        };
        return new MigrationResult(normalized, report);
    }

    /// <summary>Migrates a legacy v1 settings JSON document.</summary>
    public static MigrationResult MigrateV1(string json)
    {
        var mapped = ImmutableArray.CreateBuilder<MigrationEntry>();
        var dropped = ImmutableArray.CreateBuilder<MigrationEntry>();
        var failed = ImmutableArray.CreateBuilder<MigrationEntry>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failure("v1 settings root must be a JSON object.", 1);
            }

            var settings = FluidBarSettings.CreateDefault();

            // --- position / offset -> island anchor + placement ---
            var anchor = IslandAnchor.TopCenter;
            if (TryGetProperty(root, "Position", out var positionElement) && positionElement.ValueKind == JsonValueKind.String)
            {
                var raw = positionElement.GetString();
                anchor = MapAnchor(raw, out var recognized);
                if (recognized)
                {
                    mapped.Add(new MigrationEntry("Position", "placement.monitors.primary.anchor", raw));
                }
                else
                {
                    dropped.Add(new MigrationEntry("Position", null, $"Unknown position '{raw}'; defaulted to top-center."));
                }
            }

            var offsetX = TryGetNumber(root, "OffsetX") ?? 0;
            var offsetY = TryGetNumber(root, "OffsetY") ?? 0;
            if (offsetX != 0 || offsetY != 0)
            {
                mapped.Add(new MigrationEntry("OffsetX/OffsetY", "placement.monitors.primary.offset", null));
            }

            var placement = new Dictionary<string, MonitorPlacement>(StringComparer.Ordinal)
            {
                [SettingsJsonNames.PrimaryPlacementKey] = new MonitorPlacement
                {
                    Anchor = anchor,
                    OffsetX = offsetX,
                    OffsetY = offsetY,
                },
            };

            // --- appearance ---
            var appearance = settings.Appearance;
            if (TryGetNumber(root, "Opacity") is { } opacity)
            {
                var fill = opacity > 1 ? opacity / 100.0 : opacity;
                appearance = appearance with { FillOpacity = fill };
                mapped.Add(new MigrationEntry("Opacity", "appearance.fillOpacity", "Percent values are divided by 100."));
            }

            var themeElement = TryGetProperty(root, "Theme", out var themeOut) ? themeOut
                : TryGetProperty(root, "SettingsTheme", out var settingsThemeOut) ? settingsThemeOut : default;
            if (themeElement.ValueKind == JsonValueKind.String)
            {
                var rawTheme = themeElement.GetString();
                var theme = rawTheme?.ToLowerInvariant() switch
                {
                    "light" => AppTheme.Light,
                    "dark" => AppTheme.Dark,
                    "system" => AppTheme.System,
                    _ => AppTheme.System,
                };
                appearance = appearance with { Theme = theme };
                mapped.Add(new MigrationEntry(themeElement.ValueKind == JsonValueKind.String ? "Theme/SettingsTheme" : "Theme",
                    "appearance.theme", rawTheme));
            }

            if (TryGetProperty(root, "AccentColor", out var accentElement) && accentElement.ValueKind == JsonValueKind.String)
            {
                var hex = accentElement.GetString();
                if (string.Equals(hex, "#0A84FF", StringComparison.OrdinalIgnoreCase))
                {
                    appearance = appearance with { Accent = appearance.Accent with { Mode = AccentMode.System } };
                    mapped.Add(new MigrationEntry("AccentColor", "appearance.accent.mode", "v1 default maps to system accent."));
                }
                else if (IsHexColor(hex))
                {
                    appearance = appearance with
                    {
                        Accent = appearance.Accent with { Mode = AccentMode.Custom, CustomHex = hex!.ToUpperInvariant() },
                    };
                    mapped.Add(new MigrationEntry("AccentColor", "appearance.accent.customHex", hex));
                }
                else
                {
                    dropped.Add(new MigrationEntry("AccentColor", null, $"'{hex}' is not a valid color."));
                }
            }

            // --- island behaviour ---
            var island = settings.Island;
            if (TryGetNumber(root, "AutoHideDelayMs") is { } autoHideDelay)
            {
                island = island with { AutoCollapseMs = (int)Math.Clamp(autoHideDelay, 0, 60_000) };
                mapped.Add(new MigrationEntry("AutoHideDelayMs", "island.autoCollapseMs", null));
            }

            if (TryGetProperty(root, "DisplayStrategy", out var strategyElement) && strategyElement.ValueKind == JsonValueKind.String)
            {
                var raw = strategyElement.GetString();
                var stacking = string.Equals(raw, "LatestOnly", StringComparison.OrdinalIgnoreCase)
                    ? StackingPreference.Latest
                    : StackingPreference.Stack;
                island = island with { Stacking = stacking };
                mapped.Add(new MigrationEntry("DisplayStrategy", "island.stacking", raw));
            }

            // --- hotkeys ---
            var hotkeys = settings.Hotkeys;
            if (TryGetProperty(root, "HoldToHideKey", out var holdElement) && holdElement.ValueKind == JsonValueKind.String)
            {
                var raw = holdElement.GetString();
                hotkeys = hotkeys with
                {
                    HoldToHide = string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "Disabled", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : raw,
                };
                mapped.Add(new MigrationEntry("HoldToHideKey", "hotkeys.holdToHide", raw));
            }

            // --- general ---
            var general = settings.General;
            if (TryGetBoolean(root, "HideTrayIcon") is { } hideTray)
            {
                general = general with { TrayIcon = !hideTray };
                mapped.Add(new MigrationEntry("HideTrayIcon", "general.trayIcon", null));
            }

            if (TryGetBoolean(root, "AutoUpdateCheck") is { } autoUpdate)
            {
                general = general with { AutoUpdate = autoUpdate ? AutoUpdatePreference.Notify : AutoUpdatePreference.Off };
                mapped.Add(new MigrationEntry("AutoUpdateCheck", "general.autoUpdate", null));
            }

            // --- activities ---
            var activities = settings.Activities;
            if (TryGetBoolean(root, "ClipboardEnabled") is { } clipboardEnabled)
            {
                activities = activities with { Clipboard = activities.Clipboard with { Enabled = clipboardEnabled } };
                mapped.Add(new MigrationEntry("ClipboardEnabled", "activities.clipboard.enabled", null));
            }

            if (TryGetProperty(root, "MonitorFeatureSettings", out var monitorFeatures)
                && monitorFeatures.ValueKind == JsonValueKind.Object)
            {
                foreach (var feature in monitorFeatures.EnumerateObject())
                {
                    if (!KnownV1ActivityIds.TryGetValue(feature.Name, out var activityKind))
                    {
                        dropped.Add(new MigrationEntry($"MonitorFeatureSettings.{feature.Name}", null,
                            "Unknown monitor feature id."));
                        continue;
                    }

                    // v1 never persisted Enabled (audit F-19): treat as enabled by default.
                    var enabled = TryGetBoolean(feature.Value, "Enabled") ?? true;
                    var durationMs = TryGetNumber(feature.Value, "DisplayDurationMs") is { } duration
                        ? (int)Math.Clamp(duration, 100, 60_000)
                        : (int?)null;
                    var detectCode = TryGetBoolean(feature.Value, "VerificationCodeDetection");
                    activities = ApplyActivity(activities, activityKind, enabled, durationMs, detectCode);
                    mapped.Add(new MigrationEntry(
                        $"MonitorFeatureSettings.{feature.Name}",
                        $"activities.{activityKind.ToSettingsKey()}.enabled",
                        durationMs is null ? null : $"durationMs={durationMs}"));
                }
            }

            // --- plugins ---
            var enabledPlugins = ImmutableArray.CreateBuilder<string>();
            var orphanedPlugins = ImmutableArray.CreateBuilder<string>();
            if (TryGetProperty(root, "PluginEnabled", out var pluginEnabled)
                && pluginEnabled.ValueKind == JsonValueKind.Object)
            {
                foreach (var plugin in pluginEnabled.EnumerateObject())
                {
                    if (plugin.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || !plugin.Value.GetBoolean())
                    {
                        continue;
                    }

                    if (KnownV1PluginIds.TryGetValue(plugin.Name, out var mappedId))
                    {
                        enabledPlugins.Add(mappedId);
                        mapped.Add(new MigrationEntry($"PluginEnabled.{plugin.Name}", "plugins.enabled", mappedId));
                    }
                    else
                    {
                        // Spec 8.2: unknown ids are preserved and marked orphan.
                        enabledPlugins.Add(plugin.Name);
                        orphanedPlugins.Add(plugin.Name);
                        mapped.Add(new MigrationEntry($"PluginEnabled.{plugin.Name}", "plugins.enabled", "orphan (not installed)"));
                    }
                }
            }

            // --- explicitly dropped fields (reported, never silent) ---
            foreach (var field in new[]
                     {
                         "CollapsedWidth", "CollapsedHeight", "ExpandedMaxWidth", "ExpandedHeight",
                         "CornerRadius", "BackgroundOpacity", "BackgroundColor", "AlwaysOnTop",
                         "AlwaysVisible", "RimMode", "MaxVisibleIslands", "MultiIslandGap",
                         "AgentHooksGuardEnabled", "AgentHooksGuardIntervalMs", "AgentKeepIslandVisible",
                         "HooksGuardEnabled", "HooksGuardIntervalMs", "AnimationDuration",
                         "HoverCardEnabled", "EmphasizeTransitions", "HoverCardDelayMs", "BackgroundBlur",
                     })
            {
                if (TryGetProperty(root, field, out _))
                {
                    var note = field switch
                    {
                        "AgentHooksGuardEnabled" or "AgentHooksGuardIntervalMs" or "AgentKeepIslandVisible"
                            => "Agent hook semantics are plugin-owned in v2.",
                        "HooksGuardEnabled" or "HooksGuardIntervalMs" => "Duplicate v1 guard field; dropped.",
                        "CornerRadius" => "v2 corners are derived from mode/state.",
                        _ => "Not part of the v2 settings model.",
                    };
                    dropped.Add(new MigrationEntry(field, null, note));
                }
            }

            // --- unknown top-level fields ---
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Position", "OffsetX", "OffsetY", "Opacity", "Theme", "SettingsTheme", "AccentColor",
                "AutoHideDelayMs", "DisplayStrategy", "HoldToHideKey", "HideTrayIcon", "AutoUpdateCheck",
                "ClipboardEnabled", "MonitorFeatureSettings", "PluginEnabled",
            };
            foreach (var property in root.EnumerateObject())
            {
                if (!handled.Contains(property.Name)
                    && !dropped.Any(d => string.Equals(d.SourcePath, property.Name, StringComparison.Ordinal)))
                {
                    dropped.Add(new MigrationEntry(property.Name, null, "Unknown v1 field."));
                }
            }

            var migrated = settings with
            {
                General = general,
                Appearance = appearance,
                // Spec 8.2: v1 Position maps to BOTH the default island anchor and the primary
                // monitor placement entry (legacy semantics were primary-monitor only).
                Island = island with { Anchor = anchor },
                Placement = new PlacementSettings { Monitors = placement },
                Hotkeys = hotkeys,
                Activities = activities,
                Plugins = settings.Plugins with
                {
                    Enabled = enabledPlugins.ToImmutable(),
                    Orphaned = orphanedPlugins.ToImmutable(),
                },
            };

            var normalized = SettingsValidator.Normalize(migrated, out var validation);
            foreach (var issue in validation.Issues.Where(static i => i.Severity == SettingsIssueSeverity.Error))
            {
                failed.Add(new MigrationEntry(issue.Path, null, issue.Message));
            }

            var report = new MigrationReport
            {
                FromVersion = 1,
                ToVersion = FluidBarSettings.CurrentSchemaVersion,
                Mapped = mapped.ToImmutable(),
                Dropped = dropped.ToImmutable(),
                Failed = failed.ToImmutable(),
                Success = failed.Count == 0,
                Error = failed.Count == 0 ? null : "Migration produced validation errors.",
            };
            return new MigrationResult(normalized, report);
        }
        catch (JsonException ex)
        {
            return Failure($"v1 settings JSON is invalid: {ex.Message}", 1);
        }
    }

    private static ActivitySettings ApplyActivity(
        ActivitySettings activities,
        Activities.ActivityKind kind,
        bool enabled,
        int? durationMs,
        bool? detectVerificationCode)
    {
        ActivityFeatureSettings Patch(ActivityFeatureSettings feature) => feature with
        {
            Enabled = enabled,
            DurationMs = durationMs ?? feature.DurationMs,
            DetectVerificationCode = detectVerificationCode ?? feature.DetectVerificationCode,
        };

        return kind switch
        {
            Activities.ActivityKind.Lookup => activities with { Lookup = Patch(activities.Lookup) },
            Activities.ActivityKind.Clipboard => activities with { Clipboard = Patch(activities.Clipboard) },
            Activities.ActivityKind.LockKeys => activities with { LockKeys = Patch(activities.LockKeys) },
            Activities.ActivityKind.Device => activities with { Device = Patch(activities.Device) },
            Activities.ActivityKind.Bluetooth => activities with { Bluetooth = Patch(activities.Bluetooth) },
            Activities.ActivityKind.Focus => activities with { Focus = Patch(activities.Focus) },
            Activities.ActivityKind.Volume => activities with { Volume = Patch(activities.Volume) },
            Activities.ActivityKind.Brightness => activities with { Brightness = Patch(activities.Brightness) },
            Activities.ActivityKind.Notification => activities with { Notification = Patch(activities.Notification) },
            Activities.ActivityKind.Agent => activities with { Agent = Patch(activities.Agent) },
            Activities.ActivityKind.BatteryLow => activities with { BatteryLow = Patch(activities.BatteryLow) },
            Activities.ActivityKind.Progress => activities with { Progress = Patch(activities.Progress) },
            Activities.ActivityKind.Timer => activities with { Timer = Patch(activities.Timer) },
            Activities.ActivityKind.Media => activities with { Media = Patch(activities.Media) },
            _ => activities,
        };
    }

    private static string ToSettingsKey(this Activities.ActivityKind kind) => kind switch
    {
        Activities.ActivityKind.LockKeys => "lockKeys",
        Activities.ActivityKind.BatteryLow => "batteryLow",
        _ => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..],
    };

    private static IslandAnchor MapAnchor(string? raw, out bool recognized)
    {
        recognized = true;
        return raw?.Trim().ToLowerInvariant() switch
        {
            "top" or "topcenter" or "center" => IslandAnchor.TopCenter,
            "topleft" => IslandAnchor.TopLeft,
            "topright" => IslandAnchor.TopRight,
            "bottom" or "bottomcenter" => IslandAnchor.BottomCenter,
            "bottomleft" => IslandAnchor.BottomLeft,
            "bottomright" => IslandAnchor.BottomRight,
            _ => NotRecognized(out recognized),
        };

        static IslandAnchor NotRecognized(out bool recognized)
        {
            recognized = false;
            return IslandAnchor.TopCenter;
        }
    }

    private static bool IsHexColor(string? value)
        => value is { Length: 7 or 9 } && value[0] == '#' && value[1..].All(Uri.IsHexDigit);

    private static MigrationResult Failure(string error, int fromVersion) => new(
        FluidBarSettings.CreateDefault(),
        new MigrationReport
        {
            FromVersion = fromVersion,
            ToVersion = FluidBarSettings.CurrentSchemaVersion,
            Success = false,
            Error = error,
            Failed = [new MigrationEntry("$", null, error)],
        });

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static double? TryGetNumber(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool? TryGetBoolean(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
