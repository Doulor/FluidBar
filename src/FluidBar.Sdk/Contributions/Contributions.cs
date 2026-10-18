using System.Text.Json;
using FluidBar.Sdk.Json;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Sdk.Contributions;

/// <summary>Host-rendered surfaces a plugin can contribute to. Data only: never plugin XAML or UI types.</summary>
public enum LayoutSurface
{
    [WireName("island.compact")] IslandCompact = 0,
    [WireName("island.expanded")] IslandExpanded = 1,
    [WireName("hoverCard")] HoverCard = 2,
}

/// <summary>Host-owned layout templates. Plugins fill named slots; they cannot supply arbitrary trees.</summary>
public enum LayoutTemplate
{
    [WireName("text.v1")] TextV1 = 0,
    [WireName("status.v1")] StatusV1 = 1,
    [WireName("progress.v1")] ProgressV1 = 2,
    [WireName("media.v1")] MediaV1 = 3,
    [WireName("media.hover.v1")] MediaHoverV1 = 4,
    [WireName("notification.v1")] NotificationV1 = 5,
    [WireName("agent.v1")] AgentV1 = 6,
    [WireName("clock.v1")] ClockV1 = 7,
    [WireName("list.v1")] ListV1 = 8,
}

/// <summary>Kind of content bound into a layout slot.</summary>
public enum SlotKind
{
    [WireName("text")] Text = 0,
    [WireName("image")] Image = 1,
    [WireName("icon")] Icon = 2,
    [WireName("badge")] Badge = 3,
    [WireName("progress")] Progress = 4,
    [WireName("wave")] Wave = 5,
    [WireName("code")] Code = 6,
}

/// <summary>Host text style applied to a slot. Tokens come from the host theme, not the plugin.</summary>
public enum SlotStyle
{
    [WireName("title")] Title = 0,
    [WireName("subtitle")] Subtitle = 1,
    [WireName("body")] Body = 2,
    [WireName("caption")] Caption = 3,
    [WireName("badge")] Badge = 4,
    [WireName("mono")] Mono = 5,
}

/// <summary>Declarative island/hover-card layout contribution.</summary>
public sealed record LayoutContribution
{
    public string Id { get; init; } = string.Empty;

    public LayoutSurface Surface { get; init; }

    public LayoutTemplate Template { get; init; }

    public IReadOnlyDictionary<string, LayoutSlotBinding>? Slots { get; init; }

    public LayoutConstraints? Constraints { get; init; }
}

/// <summary>Binds one host slot to plugin-provided data, a literal, or command buttons.</summary>
public sealed record LayoutSlotBinding
{
    public string? Field { get; init; }

    public string? Literal { get; init; }

    public SlotKind? Kind { get; init; }

    public int? MaxLines { get; init; }

    public SlotStyle? Style { get; init; }

    public IReadOnlyList<string>? Commands { get; init; }
}

/// <summary>Size constraints in device-independent pixels (validated against host geometry tokens).</summary>
public sealed record LayoutConstraints
{
    public double? MinWidth { get; init; }

    public double? MaxWidth { get; init; }

    public double? MinHeight { get; init; }

    public double? MaxHeight { get; init; }
}

/// <summary>
/// Declarative settings page rendered by the host with host controls, theme, i18n and accessibility.
/// A page may declare loose <see cref="Controls"/> and/or <see cref="Groups"/>; the host renders loose controls
/// first (implicit "General" group) and then the declared groups. No plugin UI code is loaded.
/// </summary>
public sealed record SettingsPageContribution
{
    public string Id { get; init; } = string.Empty;

    public LocalizedString? Title { get; init; }

    public LocalizedString? Description { get; init; }

    /// <summary>Loose controls rendered before groups (backwards compatible form).</summary>
    public IReadOnlyList<SettingControlContribution>? Controls { get; init; }

    /// <summary>Optional titled groups of controls (recommended for pages with more than a few rows).</summary>
    public IReadOnlyList<SettingsGroupContribution>? Groups { get; init; }
}

/// <summary>A titled group of host-rendered controls inside a settings page.</summary>
public sealed record SettingsGroupContribution
{
    public string Id { get; init; } = string.Empty;

    public LocalizedString? Title { get; init; }

    public LocalizedString? Description { get; init; }

    public IReadOnlyList<SettingControlContribution>? Controls { get; init; }
}

/// <summary>Control types the host can render in a plugin settings page. No plugin UI code is loaded.</summary>
public enum SettingControlType
{
    [WireName("toggle")] Toggle = 0,
    [WireName("select")] Select = 1,
    [WireName("slider")] Slider = 2,
    [WireName("number")] Number = 3,
    [WireName("text")] Text = 4,
    [WireName("path")] Path = 5,
    [WireName("secret")] Secret = 6,
    [WireName("button")] Button = 7,
    [WireName("link")] Link = 8,
    [WireName("info")] Info = 9,
    [WireName("status")] Status = 10,
}

