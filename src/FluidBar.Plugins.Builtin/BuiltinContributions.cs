using System.Collections.Immutable;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Plugins.Builtin;

/// <summary>Stable manifest-derived ids of the first-party plugin contributions.</summary>
public static class BuiltinIds
{
    // Plugin ids (also the SDK event sources).
    public const string MediaPlugin = "fluidbar-media";
    public const string ClipboardPlugin = "fluidbar-clipboard";
    public const string NotificationsPlugin = "fluidbar-notifications";
    public const string AgentPlugin = "fluidbar-agent";
    public const string DevicesPlugin = "fluidbar-devices";

    // Commands.
    public const string MediaTogglePlayPause = "media.toggle-play-pause";
    public const string MediaNext = "media.next";
    public const string MediaPrevious = "media.previous";
    public const string ClipboardCopyLatest = "clipboard.copy-latest";
    public const string ClipboardPinLatest = "clipboard.pin-latest";
    public const string ClipboardClearHistory = "clipboard.clear-history";
    public const string NotificationsCopyCode = "notifications.copy-code";
    public const string NotificationsRequestAccess = "notifications.request-access";
    public const string NotificationsRetract = "notifications.retract";
    public const string AgentInstallHooks = "agent.install-hooks";
    public const string AgentRepairHooks = "agent.repair-hooks";
    public const string AgentUninstallHooks = "agent.uninstall-hooks";
    public const string DevicesRetract = "devices.retract";

    // Layout ids.
    public const string MediaCompactLayout = "media.compact";
    public const string MediaHoverLayout = "media.hover";
    public const string ClipboardCompactLayout = "clipboard.compact";
    public const string NotificationsCompactLayout = "notifications.compact";
    public const string AgentCompactLayout = "agent.compact";
    public const string DevicesTransientLayout = "devices.transient";

    // Settings pages.
    public const string MediaSettings = "media.settings";
    public const string ClipboardSettings = "clipboard.settings";
    public const string NotificationsSettings = "notifications.settings";
    public const string AgentSettings = "agent.settings";
    public const string DevicesSettings = "devices.settings";
}

/// <summary>
/// Declarative contributions for the first-party plugins: settings pages, commands, context-menu items,
/// island/hover layouts backed by host templates and accessory widgets. Data only, no UI types.
/// </summary>
public static class BuiltinContributions
{
    private static LocalizedString Loc(string key) => LocalizedString.From(key);

    private static CommandContribution Command(string id, params CommandContext[] contexts) => new()
    {
        Id = id,
        Title = Loc($"command.{id}.title"),
        Description = Loc($"command.{id}.description"),
        Contexts = contexts.Length == 0
            ? [CommandContext.Global, CommandContext.Island, CommandContext.ContextMenu, CommandContext.CommandPalette, CommandContext.Settings]
            : contexts,
    };

    private static SettingsPageContribution Page(string id, params SettingControlContribution[] controls) => new()
    {
        Id = id,
        Title = Loc($"settings.{id}.title"),
        Description = Loc($"settings.{id}.description"),
        Controls = controls,
    };

    private static SettingControlContribution Toggle(string key, string titleKey) => new()
    {
        Type = SettingControlType.Toggle,
        Key = key,
        Title = Loc(titleKey),
        Default = System.Text.Json.JsonSerializer.SerializeToElement(false),
    };

    private static SettingControlContribution Slider(string key, string titleKey, double min, double max, double step,
        double value, string? unit = null) => new()
    {
        Type = SettingControlType.Slider,
        Key = key,
        Title = Loc(titleKey),
        Description = titleKey.EndsWith(".title", StringComparison.Ordinal)
            ? Loc(titleKey[..^".title".Length] + ".description")
            : null,
        Min = min,
        Max = max,
        Step = step,
        Unit = unit,
        Default = System.Text.Json.JsonSerializer.SerializeToElement(value),
    };

    private static SettingControlContribution Info(string key, string titleKey) => new()
    {
        Type = SettingControlType.Info,
        Key = key,
        Title = Loc(titleKey),
    };

    private static SettingControlContribution Button(string key, string commandId) => new()
    {
        Type = SettingControlType.Button,
        Key = key,
        Title = Loc($"setting.{key}.title"),
        Command = commandId,
        Style = SettingButtonStyle.Secondary,
    };

