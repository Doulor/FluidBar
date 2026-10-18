using FluidBar.App.Settings;

namespace FluidBar.App.Views.Settings;

/// <summary>One installed plugin row shown in the Plugins section.</summary>
/// <param name="Id">Plugin id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Version">Installed version.</param>
/// <param name="Publisher">Publisher display name.</param>
/// <param name="Builtin">Whether the plugin is compiled into the app.</param>
/// <param name="CapabilityKeys">Localization keys for its capabilities.</param>
/// <param name="Orphan">Enabled in settings but not installed.</param>
public sealed record PluginListItem(
    string Id,
    string Name,
    string Version,
    string Publisher,
    bool Builtin,
    IReadOnlyList<string> CapabilityKeys,
    bool Orphan = false);

/// <summary>App-facing plugin list contract (production adapter wraps FluidBar.PluginHost later).</summary>
public interface IPluginUiService
{
    /// <summary>Returns the installed plugins.</summary>
    Task<IReadOnlyList<PluginListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Enables or disables a plugin (host-owned switch).</summary>
    Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken);

    /// <summary>Raised when the plugin list/enabled state changed.</summary>
    event EventHandler? Changed;
}

/// <summary>Deterministic plugin list service for previews/headless tests.</summary>
public sealed class DesignPluginUiService : IPluginUiService
{
    private readonly SettingsController _settings;
    private readonly List<PluginListItem> _plugins =
    [
        new PluginListItem("builtin.media", "Now Playing", "2.0.0", "FluidBar", true,
            ["Capability.Media", "Capability.MediaControl"]),
        new PluginListItem("builtin.clipboard", "Clipboard", "2.0.0", "FluidBar", true,
            ["Capability.Clipboard"]),
        new PluginListItem("builtin.notifications", "Notifications", "2.0.0", "FluidBar", true,
            ["Capability.Notifications"]),
        new PluginListItem("builtin.agent", "Agent Hooks", "2.0.0", "FluidBar", true,
            ["Capability.UiIsland", "Capability.UiSettings"]),
        new PluginListItem("code-pulse", "Code Pulse", "2.2.0", "community-lab", false,
            ["Capability.Network.Domains", "Capability.Notifications"]),
    ];

    /// <summary>Creates the design service.</summary>
    public DesignPluginUiService(SettingsController settings) => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public Task<IReadOnlyList<PluginListItem>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var enabled = _settings.Current.Plugins.Enabled;
        var list = _plugins
            .Select(p => p with { Orphan = !p.Builtin && !enabled.Contains(p.Id) && false })
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
}
