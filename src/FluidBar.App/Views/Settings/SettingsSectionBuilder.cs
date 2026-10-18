using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.App.Views.Market;
using FluidBar.Core.Layout;

namespace FluidBar.App.Views.Settings;

/// <summary>Dependencies the settings sections need (injected so headless tests can swap fakes).</summary>
public sealed record SettingsWindowContext
{
    /// <summary>Settings controller.</summary>
    public required SettingsController Settings { get; init; }

    /// <summary>Localizer.</summary>
    public required ILocalizer Localizer { get; init; }

    /// <summary>Marketplace service.</summary>
    public required IMarketplaceUiService Marketplace { get; init; }

    /// <summary>Plugin list service.</summary>
    public required IPluginUiService Plugins { get; init; }

    /// <summary>Current settings-window surfaces.</summary>
    public required AppSurfaceColors Surfaces { get; init; }

    /// <summary>Owner window accessor (consent dialog parenting).</summary>
    public required Func<Window?> Owner { get; init; }

    /// <summary>Opens a path/folder with the shell (no-op in tests).</summary>
    public required Action<string?> OpenPath { get; init; }

    /// <summary>Shows a transient status line in the section (exported path, errors).</summary>
    public required Action<string> ShowStatus { get; init; }
}

/// <summary>Builds the seven settings sections from the strongly typed settings model.</summary>
public static class SettingsSectionBuilder
{
    /// <summary>Builds the section content.</summary>
    public static Control Build(SettingsWindowSection section, SettingsWindowContext context) => section switch
    {
        SettingsWindowSection.Appearance => BuildAppearance(context),
        SettingsWindowSection.Island => BuildIsland(context),
        SettingsWindowSection.Plugins => BuildPlugins(context),
        SettingsWindowSection.Marketplace => BuildMarketplace(context),
        SettingsWindowSection.Advanced => BuildAdvanced(context),
        SettingsWindowSection.About => BuildAbout(context),
        _ => BuildGeneral(context),
    };

