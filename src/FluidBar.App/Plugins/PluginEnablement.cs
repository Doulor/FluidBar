using FluidBar.App.Settings;

namespace FluidBar.App.Plugins;

/// <summary>
/// Persisted per-plugin enablement. Disabling is a host-owned switch: it stops the plugin and hides its
/// contributions without deleting its files or settings.
/// </summary>
public interface IPluginEnablementStore
{
    /// <summary>Whether the plugin is enabled (defaults to enabled for unknown ids).</summary>
    bool IsEnabled(string pluginId);

    /// <summary>Persists the switch.</summary>
    void SetEnabled(string pluginId, bool enabled);
}

/// <summary>Enablement backed by the App settings controller (<c>Plugins.Enabled</c>).</summary>
public sealed class SettingsPluginEnablementStore : IPluginEnablementStore
{
    private readonly SettingsController _settings;

    /// <summary>Creates the store over the live settings controller.</summary>
    public SettingsPluginEnablementStore(SettingsController settings)
        => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <inheritdoc />
    public bool IsEnabled(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return _settings.Current.Plugins.Enabled.Contains(pluginId, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public void SetEnabled(string pluginId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        _settings.Update(current =>
        {
            var set = current.Plugins.Enabled.ToList();
            if (enabled && !set.Contains(pluginId, StringComparer.Ordinal))
            {
                set.Add(pluginId);
            }
            else if (!enabled)
            {
                set.RemoveAll(candidate => string.Equals(candidate, pluginId, StringComparison.Ordinal));
            }

            return current with { Plugins = current.Plugins with { Enabled = set } };
        });
    }
}

/// <summary>In-memory enablement used by tests and headless composition.</summary>
public sealed class InMemoryPluginEnablementStore : IPluginEnablementStore
{
    private readonly HashSet<string> _disabled = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsEnabled(string pluginId) => !_disabled.Contains(pluginId);

    /// <inheritdoc />
    public void SetEnabled(string pluginId, bool enabled)
    {
        if (enabled)
        {
            _disabled.Remove(pluginId);
        }
        else
        {
            _disabled.Add(pluginId);
        }
    }
}
