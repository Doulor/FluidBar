using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using FluidBar.PluginHost.Loader;
using FluidBar.PluginHost.Logging;
using FluidBar.PluginHost.Runtime;
using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.PluginHost.Loader;

/// <summary>
/// UI-free plugin host: discovery, manifest/API/policy gates, one collectible AssemblyLoadContext per plugin,
/// lifecycle timeouts, failure isolation and best-effort unload verification.
/// </summary>
/// <remarks>
/// Security posture: an in-process .NET plugin runs with the user's full rights. This host isolates types and
/// lifetimes, not privileges; capability metadata is disclosure/consent only. Signing and review are the trust
/// boundary and are enforced before the host ever sees a package directory.
/// </remarks>
public sealed class PluginHost : IAsyncDisposable
{
    private const int MaxCollectionAttempts = 12;

    private readonly PluginHostOptions _options;
    private readonly IPluginHostLogger _logger;
    private readonly HostAssemblyCatalog _hostAssemblies;
    private readonly PluginDiscoverer _discoverer;
    private readonly Dictionary<string, PluginHandle> _handles = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _registryGate = new();
    private bool _disposed;

    public PluginHost(PluginHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _logger = options.Logger;
        _hostAssemblies = new HostAssemblyCatalog(options.AdditionalSharedAssemblies);
        _discoverer = new PluginDiscoverer(options);

        if (options.CreateDirectories)
        {
            Directory.CreateDirectory(options.PluginRootDirectory);
            Directory.CreateDirectory(options.ResolveSettingsRoot());
            Directory.CreateDirectory(options.ResolveDataRoot());
        }
    }

    /// <summary>Raised on every lifecycle transition; handlers must be fast and must not call back into the host.</summary>
    public event EventHandler<PluginStateChangedEventArgs>? StateChanged;

    /// <summary>Snapshot of all known handles (including failed/disabled ones).</summary>
    public IReadOnlyList<PluginHandle> Plugins
    {
        get
        {
            lock (_registryGate)
            {
                return [.. _handles.Values];
            }
        }
    }

    /// <summary>Finds a handle by plugin id.</summary>
    public PluginHandle? Find(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        lock (_registryGate)
        {
            return _handles.TryGetValue(pluginId, out var handle) ? handle : null;
        }
    }

