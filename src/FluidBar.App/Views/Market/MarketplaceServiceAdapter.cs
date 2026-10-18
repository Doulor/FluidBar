using FluidBar.App.Localization;
using FluidBar.App.Views.Plugins;
using FluidBar.Marketplace.Catalog;
using FluidBar.Marketplace.Index;
using FluidBar.Sdk.Versioning;

namespace FluidBar.App.Views.Market;

/// <summary>Installed-state operations needed by the marketplace UI (host adapter over PluginInstallService).</summary>
public interface IPluginInstallOperations
{
    /// <summary>Installed active version, or null when not installed.</summary>
    string? GetInstalledVersion(string pluginId);

    /// <summary>Installed version directories, newest first (rollback targets).</summary>
    IReadOnlyList<string> GetInstalledVersions(string pluginId) => [];

    /// <summary>Whether the host currently has the plugin enabled.</summary>
    bool IsPluginEnabled(string pluginId) => true;

    /// <summary>Installs/updates a plugin from the signed index, reporting real install phases.</summary>
    Task InstallAsync(
        MarketplacePluginEntry entry,
        MarketplaceVersionInfo version,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken);

    /// <summary>Rolls back to a previous version.</summary>
    Task RollbackAsync(string pluginId, string version, IProgress<InstallProgress> progress, CancellationToken cancellationToken);

    /// <summary>Uninstalls a plugin (optionally keeping its settings).</summary>
    Task UninstallAsync(string pluginId, bool keepSettings, CancellationToken cancellationToken);
}

/// <summary>
/// Marketplace UI service over the real <c>FluidBar.Marketplace</c> domain: signed index browsing via
/// <see cref="MarketplaceCatalogClient"/>, mapping entries to <see cref="MarketPlugin"/> cards, and
/// install/rollback/uninstall delegation through <see cref="IPluginInstallOperations"/> (the App wires
/// <c>PluginInstallService</c> + package sources). A direct index provider is injectable for tests.
/// </summary>
public sealed class MarketplaceServiceAdapter : IMarketplaceUiService, IDisposable
{
    private readonly MarketplaceCatalogClient? _client;
    private readonly Func<CancellationToken, Task<MarketplaceIndexDocument>>? _indexProvider;
    private readonly ILocalizer _localizer;
    private readonly IPluginInstallOperations? _installs;
    private readonly string _channel;
    private readonly SemanticVersion? _appVersion;
    private readonly SemanticVersion? _hostApiVersion;
    private MarketplaceIndexDocument? _cachedIndex;

