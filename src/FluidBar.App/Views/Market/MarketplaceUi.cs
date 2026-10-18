using System.Collections.ObjectModel;

namespace FluidBar.App.Views.Market;

/// <summary>Capability disclosure attached to an install plan (spec 6.5: disclosure, not a sandbox).</summary>
/// <param name="Id">Stable capability token.</param>
/// <param name="LocalizationKey">Localized capability line.</param>
/// <param name="Detail">Optional detail (domains, paths).</param>
public readonly record struct PluginCapabilityDisclosure(string Id, string LocalizationKey, string? Detail);

/// <summary>Installation state of one marketplace plugin.</summary>
public enum MarketInstallState
{
    /// <summary>Not installed.</summary>
    NotInstalled,

    /// <summary>Installation in flight.</summary>
    Installing,

    /// <summary>Installed and up to date.</summary>
    Installed,

    /// <summary>Installed with a newer version available.</summary>
    UpdateAvailable,

    /// <summary>Installed and disabled.</summary>
    Disabled,

    /// <summary>Operation failed (reason shown).</summary>
    Failed,
}

/// <summary>One marketplace plugin card model.</summary>
public sealed record MarketPlugin
{
    /// <summary>Plugin id.</summary>
    public required string Id { get; init; }

    /// <summary>Display name (localized by the index listing; user data).</summary>
    public required string Name { get; init; }

    /// <summary>Short summary.</summary>
    public required string Summary { get; init; }

    /// <summary>Publisher display name.</summary>
    public required string Publisher { get; init; }

    /// <summary>Latest version string.</summary>
    public required string LatestVersion { get; init; }

    /// <summary>Installed version, if any.</summary>
    public string? InstalledVersion { get; init; }

    /// <summary>Category token.</summary>
    public required string Category { get; init; }

    /// <summary>Install count.</summary>
    public int Installs { get; init; }

    /// <summary>Whether the publisher key is verified.</summary>
    public bool Verified { get; init; }

    /// <summary>Featured placement.</summary>
    public bool Featured { get; init; }

    /// <summary>Previous versions, newest first (rollback targets).</summary>
    public IReadOnlyList<string> VersionHistory { get; init; } = [];

    /// <summary>Capability disclosures shown on the consent dialog.</summary>
    public IReadOnlyList<PluginCapabilityDisclosure> Capabilities { get; init; } = [];

    /// <summary>Current install state.</summary>
    public MarketInstallState State { get; init; }
}

/// <summary>Progress of one install/update/rollback operation (all stages visible, spec 6.4).</summary>
/// <param name="StageToken">Stable stage token (download/verify/extract/activate).</param>
/// <param name="LocalizationKey">Localized stage label key.</param>
/// <param name="Percent">0..100 progress.</param>
public readonly record struct InstallProgress(string StageToken, string LocalizationKey, int Percent);

/// <summary>Marketplace browser query.</summary>
/// <param name="Search">Search text.</param>
/// <param name="Category">Category token (<c>all</c> for every category).</param>
/// <param name="Channel">Update channel (<c>stable</c>/<c>beta</c>).</param>
public readonly record struct MarketplaceQuery(string? Search, string Category, string Channel);

/// <summary>
/// App-facing marketplace contract. The production implementation adapts
/// <c>FluidBar.Marketplace</c> (signed index + install transaction); the design implementation below
/// exercises the whole UI without network or filesystem access.
/// </summary>
public interface IMarketplaceUiService
{
    /// <summary>Returns the plugin list matching the query.</summary>
    Task<IReadOnlyList<MarketPlugin>> BrowseAsync(MarketplaceQuery query, CancellationToken cancellationToken);

    /// <summary>Returns the current install plan (capabilities + versions) for the consent dialog.</summary>
    Task<MarketPlugin?> GetPluginAsync(string pluginId, CancellationToken cancellationToken);

    /// <summary>Installs a plugin after consent, reporting staged progress.</summary>
    Task InstallAsync(string pluginId, IProgress<InstallProgress> progress, CancellationToken cancellationToken);

    /// <summary>Rolls back to <paramref name="version"/> (keeps the previous install until active).</summary>
    Task RollbackAsync(string pluginId, string version, IProgress<InstallProgress> progress, CancellationToken cancellationToken);

    /// <summary>Uninstalls a plugin (optionally keeping its settings).</summary>
    Task UninstallAsync(string pluginId, bool keepSettings, CancellationToken cancellationToken);

    /// <summary>Raised when the catalog or install states changed.</summary>
    event EventHandler? Changed;
}

/// <summary>Deterministic marketplace service with realistic data for previews/headless tests.</summary>
public sealed class DesignMarketplaceUiService : IMarketplaceUiService
{
    private readonly List<MarketPlugin> _plugins = [];
    private readonly object _gate = new();

