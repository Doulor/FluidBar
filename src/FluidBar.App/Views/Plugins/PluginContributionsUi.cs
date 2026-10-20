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
/// <para>
/// Every row, editor, chip and button comes from the shared settings control layer, so a plugin page
/// looks exactly like the General/Appearance panes (same 53 px row rhythm, same switch, same field
/// chrome, same caption/section treatment).
/// </para>
/// </summary>
public static class PluginSettingsPageRenderer
{
    private const string SecretMask = "••••••••";

    /// <summary>Builds one settings page for <paramref name="pluginId"/>.</summary>
    /// <param name="page">Declarative page.</param>
    /// <param name="pluginId">Owning plugin id (settings value namespace).</param>
    /// <param name="localizer">Localizer for titles/descriptions.</param>
    /// <param name="values">Host-owned value store.</param>
    /// <param name="invokeCommand">Invokes a declared command (id, commandId).</param>
    /// <param name="openUrl">Opens a link row target.</param>
    /// <param name="palette">Pane palette; defaults to the dark prototype palette.</param>
    /// <param name="accent">Window accent (switch track), matching the other panes.</param>
    public static Control BuildPage(
        SettingsPageContribution page,
        string pluginId,
        ILocalizer localizer,
        IPluginSettingsValues values,
        Action<string, string> invokeCommand,
        Action<string?> openUrl,
        SettingsPalette? palette = null,
        Color? accent = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(invokeCommand);
        ArgumentNullException.ThrowIfNull(openUrl);

        var theme = palette ?? SettingsPalette.For(AppTheme.Dark);
        var themeAccent = accent ?? theme.Active;
        var panel = new StackPanel { Spacing = 0, Margin = new Thickness(0, 14, 0, 0) };

        var title = LocalizedValue.Resolve(page.Title, localizer);
        if (!string.IsNullOrEmpty(title))
        {
            panel.Children.Add(SettingsWidgets.Caption(title, theme));
        }

        var description = LocalizedValue.Resolve(page.Description, localizer);
        if (!string.IsNullOrEmpty(description))
        {
            var hint = SettingsControls.StatusLine(description, theme, SettingsTone.Neutral,
                SettingsMetrics.RowDescSize);
            hint.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(hint);
        }

        var rows = (page.Controls ?? [])
            .Select(control => BuildControl(pluginId, control, localizer, values, invokeCommand, openUrl, theme, themeAccent))
            .ToList();
        if (rows.Count > 0)
        {
            panel.Children.Add(SettingsWidgets.Group(theme, topBorder: true, [.. rows]));
        }

        foreach (var group in page.Groups ?? [])
        {
            var groupTitle = LocalizedValue.Resolve(group.Title, localizer);
            var groupDescription = LocalizedValue.Resolve(group.Description, localizer);
            var groupRows = (group.Controls ?? [])
                .Select(control => BuildControl(pluginId, control, localizer, values, invokeCommand, openUrl, theme, themeAccent))
                .ToList();
            if (groupRows.Count == 0)
            {
                continue;
            }

            var header = !string.IsNullOrEmpty(groupTitle) || !string.IsNullOrEmpty(groupDescription);
            if (!string.IsNullOrEmpty(groupTitle))
            {
                panel.Children.Add(SettingsWidgets.Caption(groupTitle, theme));
            }

            if (!string.IsNullOrEmpty(groupDescription))
            {
                var hint = SettingsControls.StatusLine(groupDescription, theme, SettingsTone.Neutral,
                    SettingsMetrics.RowDescSize);
                hint.Margin = new Thickness(0, 0, 0, 4);
                panel.Children.Add(hint);
            }

            panel.Children.Add(SettingsWidgets.Group(theme, topBorder: !header, [.. groupRows]));
        }

        if (panel.Children.Count == 0)
        {
            panel.Children.Add(SettingsControls.EmptyState(
                localizer.Get(LocKeys.Settings.Plugins.DetailEmpty), theme));
        }

        return panel;
    }

    private static Control BuildControl(
        string pluginId,
        SettingControlContribution control,
        ILocalizer localizer,
        IPluginSettingsValues values,
        Action<string, string> invokeCommand,
        Action<string?> openUrl,
        SettingsPalette palette,
        Color accent)
    {
        ArgumentNullException.ThrowIfNull(control);
        var title = LocalizedValue.Resolve(control.Title, localizer);
        var label = string.IsNullOrEmpty(title) ? control.Key : title;
        var description = LocalizedValue.Resolve(control.Description, localizer);
        var current = values.Get(pluginId, control.Key) ?? control.Default;

        switch (control.Type)
        {
            case SettingControlType.Toggle:
            {
                var toggle = new SettingsSwitch
                {
                    IsOn = ReadBool(current),
                    Accent = new SolidColorBrush(accent),
                    OffTrack = palette.RaisedHoverBrush,
                    OffHairline = palette.LineStrongBrush,
                    OffKnob = palette.SecondaryBrush,
                };
                toggle.Toggled += value => values.Set(pluginId, control.Key, JsonSerializer.SerializeToElement(value));
                return SettingsWidgets.Row(label, description, toggle, palette);
            }

            case SettingControlType.Select:
            {
                return SettingsWidgets.Row(label, description, BuildSelect(pluginId, control, current, values, localizer, palette), palette);
            }

            case SettingControlType.Slider:
            {
                return SettingsWidgets.Row(label, description, BuildSlider(pluginId, control, current, values, palette), palette);
            }

            case SettingControlType.Number:
            {
                var number = ReadDouble(current) ?? control.Min ?? 0;
                var field = SettingsControls.TextField(
                    palette,
                    SettingsFieldKind.Number,
                    number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    onCommit: text =>
                    {
                        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.CurrentCulture, out var parsed))
                        {
                            values.Set(pluginId, control.Key, JsonSerializer.SerializeToElement(parsed));
                        }
                    });
                return SettingsWidgets.Row(label, description, field, palette);
            }

