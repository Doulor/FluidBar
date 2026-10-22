using System.Text.Json;
using FluidBar.App.Localization;
using FluidBar.App.Views.Market;
using FluidBar.App.Views.Plugins;
using FluidBar.App.Views.Settings;
using FluidBar.Marketplace.Catalog;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Signing;
using FluidBar.PluginHost.Loader;
using FluidBar.PluginHost.Logging;
using FluidBar.Plugins.Builtin;
using FluidBar.Sdk;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;
using PluginHostType = FluidBar.PluginHost.Loader.PluginHost;

namespace FluidBar.App.Plugins;

/// <summary>One built-in plugin registration: its descriptor and the running session (null when unavailable).</summary>
/// <param name="Descriptor">Compiled-in descriptor (id, name, contributions).</param>
/// <param name="Session">Running session, or null when a required platform service is missing.</param>
/// <param name="Error">Why the plugin is not running (null when it is).</param>
public sealed record BuiltinPluginSessionEntry(BuiltinPluginDescriptor Descriptor, InProcessPluginSession? Session, string? Error);

/// <summary>Composition inputs for <see cref="PluginRuntime"/>; every path, clock and transport is injectable.</summary>
public sealed record PluginRuntimeOptions
{
    /// <summary>Plugins root (<c>%APPDATA%\FluidBar\plugins</c> in production).</summary>
    public required string PluginRootDirectory { get; init; }

    /// <summary>Unsigned/local plugin root (<c>%APPDATA%\FluidBar\plugins\local</c>); null disables local loading.</summary>
    public string? LocalPluginRootDirectory { get; init; }

    /// <summary>Host cleanup hook for page/command registrations when a plugin stops.</summary>
    public Action<string>? PluginStopped { get; init; }

    /// <summary>Running FluidBar version used by the min/max app-version gates.</summary>
    public required SemanticVersion AppVersion { get; init; }

    /// <summary>App localizer (marketplace card text and consent labels).</summary>
    public required ILocalizer Localizer { get; init; }

    /// <summary>SDK ABI version implemented by the host.</summary>
    public SemanticVersion HostApiVersion { get; init; } = PluginApiVersion.Current;

    /// <summary>Host log sink (bridged onto the App JSON-lines logger).</summary>
    public IPluginHostLogger? Logger { get; init; }

    /// <summary>Install/marketplace log sink (bridged onto the App JSON-lines logger).</summary>
    public IPluginInstallLogger? InstallLogger { get; init; }

    /// <summary>UI-thread dispatcher for plugins.</summary>
    public IHostDispatcher? Dispatcher { get; init; }

    /// <summary>Island publisher factory (App adapts SDK events onto the Core hub).</summary>
    public Func<PluginManifest, IIslandPublisher>? IslandPublisherFactory { get; init; }

    /// <summary>Notification sink factory (App-owned, host-rendered).</summary>
    public Func<PluginManifest, INotificationSink>? NotificationSinkFactory { get; init; }

    /// <summary>Platform services for first-party plugins; null/empty skips the affected plugins.</summary>
    public BuiltinPlatformServices? BuiltinServices { get; init; }

    /// <summary>Descriptor override (tests/previews); defaults to <see cref="BuiltinPluginCatalog.Descriptors"/>.</summary>
    public IReadOnlyList<BuiltinPluginDescriptor>? BuiltinDescriptors { get; init; }

    /// <summary>Persisted enable/disable switches.</summary>
    public IPluginEnablementStore? Enablement { get; init; }

    /// <summary>Signed-index client (production).</summary>
    public MarketplaceCatalogClient? CatalogClient { get; init; }

    /// <summary>Index URL used to build a catalog client when <see cref="CatalogClient"/> is not supplied.</summary>
    public Uri? MarketplaceIndexUrl { get; init; }

    /// <summary>Direct index provider (tests; no HTTP).</summary>
    public Func<CancellationToken, Task<MarketplaceIndexDocument>>? IndexProvider { get; init; }