    /// <summary>Creates the adapter over a catalog client (production) or an index provider (tests).</summary>
    public MarketplaceServiceAdapter(
        ILocalizer localizer,
        string channel,
        MarketplaceCatalogClient? client = null,
        Func<CancellationToken, Task<MarketplaceIndexDocument>>? indexProvider = null,
        IPluginInstallOperations? installs = null,
        SemanticVersion? appVersion = null,
        SemanticVersion? hostApiVersion = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _channel = channel;
        _client = client;
        _indexProvider = indexProvider;
        _installs = installs;
        _appVersion = appVersion;
        _hostApiVersion = hostApiVersion;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Last browse error (localized key/message) for the UI status line.</summary>
    public string? LastError { get; private set; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketPlugin>> BrowseAsync(MarketplaceQuery query, CancellationToken cancellationToken)
    {
        var index = await GetIndexAsync(cancellationToken).ConfigureAwait(false);
        var entries = index.Plugins ?? [];
        var channel = query.Channel is "beta" ? PluginChannel.Beta : PluginChannel.Stable;
        var items = entries
            .Where(static entry => entry.Status != PluginListingStatus.Blocked)
            .Where(entry => entry.Latest is not null && (channel == PluginChannel.Beta || entry.Latest.Channel == PluginChannel.Stable))
            .Where(entry => IsCompatible(entry))
            .Where(entry => string.IsNullOrEmpty(query.Category) || query.Category == "all"
                || (entry.Categories?.Contains(query.Category, StringComparer.OrdinalIgnoreCase) ?? false))
            .Where(entry => string.IsNullOrWhiteSpace(query.Search)
                || Resolve(entry.Name).Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || Resolve(entry.Summary).Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || entry.Id.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .Select(ToMarketPlugin)
            .ToList();
        return items;
    }

    /// <inheritdoc />
    public async Task<MarketPlugin?> GetPluginAsync(string pluginId, CancellationToken cancellationToken)
    {
        var entry = await GetEntryAsync(pluginId, cancellationToken).ConfigureAwait(false);
        return entry is null ? null : ToMarketPlugin(entry);
    }

    /// <summary>Returns the signed index entry for one plugin (install/consent data).</summary>
    public async Task<MarketplacePluginEntry?> GetEntryAsync(string pluginId, CancellationToken cancellationToken)
    {
        var index = await GetIndexAsync(cancellationToken).ConfigureAwait(false);
        return (index.Plugins ?? []).FirstOrDefault(candidate => string.Equals(candidate.Id, pluginId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task InstallAsync(string pluginId, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var entry = await GetEntryAsync(pluginId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not in the marketplace index.");
        var version = entry.Latest
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' has no installable version.");
        if (entry.Status == PluginListingStatus.Blocked)
        {
            throw new InvalidOperationException(entry.Blocked?.Reason ?? $"Plugin '{pluginId}' is blocked by the marketplace.");
        }

        if (version.Yanked)
        {
            throw new InvalidOperationException(version.YankReason ?? $"Version '{version.Version}' was withdrawn.");
        }

        if (_installs is null)
        {
            throw new InvalidOperationException(_localizer.Get(LocKeys.Error.Install.Failed));
        }

        await _installs.InstallAsync(entry, version, progress, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task RollbackAsync(string pluginId, string version, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (_installs is null)
        {
            throw new InvalidOperationException(_localizer.Get(LocKeys.Error.Install.Failed));
        }

        await _installs.RollbackAsync(pluginId, version, progress, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task UninstallAsync(string pluginId, bool keepSettings, CancellationToken cancellationToken)
    {
        if (_installs is null)
        {
            throw new InvalidOperationException(_localizer.Get(LocKeys.Error.Install.Failed));
        }

        await _installs.UninstallAsync(pluginId, keepSettings, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Maps one signed index entry to a UI card (also used by tests).</summary>
    public MarketPlugin ToMarketPlugin(MarketplacePluginEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var installedVersion = _installs?.GetInstalledVersion(entry.Id);
        var latest = entry.Latest?.Version ?? string.Empty;
        var enabled = _installs?.IsPluginEnabled(entry.Id) ?? true;
        var state = installedVersion is null
            ? MarketInstallState.NotInstalled
            : !enabled
                ? MarketInstallState.Disabled
                : string.Equals(installedVersion, latest, StringComparison.Ordinal)
                    ? MarketInstallState.Installed
                    : MarketInstallState.UpdateAvailable;

        var installedVersions = _installs?.GetInstalledVersions(entry.Id) ?? [];
        var history = new List<string>();
        foreach (var version in installedVersions)
        {
            history.Add(version);
        }

        if (!string.IsNullOrEmpty(latest) && !history.Contains(latest, StringComparer.Ordinal))
        {
            history.Insert(0, latest);
        }

        return new MarketPlugin
        {
            Id = entry.Id,
            Name = string.IsNullOrEmpty(Resolve(entry.Name)) ? entry.Id : Resolve(entry.Name),
            Summary = Resolve(entry.Summary),
            Publisher = entry.Publisher?.DisplayName ?? entry.Publisher?.Id ?? string.Empty,
            LatestVersion = latest,
            InstalledVersion = installedVersion,
            Category = entry.Categories?.FirstOrDefault() ?? "other",
            Installs = (int)Math.Min(int.MaxValue, entry.Stats?.Downloads ?? 0),
            Verified = entry.Publisher?.Verified ?? false,
            Featured = entry.Featured,
            State = state,
            VersionHistory = history,
            Capabilities = PluginCapabilityDisclosureMap.FromTokens(entry.Latest?.Capabilities),
        };
    }

    /// <inheritdoc />
    public void Dispose() => _client?.Dispose();

    private bool IsCompatible(MarketplacePluginEntry entry)
    {
        var appVersion = _appVersion;
        var hostApiVersion = _hostApiVersion;
        if (appVersion is null || hostApiVersion is null)
        {
            return true;
        }

        return MarketplaceIndexValidator.IsCompatible(entry, appVersion.Value, hostApiVersion.Value);
    }

    private async Task<MarketplaceIndexDocument> GetIndexAsync(CancellationToken cancellationToken)
    {
        if (_indexProvider is not null)
        {
            return await _indexProvider(cancellationToken).ConfigureAwait(false);
        }

        if (_client is null)
        {
            throw new InvalidOperationException(_localizer.Get(LocKeys.Error.Network.Offline));
        }

        if (_cachedIndex is not null)
        {
            return _cachedIndex;
        }

        var result = await _client.FetchIndexAsync(cancellationToken).ConfigureAwait(false);
        if (result.Index is null)
        {
            LastError = result.Error ?? _localizer.Get(LocKeys.Error.Network.Offline);
            throw new InvalidOperationException(LastError);
        }

        _cachedIndex = result.Index;
        return _cachedIndex;
    }

    private string Resolve(FluidBar.Sdk.Manifest.LocalizedString? value) => LocalizedValue.Resolve(value, _localizer);
}

/// <summary>
/// Maps signed-index capability tokens onto the App's disclosure lines. The index carries tokens only; the
/// full manifest (reasons/domains) is verified during install and returned in the install result. This is
/// disclosure for consent, never an enforcement claim.
/// </summary>
public static class PluginCapabilityDisclosureMap
{
    private static readonly IReadOnlyDictionary<string, (string Key, string? Detail)> Tokens =
        new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase)
        {
            ["network"] = (LocKeys.Capability.NetworkLabel, null),
            ["clipboard"] = (LocKeys.Capability.Clipboard, null),
            ["files"] = (LocKeys.Capability.Files, null),
            ["media"] = (LocKeys.Capability.Media, null),
            ["notifications"] = (LocKeys.Capability.Notifications, null),
            ["shell"] = (LocKeys.Capability.ShellExecute, null),
            ["hotkeys"] = (LocKeys.Capability.Commands, null),
            ["process"] = (LocKeys.Capability.ProcessSpawn, null),
            ["registry"] = (LocKeys.Capability.Registry, null),
            ["telemetry"] = (LocKeys.Capability.Network.Internet, null),
            ["background"] = (LocKeys.Capability.Background, null),
            ["ui"] = (LocKeys.Capability.UiIsland, null),
        };

    /// <summary>Converts capability tokens to localized disclosure rows (unknown tokens are passed through).</summary>
    public static IReadOnlyList<PluginCapabilityDisclosure> FromTokens(IEnumerable<string>? tokens)
    {
        if (tokens is null)
        {
            return [];
        }

        var result = new List<PluginCapabilityDisclosure>();
        foreach (var token in tokens.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Tokens.TryGetValue(token, out var mapped))
            {
                result.Add(new PluginCapabilityDisclosure(token, mapped.Key, mapped.Detail));
            }
            else
            {
                result.Add(new PluginCapabilityDisclosure(token, "Capability." + token, null));
            }
        }

        return result;
    }
}