    private static LayoutContribution Layout(string id, LayoutSurface surface, LayoutTemplate template,
        IReadOnlyDictionary<string, LayoutSlotBinding> slots) => new()
    {
        Id = id,
        Surface = surface,
        Template = template,
        Slots = slots,
    };

    private static System.Collections.Immutable.ImmutableDictionary<string, LayoutSlotBinding> FieldSlots(params (string Slot, string Field, SlotStyle? Style)[] bindings)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, LayoutSlotBinding>(StringComparer.Ordinal);
        foreach (var (slot, field, style) in bindings)
        {
            builder[slot] = new LayoutSlotBinding { Field = field, Style = style };
        }

        return builder.ToImmutable();
    }

    private static ContextMenuItemContribution MenuItem(string id, string commandId, ContextMenuGroup group) => new()
    {
        Id = id,
        Command = commandId,
        Title = Loc($"menu.{id}.title"),
        When = string.Empty,
        Group = group,
    };

    /// <summary>Media plugin contributions.</summary>
    public static PluginContributions ForMedia() => new()
    {
        Layouts =
        [
            Layout(BuiltinIds.MediaCompactLayout, LayoutSurface.IslandCompact, LayoutTemplate.MediaV1,
                FieldSlots(("icon", "icon", SlotStyle.Title), ("title", "title", SlotStyle.Title),
                    ("subtitle", "subtitle", SlotStyle.Subtitle), ("badge", "badge", SlotStyle.Badge),
                    ("progress", "progress", null), ("accessory", "wave", null))),
            Layout(BuiltinIds.MediaHoverLayout, LayoutSurface.HoverCard, LayoutTemplate.MediaHoverV1,
                FieldSlots(("title", "title", SlotStyle.Title), ("subtitle", "subtitle", SlotStyle.Subtitle),
                    ("body", "body", SlotStyle.Body), ("artwork", "artwork", null),
                    ("progress", "progress", null), ("actions", "actions", null))),
        ],
        SettingsPages =
        [
            Page(BuiltinIds.MediaSettings,
                Toggle("media.showWave", "setting.media.showWave.title"),
                Toggle("media.showLyrics", "setting.media.showLyrics.title"),
                // Island pill form: cover + live lyric + wave instead of title/artist (default on).
                Toggle("media.compactLyrics", "setting.media.compactLyrics.title"),
                // Karaoke lead/lag belongs to the MEDIA plugin: it only compensates this source's clock.
                Slider("media.lyricOffsetMs", "setting.media.lyricOffset.title", -800, 800, 50, 0, "ms")),
        ],
        Commands =
        [
            Command(BuiltinIds.MediaTogglePlayPause, CommandContext.Island, CommandContext.ContextMenu,
                CommandContext.CommandPalette, CommandContext.Global),
            Command(BuiltinIds.MediaNext, CommandContext.Island, CommandContext.ContextMenu),
            Command(BuiltinIds.MediaPrevious, CommandContext.Island, CommandContext.ContextMenu),
        ],
        ContextMenu =
        [
            MenuItem("media.toggle", BuiltinIds.MediaTogglePlayPause, ContextMenuGroup.Controls),
            MenuItem("media.next", BuiltinIds.MediaNext, ContextMenuGroup.Controls),
            MenuItem("media.previous", BuiltinIds.MediaPrevious, ContextMenuGroup.Controls),
        ],
        Widgets =
        [
            new WidgetContribution
            {
                Id = "media.wave",
                Slot = WidgetSlot.IslandAccessory,
                Template = WidgetTemplate.WaveV1,
                Size = WidgetSize.Small,
            },
        ],
    };

    /// <summary>Clipboard plugin contributions.</summary>
    public static PluginContributions ForClipboard() => new()
    {
        Layouts =
        [
            Layout(BuiltinIds.ClipboardCompactLayout, LayoutSurface.IslandCompact, LayoutTemplate.TextV1,
                FieldSlots(("icon", "icon", SlotStyle.Title), ("title", "title", SlotStyle.Title),
                    ("body", "preview", SlotStyle.Body), ("badge", "badge", SlotStyle.Badge))),
        ],
        SettingsPages =
        [
            Page(BuiltinIds.ClipboardSettings, Toggle("clipboard.enabled", "setting.clipboard.enabled.title")),
        ],
        Commands =
        [
            Command(BuiltinIds.ClipboardCopyLatest, CommandContext.Island, CommandContext.ContextMenu,
                CommandContext.CommandPalette),
            Command(BuiltinIds.ClipboardPinLatest, CommandContext.ContextMenu),
            Command(BuiltinIds.ClipboardClearHistory, CommandContext.Settings, CommandContext.ContextMenu),
        ],
        ContextMenu =
        [
            MenuItem("clipboard.copy", BuiltinIds.ClipboardCopyLatest, ContextMenuGroup.Clipboard),
            MenuItem("clipboard.pin", BuiltinIds.ClipboardPinLatest, ContextMenuGroup.Clipboard),
            MenuItem("clipboard.clear", BuiltinIds.ClipboardClearHistory, ContextMenuGroup.Danger),
        ],
    };

    /// <summary>Notifications plugin contributions.</summary>
    public static PluginContributions ForNotifications() => new()
    {
        Layouts =
        [
            Layout(BuiltinIds.NotificationsCompactLayout, LayoutSurface.IslandCompact, LayoutTemplate.NotificationV1,
                FieldSlots(("icon", "icon", SlotStyle.Title), ("title", "title", SlotStyle.Title),
                    ("body", "body", SlotStyle.Body), ("badge", "code", SlotStyle.Badge),
                    ("actions", "actions", null))),
        ],
        SettingsPages =
        [
            Page(BuiltinIds.NotificationsSettings,
                Info("notifications.permission", "setting.notifications.permission.title"),
                Toggle("notifications.verificationCode", "setting.notifications.verificationCode.title")),
        ],
        Commands =
        [
            Command(BuiltinIds.NotificationsCopyCode, CommandContext.Island, CommandContext.ContextMenu),
            Command(BuiltinIds.NotificationsRequestAccess, CommandContext.Settings),
            Command(BuiltinIds.NotificationsRetract, CommandContext.ContextMenu),
        ],
        ContextMenu =
        [
            MenuItem("notifications.copyCode", BuiltinIds.NotificationsCopyCode, ContextMenuGroup.Clipboard),
            MenuItem("notifications.clear", BuiltinIds.NotificationsRetract, ContextMenuGroup.Danger),
        ],
    };

    /// <summary>Agent status plugin contributions (hook install is consent-gated).</summary>
    public static PluginContributions ForAgent() => new()
    {
        Layouts =
        [
            Layout(BuiltinIds.AgentCompactLayout, LayoutSurface.IslandCompact, LayoutTemplate.AgentV1,
                FieldSlots(("icon", "icon", SlotStyle.Title), ("title", "title", SlotStyle.Title),
                    ("subtitle", "agent", SlotStyle.Subtitle), ("body", "body", SlotStyle.Body),
                    ("badge", "badge", SlotStyle.Badge), ("progress", "progress", null),
                    ("actions", "actions", null))),
        ],
        SettingsPages =
        [
            Page(BuiltinIds.AgentSettings,
                Button("agent.installHooks", BuiltinIds.AgentInstallHooks),
                Button("agent.repairHooks", BuiltinIds.AgentRepairHooks),
                Button("agent.uninstallHooks", BuiltinIds.AgentUninstallHooks)),
        ],
        Commands =
        [
            Command(BuiltinIds.AgentInstallHooks, CommandContext.Settings),
            Command(BuiltinIds.AgentRepairHooks, CommandContext.Settings),
            Command(BuiltinIds.AgentUninstallHooks, CommandContext.Settings),
        ],
    };

    /// <summary>Devices plugin contributions.</summary>
    public static PluginContributions ForDevices() => new()
    {
        Layouts =
        [
            Layout(BuiltinIds.DevicesTransientLayout, LayoutSurface.IslandCompact, LayoutTemplate.StatusV1,
                FieldSlots(("icon", "icon", SlotStyle.Title), ("title", "title", SlotStyle.Title),
                    ("subtitle", "source", SlotStyle.Subtitle), ("badge", "badge", SlotStyle.Badge))),
        ],
        SettingsPages =
        [
            Page(BuiltinIds.DevicesSettings,
                Toggle("devices.usb", "setting.devices.usb.title"),
                Toggle("devices.bluetooth", "setting.devices.bluetooth.title")),
        ],
        Commands = [Command(BuiltinIds.DevicesRetract, CommandContext.ContextMenu)],
        ContextMenu = [MenuItem("devices.clear", BuiltinIds.DevicesRetract, ContextMenuGroup.Controls)],
    };
}