    /// <summary>Injected marketplace trust keys (empty disables signature-verified catalog use).</summary>
    public MarketplaceTrustOptions Trust { get; init; } = MarketplaceTrustOptions.Empty;

    /// <summary>Marketplace channel (<c>stable</c>/<c>beta</c>).</summary>
    public string Channel { get; init; } = "stable";

    /// <summary>Require the marketplace countersignature for marketplace installs.</summary>
    public bool RequireMarketplaceSignature { get; init; } = true;

    /// <summary>Allow unsigned offline local installs (explicit user consent; UI shows the warning).</summary>
    public bool AllowUnsignedLocalInstall { get; init; } = true;

    /// <summary>HTTP transport for catalog/package/signature downloads; the runtime owns it when omitted.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Clock used by leases and install state.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Retained installed versions per plugin (active + previous by default).</summary>
    public int RetainedVersions { get; init; } = 2;

    /// <summary>Load first-party plugins during composition.</summary>
    public bool LoadBuiltins { get; init; } = true;

    /// <summary>Load installed community plugins during composition.</summary>
    public bool LoadCommunity { get; init; } = true;

    /// <summary>Host UI locales used for plugin localization fallback.</summary>
    public IReadOnlyList<string> HostUiLocales { get; init; } = ["en-US"];

    /// <summary>Directory used for built-in locale lookups (defaults to the App directory).</summary>
    public string? BuiltinPackageDirectory { get; init; }
}

/// <summary>Composition outcome (load failures are values, never exceptions thrown at the App).</summary>
/// <param name="CommunityResults">Per-plugin community load results.</param>
/// <param name="BuiltinErrors">Built-ins that could not start (platform service missing or lifecycle failure).</param>
/// <param name="DiscoveredPackages">Discovered community packages (including invalid ones).</param>
public sealed record PluginRuntimeReport(
    IReadOnlyList<PluginLoadResult> CommunityResults,
    IReadOnlyList<string> BuiltinErrors,
    IReadOnlyList<PluginDiscoveryInfo> DiscoveredPackages);

/// <summary>
/// App composition root for the plugin platform: one <see cref="PluginHost"/> for community packages,
/// <see cref="InProcessPluginSession"/> instances for the compiled-in plugins, a
/// <see cref="PluginInstallService"/> wired to the host through an <see cref="IPluginActivator"/>, and the
/// marketplace/plugin UI services over both. Nothing here touches the UI thread; callers create it from a
/// background task during startup.
/// </summary>
public sealed class PluginRuntime : IAsyncDisposable, IPluginContributionUiService, ILocalPluginUiService
{
    private readonly PluginRuntimeOptions _options;
    private readonly PluginHostOptions _hostOptions;
    private readonly IPluginEnablementStore _enablement;
    private readonly IPluginHostLogger _logger;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly MarketplaceCatalogClient? _catalog;
    private readonly bool _ownsCatalog;
    private readonly PluginHostType? _localHost;
    private readonly List<BuiltinPluginSessionEntry> _builtins = [];
    private readonly List<PluginLoadResult> _communityResults = [];
    private readonly List<string> _builtinErrors = [];
    private readonly IPluginSettingsValues _values;
    private IReadOnlyList<PluginDiscoveryInfo> _discoveries = [];
    private IReadOnlyList<PluginDiscoveryInfo> _localDiscoveries = [];
    private bool _disposed;

    private PluginRuntime(
        PluginRuntimeOptions options,
        PluginHostOptions hostOptions,
        PluginHostType host,
        PluginInstallService installs,
        IMarketplaceUiService marketplace,
        HttpClient http,
        bool ownsHttp,
        MarketplaceCatalogClient? catalog,
        bool ownsCatalog,
        PluginHostType? localHost = null)
    {
        _options = options;
        _hostOptions = hostOptions;
        _enablement = options.Enablement ?? new InMemoryPluginEnablementStore();
        _logger = hostOptions.Logger;
        Host = host;
        Installs = installs;
        Marketplace = marketplace;
        _http = http;
        _ownsHttp = ownsHttp;
        _catalog = catalog;
        _ownsCatalog = ownsCatalog;
        _localHost = localHost;
        _values = new RuntimePluginSettingsValues(this);
        Host.StateChanged += OnHostStateChanged;
    }