    private static StackPanel Section(string title, string? subtitle, params Control[] rows)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 8, 24) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = Fonts.DisplaySemiBold.FontFamily,
            FontSize = Typography.SettingsSectionSize + 2,
            FontWeight = FontWeight.SemiBold,
        });
        if (!string.IsNullOrEmpty(subtitle))
        {
            panel.Children.Add(new TextBlock { Text = subtitle, FontSize = Typography.SettingsCaptionSize, Opacity = 0.65 });
        }

        foreach (var row in rows)
        {
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control Row(string? label, string? description, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2, 0, 2), MinHeight = 38 };
        var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(label))
        {
            info.Children.Add(new TextBlock { Text = label, FontSize = Typography.SettingsBodySize });
        }

        if (!string.IsNullOrEmpty(description))
        {
            info.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = Typography.SettingsCaptionSize,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Grid.SetColumn(info, 0);
        grid.Children.Add(info);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static Control ToggleRow(string label, string? description, bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = string.Empty, OffContent = string.Empty };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        return Row(label, description, toggle);
    }

    private static Control ComboRow(string label, string? description, (string Label, string Value)[] options, string current, Action<string> changed)
    {
        var combo = new ComboBox { Width = 220 };
        var selected = 0;
        for (var i = 0; i < options.Length; i++)
        {
            var item = new ComboBoxItem { Content = options[i].Label, Tag = options[i].Value };
            combo.Items.Add(item);
            if (string.Equals(options[i].Value, current, StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
            }
        }

        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string value })
            {
                changed(value);
            }
        };
        return Row(label, description, combo);
    }

    private static Control SliderRow(string label, string? description, double min, double max, double step, double value, Func<double, string> format, Action<double> changed)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = step,
            IsSnapToTickEnabled = step > 0,
            Value = Math.Clamp(value, min, max),
            Width = 240,
        };
        var valueText = new TextBlock
        {
            Text = format(slider.Value),
            FontFamily = Fonts.MonoRegular.FontFamily,
            FontSize = Typography.SettingsCaptionSize,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 64,
            TextAlignment = TextAlignment.Right,
            Opacity = 0.75,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(slider);
        panel.Children.Add(valueText);
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                valueText.Text = format(slider.Value);
                changed(slider.Value);
            }
        };
        return Row(label, description, panel);
    }

    private static Control ButtonRow(string label, string? description, string buttonText, Action clicked)
    {
        var button = new Button { Content = buttonText };
        button.Click += (_, _) => clicked();
        return Row(label, description, button);
    }

    private static Control BuildGeneral(SettingsWindowContext ctx)
    {
        var settings = ctx.Settings.Current;
        var languageOptions = new (string, string)[]
        {
            (ctx.Localizer.Get(LocKeys.Locale.System), "system"),
            (ctx.Localizer.Get(LocKeys.Locale.English), "en-US"),
            (ctx.Localizer.Get(LocKeys.Locale.Chinese), "zh-Hans"),
            (ctx.Localizer.Get(LocKeys.Locale.Pseudo), "qps-ploc"),
        };

        return Section(
            ctx.Localizer.Get(LocKeys.Settings.General.Title),
            ctx.Localizer.Get(LocKeys.Settings.General.LanguageHint),
            ComboRow(ctx.Localizer.Get(LocKeys.Settings.General.Language), ctx.Localizer.Get(LocKeys.Settings.General.LanguageHint), languageOptions, settings.Language,
                value => ctx.Settings.Update(s => s with { Language = value })),
            ToggleRow(ctx.Localizer.Get(LocKeys.Settings.General.StartWithWindows), ctx.Localizer.Get(LocKeys.Settings.General.StartWithWindowsHint), settings.StartWithWindows,
                value => ctx.Settings.Update(s => s with { StartWithWindows = value })),
            ToggleRow(ctx.Localizer.Get(LocKeys.Settings.General.TrayIcon), ctx.Localizer.Get(LocKeys.Settings.General.TrayIconHint), settings.TrayIcon,
                value => ctx.Settings.Update(s => s with { TrayIcon = value })),
            ComboRow(ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdateLabel), null,
                [
                    (ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdate.Notify), "notify"),
                    (ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdate.Auto), "auto"),
                    (ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdate.Off), "off"),
                ],
                settings.AutoUpdate,
                value => ctx.Settings.Update(s => s with { AutoUpdate = value })),
            ToggleRow(ctx.Localizer.Get(LocKeys.Settings.General.Telemetry), ctx.Localizer.Get(LocKeys.Settings.General.TelemetryHint), false, _ => { }),
            new TextBlock
            {
                Text = ctx.Localizer.Get(LocKeys.Settings.General.Hotkeys),
                FontWeight = FontWeight.SemiBold,
                FontSize = Typography.SettingsBodySize,
                Margin = new Thickness(0, 10, 0, 0),
            },
            KeyRow("Ctrl+Alt+D", ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.Toggle)),
            KeyRow("Ctrl+Alt+H", ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.Hide)),
            KeyRow("Ctrl+Alt+S", ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.Settings)),
            KeyRow("LeftAlt", ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.HoldToHide)),
            KeyRow("Esc / 1–6", ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.Toggle)));
    }

    private static Control KeyRow(string keys, string description)
    {
        var text = new TextBlock
        {
            Text = keys,
            FontFamily = Fonts.MonoRegular.FontFamily,
            FontSize = 12,
        };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 2),
            Child = text,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(border);
        row.Children.Add(new TextBlock
        {
            Text = description,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7,
            FontSize = Typography.SettingsCaptionSize,
        });
        return row;
    }

    private static Control BuildAppearance(SettingsWindowContext ctx)
    {
        var settings = ctx.Settings.Current;
        var preview = new IslandPreview();
        preview.Update(settings, ctx.Localizer);
        var accentBox = new TextBox { Text = settings.AccentFallback, Width = 100 };
        accentBox.LostFocus += (_, _) =>
        {
            var hex = accentBox.Text;
            if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out _))
            {
                ctx.Settings.Update(s => s with { AccentFallback = hex.Trim() });
            }
        };

        return Section(
            ctx.Localizer.Get(LocKeys.Settings.Appearance.Title),
            ctx.Localizer.Get(LocKeys.Settings.Appearance.PreviewHint),
            ComboRow(ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.Title), null,
                [
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.System), "system"),
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.Dark), "dark"),
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.Light), "light"),
                ],
                settings.Theme switch { AppTheme.Dark => "dark", AppTheme.Light => "light", _ => "system" },
                value => ctx.Settings.Update(s => s with
                {
                    Theme = value switch { "dark" => AppTheme.Dark, "light" => AppTheme.Light, _ => AppTheme.System },
                })),
            ComboRow(ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.Title), ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.AcrylicUnavailable),
                [
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.Obsidian), "obsidian"),
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.Acrylic), "acrylic"),
                ],
                settings.Material == IslandMaterial.Acrylic ? "acrylic" : "obsidian",
                value => ctx.Settings.Update(s => s with
                {
                    Material = value == "acrylic" ? IslandMaterial.Acrylic : IslandMaterial.Obsidian,
                })),
            ComboRow(ctx.Localizer.Get(LocKeys.Settings.Appearance.PaletteLabel), null,
                [
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Obsidian), "obsidian"),
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Graphite), "graphite"),
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Slate), "slate"),
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Nord), "nord"),
                ],
                settings.Palette,
                value => ctx.Settings.Update(s => s with { Palette = value })),
            ComboRow(ctx.Localizer.Get(LocKeys.Settings.Appearance.Accent.Title), null,
                [
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Accent.Artwork), "artwork"),
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Accent.System), "system"),
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.Accent.Custom), "custom"),
                ],
                settings.AccentMode,
                value => ctx.Settings.Update(s => s with { AccentMode = value })),
            Row(ctx.Localizer.Get(LocKeys.Settings.Appearance.Accent.Custom), null, accentBox),
            SliderRow(ctx.Localizer.Get(LocKeys.Settings.Appearance.FillOpacity), null, LayoutTokens.MinFillOpacity, 1.0, 0.01, settings.FillOpacity,
                value => $"{value * 100:0}%", value => ctx.Settings.Update(s => s with { FillOpacity = value })),
            SliderRow(ctx.Localizer.Get(LocKeys.Settings.Appearance.FontScale), null, 0.8, 1.6, 0.05, settings.FontScale,
                value => $"{value:0.00}×", value => ctx.Settings.Update(s => s with { FontScale = value })),
            ComboRow(ctx.Localizer.Get(LocKeys.Settings.Appearance.ReducedMotion.Title), null,
                [
                    (ctx.Localizer.Get(LocKeys.Settings.Appearance.ReducedMotion.System), "system"),
                    (ctx.Localizer.Get(LocKeys.Common.On), "on"),
                    (ctx.Localizer.Get(LocKeys.Common.Off), "off"),
                ],
                settings.ReducedMotion switch
                {
                    ReducedMotionPreference.On => "on",
                    ReducedMotionPreference.Off => "off",
                    _ => "system",
                },
                value => ctx.Settings.Update(s => s with
                {
                    ReducedMotion = value switch
                    {
                        "on" => ReducedMotionPreference.On,
                        "off" => ReducedMotionPreference.Off,
                        _ => ReducedMotionPreference.System,
                    },
                })),
            SliderRow(ctx.Localizer.Get(LocKeys.Settings.Appearance.MotionStrength), null, 0.5, 1.5, 0.05, settings.MotionStrength,
                value => $"{value:0.00}×", value => ctx.Settings.Update(s => s with { MotionStrength = value })),
            new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(12),
                Child = preview,
            });
    }

    private static Control BuildIsland(SettingsWindowContext ctx)
    {
        var settings = ctx.Settings.Current;
        return Section(
            ctx.Localizer.Get(LocKeys.Settings.Island.Title),
            null,
            ComboRow(ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Title), null,
                [
                    (ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Float), "float"),
                    (ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Flush), "flush"),
                    (ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Dock), "dock"),
                ],
                settings.Mode,
                value => ctx.Settings.Update(s => s with { Mode = value })),
            ComboRow(ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.Title), null,
                [
                    (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.TopLeft), "top-left"),
                    (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.TopCenter), "top-center"),
                    (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.TopRight), "top-right"),
                    (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.BottomLeft), "bottom-left"),
                    (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.BottomCenter), "bottom-center"),
                    (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.BottomRight), "bottom-right"),
                ],
                settings.Anchor,
                value => ctx.Settings.Update(s => s with { Anchor = value })),
            ComboRow(ctx.Localizer.Get(LocKeys.Settings.Island.Stacking.Title), null,
                [
                    (ctx.Localizer.Get(LocKeys.Settings.Island.Stacking.Stacked), "stacked"),
                    (ctx.Localizer.Get(LocKeys.Settings.Island.Stacking.Latest), "latest"),
                ],
                settings.Stacking == StackingStrategySetting.Latest ? "latest" : "stacked",
                value => ctx.Settings.Update(s => s with
                {
                    Stacking = value == "latest" ? StackingStrategySetting.Latest : StackingStrategySetting.Stacked,
                })),
            ToggleRow(ctx.Localizer.Get(LocKeys.Settings.Island.HoverPeek), ctx.Localizer.Get(LocKeys.Settings.Island.HoverPeekHint), settings.HoverPeek,
                value => ctx.Settings.Update(s => s with { HoverPeek = value })),
            SliderRow(ctx.Localizer.Get(LocKeys.Settings.Island.AutoCollapseMs), ctx.Localizer.Get(LocKeys.Settings.Island.AutoCollapseMsHint), 0, 8000, 100, settings.AutoCollapseMs,
                value => $"{value:0} ms", value => ctx.Settings.Update(s => s with { AutoCollapseMs = (int)Math.Round(value) })),
            SliderRow(ctx.Localizer.Get(LocKeys.Settings.Island.AutoHideMs), ctx.Localizer.Get(LocKeys.Settings.Island.AutoHideMsHint), 0, 30000, 500, settings.AutoHideMs,
                value => $"{value:0} ms", value => ctx.Settings.Update(s => s with { AutoHideMs = (int)Math.Round(value) })),
            ToggleRow(ctx.Localizer.Get(LocKeys.Settings.Island.PeekOnTrackChange), null, settings.PeekOnTrackChange,
                value => ctx.Settings.Update(s => s with { PeekOnTrackChange = value })),
            ComboRow(ctx.Localizer.Get(LocKeys.Settings.Island.DisplayOn.Title), null,
                [
                    (ctx.Localizer.Get(LocKeys.Settings.Island.DisplayOn.Primary), "primary"),
                    (ctx.Localizer.Get(LocKeys.Settings.Island.DisplayOn.All), "all"),
                    (ctx.Localizer.Get(LocKeys.Settings.Island.DisplayOn.FollowMouse), "follow-mouse"),
                ],
                settings.DisplayOn switch
                {
                    DisplayTarget.All => "all",
                    DisplayTarget.FollowMouse => "follow-mouse",
                    _ => "primary",
                },
                value => ctx.Settings.Update(s => s with
                {
                    DisplayOn = value switch
                    {
                        "all" => DisplayTarget.All,
                        "follow-mouse" => DisplayTarget.FollowMouse,
                        _ => DisplayTarget.Primary,
                    },
                })));
    }

    private static Control BuildPlugins(SettingsWindowContext ctx)
    {
        var list = new StackPanel { Spacing = 6 };
        var search = new TextBox { Watermark = ctx.Localizer.Get(LocKeys.Settings.Plugins.Search), Width = 260 };
        var contributions = ctx.Plugins as FluidBar.App.Views.Plugins.IPluginContributionUiService;
        string? selectedPlugin = null;
        var panel = Section(
            ctx.Localizer.Get(LocKeys.Settings.Plugins.Title),
            null,
            search,
            list);
        search.TextChanged += (_, _) => _ = LoadAsync();
        _ = LoadAsync();
        return panel;

        void ShowDetail(string pluginId)
        {
            if (contributions is null)
            {
                return;
            }

            selectedPlugin = pluginId;
            panel.Children.Clear();
            var back = new Button { Content = ctx.Localizer.Get(LocKeys.Common.Back) };
            back.Click += (_, _) =>
            {
                selectedPlugin = null;
                panel.Children.Clear();
                panel.Children.Add(search);
                panel.Children.Add(list);
                _ = LoadAsync();
            };
            panel.Children.Add(back);
            var name = FluidBar.App.Views.Plugins.LocalizedValue.Resolve(contributions.DisplayName(pluginId), ctx.Localizer);
            panel.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = Typography.SettingsSectionSize,
                FontWeight = FontWeight.SemiBold,
            });
            foreach (var page in contributions.SettingsPages(pluginId))
            {
                panel.Children.Add(FluidBar.App.Views.Plugins.PluginSettingsPageRenderer.BuildPage(
                    page,
                    ctx.Localizer,
                    contributions.Values,
                    commandId =>
                    {
                        contributions.InvokeCommand(commandId);
                        ctx.ShowStatus(commandId);
                    },
                    ctx.OpenPath));
            }
        }

        async Task LoadAsync()
        {
            try
            {
                var plugins = await ctx.Plugins.ListAsync(CancellationToken.None);
                list.Children.Clear();
                var query = search.Text?.Trim() ?? string.Empty;
                foreach (var plugin in plugins.Where(p => query.Length == 0
                    || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || p.Id.Contains(query, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Children.Add(BuildPluginRow(ctx, plugin, ShowDetail));
                }
            }
            catch (OperationCanceledException)
            {
                // Section closed while loading; nothing to attach.
            }
            catch (Exception ex)
            {
                ctx.ShowStatus(ex.Message);
            }
        }
    }

    private static Control BuildPluginRow(SettingsWindowContext ctx, PluginListItem plugin, Action<string> showDetail)
    {
        var enabled = ctx.Settings.Current.Plugins.Enabled.Contains(plugin.Id);
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var info = new StackPanel { Spacing = 2 };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = plugin.Name, FontSize = 14, FontWeight = FontWeight.SemiBold });
        title.Children.Add(new TextBlock
        {
            Text = ctx.Localizer.Get(plugin.Builtin ? LocKeys.Settings.Plugins.Builtin : LocKeys.Settings.Plugins.Community),
            FontSize = 10,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
        });
        info.Children.Add(title);
        info.Children.Add(new TextBlock
        {
            Text = ctx.Localizer.Format(LocKeys.Settings.Plugins.Version, ("version", plugin.Version))
                + "  ·  " + ctx.Localizer.Format(LocKeys.Settings.Plugins.By, ("author", plugin.Publisher)),
            FontSize = Typography.SettingsCaptionSize,
            Opacity = 0.65,
        });
        if (plugin.CapabilityKeys.Count > 0)
        {
            info.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", plugin.CapabilityKeys.Select(ctx.Localizer.Get)),
                FontSize = 10,
                Opacity = 0.55,
            });
        }

        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (ctx.Plugins is FluidBar.App.Views.Plugins.IPluginContributionUiService contributionService
            && contributionService.SettingsPages(plugin.Id).Count > 0)
        {
            var settingsButton = new Button { Content = ctx.Localizer.Get(LocKeys.Settings.Plugins.SettingsButton), Tag = plugin.Id };
            settingsButton.Click += (_, _) => showDetail(plugin.Id);
            actions.Children.Add(settingsButton);
        }

        var toggle = new ToggleSwitch { IsChecked = enabled, VerticalAlignment = VerticalAlignment.Center };
        toggle.IsCheckedChanged += (_, _) => _ = ctx.Plugins.SetEnabledAsync(plugin.Id, toggle.IsChecked == true, CancellationToken.None);
        actions.Children.Add(toggle);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        border.Child = grid;
        return border;
    }

    private static Control BuildMarketplace(SettingsWindowContext ctx)
    {
        var view = new MarketplaceSectionView(ctx.Marketplace, ctx.Localizer, ctx.Surfaces, ctx.Owner, ctx.Settings.Current.Plugins.Channel);
        view.Refresh();
        return Section(ctx.Localizer.Get(LocKeys.Settings.Marketplace.Title), null, view);
    }

    private static Control BuildAdvanced(SettingsWindowContext ctx)
    {
        var settings = ctx.Settings.Current;
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar");
        var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluidBar");
        var status = new TextBlock { FontSize = Typography.SettingsCaptionSize, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        void SetStatus(string text)
        {
            status.Text = text;
            ctx.ShowStatus(text);
        }

        var scope = new ComboBox { Width = 220 };
        scope.Items.Add(new ComboBoxItem { Content = ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetAppearance), Tag = "appearance" });
        scope.Items.Add(new ComboBoxItem { Content = ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetPluginsAndCaches), Tag = "plugins" });
        scope.Items.Add(new ComboBoxItem { Content = ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetEverything), Tag = "all" });
        scope.SelectedIndex = 0;
        var confirm = new TextBox { Watermark = ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetConfirmPlaceholder), Width = 180 };
        var resetButton = new Button { Content = ctx.Localizer.Get(LocKeys.Settings.Advanced.Reset), IsEnabled = false };
        confirm.TextChanged += (_, _) => resetButton.IsEnabled = string.Equals(confirm.Text, "RESET", StringComparison.Ordinal);
        resetButton.Click += (_, _) =>
        {
            if (!resetButton.IsEnabled)
            {
                return;
            }

            var selected = scope.SelectedItem is ComboBoxItem { Tag: string token } ? token : "appearance";
            var defaults = AppSettings.Default with
            {
                Language = settings.Language,
                Theme = settings.Theme,
                Advanced = settings.Advanced with { SafeMode = selected == "all" ? false : settings.Advanced.SafeMode },
            };
            ctx.Settings.Replace(defaults);
            SetStatus(ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetDone));
        };

        var exportButton = new Button { Content = ctx.Localizer.Get(LocKeys.Settings.Advanced.Export) };
        exportButton.Click += (_, _) => _ = ExportAsync();
        var importButton = new Button { Content = ctx.Localizer.Get(LocKeys.Settings.Advanced.Import) };
        importButton.Click += (_, _) => _ = ImportAsync();

        return Section(
            ctx.Localizer.Get(LocKeys.Settings.Advanced.Title),
            null,
            ButtonRow(ctx.Localizer.Get(LocKeys.Settings.Advanced.DataFolder), appData, ctx.Localizer.Get(LocKeys.Settings.Advanced.OpenFolder),
                () => ctx.OpenPath(appData)),
            ButtonRow(ctx.Localizer.Get(LocKeys.Settings.Advanced.Logs), Path.Combine(localData, "logs"), ctx.Localizer.Get(LocKeys.Settings.Advanced.ViewLogs),
                () => ctx.OpenPath(Path.Combine(localData, "logs"))),
            Row(ctx.Localizer.Get(LocKeys.Settings.Advanced.Export), null, exportButton),
            Row(ctx.Localizer.Get(LocKeys.Settings.Advanced.Import), null, importButton),
            ToggleRow(ctx.Localizer.Get(LocKeys.Settings.Advanced.FrameStats), null, settings.Advanced.FrameStats,
                value => ctx.Settings.Update(s => s with { Advanced = s.Advanced with { FrameStats = value } })),
            ToggleRow(ctx.Localizer.Get(LocKeys.Settings.Advanced.CrashGuard), null, settings.Advanced.CrashGuard,
                value => ctx.Settings.Update(s => s with { Advanced = s.Advanced with { CrashGuard = value } })),
            ToggleRow(ctx.Localizer.Get(LocKeys.Settings.Advanced.SafeMode), ctx.Localizer.Get(LocKeys.Settings.Advanced.SafeModeHint), settings.Advanced.SafeMode,
                value => ctx.Settings.Update(s => s with { Advanced = s.Advanced with { SafeMode = value } })),
            Row(ctx.Localizer.Get(LocKeys.Settings.Advanced.Reset), ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetConfirmBody), scope),
            Row(ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetConfirmPlaceholder), null, confirm),
            Row(null, null, resetButton),
            status);

        async Task ExportAsync()
        {
            try
            {
                var folder = Path.Combine(appData, "backups");
                var path = Path.Combine(folder, $"fluidbar-settings-{DateTime.Now:yyyyMMdd-HHmm}.json");
                var json = ctx.Settings.Current.ToJson();
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(path, json);
                }).ConfigureAwait(true);
                SetStatus(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus(ex.Message);
            }
        }

        async Task ImportAsync()
        {
            try
            {
                var storage = ctx.Owner()?.StorageProvider;
                if (storage is null)
                {
                    SetStatus(ctx.Localizer.Get(LocKeys.Error.Generic));
                    return;
                }

                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = ctx.Localizer.Get(LocKeys.Settings.Advanced.Import),
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
                });
                var file = files.FirstOrDefault();
                if (file is null)
                {
                    return;
                }

                var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                var text = await reader.ReadToEndAsync();
                var parsed = AppSettings.TryParse(text, out var error);
                if (parsed is null)
                {
                    SetStatus(error ?? ctx.Localizer.Get(LocKeys.Error.Generic));
                    return;
                }

                ctx.Settings.Replace(parsed);
                SetStatus(ctx.Localizer.Get(LocKeys.Settings.Advanced.Import));
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }
    }

    private static Control BuildAbout(SettingsWindowContext ctx)
    {
        var assembly = typeof(AppSettings).Assembly;
        var version = assembly.GetName().Version?.ToString(3) ?? "2.0.0";
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;
        var updates = new TextBlock { FontSize = Typography.SettingsCaptionSize, Foreground = new SolidColorBrush(Accents.Success) };
        var check = new Button { Content = ctx.Localizer.Get(LocKeys.Settings.About.CheckForUpdates) };
        check.Click += (_, _) => updates.Text = ctx.Localizer.Get(LocKeys.Settings.About.UpToDate);

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var website = new Button { Content = ctx.Localizer.Get(LocKeys.Settings.About.Website) };
        website.Click += (_, _) => ctx.OpenPath("https://fluidbar.app");
        var source = new Button { Content = ctx.Localizer.Get(LocKeys.Settings.About.SourceCode) };
        source.Click += (_, _) => ctx.OpenPath("https://github.com/Doulor/FluidBar");
        links.Children.Add(website);
        links.Children.Add(source);

        var mark = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.Parse("#08080A")),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = new MarkGlyph(),
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        header.Children.Add(mark);
        var title = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock
        {
            Text = "FluidBar",
            FontFamily = Fonts.DisplaySemiBold.FontFamily,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
        });
        title.Children.Add(new TextBlock
        {
            Text = ctx.Localizer.Format(LocKeys.Settings.About.Version, ("version", version)),
            FontSize = Typography.SettingsCaptionSize,
            Opacity = 0.7,
        });
        title.Children.Add(new TextBlock
        {
            Text = ctx.Localizer.Format(LocKeys.Settings.About.Build, ("build", informational)),
            FontSize = 10,
            Opacity = 0.5,
        });
        header.Children.Add(title);

        return Section(
            ctx.Localizer.Get(LocKeys.Settings.About.Title),
            null,
            header,
            Row(ctx.Localizer.Get(LocKeys.Settings.About.License), null, check),
            updates,
            Row(ctx.Localizer.Get(LocKeys.Settings.About.Contributors), "Doulor", new TextBlock
            {
                Text = "MIT · 2026",
                FontSize = Typography.SettingsCaptionSize,
                Opacity = 0.7,
            }),
            Row(ctx.Localizer.Get(LocKeys.Settings.About.Acknowledgments), null, links));
    }

    /// <summary>Draws the app mark glyph (vector, no emoji/bitmap).</summary>
    private sealed class MarkGlyph : Control
    {
        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height).Deflate(8);
            Icons.Draw(context, IconKind.Agent, bounds, new SolidColorBrush(Color.Parse("#4CC9F0")));
        }
    }
}