    /// <summary>Discovers plugin packages without loading them (used by the plugins settings page).</summary>
    public async Task<IReadOnlyList<PluginDiscoveryInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _discoverer.DiscoverAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads every discovered, valid, compatible plugin. One failure never prevents the others.</summary>
    public async Task<IReadOnlyList<PluginLoadResult>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var discoveries = await _discoverer.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<PluginLoadResult>(discoveries.Count);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var discovery in discoveries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!discovery.IsValid || discovery.Manifest is null)
                {
                    if (discovery.Manifest is null)
                    {
                        _logger.Error(discovery.PluginId, $"Plugin package rejected during discovery: {string.Join("; ", discovery.Errors)}");
                        results.Add(PluginLoadResult.Fail(null, PluginLoadFailureKind.ManifestInvalid, string.Join("; ", discovery.Errors), [.. discovery.Errors]));
                        continue;
                    }

                    // A parsed manifest still goes through LoadCore so apiVersion mismatches are classified
                    // as Incompatible and validation failures carry precise codes.
                }

                var existing = Find(discovery.PluginId);
                if (existing?.State == PluginLifecycleState.Active)
                {
                    results.Add(PluginLoadResult.Ok(existing));
                    continue;
                }

                var handle = ResolveHandle(discovery.Manifest, discovery.PackageDirectory);
                results.Add(await LoadCoreAsync(handle, cancellationToken).ConfigureAwait(false));
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        return results;
    }

    /// <summary>Loads one plugin by id from the plugin root.</summary>
    public async Task<PluginLoadResult> LoadAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = Find(pluginId);
            if (existing?.State == PluginLifecycleState.Active)
            {
                return PluginLoadResult.Ok(existing);
            }

            var discoveries = await _discoverer.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            var discovery = discoveries.FirstOrDefault(candidate =>
                string.Equals(candidate.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));
            if (discovery is null)
            {
                return PluginLoadResult.Fail(
                    existing,
                    PluginLoadFailureKind.ManifestMissing,
                    $"No plugin package '{pluginId}' was found under '{_options.PluginRootDirectory}'.");
            }

            if (!discovery.IsValid || discovery.Manifest is null)
            {
                if (discovery.Manifest is null)
                {
                    return PluginLoadResult.Fail(
                        existing,
                        PluginLoadFailureKind.ManifestInvalid,
                        string.Join("; ", discovery.Errors),
                        [.. discovery.Errors]);
                }
            }

            var handle = ResolveHandle(discovery.Manifest, discovery.PackageDirectory);
            return await LoadCoreAsync(handle, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>Loads a plugin directly from a package directory (developer loop / offline dev install).</summary>
    public async Task<PluginLoadResult> LoadFromDirectoryAsync(string packageDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ThrowIfDisposed();
        var manifestPath = Path.Combine(packageDirectory, "manifest.json");
        PluginManifest manifest;
        try
        {
            manifest = await PluginManifestParser.ParseFileAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        }
        catch (PluginManifestException exception)
        {
            _logger.Error("<host>", $"Could not read manifest from '{packageDirectory}': {exception.Message}");
            return PluginLoadResult.Fail(null, PluginLoadFailureKind.ManifestInvalid, exception.Message, exception.Errors);
        }
        catch (IOException exception)
        {
            return PluginLoadResult.Fail(null, PluginLoadFailureKind.ManifestInvalid, exception.Message);
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var handle = ResolveHandle(manifest, Path.GetFullPath(packageDirectory));
            return await LoadCoreAsync(handle, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>Stops a plugin without unloading it (user disables a plugin in settings).</summary>
    public async Task<bool> StopAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var handle = Find(pluginId);
            if (handle is null)
            {
                return false;
            }

            await StopSessionAsync(handle, cancellationToken).ConfigureAwait(false);
            var stopped = handle.State == PluginLifecycleState.Stopped;
            if (stopped)
            {
                _options.PluginStopped?.Invoke(pluginId);
            }

            return stopped;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>Starts a stopped (still loaded) plugin again.</summary>
    public async Task<PluginLoadResult> StartAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var handle = Find(pluginId);
            if (handle is null)
            {
                return PluginLoadResult.Fail(null, PluginLoadFailureKind.Disabled, $"Unknown plugin '{pluginId}'.");
            }

            if (handle.State != PluginLifecycleState.Stopped)
            {
                return PluginLoadResult.Fail(handle, PluginLoadFailureKind.Disabled, $"Plugin is in state {handle.State}; only a stopped plugin can be started.");
            }

            var start = await RunStartOnlyAsync(handle, cancellationToken).ConfigureAwait(false);
            return start.Success ? PluginLoadResult.Ok(handle) : PluginLoadResult.Fail(handle, start.FailureKind, start.Message);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>Unloads one plugin and verifies collection with a WeakReference + loaded-assembly check.</summary>
    public async Task<PluginUnloadResult> UnloadAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var handle = Find(pluginId);
            if (handle is null)
            {
                return new PluginUnloadResult(pluginId, false, false, 0, TimeSpan.Zero, false, $"Unknown plugin '{pluginId}'.");
            }

            var unloaded = await UnloadCoreAsync(handle, cancellationToken).ConfigureAwait(false);
            if (unloaded.Success)
            {
                _options.PluginStopped?.Invoke(pluginId);
            }

            return unloaded;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>Unloads then loads the plugin again (dev loop, version switch after a verified unload).</summary>
    public async Task<PluginLoadResult> ReloadAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var unloaded = await UnloadAsync(pluginId, cancellationToken).ConfigureAwait(false);
        if (!unloaded.Success || unloaded.RestartRequired)
        {
            return PluginLoadResult.Fail(
                Find(pluginId),
                PluginLoadFailureKind.Unloaded,
                unloaded.FailureReason ?? "Unload did not prove collection; restart FluidBar before reloading.");
        }

        return await LoadAsync(pluginId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Invokes a manifest-declared command with host timeout and exception isolation.</summary>
    public async Task<PluginCommandExecutionResult> ExecuteCommandAsync(
        string pluginId,
        string commandId,
        CommandInvocationSource source = CommandInvocationSource.Programmatic,
        IReadOnlyDictionary<string, string?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ThrowIfDisposed();

        var handle = Find(pluginId);
        if (handle is null || handle.State != PluginLifecycleState.Active || handle.Runtime is null)
        {
            return PluginCommandExecutionResult.NotAvailable($"Plugin '{pluginId}' is not active.");
        }

        var runtime = handle.Runtime;
        if (!runtime.Commands.TryGetHandler(commandId, out var handler) || handler is null)
        {
            return PluginCommandExecutionResult.NotAvailable($"Command '{commandId}' has no handler registered.");
        }

        var commandContext = new PluginCommandContext
        {
            PluginId = pluginId,
            CommandId = commandId,
            Source = source,
            Arguments = arguments,
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.CommandTimeout);
            await handler(commandContext, timeout.Token).AsTask().WaitAsync(_options.CommandTimeout, cancellationToken).ConfigureAwait(false);
            return PluginCommandExecutionResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PluginCommandExecutionResult.Failed("Command invocation was cancelled.");
        }
        catch (TimeoutException)
        {
            _logger.Warn(pluginId, $"Command '{commandId}' exceeded the {_options.CommandTimeout} timeout.");
            return PluginCommandExecutionResult.Failed("Command timed out.");
        }
        catch (OperationCanceledException)
        {
            return PluginCommandExecutionResult.Failed("Command timed out.");
        }
        catch (Exception exception)
        {
            // Format immediately: never retain the exception object (it can root the plugin ALC).
            _logger.Error(pluginId, $"Command '{commandId}' failed: {DescribeException(exception)}");
            return PluginCommandExecutionResult.Failed("Command failed; see the plugin log for details.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        PluginHandle[] handles;
        lock (_registryGate)
        {
            handles = [.. _handles.Values];
        }

        foreach (var handle in handles)
        {
            try
            {
                await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    await UnloadCoreAsync(handle, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _lifecycleGate.Release();
                }
            }
            catch (Exception exception)
            {
                _logger.Error(handle.PluginId, $"Plugin unload during host shutdown failed: {DescribeException(exception)}");
            }
        }

        _lifecycleGate.Dispose();
    }

    // ---- load pipeline -------------------------------------------------------------------------------------

    private async Task<PluginLoadResult> LoadCoreAsync(PluginHandle handle, CancellationToken cancellationToken)
    {
        var manifest = handle.Manifest;
        handle.LastLoadAttempt = DateTimeOffset.UtcNow;
        handle.LastError = null;
        handle.LastValidationErrors = [];

        // 1. Manifest validation (JSON schema rules + cross-field policy) BEFORE any assembly is touched.
        if (!TryTransition(handle, PluginLifecycleState.Validating))
        {
            return PluginLoadResult.Fail(handle, PluginLoadFailureKind.Unexpected, $"Cannot validate from state {handle.State}.");
        }

        // 1. API / app-version gates BEFORE validation details and BEFORE any assembly is touched.
        //    (Keeps an apiVersion-major mismatch classified as "incompatible", not "invalid manifest".)
        if (manifest.TryGetApiVersion(out _) &&
            manifest.TryGetMinAppVersion(out _) &&
            manifest.TryGetMaxAppVersion(out _))
        {
            var earlyCompatibility = CheckCompatibility(manifest);
            if (!earlyCompatibility.IsCompatible)
            {
                handle.LastError = earlyCompatibility.Reason;
                TryTransition(handle, PluginLifecycleState.Incompatible);
                _logger.Error(handle.PluginId, $"Plugin incompatible: {earlyCompatibility.Reason}");
                return PluginLoadResult.Fail(handle, PluginLoadFailureKind.Incompatible, earlyCompatibility.Reason);
            }
        }

        var validation = PluginManifestValidator.Validate(manifest, _options.ValidationOptions);
        if (!validation.IsValid)
        {
            var errors = validation.Errors.Select(static issue => issue.ToString()).ToArray();
            handle.LastValidationErrors = errors;
            return await FailLoadAsync(
                handle,
                PluginLoadFailureKind.ManifestInvalid,
                $"Manifest validation failed: {string.Join("; ", errors)}",
                cancellationToken).ConfigureAwait(false);
        }

        // 2. API / app-version gates BEFORE creating an ALC.
        var compatibility = CheckCompatibility(manifest);
        if (!compatibility.IsCompatible)
        {
            handle.LastError = compatibility.Reason;
            TryTransition(handle, PluginLifecycleState.Incompatible);
            _logger.Error(handle.PluginId, $"Plugin incompatible: {compatibility.Reason}");
            return PluginLoadResult.Fail(handle, PluginLoadFailureKind.Incompatible, compatibility.Reason);
        }

        // 3. Package policy/security scan BEFORE creating an ALC (signature/hash is verified by the installer).
        var inspection = PluginPackageInspector.Inspect(handle.PackageDirectory, manifest, _hostAssemblies);
        if (!inspection.IsValid)
        {
            var issues = inspection.Errors.Select(static issue => $"{issue.Code} {issue.Path}: {issue.Message}").ToArray();
            handle.LastValidationErrors = issues;
            var failureKind = inspection.Errors.Any(static issue =>
                issue.Code is "package.entry.notFound" or "package.entry.outside" or "package.entry.missing")
                ? PluginLoadFailureKind.EntryMissing
                : PluginLoadFailureKind.ForbiddenReference;
            return await FailLoadAsync(
                handle,
                failureKind,
                $"Package inspection failed: {string.Join("; ", issues)}",
                cancellationToken).ConfigureAwait(false);
        }

        if (!TryTransition(handle, PluginLifecycleState.Validated))
        {
            return PluginLoadResult.Fail(handle, PluginLoadFailureKind.Unexpected, $"Cannot load from state {handle.State}.");
        }

        // 4. Entry path containment (zip-slip defence) before any I/O outside the package.
        if (manifest.Entry is not { } entry)
        {
            return await FailLoadAsync(handle, PluginLoadFailureKind.EntryMissing, "Manifest has no entry block.", cancellationToken).ConfigureAwait(false);
        }

        if (!PluginPackageInspector.TryResolvePackagePath(handle.PackageDirectory, entry.Assembly, out var entryPath, out var entryError) ||
            !File.Exists(entryPath))
        {
            return await FailLoadAsync(
                handle,
                PluginLoadFailureKind.EntryMissing,
                entryError ?? $"Entry assembly '{entry.Assembly}' is missing from the package.",
                cancellationToken).ConfigureAwait(false);
        }

        if (!TryTransition(handle, PluginLifecycleState.Loading))
        {
            return PluginLoadResult.Fail(handle, PluginLoadFailureKind.Unexpected, $"Cannot load from state {handle.State}.");
        }

        // 5. Activate: create ALC, load assembly, resolve type, construct instance, build the context.
        var activation = CreateSession(handle, entryPath);
        if (activation.FailureKind != PluginLoadFailureKind.None)
        {
            return await FailLoadAsync(handle, activation.FailureKind, activation.Message, cancellationToken).ConfigureAwait(false);
        }

        // 6. Initialize + Start with host timeouts and exception isolation.
        var start = await RunInitializeAndStartAsync(handle, cancellationToken).ConfigureAwait(false);
        if (!start.Success)
        {
            return await FailLoadAsync(handle, start.FailureKind, start.Message, cancellationToken).ConfigureAwait(false);
        }

        handle.ResetFailures();
        handle.IsRestartRequired = false;
        handle.ActivatedAt = DateTimeOffset.UtcNow;
        _logger.Info(handle.PluginId, $"Plugin v{manifest.Version} activated.");
        return PluginLoadResult.Ok(handle);
    }

    private VersionCompatibility CheckCompatibility(PluginManifest manifest)
    {
        if (!manifest.TryGetApiVersion(out var apiVersion) ||
            !manifest.TryGetMinAppVersion(out var minAppVersion) ||
            !manifest.TryGetMaxAppVersion(out var maxAppVersion))
        {
            return new VersionCompatibility(false, VersionCompatibilityFailure.InvalidVersion, "Manifest versions could not be parsed.");
        }

        var compatibility = PluginApiVersion.CheckCompatibility(
            apiVersion,
            minAppVersion,
            maxAppVersion,
            _options.HostApiVersion,
            _options.AppVersion);
        if (!compatibility.IsCompatible)
        {
            return compatibility;
        }

        if (manifest.Dependencies?.Sdk is { } sdkRange)
        {
            return PluginApiVersion.CheckSdkDependency(sdkRange, _options.HostApiVersion);
        }

        return VersionCompatibility.Compatible;
    }

    /// <summary>Creates the plugin session. Synchronous and non-inlined: no plugin reference escapes this frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private CreateSessionResult CreateSession(PluginHandle handle, string entryPath)
    {
        var manifest = handle.Manifest;
        var loadContext = new PluginLoadContext(entryPath, handle.PluginId, _hostAssemblies);
        handle.LoadContext = loadContext;
        handle.LoadContextReference = new WeakReference(loadContext, trackResurrection: false);

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(entryPath);
            var entryType = manifest.Entry!.Type;
            var type = assembly.GetType(entryType, throwOnError: false);
            if (type is null)
            {
                return new CreateSessionResult(PluginLoadFailureKind.EntryTypeMissing, $"Entry type '{entryType}' was not found.");
            }

            if (!typeof(IFluidBarPlugin).IsAssignableFrom(type))
            {
                return new CreateSessionResult(
                    PluginLoadFailureKind.EntryTypeInvalid,
                    $"Entry type '{entryType}' does not implement {nameof(IFluidBarPlugin)} from the host FluidBar.Sdk identity.");
            }

            if (Activator.CreateInstance(type) is not IFluidBarPlugin instance)
            {
                return new CreateSessionResult(PluginLoadFailureKind.ConstructorFailed, "Entry type could not be constructed as a plugin instance.");
            }

            handle.PluginInstance = instance;
            handle.PluginInstanceReference = new WeakReference(instance, trackResurrection: false);
            if (!TryTransition(handle, PluginLifecycleState.Loaded))
            {
                return new CreateSessionResult(PluginLoadFailureKind.Unexpected, $"Cannot load from state {handle.State}.");
            }

            handle.Runtime = CreateRuntime(handle);
            return CreateSessionResult.Success;
        }
        catch (TargetInvocationException exception)
        {
            return new CreateSessionResult(
                PluginLoadFailureKind.ConstructorFailed,
                DescribeException(exception.InnerException ?? exception));
        }
        catch (Exception exception) when (exception is FileLoadException or FileNotFoundException or BadImageFormatException or TypeLoadException or MissingMethodException)
        {
            return new CreateSessionResult(PluginLoadFailureKind.EntryTypeInvalid, DescribeException(exception));
        }
        catch (PluginLocalizationException exception)
        {
            return new CreateSessionResult(PluginLoadFailureKind.InitializeFailed, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CreateSessionResult(PluginLoadFailureKind.Unexpected, DescribeException(exception));
        }
    }

    private PluginRuntimeRoot CreateRuntime(PluginHandle handle)
    {
        var manifest = handle.Manifest;
        var dataDirectory = handle.DataDirectory;
        if (_options.CreateDirectories)
        {
            Directory.CreateDirectory(dataDirectory);
        }

        var settingsPath = Path.Combine(_options.ResolveSettingsRoot(), manifest.Id, "settings.json");
        var settings = new PluginSettingsStore(manifest.Id, settingsPath, _logger);
        settings.Load();

        var storage = new PluginStorage(dataDirectory);
        var localizer = PluginLocalizer.Create(manifest.Id, handle.PackageDirectory, manifest, _options.HostUiLocales, _logger);
        var commands = new PluginCommandRegistry(manifest.Id, manifest, _logger);
        var leases = new PluginLeaseCoordinator(manifest.Id, _logger, _options.TimeProvider);

        var islandPublisher = _options.IslandPublisherFactory?.Invoke(manifest) ?? NullIslandPublisher.Instance;
        var notifications = _options.NotificationSinkFactory?.Invoke(manifest) ?? NullNotificationSink.Instance;
        var context = new PluginContext(
            manifest,
            handle.PackageDirectory,
            dataDirectory,
            _options.HostApiVersion,
            new PluginIslandPublisher(manifest.Id, islandPublisher, _logger, manifest),
            localizer,
            settings,
            storage,
            new PluginLogger(manifest.Id, _logger),
            _options.Dispatcher ?? InlineHostDispatcher.Instance,
            leases,
            commands,
            new PluginNotificationSink(manifest.Id, notifications, _logger),
            PluginCapabilityGrants.Resolve(manifest, _options.GrantedCapabilities),
            _options.TimeProvider,
            _logger);

        return new PluginRuntimeRoot(context, settings, leases, commands, manifest.Id, _logger);
    }

    /// <summary>Runs Initialize + Start. Async frame holds plugin references only until it returns.</summary>
    private async Task<LifecycleResult> RunInitializeAndStartAsync(PluginHandle handle, CancellationToken cancellationToken)
    {
        var instance = handle.PluginInstance as IFluidBarPlugin;
        var runtime = handle.Runtime;
        if (instance is null || runtime is null)
        {
            return LifecycleResult.Fail(PluginLoadFailureKind.EntryTypeInvalid, "Plugin session is incomplete.");
        }

        if (!TryTransition(handle, PluginLifecycleState.Initializing))
        {
            return LifecycleResult.Fail(PluginLoadFailureKind.Unexpected, $"Cannot initialize from state {handle.State}.");
        }

        var initialize = await AwaitLifecycleAsync(
            token => instance.InitializeAsync(runtime.Context, token),
            _options.InitializeTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!initialize.Success)
        {
            return LifecycleResult.Fail(
                initialize.TimedOut ? PluginLoadFailureKind.Timeout : PluginLoadFailureKind.InitializeFailed,
                initialize.Message);
        }

        if (!TryTransition(handle, PluginLifecycleState.Initialized) ||
            !TryTransition(handle, PluginLifecycleState.Starting))
        {
            return LifecycleResult.Fail(PluginLoadFailureKind.Unexpected, $"Cannot start from state {handle.State}.");
        }

        var start = await AwaitLifecycleAsync(
            token => instance.StartAsync(token),
            _options.StartTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!start.Success)
        {
            return LifecycleResult.Fail(
                start.TimedOut ? PluginLoadFailureKind.Timeout : PluginLoadFailureKind.StartFailed,
                start.Message);
        }

        return TryTransition(handle, PluginLifecycleState.Active)
            ? LifecycleResult.Ok
            : LifecycleResult.Fail(PluginLoadFailureKind.Unexpected, $"Cannot activate from state {handle.State}.");
    }

    /// <summary>Starts a plugin that is already loaded (re-enable after <see cref="StopAsync"/>).</summary>
    private async Task<LifecycleResult> RunStartOnlyAsync(PluginHandle handle, CancellationToken cancellationToken)
    {
        var instance = handle.PluginInstance as IFluidBarPlugin;
        if (instance is null)
        {
            return LifecycleResult.Fail(PluginLoadFailureKind.Disabled, "Plugin instance is not loaded.");
        }

        if (!TryTransition(handle, PluginLifecycleState.Starting))
        {
            return LifecycleResult.Fail(PluginLoadFailureKind.Unexpected, $"Cannot start from state {handle.State}.");
        }

        var start = await AwaitLifecycleAsync(
            token => instance.StartAsync(token),
            _options.StartTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!start.Success)
        {
            return LifecycleResult.Fail(start.TimedOut ? PluginLoadFailureKind.Timeout : PluginLoadFailureKind.StartFailed, start.Message);
        }

        handle.ResetFailures();
        handle.ActivatedAt = DateTimeOffset.UtcNow;
        return TryTransition(handle, PluginLifecycleState.Active)
            ? LifecycleResult.Ok
            : LifecycleResult.Fail(PluginLoadFailureKind.Unexpected, $"Cannot activate from state {handle.State}.");
    }

    /// <summary>Isolates a failed load: dispose the session, drop references, unload the ALC, verify collection.</summary>
    private async Task<PluginLoadResult> FailLoadAsync(
        PluginHandle handle,
        PluginLoadFailureKind failureKind,
        string message,
        CancellationToken cancellationToken)
    {
        handle.RecordFailure();
        handle.LastError = message;
        _logger.Error(handle.PluginId, $"Plugin load failed ({failureKind}): {message}");

        await DisposeSessionQuietlyAsync(handle).ConfigureAwait(false);
        var references = PrepareUnload(handle);
        if (PluginLifecycleTransitions.CanTransition(handle.State, PluginLifecycleState.Unloading))
        {
            TryTransition(handle, PluginLifecycleState.Unloading);
        }

        var evidence = await CollectAsync(references, _options.UnloadCollectionTimeout, cancellationToken).ConfigureAwait(false);
        if (!evidence.Collected)
        {
            handle.IsRestartRequired = true;
            _logger.Warn(handle.PluginId, $"Failed plugin ALC was not collected after {evidence.Attempts} GC attempt(s); restart required to finish cleanup.");
        }

        var nextState = handle.ConsecutiveFailures >= _options.MaxConsecutiveFailures
            ? PluginLifecycleState.Disabled
            : PluginLifecycleState.Failed;
        TryTransition(handle, nextState);
        return PluginLoadResult.Fail(handle, failureKind, message);
    }

    private async Task<PluginUnloadResult> UnloadCoreAsync(PluginHandle handle, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (handle.State == PluginLifecycleState.Unloaded)
        {
            return new PluginUnloadResult(
                handle.PluginId,
                true,
                !handle.IsRestartRequired,
                0,
                stopwatch.Elapsed,
                handle.IsRestartRequired,
                handle.IsRestartRequired ? "Previous unload did not prove collection; restart required." : null);
        }

        if (handle.State is PluginLifecycleState.Active or PluginLifecycleState.Initialized or PluginLifecycleState.Starting)
        {
            await StopSessionAsync(handle, cancellationToken).ConfigureAwait(false);
        }

        await DisposeSessionQuietlyAsync(handle).ConfigureAwait(false);
        var references = PrepareUnload(handle);
        if (PluginLifecycleTransitions.CanTransition(handle.State, PluginLifecycleState.Unloading))
        {
            TryTransition(handle, PluginLifecycleState.Unloading);
        }

        var evidence = await CollectAsync(references, _options.UnloadCollectionTimeout, cancellationToken).ConfigureAwait(false);
        TryTransition(handle, PluginLifecycleState.Unloaded);
        handle.IsRestartRequired = !evidence.Collected;
        if (evidence.Collected)
        {
            _logger.Info(handle.PluginId, $"Plugin unloaded and collected ({evidence.Attempts} GC attempt(s), {stopwatch.ElapsedMilliseconds} ms).");
        }
        else
        {
            _logger.Warn(
                handle.PluginId,
                $"Plugin unload could not prove collection after {evidence.Attempts} GC attempt(s); {evidence.RemainingAssemblies} plugin assembly/assemblies still loaded. Restart required.");
        }

        return new PluginUnloadResult(
            handle.PluginId,
            true,
            evidence.Collected,
            evidence.Attempts,
            stopwatch.Elapsed,
            !evidence.Collected,
            evidence.Collected ? null : "AssemblyLoadContext or plugin assembly is still loaded; restart FluidBar to finish removing it.");
    }

    private async Task StopSessionAsync(PluginHandle handle, CancellationToken cancellationToken)
    {
        if (handle.PluginInstance is not IFluidBarPlugin instance)
        {
            return;
        }

        if (!TryTransition(handle, PluginLifecycleState.Stopping))
        {
            return;
        }

        var outcome = await AwaitLifecycleAsync(
            token => instance.StopAsync(token),
            _options.StopTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!outcome.Success)
        {
            _logger.Warn(handle.PluginId, $"Plugin StopAsync did not complete cleanly: {outcome.Message}");
        }

        TryTransition(handle, PluginLifecycleState.Stopped);
    }

    /// <summary>Disposes the plugin and its runtime services. The async frame ends before collection starts.</summary>
    private async Task DisposeSessionQuietlyAsync(PluginHandle handle)
    {
        var runtime = handle.Runtime;
        handle.Runtime = null;
        if (runtime is not null)
        {
            try
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.Warn(handle.PluginId, $"Runtime service disposal failed: {DescribeException(exception)}");
            }
        }

        var instance = handle.PluginInstance as IFluidBarPlugin;
        handle.PluginInstance = null;
        if (instance is null)
        {
            return;
        }

        try
        {
            await instance.DisposeAsync().AsTask().WaitAsync(_options.DisposeTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.Warn(handle.PluginId, $"Plugin DisposeAsync exceeded the {_options.DisposeTimeout} timeout.");
        }
        catch (Exception exception)
        {
            _logger.Warn(handle.PluginId, $"Plugin DisposeAsync failed: {DescribeException(exception)}");
        }
    }

    /// <summary>Drops every strong host reference and requests ALC unload. Non-inlined so JIT locals cannot pin.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static UnloadReferences PrepareUnload(PluginHandle handle)
    {
        var instanceReference = handle.PluginInstanceReference;
        var loadContextReference = handle.LoadContextReference;
        var loadContext = handle.LoadContext;
        var loadContextName = loadContext?.Name;

        handle.PluginInstance = null;
        handle.Runtime = null;
        handle.LoadContext = null;

        try
        {
            loadContext?.Unload();
        }
        catch (InvalidOperationException)
        {
            // The runtime rejected/double-requested unload; the WeakReference check below is authoritative.
        }

        return new UnloadReferences(instanceReference, loadContextReference, handle.EntryAssemblyName, loadContextName);
    }

    /// <summary>
    /// Forced-GC collection loop in a separate frame (JIT local lifetimes can fake a leak).
    /// <c>AssemblyLoadContext.All</c> is NOT proof: it drops ALCs on the unload request, so WeakReferences and
    /// the loaded-assembly count are the evidence.
    /// </summary>
    private static async Task<CollectionEvidence> CollectAsync(UnloadReferences references, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (references.LoadContextReference is null && references.InstanceReference is null)
        {
            return new CollectionEvidence(true, 0, 0);
        }

        var attempts = 0;
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            attempts++;
            for (var i = 0; i < 2; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
            }

            var instanceAlive = references.InstanceReference?.IsAlive == true;
            var contextAlive = references.LoadContextReference?.IsAlive == true;
            var remainingAssemblies = CountLoadedAssemblies(references.EntryAssemblyName, references.LoadContextName);
            if (!instanceAlive && !contextAlive && remainingAssemblies == 0)
            {
                return new CollectionEvidence(true, attempts, 0);
            }

            if (attempts >= MaxCollectionAttempts || stopwatch.Elapsed >= timeout)
            {
                return new CollectionEvidence(false, attempts, remainingAssemblies);
            }

            await Task.Delay(15, cancellationToken).ConfigureAwait(false);
        }
    }

    private static int CountLoadedAssemblies(string? entryAssemblyName, string? loadContextName)
    {
        if (string.IsNullOrWhiteSpace(entryAssemblyName))
        {
            return 0;
        }

        var count = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, entryAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Count only this load's ALC: a previously leaked fixture ALC must not poison the evidence.
            if (loadContextName is not null &&
                !string.Equals(AssemblyLoadContext.GetLoadContext(assembly)?.Name, loadContextName, StringComparison.Ordinal))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private async Task<LifecycleOutcome> AwaitLifecycleAsync(
        Func<CancellationToken, ValueTask> action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // The linked CTS lets the host cancel a plugin call that exceeded its timeout, so hung work does not
        // keep the plugin instance/ALC alive after the loader moved on.
        using var callCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callCancellation.CancelAfter(timeout);
        try
        {
            await action(callCancellation.Token).AsTask().WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return new LifecycleOutcome(true, false, string.Empty);
        }
        catch (TimeoutException)
        {
            return new LifecycleOutcome(false, true, $"Lifecycle call exceeded the {timeout.TotalMilliseconds:0} ms timeout.");
        }
        catch (OperationCanceledException) when (callCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A cooperative plugin observes the timeout token and throws OCE before WaitAsync raises TimeoutException.
            // Both paths mean "the host timeout fired", never "the plugin failed to start".
            return new LifecycleOutcome(false, true, $"Lifecycle call exceeded the {timeout.TotalMilliseconds:0} ms timeout.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new LifecycleOutcome(false, false, "Lifecycle call was cancelled.");
        }
        catch (Exception exception)
        {
            return new LifecycleOutcome(false, false, DescribeException(exception));
        }
    }

    // ---- plumbing -------------------------------------------------------------------------------------------

    private PluginHandle CreateHandle(PluginManifest manifest, string packageDirectory)
    {
        var dataDirectory = Path.Combine(_options.ResolveDataRoot(), manifest.Id, "data");
        if (_options.CreateDirectories)
        {
            Directory.CreateDirectory(dataDirectory);
        }

        var entryAssemblyName = manifest.Entry is { Assembly.Length: > 0 } entry
            ? Path.GetFileNameWithoutExtension(entry.Assembly)
            : null;
        return new PluginHandle(manifest, packageDirectory, dataDirectory, entryAssemblyName);
    }

    private void RegisterHandle(PluginHandle handle)
    {
        lock (_registryGate)
        {
            _handles[handle.PluginId] = handle;
        }
    }

    /// <summary>
    /// Reuses an existing handle for the same installed version so failure counters, state and the plugins UI
    /// stay consistent across retries/reloads; a different package/version gets a fresh handle.
    /// </summary>
    private PluginHandle ResolveHandle(PluginManifest manifest, string packageDirectory)
    {
        var existing = Find(manifest.Id);
        if (existing is not null &&
            string.Equals(existing.PackageDirectory, packageDirectory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Manifest.Version, manifest.Version, StringComparison.Ordinal))
        {
            return existing;
        }

        var handle = CreateHandle(manifest, packageDirectory);
        RegisterHandle(handle);
        return handle;
    }

    private bool TryTransition(PluginHandle handle, PluginLifecycleState next, string? error = null)
    {
        var previous = handle.State;
        if (previous == next)
        {
            return true;
        }

        if (!PluginLifecycleTransitions.CanTransition(previous, next))
        {
            // Deterministic loader: an invalid transition is refused and logged instead of corrupting state.
            _logger.Error(handle.PluginId, $"Refused invalid lifecycle transition {previous} -> {next}.");
            return false;
        }

        handle.State = next;
        _logger.Debug(handle.PluginId, $"Lifecycle {previous} -> {next}");
        var handlers = StateChanged;
        if (handlers is not null)
        {
            try
            {
                handlers(this, new PluginStateChangedEventArgs(handle.PluginId, previous, next, error));
            }
            catch (Exception exception)
            {
                _logger.Error(handle.PluginId, $"StateChanged handler failed: {DescribeException(exception)}");
            }
        }

        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PluginHost));
        }
    }

    private static string DescribeException(Exception exception) =>
        $"{exception.GetType().FullName}: {exception.Message}";

    private readonly record struct CreateSessionResult(PluginLoadFailureKind FailureKind, string Message)
    {
        internal static CreateSessionResult Success { get; } = new(PluginLoadFailureKind.None, string.Empty);
    }

    private readonly record struct LifecycleOutcome(bool Success, bool TimedOut, string Message);

    private readonly record struct LifecycleResult(bool Success, PluginLoadFailureKind FailureKind, string Message)
    {
        internal static LifecycleResult Ok { get; } = new(true, PluginLoadFailureKind.None, string.Empty);

        internal static LifecycleResult Fail(PluginLoadFailureKind kind, string message) => new(false, kind, message);
    }

    private readonly record struct UnloadReferences(
        WeakReference? InstanceReference,
        WeakReference? LoadContextReference,
        string? EntryAssemblyName,
        string? LoadContextName);

    private readonly record struct CollectionEvidence(bool Collected, int Attempts, int RemainingAssemblies);
}
