using System.Text.Json;
using FluidBar.PluginHost.Logging;
using FluidBar.PluginHost.Runtime;
using FluidBar.Sdk;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Localization;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.PluginHost.Loader;

/// <summary>
/// Runs one first-party (compiled-in) plugin through the same SDK lifecycle and per-plugin services as a
/// loaded package, without an AssemblyLoadContext. Built-ins ship inside the App and therefore cannot be
/// isolated by a collectible ALC; they still get host-owned settings, storage, localization, leases, a
/// command registry, the App dispatcher and the App island publisher.
/// </summary>
/// <remarks>
/// <para>This is the documented built-in lane: <c>docs/rewrite/contracts/builtin-plugins.md</c> requires the
/// built-ins to use the same <see cref="IFluidBarPlugin"/> lifecycle as community plugins. Failure isolation
/// matches <see cref="PluginHost"/>: a throwing lifecycle call is logged, the plugin becomes
/// <see cref="PluginLifecycleState.Failed"/> and the App keeps running.</para>
/// <para>There is no manifest validation or package inspection here: the manifest is synthesized by the App
/// from the compiled-in descriptor, so it is trusted by construction.</para>
/// </remarks>
public sealed class InProcessPluginSession : IAsyncDisposable
{
    private readonly PluginHostOptions _options;
    private readonly IPluginHostLogger _logger;
    private readonly IFluidBarPlugin _plugin;
    private readonly string _packageDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PluginRuntimeRoot? _runtime;
    private bool _disposed;

    /// <summary>Creates a session for one compiled-in plugin.</summary>
    /// <param name="options">Host configuration (paths, timeouts, dispatcher, publishers, grants).</param>
    /// <param name="manifest">Synthetic manifest built from the compiled-in descriptor.</param>
    /// <param name="packageDirectory">Directory used for locale lookups (normally the App directory).</param>
    /// <param name="plugin">Constructed plugin instance.</param>
    public InProcessPluginSession(
        PluginHostOptions options,
        PluginManifest manifest,
        string packageDirectory,
        IFluidBarPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentNullException.ThrowIfNull(plugin);
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new ArgumentException("The synthetic manifest must have a plugin id.", nameof(manifest));
        }