/// <summary>How a read-only status row formats its value (plugin writes the value into a settings key).</summary>
public enum SettingStatusFormat
{
    [WireName("text")] Text = 0,
    [WireName("number")] Number = 1,
    [WireName("boolean")] Boolean = 2,
    [WireName("timestamp")] Timestamp = 3,
}

/// <summary>Visual style of a settings button.</summary>
public enum SettingButtonStyle
{
    [WireName("primary")] Primary = 0,
    [WireName("secondary")] Secondary = 1,
    [WireName("danger")] Danger = 2,
}

/// <summary>A single host-rendered setting control.</summary>
public sealed record SettingControlContribution
{
    public SettingControlType Type { get; init; }

    public string Key { get; init; } = string.Empty;

    public LocalizedString? Title { get; init; }

    public LocalizedString? Description { get; init; }

    /// <summary>Default value as JSON; validated against the control type by the manifest validator.</summary>
    public JsonElement? Default { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    public double? Step { get; init; }

    public string? Unit { get; init; }

    public IReadOnlyList<SettingOptionContribution>? Options { get; init; }

    public string? Command { get; init; }

    public SettingButtonStyle? Style { get; init; }

    public SettingValidation? Validation { get; init; }

    /// <summary>Display format for read-only <see cref="SettingControlType.Status"/> rows.</summary>
    public SettingStatusFormat? Format { get; init; }
}

/// <summary>Label/value pair used by a select control.</summary>
public sealed record SettingOptionContribution
{
    public string Value { get; init; } = string.Empty;

    public LocalizedString? Label { get; init; }
}

/// <summary>Cross-field validation the host applies to a text/number setting before persisting it.</summary>
public sealed record SettingValidation
{
    public string? Pattern { get; init; }

    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }
}

/// <summary>Where a manifest-declared command can be invoked from.</summary>
public enum CommandContext
{
    [WireName("global")] Global = 0,
    [WireName("island")] Island = 1,
    [WireName("contextMenu")] ContextMenu = 2,
    [WireName("commandPalette")] CommandPalette = 3,
    [WireName("settings")] Settings = 4,
}

/// <summary>Manifest-declared command. The handler is registered at runtime through <c>ICommandRegistry</c>.</summary>
public sealed record CommandContribution
{
    public string Id { get; init; } = string.Empty;

    public LocalizedString? Title { get; init; }

    public LocalizedString? Description { get; init; }

    public string? Icon { get; init; }

    public string? DefaultHotkey { get; init; }

    public IReadOnlyList<CommandContext>? Contexts { get; init; }
}

/// <summary>Grouping of a context-menu item in the host menu.</summary>
public enum ContextMenuGroup
{
    [WireName("primary")] Primary = 0,
    [WireName("controls")] Controls = 1,
    [WireName("clipboard")] Clipboard = 2,
    [WireName("navigation")] Navigation = 3,
    [WireName("danger")] Danger = 4,
}

/// <summary>Declarative context-menu action; the host renders and evaluates <c>when</c>.</summary>
public sealed record ContextMenuItemContribution
{
    public string Id { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public LocalizedString? Title { get; init; }

    /// <summary>Restricted predicate grammar evaluated by the host, e.g. <c>source == 'x' &amp;&amp; hasTrack</c>.</summary>
    public string When { get; init; } = string.Empty;

    public ContextMenuGroup? Group { get; init; }
}

/// <summary>Slot an accessory widget can occupy.</summary>
public enum WidgetSlot
{
    [WireName("island.accessory")] IslandAccessory = 0,
    [WireName("island.leading")] IslandLeading = 1,
    [WireName("hoverCard.footer")] HoverCardFooter = 2,
}

/// <summary>Host-owned widget template.</summary>
public enum WidgetTemplate
{
    [WireName("wave.v1")] WaveV1 = 0,
    [WireName("badge.v1")] BadgeV1 = 1,
    [WireName("icon.v1")] IconV1 = 2,
    [WireName("text.v1")] TextV1 = 3,
}

/// <summary>Widget footprint token.</summary>
public enum WidgetSize
{
    [WireName("small")] Small = 0,
    [WireName("medium")] Medium = 1,
}

/// <summary>Declarative accessory widget contributed by a plugin.</summary>
public sealed record WidgetContribution
{
    public string Id { get; init; } = string.Empty;

    public WidgetSlot Slot { get; init; }

    public WidgetTemplate Template { get; init; }

    public WidgetSize? Size { get; init; }
}

/// <summary>The full declarative contribution block of a manifest.</summary>
public sealed record PluginContributions
{
    public IReadOnlyList<LayoutContribution>? Layouts { get; init; }

    public IReadOnlyList<SettingsPageContribution>? SettingsPages { get; init; }

    public IReadOnlyList<CommandContribution>? Commands { get; init; }

    public IReadOnlyList<ContextMenuItemContribution>? ContextMenu { get; init; }

    public IReadOnlyList<WidgetContribution>? Widgets { get; init; }
}