            case SettingControlType.Secret:
            {
                // Secrets are redacted in the UI and are never bound to their value; the plugin reads
                // them through IPluginSettings.
                var secret = SettingsControls.TextField(palette, SettingsFieldKind.Secret, SecretMask);
                return SettingsWidgets.Row(label, description, secret, palette);
            }

            case SettingControlType.Path:
            {
                var path = SettingsControls.TextField(palette, SettingsFieldKind.Path, ReadString(current) ?? string.Empty);
                return SettingsWidgets.Row(label, description, path, palette);
            }

            case SettingControlType.Button:
            {
                var command = control.Command;
                var button = SettingsControls.Button(
                    localizer.Get(LocKeys.Settings.Plugins.DetailRun),
                    palette,
                    SettingsButtonVariant.Raised,
                    SettingsButtonSize.Sm,
                    SettingsIconKind.ArrowRight,
                    () =>
                    {
                        if (!string.IsNullOrWhiteSpace(command))
                        {
                            invokeCommand(pluginId, command);
                        }
                    },
                    enabled: !string.IsNullOrWhiteSpace(command));
                return SettingsWidgets.Row(label, description, button, palette);
            }

            case SettingControlType.Link:
            {
                var url = ReadString(current);
                var link = SettingsControls.IconButton(SettingsIconKind.External, palette, () => openUrl(url));
                ToolTip.SetTip(link, label);
                return SettingsWidgets.Row(label, description, link, palette);
            }

            case SettingControlType.Info:
            {
                var text = ReadString(current) ?? string.Empty;
                var value = string.IsNullOrEmpty(text)
                    ? SettingsControls.StatusLine("—", palette)
                    : SettingsControls.StatusLine(text, palette, SettingsTone.Info);
                return SettingsWidgets.Row(label, description, value, palette);
            }

            case SettingControlType.Status:
            {
                var text = FormatStatus(current, control);
                if (!string.IsNullOrEmpty(control.Unit) && !string.IsNullOrEmpty(text))
                {
                    text = text + " " + control.Unit;
                }

                var tone = string.IsNullOrEmpty(text) ? SettingsTone.Neutral : SettingsTone.Info;
                var value = SettingsControls.StatusLine(string.IsNullOrEmpty(text) ? "—" : text, palette, tone);
                return SettingsWidgets.Row(label, description, value, palette);
            }

            default:
            {
                var field = SettingsControls.TextField(
                    palette,
                    SettingsFieldKind.Text,
                    ReadString(current) ?? string.Empty,
                    width: 220,
                    onCommit: text => values.Set(pluginId, control.Key, JsonSerializer.SerializeToElement(text)));
                return SettingsWidgets.Row(label, description, field, palette);
            }
        }
    }

    private static Control BuildSelect(
        string pluginId,
        SettingControlContribution control,
        JsonElement? current,
        IPluginSettingsValues values,
        ILocalizer localizer,
        SettingsPalette palette)
    {
        var options = control.Options ?? [];
        var items = new List<(string Label, string Value)>(options.Count);
        foreach (var option in options)
        {
            items.Add((LocalizedValue.Resolve(option.Label, localizer), option.Value));
        }

        return SettingsControls.Select(
            items,
            ReadString(current) ?? string.Empty,
            palette,
            value => values.Set(pluginId, control.Key, JsonSerializer.SerializeToElement(value)));
    }

    private static Control BuildSlider(
        string pluginId,
        SettingControlContribution control,
        JsonElement? current,
        IPluginSettingsValues values,
        SettingsPalette palette)
    {
        var slider = new SettingsSlider
        {
            Minimum = control.Min ?? 0,
            Maximum = control.Max ?? 100,
            Step = control.Step ?? 1,
            Value = ReadDouble(current) ?? control.Min ?? 0,
            Fill = palette.ForegroundBrush,
            Track = palette.RaisedHoverBrush,
            Width = SettingsMetrics.RangeWidth - SettingsMetrics.RangeGap - SettingsMetrics.ValueTagMinWidth,
        };
        var format = string.IsNullOrEmpty(control.Unit)
            ? (Func<double, string>)(value => value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture))
            : value => value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + " " + control.Unit;
        var tag = SettingsControls.Chip(format(slider.Value), palette, SettingsChipKind.Tag);
        var tagLabel = tag.Label;
        slider.ValueChanged += value =>
        {
            tagLabel.Text = format(value);
            values.Set(pluginId, control.Key, JsonSerializer.SerializeToElement(value));
        };
        return SettingsControls.Range(slider, tag);
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
