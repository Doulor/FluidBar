namespace FluidBar.App.Plugins;

/// <summary>Minimal M0 local-plugin management surface used by the settings Plugins page.</summary>
public sealed record LocalPluginListItem(
    string Id,
    string Version,
    bool Active,
    string? Error);

/// <summary>Optional service implemented by the production runtime for the settings local-plugin entry.</summary>
public interface ILocalPluginUiService
{
    /// <summary>Unsigned/local plugin root.</summary>
    string LocalPluginRootDirectory { get; }

    /// <summary>Current local plugin packages and load state.</summary>
    IReadOnlyList<LocalPluginListItem> ListLocalPlugins();

    /// <summary>Reloads one local plugin from disk.</summary>
    Task<LocalPluginListItem> ReloadLocalPluginAsync(string pluginId, CancellationToken cancellationToken);

    /// <summary>Unloads one local plugin; its page is removed through PluginStopped.</summary>
    Task<bool> UnloadLocalPluginAsync(string pluginId, CancellationToken cancellationToken);
}
