using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.App.Theme;
using FluidBar.Plugins.Builtin;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Manifest;

namespace FluidBar.App.Views.Plugins;

/// <summary>Reads host-owned plugin setting values for generic settings-page rendering.</summary>
public interface IPluginSettingsValues
{
    /// <summary>Returns a JSON value for a plugin control key, or null when the plugin has not written it.</summary>
    JsonElement? Get(string pluginId, string key);

    /// <summary>Stores a value (host-owned store; the plugin sees it through <c>IPluginSettings</c>).</summary>
    void Set(string pluginId, string key, JsonElement value);
}

/// <summary>In-memory value store used by design/headless previews without a plugin host.</summary>
public sealed class InMemoryPluginSettingsValues : IPluginSettingsValues
{
    private readonly Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public JsonElement? Get(string pluginId, string key)
        => _values.TryGetValue(pluginId + "\u001F" + key, out var value) ? value : null;

    /// <inheritdoc />
    public void Set(string pluginId, string key, JsonElement value)
        => _values[pluginId + "\u001F" + key] = value;
}

/// <summary>
/// App-facing surface for declarative plugin contributions (settings pages/commands/capabilities).
/// Production adapter wraps the frozen SDK descriptors from <c>BuiltinPluginCatalog</c> and (later)
/// <c>PluginHost</c> contributions; the renderer never accepts plugin XAML.
/// </summary>
public interface IPluginContributionUiService : IPluginUiService
{
    /// <summary>Localized display name of the plugin descriptor.</summary>
    LocalizedString? DisplayName(string pluginId);

    /// <summary>Declarative settings pages of the plugin.</summary>
    IReadOnlyList<SettingsPageContribution> SettingsPages(string pluginId);

    /// <summary>Declared commands of the plugin.</summary>
    IReadOnlyList<CommandContribution> Commands(string pluginId);

    /// <summary>Declared capabilities (disclosure, not enforcement).</summary>
    IReadOnlyList<PluginCapability> Capabilities(string pluginId);

    /// <summary>Host-owned values storage for the generic settings page controls.</summary>
    IPluginSettingsValues Values { get; }

    /// <summary>Invokes a declared command by id through the host (failure is logged, never thrown at the UI).</summary>
    Task InvokeCommandAsync(string pluginId, string commandId);
}

/// <summary>Declarative contribution UI service over the first-party plugin catalog.</summary>
public sealed class BuiltinPluginContributionUiService : IPluginContributionUiService
{
    private readonly SettingsController _settings;
    private readonly ILocalizer _localizer;
    private readonly Dictionary<string, BuiltinPluginDescriptor> _descriptors;

    /// <summary>Creates the service.</summary>
    public BuiltinPluginContributionUiService(SettingsController settings, ILocalizer localizer)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _descriptors = BuiltinPluginCatalog.Descriptors.ToDictionary(static d => d.Id, StringComparer.Ordinal);
        Values = new InMemoryPluginSettingsValues();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IPluginSettingsValues Values { get; }

    /// <summary>Last command invoked (diagnostics/tests).</summary>
    public string? LastInvokedCommand { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<PluginListItem>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var enabled = _settings.Current.Plugins.Enabled;
        var list = BuiltinPluginCatalog.Descriptors
            .Select(descriptor => new PluginListItem(
                descriptor.Id,
                LocalizedValue.Resolve(descriptor.DisplayName, _localizer),
                "2.0.0",
                "FluidBar",
                Builtin: true,
                descriptor.Capabilities.Select(static capability => $"Capability.{capability}").ToList(),
                Orphan: !enabled.Contains(descriptor.Id) && false))
            .ToList();
        return Task.FromResult<IReadOnlyList<PluginListItem>>(list);
    }

    /// <inheritdoc />
    public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _settings.Update(current =>
        {
            var set = current.Plugins.Enabled.ToList();
            if (enabled && !set.Contains(pluginId))
            {
                set.Add(pluginId);
            }
            else if (!enabled)
            {
                set.Remove(pluginId);
            }

            return current with { Plugins = current.Plugins with { Enabled = set } };
        });
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public LocalizedString? DisplayName(string pluginId)
        => _descriptors.TryGetValue(pluginId, out var descriptor) ? descriptor.DisplayName : null;

    /// <inheritdoc />
    public IReadOnlyList<SettingsPageContribution> SettingsPages(string pluginId)
        => _descriptors.TryGetValue(pluginId, out var descriptor)
            ? descriptor.Contributions.SettingsPages ?? []
            : [];

    /// <inheritdoc />
    public IReadOnlyList<CommandContribution> Commands(string pluginId)
        => _descriptors.TryGetValue(pluginId, out var descriptor)
            ? descriptor.Contributions.Commands ?? []
            : [];

    /// <inheritdoc />
    public IReadOnlyList<PluginCapability> Capabilities(string pluginId)
        => _descriptors.TryGetValue(pluginId, out var descriptor) ? descriptor.Capabilities : [];

