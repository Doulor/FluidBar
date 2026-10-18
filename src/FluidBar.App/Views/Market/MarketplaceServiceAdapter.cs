using FluidBar.App.Localization;
using FluidBar.App.Views.Plugins;
using FluidBar.App.Views.Market;
using FluidBar.Marketplace.Catalog;
using FluidBar.Marketplace.Index;
using FluidBar.Sdk.Capabilities;

namespace FluidBar.App.Views.Market;

/// <summary>Installed-state operations needed by the marketplace UI (host adapter over PluginInstallService).</summary>
public interface IPluginInstallOperations
{
    /// <summary>Installed active version, or null when not installed.</summary>
    string? GetInstalledVersion(string pluginId);

    /// <summary>Installs/updates a plugin from the signed index.</summary>
    Task InstallAsync(string pluginId, string version, IProgress<InstallProgress> progress, CancellationToken cancellationToken);

    /// <summary>Rolls back to a previous version.</summary>
    Task RollbackAsync(string pluginId, string version, IProgress<InstallProgress> progress, CancellationToken cancellationToken);

    /// <summary>Uninstalls a plugin (optionally keeping its settings).</summary>
    Task UninstallAsync(string pluginId, bool keepSettings, CancellationToken cancellationToken);
}

/// <summary>
/// Marketplace UI service over the real <c>FluidBar.Marketplace</c> domain: signed index browsing via
/// <see cref="MarketplaceCatalogClient"/>, mapping entries to <see cref="MarketPlugin"/> cards, and
/// install/rollback/uninstall delegation through <see cref="IPluginInstallOperations"/> (the host wires
/// <c>PluginInstallService</c> + package sources). A direct index provider is injectable for tests.
/// </summary>
public sealed class MarketplaceServiceAdapter : IMarketplaceUiService, IDisposable
{
    private readonly MarketplaceCatalogClient? _client;
    private readonly Func<CancellationToken, Task<MarketplaceIndexDocument>>? _indexProvider;
    private readonly ILocalizer _localizer;
    private readonly IPluginInstallOperations? _installs;
    private readonly string _channel;
    private MarketplaceIndexDocument? _cachedIndex;

    /// <summary>Creates the adapter over a catalog client (production) or an index provider (tests).</summary>
    public MarketplaceServiceAdapter(
        ILocalizer localizer,
        string channel,
        MarketplaceCatalogClient? client = null,
        Func<CancellationToken, Task<MarketplaceIndexDocument>>? indexProvider = null,
        IPluginInstallOperations? installs = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _channel = channel;
        _client = client;
        _indexProvider = indexProvider;
        _installs = installs;
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
            .Where(static entry => entry.Status == PluginListingStatus.Active)
            .Where(entry => entry.Latest is not null && (channel == PluginChannel.Beta || entry.Latest.Channel == PluginChannel.Stable))
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
        var index = await GetIndexAsync(cancellationToken).ConfigureAwait(false);
        var entry = (index.Plugins ?? []).FirstOrDefault(candidate => string.Equals(candidate.Id, pluginId, StringComparison.Ordinal));
        return entry is null ? null : ToMarketPlugin(entry);
    }

    /// <inheritdoc />
    public async Task InstallAsync(string pluginId, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var plugin = await GetPluginAsync(pluginId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not in the marketplace index.");
        if (_installs is null)
        {
            throw new InvalidOperationException(_localizer.Get(LocKeys.Error.Install.Failed));
        }

        progress.Report(new InstallProgress("download", LocKeys.Market.Stage.Download, 10));
        await _installs.InstallAsync(pluginId, plugin.LatestVersion, progress, cancellationToken).ConfigureAwait(false);
        progress.Report(new InstallProgress("activate", LocKeys.Market.Stage.Activate, 100));
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
        var state = installedVersion is null
            ? MarketInstallState.NotInstalled
            : string.Equals(installedVersion, latest, StringComparison.Ordinal)
                ? MarketInstallState.Installed
                : MarketInstallState.UpdateAvailable;
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
            VersionHistory = entry.Latest is null ? [] : [latest],
            Capabilities = [],
        };
    }

    /// <inheritdoc />
    public void Dispose() => _client?.Dispose();

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