    /// <summary>Raised when the plugin list/enabled state/catalog changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Community plugin host.</summary>
    public PluginHostType Host { get; }

    /// <summary>Local (unsigned, user-owned) plugin host; null when local loading is disabled.</summary>
    public PluginHostType? LocalHost => _localHost;

    /// <summary>Marketplace install/update/rollback/uninstall state machine.</summary>
    public PluginInstallService Installs { get; }

    /// <summary>Marketplace UI service (real adapter, or a clearly-labelled demo fallback).</summary>
    public IMarketplaceUiService Marketplace { get; }

    /// <summary>Plugin list/contributions UI service (this runtime).</summary>
    public IPluginUiService Plugins => this;

    /// <summary>Built-in registrations, including unavailable ones (honest state for the UI).</summary>
    public IReadOnlyList<BuiltinPluginSessionEntry> Builtins => _builtins;

    /// <summary>Composition/load report.</summary>
    public PluginRuntimeReport Report => new(_communityResults, _builtinErrors, _discoveries);

    /// <summary>Resolved plugins root.</summary>
    public string PluginRootDirectory => _options.PluginRootDirectory;

    /// <summary>Host-owned settings values for the generic settings-page renderer.</summary>
    public IPluginSettingsValues Values => _values;

    /// <summary>Creates the runtime, composes the host/installer/UI services and loads plugins.</summary>
    public static async Task<PluginRuntime> CreateAsync(PluginRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PluginRootDirectory);
        ArgumentNullException.ThrowIfNull(options.Localizer);
        if (options.AppVersion.Major <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "AppVersion must be a positive semantic version.");
        }

        Directory.CreateDirectory(options.PluginRootDirectory);
        var logger = options.Logger ?? NullPluginHostLogger.Instance;
        var hostOptions = new PluginHostOptions
        {
            PluginRootDirectory = options.PluginRootDirectory,
            AppVersion = options.AppVersion,
            HostApiVersion = options.HostApiVersion,
            Logger = logger,
            Dispatcher = options.Dispatcher,
            IslandPublisherFactory = options.IslandPublisherFactory,
            NotificationSinkFactory = options.NotificationSinkFactory,
            PluginStopped = options.PluginStopped,
            TimeProvider = options.TimeProvider,
            HostUiLocales = options.HostUiLocales,
        };

        var host = new PluginHostType(hostOptions);
        PluginHostType? localHost = null;
        if (!string.IsNullOrWhiteSpace(options.LocalPluginRootDirectory)
            && !string.Equals(Path.GetFullPath(options.LocalPluginRootDirectory),
                Path.GetFullPath(options.PluginRootDirectory), StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(options.LocalPluginRootDirectory);
            localHost = new PluginHostType(hostOptions with
            {
                PluginRootDirectory = Path.GetFullPath(options.LocalPluginRootDirectory),
            });
        }
        var installLogger = options.InstallLogger ?? NullPluginInstallLogger.Instance;
        var installs = new PluginInstallService(new PluginInstallOptions
        {
            PluginRootDirectory = options.PluginRootDirectory,
            Activator = new HostPluginActivator(host, logger),
            Trust = options.Trust,
            RequireMarketplaceSignature = options.RequireMarketplaceSignature,
            AllowUnsignedLocalInstall = options.AllowUnsignedLocalInstall,
            AppVersion = options.AppVersion,
            HostApiVersion = options.HostApiVersion,
            RetainedVersions = options.RetainedVersions,
            Logger = installLogger,
            TimeProvider = options.TimeProvider,
        });

        var ownsHttp = options.HttpClient is null;
        var http = options.HttpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var catalog = options.CatalogClient;
        var ownsCatalog = false;
        if (catalog is null && options.MarketplaceIndexUrl is { } indexUrl)
        {
            catalog = new MarketplaceCatalogClient(
                new MarketplaceCatalogOptions
                {
                    IndexUrl = indexUrl,
                    Trust = options.Trust,
                    AppVersion = options.AppVersion,
                    HostApiVersion = options.HostApiVersion,
                });
            ownsCatalog = true;
        }

        var installsAdapter = new PluginInstallOperations(installs, host, http, options.TimeProvider);
        MarketplaceServiceAdapter? realMarketplace = null;
        if (options.IndexProvider is not null || catalog is not null)
        {
            realMarketplace = new MarketplaceServiceAdapter(
                options.Localizer,
                options.Channel,
                catalog,
                options.IndexProvider,
                installsAdapter,
                options.AppVersion,
                options.HostApiVersion);
        }

        var marketplace = new MarketplaceFallbackUiService(
            realMarketplace,
            new DesignMarketplaceUiService(),
            options.Localizer);

        var runtime = new PluginRuntime(options, hostOptions, host, installs, marketplace, http, ownsHttp, catalog, ownsCatalog, localHost);
        try
        {
            if (options.LoadBuiltins)
            {
                await runtime.LoadBuiltinsAsync(cancellationToken).ConfigureAwait(false);
            }

            if (options.LoadCommunity)
            {
                await runtime.LoadCommunityAsync(cancellationToken).ConfigureAwait(false);
            }

            return runtime;
        }
        catch
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Loads every compiled-in plugin whose platform services exist; one failure never stops the rest.</summary>
    public async Task LoadBuiltinsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var services = _options.BuiltinServices ?? new BuiltinPlatformServices();
        var descriptors = _options.BuiltinDescriptors ?? BuiltinPluginCatalog.Descriptors;
        var packageDirectory = _options.BuiltinPackageDirectory ?? AppContext.BaseDirectory;

        foreach (var descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IFluidBarPlugin? plugin;
            try
            {
                plugin = descriptor.Factory(services);
            }
            catch (Exception exception)
            {
                _builtinErrors.Add($"{descriptor.Id}: {exception.GetType().Name}: {exception.Message}");
                _logger.Error(descriptor.Id, $"Built-in plugin construction failed: {exception.GetType().FullName}: {exception.Message}");
                _builtins.Add(new BuiltinPluginSessionEntry(descriptor, null, exception.Message));
                continue;
            }

            if (plugin is null)
            {
                _builtins.Add(new BuiltinPluginSessionEntry(
                    descriptor,
                    null,
                    LocKeys.Settings.Plugins.ErrorPlatformServiceUnavailable));
                continue;
            }

            var manifest = BuiltinPluginManifests.Create(descriptor);
            var session = new InProcessPluginSession(_hostOptions, manifest, packageDirectory, plugin);
            session.StateChanged += OnHostStateChanged;
            var result = await session.StartAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                _builtinErrors.Add($"{descriptor.Id}: {result.Message}");
                _builtins.Add(new BuiltinPluginSessionEntry(descriptor, session, result.Message));
                continue;
            }

            if (!_enablement.IsEnabled(descriptor.Id))
            {
                await session.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            _builtins.Add(new BuiltinPluginSessionEntry(descriptor, session, null));
        }
    }

    /// <summary>Loads every discovered community plugin; disabled plugins are stopped after loading.</summary>
    public async Task<IReadOnlyList<PluginLoadResult>> LoadCommunityAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        IReadOnlyList<PluginDiscoveryInfo> discoveries;
        try
        {
            discoveries = await Host.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Error("<host>", $"Plugin discovery failed: {exception.GetType().FullName}: {exception.Message}");
            discoveries = [];
        }

        _discoveries = discoveries;
        IReadOnlyList<PluginLoadResult> results;
        try
        {
            results = await Host.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Error("<host>", $"Plugin loading failed: {exception.GetType().FullName}: {exception.Message}");
            results = [];
        }

        _communityResults.Clear();
        _communityResults.AddRange(results);

        if (_localHost is not null)
        {
            try
            {
                var localDiscoveries = await _localHost.DiscoverAsync(cancellationToken).ConfigureAwait(false);
                _localDiscoveries = localDiscoveries;
                _discoveries = [.. discoveries, .. localDiscoveries];
                var localResults = await _localHost.LoadAllAsync(cancellationToken).ConfigureAwait(false);
                _communityResults.AddRange(localResults);
                // M0 local channel is opt-out: a package dropped into the local root runs on startup.
                // Explicit unloads stop the current session (settings handles that); persistence of a
                // local disable flag is part of the M1 settings work.
            }
            catch (Exception exception)
            {
                _logger.Error("<local-host>", $"Local plugin loading failed: {exception.GetType().FullName}: {exception.Message}");
            }
        }

        foreach (var result in results)
        {
            if (result.Success && !_enablement.IsEnabled(result.PluginId))
            {
                await Host.StopAsync(result.PluginId, cancellationToken).ConfigureAwait(false);
            }
        }

        RaiseChanged();
        return _communityResults;
    }

    /// <summary>Installs an offline <c>.fbp</c> (unsigned allowed only when explicitly requested).</summary>
    public async Task<InstallResult> InstallFromFileAsync(
        string packagePath,
        bool allowUnsigned,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var result = await Installs.InstallFromFileAsync(packagePath, allowUnsigned: allowUnsigned, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result.Success)
        {
            await LoadCommunityAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>Executes a declared command through the host (built-in or community).</summary>
    public async Task<PluginCommandExecutionResult> ExecuteCommandAsync(
        string pluginId,
        string commandId,
        CommandInvocationSource source = CommandInvocationSource.Settings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var builtin = FindBuiltinSession(pluginId);
        if (builtin is not null)
        {
            return await builtin.ExecuteCommandAsync(commandId, source, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (_localHost?.Find(pluginId) is not null)
        {
            return await _localHost.ExecuteCommandAsync(pluginId, commandId, source, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return await Host.ExecuteCommandAsync(pluginId, commandId, source, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a host-owned setting value for the generic settings-page renderer.</summary>
    public JsonElement? GetSettingValue(string pluginId, string key)
    {
        var builtin = FindBuiltinSession(pluginId);
        if (builtin is not null)
        {
            return builtin.GetSettingValue(key);
        }

        return Host.Find(pluginId)?.GetSettingValue(key);
    }

    /// <summary>Writes a host-owned setting value and flushes it asynchronously.</summary>
    public bool SetSettingValue(string pluginId, string key, JsonElement value)
    {
        bool written;
        var builtin = FindBuiltinSession(pluginId);
        if (builtin is not null)
        {
            written = builtin.SetSettingValue(key, value);
        }
        else
        {
            written = Host.Find(pluginId)?.SetSettingValue(key, value) == true;
        }

        if (written)
        {
            _ = SaveSettingsAsync(pluginId);
        }

        return written;
    }

    /// <summary>Flushes a plugin's host-owned settings (best effort, never throws at the UI).</summary>
    public async Task SaveSettingsAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        try
        {
            var builtin = FindBuiltinSession(pluginId);
            if (builtin is not null)
            {
                await builtin.SaveSettingsAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (Host.Find(pluginId) is { } handle)
            {
                await handle.SaveSettingsAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.Warn(pluginId, $"Settings could not be saved: {exception.GetType().FullName}: {exception.Message}");
        }
    }

    /// <summary>Finds a built-in session by id.</summary>
    public InProcessPluginSession? FindBuiltinSession(string pluginId)
        => _builtins.FirstOrDefault(entry => string.Equals(entry.Descriptor.Id, pluginId, StringComparison.Ordinal))?.Session;

    /// <summary>Disposes the runtime without blocking the caller (App shutdown path).</summary>
    public void DisposeInBackground() => _ = DisposeInBackgroundAsync();

    /// <summary>Starts a background dispose and returns a task that completes when it finishes.</summary>
    public Task DisposeInBackgroundAsync()
        => Task.Run(async () =>
        {
            try
            {
                await DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.Error("<host>", $"Plugin runtime shutdown failed: {exception.GetType().FullName}: {exception.Message}");
            }
        });

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Host.StateChanged -= OnHostStateChanged;
        foreach (var entry in _builtins)
        {
            if (entry.Session is not null)
            {
                entry.Session.StateChanged -= OnHostStateChanged;
                try
                {
                    await entry.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.Warn(entry.Descriptor.Id, "Built-in plugin shutdown timed out; continuing.");
                }
                catch (Exception exception)
                {
                    _logger.Warn(entry.Descriptor.Id, $"Built-in plugin shutdown failed: {exception.GetType().FullName}: {exception.Message}");
                }
            }
        }

        _builtins.Clear();
        try
        {
            await Host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.Warn("<host>", "Plugin host shutdown timed out; continuing.");
        }
        catch (Exception exception)
        {
            _logger.Error("<host>", $"Plugin host shutdown failed: {exception.GetType().FullName}: {exception.Message}");
        }

        if (_localHost is not null)
        {
            try
            {
                await _localHost.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.Warn("<local-host>", "Local plugin host shutdown timed out; continuing.");
            }
            catch (Exception exception)
            {
                _logger.Error("<local-host>", $"Local plugin host shutdown failed: {exception.GetType().FullName}: {exception.Message}");
            }
        }

        Installs.Dispose();
        if (_ownsCatalog)
        {
            _catalog?.Dispose();
        }

        if (_ownsHttp)
        {
            _http.Dispose();
        }

        RaiseChanged();
    }

    // ---- IPluginContributionUiService ----------------------------------------------------------------

    Task<IReadOnlyList<PluginListItem>> IPluginUiService.ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = new List<PluginListItem>();

        foreach (var entry in _builtins)
        {
            var descriptor = entry.Descriptor;
            var state = entry.Session is null
                ? "Unavailable"
                : _enablement.IsEnabled(descriptor.Id) ? entry.Session.State.ToString() : "Disabled";
            items.Add(new PluginListItem(
                descriptor.Id,
                Resolve(descriptor.DisplayName),
                entry.Session?.Manifest.Version ?? "2.0.0",
                "FluidBar",
                Builtin: true,
                descriptor.Capabilities.Select(static capability => "Capability." + capability).ToList(),
                Orphan: false)
            {
                State = state,
                Error = entry.Error,
                IsEnabled = entry.Session is not null && _enablement.IsEnabled(descriptor.Id),
                RestartRequired = false,
                Available = entry.Session is not null,
            });
        }

        var seen = new HashSet<string>(_builtins.Select(static entry => entry.Descriptor.Id), StringComparer.Ordinal);
        foreach (var handle in Host.Plugins)
        {
            seen.Add(handle.PluginId);
            var enabled = _enablement.IsEnabled(handle.PluginId);
            var state = enabled ? handle.State.ToString() : "Disabled";
            items.Add(new PluginListItem(
                handle.PluginId,
                Resolve(handle.Manifest.Name) is { Length: > 0 } name ? name : handle.PluginId,
                handle.Manifest.Version,
                handle.Manifest.Publisher?.DisplayName ?? handle.Manifest.Publisher?.Id ?? string.Empty,
                Builtin: false,
                handle.Manifest.Capabilities.GetDeclaredCapabilityNames()
                    .Select(static token => PluginCapabilityDisclosureMap.FromTokens([token]).FirstOrDefault().LocalizationKey ?? "Capability." + token)
                    .ToList(),
                Orphan: false)
            {
                State = state,
                Error = handle.LastError,
                IsEnabled = enabled,
                RestartRequired = handle.IsRestartRequired,
                Available = true,
            });
        }

        foreach (var discovery in _discoveries)
        {
            if (discovery.IsValid || seen.Contains(discovery.PluginId))
            {
                continue;
            }

            items.Add(new PluginListItem(
                discovery.PluginId,
                discovery.PluginId,
                string.Empty,
                string.Empty,
                Builtin: false,
                [],
                Orphan: false)
            {
                State = "Invalid",
                Error = string.Join("; ", discovery.Errors),
                IsEnabled = false,
                Available = false,
            });
        }

        return Task.FromResult<IReadOnlyList<PluginListItem>>(items);
    }

    async Task IPluginUiService.SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _enablement.SetEnabled(pluginId, enabled);
        try
        {
            var builtin = FindBuiltinSession(pluginId);
            if (builtin is not null)
            {
                if (enabled)
                {
                    var start = await builtin.StartAsync(cancellationToken).ConfigureAwait(false);
                    if (!start.Success)
                    {
                        _logger.Warn(pluginId, $"Built-in plugin could not be enabled: {start.Message}");
                    }
                }
                else
                {
                    await builtin.StopAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            else if (Host.Find(pluginId) is { } handle)
            {
                if (enabled)
                {
                    if (handle.State == PluginLifecycleState.Stopped)
                    {
                        await Host.StartAsync(pluginId, cancellationToken).ConfigureAwait(false);
                    }
                    else if (handle.State is PluginLifecycleState.Failed or PluginLifecycleState.Disabled or PluginLifecycleState.Unloaded)
                    {
                        await Host.LoadAsync(pluginId, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (handle.State == PluginLifecycleState.Active)
                {
                    await Host.StopAsync(pluginId, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Warn(pluginId, $"Plugin enable/disable failed: {exception.GetType().FullName}: {exception.Message}");
        }

        RaiseChanged();
    }

    string ILocalPluginUiService.LocalPluginRootDirectory => _options.LocalPluginRootDirectory ?? string.Empty;

    IReadOnlyList<LocalPluginListItem> ILocalPluginUiService.ListLocalPlugins()
    {
        if (_localHost is null)
        {
            return [];
        }

        return _localDiscoveries
            .Select(discovery =>
            {
                var handle = _localHost.Find(discovery.PluginId);
                return new LocalPluginListItem(
                    discovery.PluginId,
                    discovery.Manifest?.Version ?? "?",
                    handle?.State == PluginLifecycleState.Active,
                    handle?.LastError ?? (discovery.IsValid ? null : string.Join("; ", discovery.Errors)));
            })
            .ToArray();
    }

    async Task<LocalPluginListItem> ILocalPluginUiService.ReloadLocalPluginAsync(
        string pluginId,
        CancellationToken cancellationToken)
    {
        if (_localHost is null)
        {
            return new LocalPluginListItem(pluginId, "?", false, "local channel is disabled");
        }

        var result = await _localHost.ReloadAsync(pluginId, cancellationToken).ConfigureAwait(false);
        RaiseChanged();
        return new LocalPluginListItem(
            pluginId,
            result.Handle?.Manifest.Version ?? "?",
            result.Success,
            result.Success ? null : result.Message);
    }

    async Task<bool> ILocalPluginUiService.UnloadLocalPluginAsync(
        string pluginId,
        CancellationToken cancellationToken)
    {
        if (_localHost is null)
        {
            return false;
        }

        var result = await _localHost.UnloadAsync(pluginId, cancellationToken).ConfigureAwait(false);
        RaiseChanged();
        return result.Success;
    }

    LocalizedString? IPluginContributionUiService.DisplayName(string pluginId)
        => FindBuiltinDescriptor(pluginId)?.DisplayName ?? Host.Find(pluginId)?.Manifest.Name;

    IReadOnlyList<SettingsPageContribution> IPluginContributionUiService.SettingsPages(string pluginId)
    {
        var descriptor = FindBuiltinDescriptor(pluginId);
        if (descriptor is not null)
        {
            return descriptor.Contributions.SettingsPages ?? [];
        }

        return Host.Find(pluginId)?.Manifest.Contributes?.SettingsPages ?? [];
    }

    IReadOnlyList<CommandContribution> IPluginContributionUiService.Commands(string pluginId)
    {
        var descriptor = FindBuiltinDescriptor(pluginId);
        if (descriptor is not null)
        {
            return descriptor.Contributions.Commands ?? [];
        }

        return Host.Find(pluginId)?.Manifest.Contributes?.Commands ?? [];
    }

    IReadOnlyList<PluginCapability> IPluginContributionUiService.Capabilities(string pluginId)
    {
        var descriptor = FindBuiltinDescriptor(pluginId);
        if (descriptor is not null)
        {
            return descriptor.Capabilities;
        }

        return Host.Find(pluginId)?.Manifest.Capabilities.GetDeclaredCapabilities().ToList() ?? [];
    }

    Task IPluginContributionUiService.InvokeCommandAsync(string pluginId, string commandId)
        => ExecuteCommandAsync(pluginId, commandId).ContinueWith(
            result =>
            {
                if (!result.Result.Succeeded)
                {
                    _logger.Warn(pluginId, $"Command '{commandId}' did not run: {result.Result.Error}");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    // ---- plumbing -------------------------------------------------------------------------------------

    private BuiltinPluginDescriptor? FindBuiltinDescriptor(string pluginId)
        => _builtins.FirstOrDefault(entry => string.Equals(entry.Descriptor.Id, pluginId, StringComparison.Ordinal))?.Descriptor;

    private string Resolve(LocalizedString? value)
    {
        if (value is null || value.IsEmpty)
        {
            return string.Empty;
        }

        return value.Resolve(_options.Localizer.Culture, "en-US");
    }

    private void OnHostStateChanged(object? sender, PluginStateChangedEventArgs e) => RaiseChanged();

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Warn("<host>", $"Plugin UI change handler failed: {exception.GetType().FullName}: {exception.Message}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PluginRuntime));
        }
    }

    /// <summary>Settings values routed to the host-owned per-plugin store.</summary>
    private sealed class RuntimePluginSettingsValues(PluginRuntime owner) : IPluginSettingsValues
    {
        public JsonElement? Get(string pluginId, string key) => owner.GetSettingValue(pluginId, key);

        public void Set(string pluginId, string key, JsonElement value) => owner.SetSettingValue(pluginId, key, value);
    }

    /// <summary>Install-service activator: unload the active version, then load the new version directory.</summary>
    private sealed class HostPluginActivator(PluginHostType host, IPluginHostLogger logger) : IPluginActivator
    {
        public async Task<ActivationResult> ActivateAsync(
            PluginManifest manifest,
            string versionDirectory,
            CancellationToken cancellationToken)
        {
            if (host.Find(manifest.Id) is not null)
            {
                var unload = await host.UnloadAsync(manifest.Id, cancellationToken).ConfigureAwait(false);
                if (!unload.Success || unload.RestartRequired)
                {
                    return ActivationResult.Fail(
                        "activation.unload",
                        unload.FailureReason ?? "The previous plugin version could not be unloaded; restart FluidBar before updating.");
                }
            }

            var load = await host.LoadFromDirectoryAsync(versionDirectory, cancellationToken).ConfigureAwait(false);
            if (load.Success)
            {
                return ActivationResult.Ok();
            }

            logger.Error(manifest.Id, $"Activation of '{versionDirectory}' failed: {load.Message}");
            return ActivationResult.Fail(load.FailureKind.ToString(), load.Message);
        }

        public async Task DeactivateAsync(string pluginId, CancellationToken cancellationToken)
        {
            if (host.Find(pluginId) is not null)
            {
                await host.UnloadAsync(pluginId, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