    /// <summary>Creates the design catalog.</summary>
    public DesignMarketplaceUiService()
    {
        _plugins.Add(new MarketPlugin
        {
            Id = "now-playing",
            Name = "Now Playing+",
            Summary = "Lyrics, hi-res artwork and per-app media rules for the island.",
            Publisher = "doulor",
            LatestVersion = "2.1.0",
            InstalledVersion = "2.1.0",
            Category = "media",
            Installs = 18400,
            Verified = true,
            Featured = true,
            VersionHistory = ["2.1.0", "2.0.2", "2.0.0"],
            State = MarketInstallState.Installed,
            Capabilities =
            [
                new PluginCapabilityDisclosure("media", "Capability.Media", null),
                new PluginCapabilityDisclosure("network", "Capability.Network.Domains", "lrclib.net"),
            ],
        });
        _plugins.Add(new MarketPlugin
        {
            Id = "focus-timer",
            Name = "Focus Timer",
            Summary = "Pomodoro timer with island progress and focus-assist integration.",
            Publisher = "doulor",
            LatestVersion = "1.4.2",
            Category = "productivity",
            Installs = 9200,
            Verified = true,
            Featured = true,
            VersionHistory = ["1.4.2", "1.3.0"],
            State = MarketInstallState.NotInstalled,
            Capabilities =
            [
                new PluginCapabilityDisclosure("ui.settings", "Capability.UiSettings", null),
                new PluginCapabilityDisclosure("commands", "Capability.Commands", null),
            ],
        });
        _plugins.Add(new MarketPlugin
        {
            Id = "clipboard-stack",
            Name = "Clipboard Stack",
            Summary = "Extends clipboard history with snippets and pinned stacks.",
            Publisher = "community-lab",
            LatestVersion = "0.9.0",
            InstalledVersion = "0.8.1",
            Category = "productivity",
            Installs = 2100,
            Verified = false,
            VersionHistory = ["0.9.0", "0.8.1"],
            State = MarketInstallState.UpdateAvailable,
            Capabilities =
            [
                new PluginCapabilityDisclosure("clipboard", "Capability.Clipboard", null),
                new PluginCapabilityDisclosure("storage", "Capability.Storage", null),
            ],
        });
        _plugins.Add(new MarketPlugin
        {
            Id = "system-thermals",
            Name = "Thermals",
            Summary = "Reads CPU/GPU temperatures and exposes them as declarative widgets.",
            Publisher = "community-lab",
            LatestVersion = "1.0.1",
            Category = "system",
            Installs = 780,
            Verified = false,
            VersionHistory = ["1.0.1"],
            State = MarketInstallState.NotInstalled,
            Capabilities =
            [
                new PluginCapabilityDisclosure("network", "Capability.Network", "github.com"),
                new PluginCapabilityDisclosure("process.spawn", "Capability.ProcessSpawn", null),
                new PluginCapabilityDisclosure("ui.widgets", "Capability.UiWidgets", null),
            ],
        });
        _plugins.Add(new MarketPlugin
        {
            Id = "code-pulse",
            Name = "Code Pulse",
            Summary = "Surfaces CI failures and agent completions with one-click actions.",
            Publisher = "community-lab",
            LatestVersion = "2.2.0",
            InstalledVersion = "2.2.0",
            Category = "developer",
            Installs = 5400,
            Verified = true,
            VersionHistory = ["2.2.0", "2.1.3"],
            State = MarketInstallState.Installed,
            Capabilities =
            [
                new PluginCapabilityDisclosure("network", "Capability.Network.Domains", "api.github.com"),
                new PluginCapabilityDisclosure("notifications", "Capability.Notifications", null),
            ],
        });
    }

    /// <summary>Number of install operations performed (tests).</summary>
    public int InstallCount { get; private set; }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public Task<IReadOnlyList<MarketPlugin>> BrowseAsync(MarketplaceQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IEnumerable<MarketPlugin> items = _plugins;
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim();
                items = items.Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || p.Summary.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || p.Id.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(query.Category) && query.Category != "all")
            {
                items = items.Where(p => p.Category == query.Category);
            }

            return Task.FromResult<IReadOnlyList<MarketPlugin>>(items.ToList());
        }
    }

    /// <inheritdoc />
    public Task<MarketPlugin?> GetPluginAsync(string pluginId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_plugins.FirstOrDefault(p => p.Id == pluginId));
        }
    }

    /// <inheritdoc />
    public async Task InstallAsync(string pluginId, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        lock (_gate)
        {
            var index = _plugins.FindIndex(p => p.Id == pluginId);
            if (index < 0)
            {
                throw new InvalidOperationException($"Unknown plugin '{pluginId}'.");
            }

            _plugins[index] = _plugins[index] with { State = MarketInstallState.Installing };
        }

        Changed?.Invoke(this, EventArgs.Empty);
        await ReportAsync(progress, "download", "Market.Stage.Download", 20, cancellationToken).ConfigureAwait(false);
        await ReportAsync(progress, "verify", "Market.Stage.Verify", 45, cancellationToken).ConfigureAwait(false);
        await ReportAsync(progress, "extract", "Market.Stage.Extract", 70, cancellationToken).ConfigureAwait(false);
        await ReportAsync(progress, "activate", "Market.Stage.Activate", 100, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            var index = _plugins.FindIndex(p => p.Id == pluginId);
            if (index >= 0)
            {
                _plugins[index] = _plugins[index] with
                {
                    State = MarketInstallState.Installed,
                    InstalledVersion = _plugins[index].LatestVersion,
                };
            }

            InstallCount++;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task RollbackAsync(string pluginId, string version, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        await ReportAsync(progress, "staged", "Market.Stage.Prepare", 50, cancellationToken).ConfigureAwait(false);
        await ReportAsync(progress, "activate", "Market.Stage.Activate", 100, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            var index = _plugins.FindIndex(p => p.Id == pluginId);
            if (index >= 0)
            {
                _plugins[index] = _plugins[index] with
                {
                    InstalledVersion = version,
                    State = MarketInstallState.Installed,
                };
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public Task UninstallAsync(string pluginId, bool keepSettings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var index = _plugins.FindIndex(p => p.Id == pluginId);
            if (index >= 0)
            {
                _plugins[index] = _plugins[index] with
                {
                    InstalledVersion = null,
                    State = MarketInstallState.NotInstalled,
                };
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private static Task ReportAsync(IProgress<InstallProgress> progress, string stage, string key, int percent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InstallProgress(stage, key, percent));
        return Task.Delay(15, cancellationToken);
    }
}
