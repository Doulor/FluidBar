using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluidBar.App.Localization;
using FluidBar.App.Theme;
using FluidBar.App.Views.Plugins;
using FluidBar.App.Views.Settings;

namespace FluidBar.App.Views.Settings;

/// <summary>Icon and accent colour of a plugin row/detail header (one mapping for both surfaces).</summary>
internal static class PluginVisuals
{
    /// <summary>Prototype glyph for a plugin id.</summary>
    public static SettingsIconKind Icon(string id) => id switch
    {
        var value when value.Contains("media", StringComparison.OrdinalIgnoreCase) => SettingsIconKind.Star,
        var value when value.Contains("clipboard", StringComparison.OrdinalIgnoreCase) => SettingsIconKind.File,
        var value when value.Contains("notifications", StringComparison.OrdinalIgnoreCase) => SettingsIconKind.Alert,
        var value when value.Contains("agent", StringComparison.OrdinalIgnoreCase) => SettingsIconKind.Sliders,
        var value when value.Contains("code", StringComparison.OrdinalIgnoreCase) => SettingsIconKind.Palette,
        _ => SettingsIconKind.Puzzle,
    };

    /// <summary>Tile colour for a plugin id.</summary>
    public static Color Accent(string id) => id switch
    {
        var value when value.Contains("media", StringComparison.OrdinalIgnoreCase) => Color.Parse("#3E6E86"),
        var value when value.Contains("clipboard", StringComparison.OrdinalIgnoreCase) => Color.Parse("#8A6A3A"),
        var value when value.Contains("notifications", StringComparison.OrdinalIgnoreCase) => Color.Parse("#4A5686"),
        var value when value.Contains("agent", StringComparison.OrdinalIgnoreCase) => Color.Parse("#3F6E52"),
        var value when value.Contains("code", StringComparison.OrdinalIgnoreCase) => Color.Parse("#4A5686"),
        _ => Color.Parse("#4C5566"),
    };
}

/// <summary>
/// The plugin configuration page (task-22): one plugin's declarative settings plus its commands, built
/// exclusively from the shared settings control layer so it matches General/Appearance line for line.
/// </summary>
public static class PluginDetailView
{
    /// <summary>Builds the detail page for one plugin.</summary>
    /// <param name="plugin">Plugin list item (name, version, capabilities, state).</param>
    /// <param name="localizer">Localizer.</param>
    /// <param name="palette">Pane palette.</param>
    /// <param name="contributions">Declarative contributions.</param>
    /// <param name="invokeCommand">Invokes a declared command by id.</param>
    /// <param name="back">Returns to the plugin list.</param>
    /// <param name="accent">Window accent used by the switch controls.</param>
    public static Control Build(
        PluginListItem plugin,
        ILocalizer localizer,
        SettingsPalette palette,
        IPluginContributionUiService contributions,
        Action<string> invokeCommand,
        Action back,
        Color? accent = null)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(invokeCommand);
        ArgumentNullException.ThrowIfNull(back);

        var panel = new StackPanel { Spacing = 0 };

        // Back affordance: quiet button with the prototype's chevron (no invented chrome).
        var backButton = SettingsControls.Button(
            localizer.Get(LocKeys.Common.Back),
            palette,
            SettingsButtonVariant.Quiet,
            SettingsButtonSize.Sm,
            SettingsIconKind.ChevronLeft,
            back);
        backButton.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(backButton);