    /// <inheritdoc />
    public Task InvokeCommandAsync(string pluginId, string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        LastInvokedCommand = commandId;
        return Task.CompletedTask;
    }
}

/// <summary>Resolves SDK <see cref="LocalizedString"/> values through the host localizer (with manifest fallbacks).</summary>
public static class LocalizedValue
{
    /// <summary>Resolves a manifest localizable string; returns the key itself when nothing matches.</summary>
    public static string Resolve(LocalizedString? value, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        if (value is null || value.IsEmpty)
        {
            return string.Empty;
        }

        if (value.Translations.Count > 0)
        {
            foreach (var culture in new[] { localizer.Culture, "zh-Hans", "en-US" })
            {
                if (value.Translations.TryGetValue(culture, out var translated))
                {
                    return translated;
                }

                foreach (var pair in value.Translations)
                {
                    if (culture.StartsWith(pair.Key.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                    {
                        return pair.Value;
                    }
                }
            }

            return value.Translations.Values.First();
        }

        var key = value.Value ?? string.Empty;
        return localizer.TryGet(key, out var localized) ? localized : key;
    }
}

/// <summary>
/// Generic renderer for declarative plugin settings pages (frozen plugin contract, 2026-10-18):
/// loose controls first, then groups; no plugin XAML. Secrets are redacted; buttons require a declared
/// command; status rows are read-only host-rendered values.
/// </summary>
public static class PluginSettingsPageRenderer
{
    /// <summary>Builds one settings page for <paramref name="pluginId"/>.</summary>
    public static Control BuildPage(
        SettingsPageContribution page,
        string pluginId,
        ILocalizer localizer,
        IPluginSettingsValues values,
        Action<string, string> invokeCommand,
        Action<string?> openUrl)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(invokeCommand);
        ArgumentNullException.ThrowIfNull(openUrl);

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 12) };
        panel.Children.Add(new TextBlock
        {
            Text = LocalizedValue.Resolve(page.Title, localizer),
            FontFamily = Fonts.DisplaySemiBold.FontFamily,
            FontSize = Typography.SettingsBodySize + 1,
            FontWeight = FontWeight.SemiBold,
        });
        var description = LocalizedValue.Resolve(page.Description, localizer);
        if (!string.IsNullOrEmpty(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = Typography.SettingsCaptionSize,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        foreach (var control in page.Controls ?? [])
        {
            panel.Children.Add(BuildControl(pluginId, control, localizer, values, invokeCommand, openUrl));
        }

        foreach (var group in page.Groups ?? [])
        {
            var groupPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            var groupTitle = LocalizedValue.Resolve(group.Title, localizer);
            if (!string.IsNullOrEmpty(groupTitle))
            {
                groupPanel.Children.Add(new TextBlock
                {
                    Text = groupTitle,
                    FontSize = Typography.SettingsCaptionSize,
                    FontWeight = FontWeight.SemiBold,
                    Opacity = 0.8,
                });
            }

            var groupDescription = LocalizedValue.Resolve(group.Description, localizer);
            if (!string.IsNullOrEmpty(groupDescription))
            {
                groupPanel.Children.Add(new TextBlock { Text = groupDescription, FontSize = 10, Opacity = 0.55, TextWrapping = TextWrapping.Wrap });
            }

            foreach (var control in group.Controls ?? [])
            {
                groupPanel.Children.Add(BuildControl(pluginId, control, localizer, values, invokeCommand, openUrl));
            }

            panel.Children.Add(groupPanel);
        }

        return panel;
    }

    private static Control BuildControl(
        string pluginId,
        SettingControlContribution control,
        ILocalizer localizer,
        IPluginSettingsValues values,
        Action<string, string> invokeCommand,
        Action<string?> openUrl)
    {
        var title = LocalizedValue.Resolve(control.Title, localizer);
        var description = LocalizedValue.Resolve(control.Description, localizer);
        var current = values.Get(pluginId, control.Key) ?? control.Default;
        var editor = CreateEditor(pluginId, control, current, values);

        if (control.Type is SettingControlType.Info or SettingControlType.Status or SettingControlType.Link)
        {
            return BuildReadonlyRow(control, title, description, current, localizer, openUrl);
        }

        if (control.Type == SettingControlType.Button)
        {
            var button = new Button
            {
                Content = string.IsNullOrEmpty(title) ? control.Key : title,
                IsEnabled = !string.IsNullOrWhiteSpace(control.Command),
            };
            button.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(control.Command))
                {
                    invokeCommand(pluginId, control.Command);
                }
            };
            return button;
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 34 };
        var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(title) ? control.Key : title, FontSize = Typography.SettingsBodySize });
        if (!string.IsNullOrEmpty(description))
        {
            info.Children.Add(new TextBlock { Text = description, FontSize = Typography.SettingsCaptionSize, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        }

        Grid.SetColumn(info, 0);
        grid.Children.Add(info);
        editor.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private static Control CreateEditor(string pluginId, SettingControlContribution control, JsonElement? current, IPluginSettingsValues values)
    {
        switch (control.Type)
        {
            case SettingControlType.Toggle:
            {
                var toggle = new ToggleSwitch { IsChecked = ReadBool(current), OnContent = string.Empty, OffContent = string.Empty };
                toggle.IsCheckedChanged += (_, _) => values.Set(pluginId, control.Key, JsonSerializer.SerializeToElement(toggle.IsChecked == true));
                return toggle;
            }

            case SettingControlType.Select:
            {
                var combo = new ComboBox { Width = 200 };
                var selectedIndex = 0;
                var options = control.Options ?? [];
                for (var i = 0; i < options.Count; i++)
                {
                    combo.Items.Add(new ComboBoxItem { Content = LocalizedValue.Resolve(options[i].Label, LocalizationRuntime.Current), Tag = options[i].Value });
                    if (string.Equals(ReadString(current), options[i].Value, StringComparison.Ordinal))
                    {
                        selectedIndex = i;
                    }
                }

                combo.SelectedIndex = selectedIndex;
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is ComboBoxItem { Tag: string value })
                    {
                        values.Set(pluginId, control.Key, JsonSerializer.SerializeToElement(value));
                    }
                };
                return combo;
            }

            case SettingControlType.Slider:
            case SettingControlType.Number:
            {
                var number = ReadDouble(current) ?? control.Min ?? 0;
                if (control.Type == SettingControlType.Slider)
                {
                    var slider = new Slider
                    {
                        Width = 200,
                        Minimum = control.Min ?? 0,
                        Maximum = control.Max ?? 100,
                        TickFrequency = control.Step ?? 1,
                        IsSnapToTickEnabled = control.Step is > 0,
                        Value = number,
                    };
                    slider.PropertyChanged += (_, e) =>
                    {
                        if (e.Property == Slider.ValueProperty)
                        {
                            values.Set(pluginId, control.Key, JsonSerializer.SerializeToElement(slider.Value));
                        }
                    };
                    return slider;
                }

                var box = new TextBox { Text = number.ToString(System.Globalization.CultureInfo.CurrentCulture), Width = 120 };
                box.LostFocus += (_, _) =>
                {
                    if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var parsed))
                    {
                        values.Set(pluginId, control.Key, JsonSerializer.SerializeToElement(parsed));
                    }
                };
                return box;
            }

            case SettingControlType.Secret:
            {
                // Secrets are redacted in the UI; the plugin reads them through IPluginSettings.
                var secret = new TextBox { Text = "••••••••", IsReadOnly = true, Width = 180 };
                secret.IsEnabled = false;
                return secret;
            }

            default:
            {
                var text = new TextBox { Text = ReadString(current) ?? string.Empty, Width = 220 };
                text.LostFocus += (_, _) => values.Set(pluginId, control.Key, JsonSerializer.SerializeToElement(text.Text ?? string.Empty));
                return text;
            }
        }
    }

    private static Control BuildReadonlyRow(
        SettingControlContribution control,
        string title,
        string description,
        JsonElement? current,
        ILocalizer localizer,
        Action<string?> openUrl)
    {
        if (control.Type == SettingControlType.Link)
        {
            var url = ReadString(current);
            var link = new Button { Content = string.IsNullOrEmpty(title) ? (url ?? control.Key) : title };
            link.Click += (_, _) => openUrl(url);
            return link;
        }

        var text = control.Type == SettingControlType.Status
            ? FormatStatus(current, control)
            : ReadString(current) ?? string.Empty;
        if (!string.IsNullOrEmpty(control.Unit) && !string.IsNullOrEmpty(text))
        {
            text = $"{text} {control.Unit}";
        }

        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(title) ? control.Key : title, FontSize = Typography.SettingsBodySize });
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(text) ? "—" : text,
            FontSize = Typography.SettingsCaptionSize,
            Foreground = new SolidColorBrush(Accents.Info),
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrEmpty(description))
        {
            panel.Children.Add(new TextBlock { Text = description, FontSize = 10, Opacity = 0.55 });
        }

        return panel;
    }

    private static string FormatStatus(JsonElement? value, SettingControlContribution control)
    {
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        var format = control.Format ?? SettingStatusFormat.Text;
        var element = value.Value;
        return format switch
        {
            SettingStatusFormat.Number when element.TryGetDouble(out var number) => number.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture),
            SettingStatusFormat.Boolean => element.ValueKind == JsonValueKind.True ? "true" : "false",
            SettingStatusFormat.Timestamp when element.TryGetDateTimeOffset(out var timestamp) => timestamp.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture),
            _ => element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString(),
        };
    }

    private static bool ReadBool(JsonElement? value) => value is { } element && element.ValueKind == JsonValueKind.True;

    private static double? ReadDouble(JsonElement? value)
        => value is { } element && element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number) ? number : null;

    private static string? ReadString(JsonElement? value)
        => value is { } element && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
