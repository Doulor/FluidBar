using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluidBar.App.Controls;
using FluidBar.Core.Settings;
using FluidBar.App.Views.Plugins;
using FluidBar.App.Localization;
using Avalonia.Threading;

namespace FluidBar.App.Views.Settings;

/// <summary>Privacy switches and the explicit quick-action editor inside General settings.</summary>
public static class CardSettingsPanel
{
    private static readonly string[] Kinds = ["theme", "mute", "clipboard-pause", "settings", "sound", "open", "plugin"];

    public static Control Build(SettingsWindowContext context)
    {
        var palette = context.Palette;
        string L(string key) => context.Localizer.Get("Settings.Cards." + key);
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 20, 0, 0) };
        panel.Children.Add(SettingsWidgets.Caption(L("Privacy"), palette));
        panel.Children.Add(PrivacySwitch("Pause", context.Settings.Current.ClipboardPaused,
            value => context.Settings.Update(settings => settings with { ClipboardPaused = value })));
        panel.Children.Add(PrivacySwitch("Hide", context.Settings.Current.ClipboardHidePreviews,
            value => context.Settings.Update(settings => settings with { ClipboardHidePreviews = value })));
        var privacyActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(privacyActions);
        panel.Children.Add(SettingsWidgets.Caption(L("Actions"), palette));
        panel.Children.Add(new TextBlock { Text = L("ActionsHint"), Foreground = palette.SecondaryBrush,
            TextWrapping = TextWrapping.Wrap, FontSize = SettingsMetrics.RowDescSize });
        var rows = new StackPanel { Spacing = 8 };
        panel.Children.Add(rows);
        var name = new TextBox { PlaceholderText = L("Name"), MaxLength = 48,
            Foreground = palette.ForegroundBrush, WatermarkForeground = palette.ForegroundBrush,
            Background = palette.FieldBrush, BorderBrush = palette.LineBrush };
        var choices = Kinds.Select(kind => L("Kind." + kind)).ToArray();
        var kind = new ComboBox { ItemsSource = choices, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = palette.ForegroundBrush, Background = palette.FieldBrush };
        var target = new TextBox { PlaceholderText = L("Target"), MaxLength = 2048, IsVisible = false,
            Foreground = palette.ForegroundBrush, WatermarkForeground = palette.ForegroundBrush,
            Background = palette.FieldBrush, BorderBrush = palette.LineBrush };
        var pluginCommand = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false,
            Foreground = palette.ForegroundBrush, Background = palette.FieldBrush };
        var pluginCommands = new List<(string Label, string Target)>();
        string? selectedPluginTarget = null;
        AutomationProperties.SetName(pluginCommand, L("PluginCommand"));
        var status = new TextBlock { Foreground = palette.SecondaryBrush, TextWrapping = TextWrapping.Wrap };
        var running = false;
        privacyActions.Children.Add(SettingsControls.Button(L("Clear"), palette, onClick: () =>
            _ = RunAsync(new QuickActionSetting { Id = "clear", Name = L("Clear"), Kind = "clipboard-clear" })));
        var armedUntil = DateTimeOffset.MinValue;
        var clearAll = SettingsControls.Button(L("ClearAll"), palette, SettingsButtonVariant.Danger);
        clearAll.Click += (_, _) =>
        {
            if (DateTimeOffset.UtcNow > armedUntil)
            {
                armedUntil = DateTimeOffset.UtcNow.AddSeconds(5);
                clearAll.Content = L("ConfirmClear");
                DispatcherTimer.RunOnce(() => { armedUntil = DateTimeOffset.MinValue; clearAll.Content = L("ClearAll"); }, TimeSpan.FromSeconds(5));
                return;
            }
            armedUntil = DateTimeOffset.MinValue;
            clearAll.Content = L("ClearAll");
            _ = RunAsync(new QuickActionSetting { Id = "clear-all", Name = L("ClearAll"), Kind = "clipboard-clear-all" });
        };
        privacyActions.Children.Add(clearAll);
        AutomationProperties.SetAutomationId(name, "cards.action.name");
        AutomationProperties.SetAutomationId(kind, "cards.action.kind");
        AutomationProperties.SetAutomationId(target, "cards.action.target");
        AutomationProperties.SetName(name, L("Name"));
        AutomationProperties.SetName(kind, L("Action"));
        AutomationProperties.SetName(target, L("Target"));
        string? editingId = null;
        kind.SelectionChanged += (_, _) =>
        {
            target.IsVisible = kind.SelectedIndex == 5;
            pluginCommand.IsVisible = kind.SelectedIndex == 6;
        };
        panel.Children.Add(name);
        panel.Children.Add(kind);
        panel.Children.Add(target);
        panel.Children.Add(pluginCommand);
        panel.Children.Add(SettingsControls.Button(L("Save"), palette, SettingsButtonVariant.Solid,
            SettingsButtonSize.Md, SettingsIconKind.Check, () =>
            {
                if (kind.SelectedIndex < 0 || kind.SelectedIndex >= Kinds.Length)
                {
                    status.Text = L("Invalid");
                    return;
                }

                var action = new QuickActionSetting
                {
                    Id = editingId ?? Guid.NewGuid().ToString("N"), Name = name.Text ?? string.Empty,
                    Kind = Kinds[kind.SelectedIndex], Target = kind.SelectedIndex == 6
                        ? pluginCommand.SelectedIndex >= 0 ? pluginCommands[pluginCommand.SelectedIndex].Target : string.Empty
                        : target.Text ?? string.Empty,
                }.Normalize();
                if (action.Validate() is not null || (editingId is null && context.Settings.Current.QuickActions.Count >= 16))
                {
                    status.Text = L("Invalid");
                    return;
                }

                var actions = context.Settings.Current.QuickActions.ToList();
                var index = actions.FindIndex(item => item.Id == action.Id);
                if (index >= 0) { actions[index] = action; } else { actions.Add(action); }
                context.Settings.Update(settings => settings with { QuickActions = actions });
                editingId = null;
                name.Text = string.Empty;
                target.Text = string.Empty;
                status.Text = L("Saved");
                RebuildRows();
            }));
        panel.Children.Add(status);
        RebuildRows();
        _ = LoadPluginCommandsAsync();
        return panel;

        Control PrivacySwitch(string key, bool value, Action<bool> changed)
        {
            var toggle = new SettingsSwitch { IsOn = value, OffTrack = palette.RaisedHoverBrush,
                OffHairline = palette.LineStrongBrush, OffKnob = palette.SecondaryBrush };
            AutomationProperties.SetName(toggle, L(key));
            AutomationProperties.SetAutomationId(toggle, "cards.privacy." + key.ToLowerInvariant());
            toggle.Toggled += changed;
            void Sync(FluidBar.App.Settings.AppSettings previous, FluidBar.App.Settings.AppSettings current)
                => Dispatcher.UIThread.Post(() => toggle.IsOn = key == "Pause" ? current.ClipboardPaused : current.ClipboardHidePreviews);
            toggle.AttachedToVisualTree += (_, _) => context.Settings.Changed += Sync;
            toggle.DetachedFromVisualTree += (_, _) => context.Settings.Changed -= Sync;
            return SettingsWidgets.Row(L(key), L(key + "Hint"), toggle, palette);
        }

        void RebuildRows()
        {
            rows.Children.Clear();
            if (context.Settings.Current.QuickActions.Count == 0)
            {
                rows.Children.Add(new TextBlock { Text = L("Empty"), Foreground = palette.TertiaryBrush });
            }

            foreach (var action in context.Settings.Current.QuickActions)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 8 };
                row.Children.Add(new TextBlock { Text = action.Name, Foreground = palette.ForegroundBrush,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                var run = SettingsControls.Button(L("Run"), palette, SettingsButtonVariant.Raised,
                    SettingsButtonSize.Sm, SettingsIconKind.ArrowRight, () => _ = RunAsync(action));
                Grid.SetColumn(run, 1);
                row.Children.Add(run);
                var edit = SettingsControls.Button(L("Edit"), palette, SettingsButtonVariant.Ghost,
                    SettingsButtonSize.Sm, SettingsIconKind.Settings, () =>
                    {
                        editingId = action.Id;
                        name.Text = action.Name;
                        kind.SelectedIndex = Array.IndexOf(Kinds, action.Kind);
                        target.Text = action.Target;
                        selectedPluginTarget = action.Target;
                        pluginCommand.SelectedIndex = pluginCommands.FindIndex(item => item.Target == action.Target);
                        name.Focus();
                    });
                Grid.SetColumn(edit, 2);
                row.Children.Add(edit);
                var remove = SettingsControls.Button(L("Remove"), palette, SettingsButtonVariant.Ghost,
                    SettingsButtonSize.Sm, SettingsIconKind.Trash, () =>
                    {
                        context.Settings.Update(settings => settings with
                        { QuickActions = settings.QuickActions.Where(item => item.Id != action.Id).ToArray() });
                        if (editingId == action.Id) { editingId = null; name.Text = string.Empty; target.Text = string.Empty; }
                        RebuildRows();
                    });
                Grid.SetColumn(remove, 3);
                row.Children.Add(remove);
                rows.Children.Add(row);
            }
        }

        async Task RunAsync(QuickActionSetting action)
        {
            if (running) return;
            running = true;
            status.Text = L("Working");
            try
            {
                if (context.ExecuteQuickAction is null) { throw new InvalidOperationException("Host unavailable."); }
                await context.ExecuteQuickAction(action, CancellationToken.None);
                status.Text = L("Done");
            }
            catch (Exception)
            {
                status.Text = L("Failed");
            }
            finally { running = false; }
        }

        async Task LoadPluginCommandsAsync()
        {
            if (context.Plugins is not IPluginContributionUiService contributions) return;
            try
            {
                var plugins = await context.Plugins.ListAsync(CancellationToken.None);
                foreach (var plugin in plugins.Where(plugin => plugin.IsEnabled && plugin.Available && !plugin.Orphan))
                {
                    foreach (var command in contributions.Commands(plugin.Id))
                    {
                        var label = command.Title is null ? command.Id : LocalizedValue.Resolve(command.Title, context.Localizer);
                        pluginCommands.Add((plugin.Name + " · " + label, plugin.Id + "/" + command.Id));
                    }
                }
                pluginCommand.ItemsSource = pluginCommands.Select(item => item.Label).ToArray();
                pluginCommand.PlaceholderText = L("PluginCommand");
                if (selectedPluginTarget is not null)
                    pluginCommand.SelectedIndex = pluginCommands.FindIndex(item => item.Target == selectedPluginTarget);
            }
            catch (Exception) { status.Text = L("PluginUnavailable"); }
        }
    }
}