        _options = options;
        _logger = options.Logger;
        Manifest = manifest;
        _packageDirectory = Path.GetFullPath(packageDirectory);
        _plugin = plugin;
        State = PluginLifecycleState.Validated;
    }

    /// <summary>Raised on every lifecycle transition.</summary>
    public event EventHandler<PluginStateChangedEventArgs>? StateChanged;

    /// <summary>Manifest synthesized from the compiled-in descriptor.</summary>
    public PluginManifest Manifest { get; }

    /// <summary>Plugin id (also the SDK event source).</summary>
    public string PluginId => Manifest.Id;

    /// <summary>Current lifecycle state.</summary>
    public PluginLifecycleState State { get; private set; }

    /// <summary>Formatted failure text (never an exception object).</summary>
    public string? LastError { get; private set; }

    /// <summary>Whether the session is currently active.</summary>
    public bool IsActive => State == PluginLifecycleState.Active;

    /// <summary>Starts (or restarts) the plugin. Returns a failure result instead of throwing.</summary>
    public async Task<PluginLoadResult> StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == PluginLifecycleState.Active)
            {
                return PluginLoadResult.Ok(CreateHandleView());
            }

            if (State is PluginLifecycleState.Failed or PluginLifecycleState.Disabled or PluginLifecycleState.Unloaded)
            {
                return PluginLoadResult.Fail(
                    CreateHandleView(),
                    PluginLoadFailureKind.Disabled,
                    LastError ?? $"Built-in plugin '{PluginId}' is in state {State}; restart FluidBar to retry.");
            }

            if (_runtime is null)
            {
                if (!TryTransition(PluginLifecycleState.Loading))
                {
                    return PluginLoadResult.Fail(CreateHandleView(), PluginLoadFailureKind.Unexpected, $"Cannot load from state {State}.");
                }

                try
                {
                    _runtime = CreateRuntime();
                }
                catch (Exception exception)
                {
                    return Fail(PluginLoadFailureKind.InitializeFailed, Describe(exception));
                }

                if (!TryTransition(PluginLifecycleState.Loaded) || !TryTransition(PluginLifecycleState.Initializing))
                {
                    return Fail(PluginLoadFailureKind.Unexpected, $"Cannot initialize from state {State}.");
                }

                var initialize = await RunLifecycleAsync(
                    token => _plugin.InitializeAsync(_runtime.Context, token),
                    _options.InitializeTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (!initialize.Success)
                {
                    return Fail(
                        initialize.TimedOut ? PluginLoadFailureKind.Timeout : PluginLoadFailureKind.InitializeFailed,
                        initialize.Message);
                }

                if (!TryTransition(PluginLifecycleState.Initialized) || !TryTransition(PluginLifecycleState.Starting))
                {
                    return Fail(PluginLoadFailureKind.Unexpected, $"Cannot start from state {State}.");
                }
            }
            else if (!TryTransition(PluginLifecycleState.Starting))
            {
                return PluginLoadResult.Fail(CreateHandleView(), PluginLoadFailureKind.Unexpected, $"Cannot start from state {State}.");
            }

            var start = await RunLifecycleAsync(
                token => _plugin.StartAsync(token),
                _options.StartTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!start.Success)
            {
                return Fail(start.TimedOut ? PluginLoadFailureKind.Timeout : PluginLoadFailureKind.StartFailed, start.Message);
            }

            LastError = null;
            return TryTransition(PluginLifecycleState.Active)
                ? PluginLoadResult.Ok(CreateHandleView())
                : PluginLoadResult.Fail(CreateHandleView(), PluginLoadFailureKind.Unexpected, $"Cannot activate from state {State}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops the plugin but keeps the session/services loaded so it can be started again.</summary>
    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is PluginLifecycleState.Stopped or PluginLifecycleState.Unloaded)
            {
                return true;
            }

            if (State != PluginLifecycleState.Active)
            {
                return false;
            }

            if (!TryTransition(PluginLifecycleState.Stopping))
            {
                return false;
            }

            var stop = await RunLifecycleAsync(
                token => _plugin.StopAsync(token),
                _options.StopTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!stop.Success)
            {
                _logger.Warn(PluginId, $"Built-in plugin StopAsync did not complete cleanly: {stop.Message}");
            }

            return TryTransition(PluginLifecycleState.Stopped);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Invokes a manifest-declared command with the host timeout and exception isolation.</summary>
    public async Task<PluginCommandExecutionResult> ExecuteCommandAsync(
        string commandId,
        CommandInvocationSource source = CommandInvocationSource.Programmatic,
        IReadOnlyDictionary<string, string?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ThrowIfDisposed();
        var runtime = _runtime;
        if (State != PluginLifecycleState.Active || runtime is null)
        {
            return PluginCommandExecutionResult.NotAvailable($"Plugin '{PluginId}' is not active.");
        }

        if (!runtime.Commands.TryGetHandler(commandId, out var handler) || handler is null)
        {
            return PluginCommandExecutionResult.NotAvailable($"Command '{commandId}' has no handler registered.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.CommandTimeout);
            await handler(
                new PluginCommandContext
                {
                    PluginId = PluginId,
                    CommandId = commandId,
                    Source = source,
                    Arguments = arguments,
                },
                timeout.Token).AsTask().WaitAsync(_options.CommandTimeout, cancellationToken).ConfigureAwait(false);
            return PluginCommandExecutionResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PluginCommandExecutionResult.Failed("Command invocation was cancelled.");
        }
        catch (OperationCanceledException)
        {
            return PluginCommandExecutionResult.Failed("Command timed out.");
        }
        catch (Exception exception)
        {
            _logger.Error(PluginId, $"Command '{commandId}' failed: {Describe(exception)}");
            return PluginCommandExecutionResult.Failed("Command failed; see the plugin log for details.");
        }
    }

    /// <summary>Reads a host-owned setting value as JSON (used by the generic settings-page renderer).</summary>
    public JsonElement? GetSettingValue(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _runtime is { } runtime && runtime.Settings.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Writes a host-owned setting value (the plugin observes it through <c>IPluginSettings</c>).</summary>
    public bool SetSettingValue(string key, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_runtime is not { } runtime)
        {
            return false;
        }

        try
        {
            runtime.Settings.SetValue(key, value);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            _logger.Warn(PluginId, $"Setting '{key}' could not be written: {exception.Message}");
            return false;
        }
    }

    /// <summary>Flushes the host-owned settings file.</summary>
    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_runtime is { } runtime)
        {
            await runtime.Settings.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (State == PluginLifecycleState.Active)
            {
                await RunLifecycleAsync(token => _plugin.StopAsync(token), _options.StopTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
                TryTransition(PluginLifecycleState.Stopping);
                TryTransition(PluginLifecycleState.Stopped);
            }

            var runtime = _runtime;
            _runtime = null;
            if (runtime is not null)
            {
                try
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.Warn(PluginId, $"Built-in plugin runtime disposal failed: {Describe(exception)}");
                }
            }

            try
            {
                await _plugin.DisposeAsync().AsTask().WaitAsync(_options.DisposeTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.Warn(PluginId, $"Built-in plugin DisposeAsync failed: {Describe(exception)}");
            }

            if (TryTransition(PluginLifecycleState.Unloading))
            {
                TryTransition(PluginLifecycleState.Unloaded);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private PluginRuntimeRoot CreateRuntime()
    {
        var manifest = Manifest;
        var dataDirectory = Path.Combine(_options.ResolveDataRoot(), manifest.Id, "data");
        if (_options.CreateDirectories)
        {
            Directory.CreateDirectory(dataDirectory);
        }

        var settingsPath = Path.Combine(_options.ResolveSettingsRoot(), manifest.Id, "settings.json");
        var settings = new PluginSettingsStore(manifest.Id, settingsPath, _logger);
        settings.Load();

        var storage = new PluginStorage(dataDirectory);
        var localizer = CreateLocalizer(manifest);
        var commands = new PluginCommandRegistry(manifest.Id, manifest, _logger);
        var leases = new PluginLeaseCoordinator(manifest.Id, _logger, _options.TimeProvider);

        var islandPublisher = _options.IslandPublisherFactory?.Invoke(manifest) ?? NullIslandPublisher.Instance;
        var notifications = _options.NotificationSinkFactory?.Invoke(manifest) ?? NullNotificationSink.Instance;
        var context = new PluginContext(
            manifest,
            _packageDirectory,
            dataDirectory,
            _options.HostApiVersion,
            new PluginIslandPublisher(manifest.Id, islandPublisher, _logger),
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

    /// <summary>
    /// Built-ins have no package locale bundles; they resolve through the host locale chain and the manifest
    /// message keys. A manifest that does declare bundles is still loaded through the normal plugin localizer.
    /// </summary>
    private IPluginLocalization CreateLocalizer(PluginManifest manifest)
    {
        if (manifest.Locales is null)
        {
            var hostLocales = _options.HostUiLocales is { Count: > 0 } ? _options.HostUiLocales : ["en-US"];
            var catalog = new MessageCatalog(
                hostLocales[0],
                [new LocaleBundle { Locale = hostLocales[0] }],
                hostLocales);
            return new PluginLocalizer(manifest.Id, _logger, catalog, hostLocales[0]);
        }

        return PluginLocalizer.Create(manifest.Id, _packageDirectory, manifest, _options.HostUiLocales, _logger);
    }

    private async Task<LifecycleOutcome> RunLifecycleAsync(
        Func<CancellationToken, ValueTask> action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new LifecycleOutcome(false, false, "Lifecycle call was cancelled.");
        }
        catch (Exception exception)
        {
            return new LifecycleOutcome(false, false, Describe(exception));
        }
    }

    private PluginLoadResult Fail(PluginLoadFailureKind kind, string message)
    {
        LastError = message;
        _logger.Error(PluginId, $"Built-in plugin load failed ({kind}): {message}");
        TryTransition(PluginLifecycleState.Failed);
        return PluginLoadResult.Fail(CreateHandleView(), kind, message);
    }

    /// <summary>Lightweight handle view so callers can reuse the plugin-list/UI contract.</summary>
    private PluginHandle CreateHandleView()
    {
        var entryAssembly = Manifest.Entry is { Assembly.Length: > 0 } entry ? entry.Assembly : null;
        var dataDirectory = Path.Combine(_options.ResolveDataRoot(), Manifest.Id, "data");
        return new PluginHandle(Manifest, _packageDirectory, dataDirectory, entryAssembly)
        {
            State = State,
            LastError = LastError,
        };
    }

    private bool TryTransition(PluginLifecycleState next)
    {
        if (State == next)
        {
            return true;
        }

        if (!PluginLifecycleTransitions.CanTransition(State, next))
        {
            _logger.Error(PluginId, $"Refused invalid built-in lifecycle transition {State} -> {next}.");
            return false;
        }

        var previous = State;
        State = next;
        var handlers = StateChanged;
        if (handlers is not null)
        {
            try
            {
                handlers(this, new PluginStateChangedEventArgs(PluginId, previous, next, LastError));
            }
            catch (Exception exception)
            {
                _logger.Error(PluginId, $"StateChanged handler failed: {Describe(exception)}");
            }
        }

        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InProcessPluginSession));
        }
    }

    private static string Describe(Exception exception) => $"{exception.GetType().FullName}: {exception.Message}";

    private readonly record struct LifecycleOutcome(bool Success, bool TimedOut, string Message);
}