        // Header: 36 px plugin tile + name + publisher/version, exactly like the list row scaled up.
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, SettingsMetrics.CaptionMarginTop, 0, 0),
        };
        var icon = new Border
        {
            Width = SettingsMetrics.PluginIcon,
            Height = SettingsMetrics.PluginIcon,
            CornerRadius = new CornerRadius(SettingsMetrics.PluginIconRadius),
            Background = new SolidColorBrush(PluginVisuals.Accent(plugin.Id)),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new SettingsIcon
            {
                Kind = PluginVisuals.Icon(plugin.Id),
                Brush = Brushes.White,
                Width = SettingsMetrics.PluginGlyph,
                Height = SettingsMetrics.PluginGlyph,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(icon, 0);
        header.Children.Add(icon);

        var copy = new StackPanel
        {
            Spacing = 0,
            Margin = new Thickness(SettingsMetrics.PluginRowGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        copy.Children.Add(new TextBlock
        {
            Text = plugin.Name,
            FontFamily = Fonts.Display,
            FontSize = SettingsMetrics.AboutNameSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = palette.ForegroundBrush,
        });
        copy.Children.Add(new TextBlock
        {
            Text = plugin.Publisher + " · v" + plugin.Version,
            FontSize = SettingsMetrics.RowDescSize,
            Foreground = palette.TertiaryBrush,
            Margin = new Thickness(0, 3, 0, 0),
        });

        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = SettingsMetrics.PermGap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        chips.Children.Add(SettingsControls.Chip(
            plugin.Builtin
                ? localizer.Get(LocKeys.Settings.Plugins.Builtin)
                : localizer.Get(LocKeys.Settings.Plugins.Community),
            palette,
            SettingsChipKind.Badge));
        foreach (var key in plugin.CapabilityKeys)
        {
            chips.Children.Add(SettingsControls.Chip(
                localizer.Get(key),
                palette,
                SettingsChipKind.Permission,
                icon: SettingsIconKind.Shield));
        }

        copy.Children.Add(chips);
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);
        panel.Children.Add(header);

        if (!plugin.Available || !plugin.IsEnabled || !string.IsNullOrEmpty(plugin.Error) || plugin.RestartRequired)
        {
            var state = !plugin.Available
                ? localizer.Get(LocKeys.Settings.Plugins.StateUnavailable)
                : !plugin.IsEnabled
                    ? localizer.Get(LocKeys.Settings.Plugins.StateDisabled)
                    : plugin.RestartRequired
                        ? localizer.Get(LocKeys.Settings.Plugins.RestartRequired)
                        : plugin.Error ?? string.Empty;
            var tone = !plugin.Available || !string.IsNullOrEmpty(plugin.Error)
                ? SettingsTone.Warning
                : SettingsTone.Neutral;
            var line = SettingsControls.StatusLine(state, palette, tone);
            line.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(line);
        }

        var pages = contributions.SettingsPages(plugin.Id);
        foreach (var page in pages)
        {
            panel.Children.Add(PluginSettingsPageRenderer.BuildPage(
                page,
                plugin.Id,
                localizer,
                contributions.Values,
                (_, commandId) => invokeCommand(commandId),
                _ => { },
                palette,
                accent));
        }

        var commands = contributions.Commands(plugin.Id);
        if (commands.Count > 0)
        {
            panel.Children.Add(SettingsWidgets.Caption(localizer.Get(LocKeys.Settings.Plugins.Commands), palette));
            var rows = new List<Control>(commands.Count);
            foreach (var command in commands)
            {
                var title = LocalizedValue.Resolve(command.Title, localizer);
                var description = LocalizedValue.Resolve(command.Description, localizer);
                var commandId = command.Id;
                var run = SettingsControls.Button(
                    localizer.Get(LocKeys.Settings.Plugins.DetailRun),
                    palette,
                    SettingsButtonVariant.Raised,
                    SettingsButtonSize.Sm,
                    SettingsIconKind.ArrowRight,
                    () => invokeCommand(commandId));
                var hotkey = string.IsNullOrWhiteSpace(command.DefaultHotkey)
                    ? null
                    : SettingsControls.Chip(command.DefaultHotkey!, palette, SettingsChipKind.Keycap);
                rows.Add(SettingsWidgets.Row(
                    string.IsNullOrEmpty(title) ? command.Id : title,
                    description,
                    hotkey is null ? run : SettingsWidgets.ControlHost(hotkey, run),
                    palette));
            }

            panel.Children.Add(SettingsWidgets.Group(palette, topBorder: false, [.. rows]));
        }

        if (pages.Count == 0 && commands.Count == 0)
        {
            var empty = SettingsControls.EmptyState(localizer.Get(LocKeys.Settings.Plugins.DetailEmpty), palette);
            empty.Margin = new Thickness(0, 14, 0, 0);
            panel.Children.Add(empty);
        }

        panel.Children.Add(SettingsControls.FooterNote(
            plugin.Builtin
                ? localizer.Get(LocKeys.Settings.Plugins.DetailMeta)
                : localizer.Get(LocKeys.Settings.Plugins.Community),
            palette));

        return panel;
    }
}
