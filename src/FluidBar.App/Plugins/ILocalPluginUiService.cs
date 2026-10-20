namespace FluidBar.App.Plugins;

/// <summary>Minimal M0 local-plugin management surface used by the settings Plugins page.</summary>
public sealed record LocalPluginListItem(
    string Id,
    string Version,
    bool Active,
    string? Error)
{
    /// <summary>Manifest-declared capability disclosures shown before loading.</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>Whether this local package is discoverable and safe to load.</summary>
    public bool Available { get; init; } = true;
}

/// <summary>Optional service implemented by the production runtime for the settings local-plugin entry.</summary>
public interface ILocalPluginUiService
{
    /// <summary>Unsigned/local plugin root.</summary>
    string LocalPluginRootDirectory { get; }

    /// <summary>Current local plugin packages and load state.</summary>
    IReadOnlyList<LocalPluginListItem> ListLocalPlugins();

    /// <summary>Loads one local plugin from disk (after the risk acknowledgement).</summary>
    Task<LocalPluginListItem> LoadLocalPluginAsync(string pluginId, CancellationToken cancellationToken)
        => Task.FromResult(new LocalPluginListItem(pluginId, "?", false, "local loading is unavailable"));

    /// <summary>Reloads one local plugin from disk.</summary>
    Task<LocalPluginListItem> ReloadLocalPluginAsync(string pluginId, CancellationToken cancellationToken);

    /// <summary>Unloads one local plugin; its page is removed through PluginStopped.</summary>
    Task<bool> UnloadLocalPluginAsync(string pluginId, CancellationToken cancellationToken);

    /// <summary>Whether the one-time risk notice was accepted for this exact id@version.</summary>
    bool IsLocalRiskAcknowledged(string pluginId, string version) => false;

    /// <summary>Records the one-time risk acknowledgement for this exact id@version.</summary>
    void AcknowledgeLocalRisk(string pluginId, string version)
    {
    }

    /// <summary>Log directory shown by the local plugin log action.</summary>
    string? LogDirectory => null;
}
