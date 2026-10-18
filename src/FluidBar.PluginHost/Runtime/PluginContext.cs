using FluidBar.PluginHost.Logging;
using FluidBar.Sdk;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.PluginHost.Runtime;

/// <summary>Host implementation of <see cref="IPluginContext"/>; composition root for the per-plugin services.</summary>
internal sealed class PluginContext : IPluginContext, IDisposable
{
    private const int MaxDisposals = 64;

    private readonly string _pluginId;
    private readonly IPluginHostLogger _hostLogger;
    private readonly List<DisposalRegistration> _disposals = [];
    private readonly CancellationTokenSource _hostStopping = new();
    private readonly object _gate = new();
    private bool _disposed;

    internal PluginContext(
        PluginManifest manifest,
        string packageDirectory,
        string dataDirectory,
        SemanticVersion apiVersion,
        IIslandPublisher islands,
        IPluginLocalization localization,
        IPluginSettings settings,
        IPluginStorage storage,
        IPluginLog log,
        IHostDispatcher dispatcher,
        IPluginLeases leases,
        ICommandRegistry commands,
        INotificationSink notifications,
        IReadOnlySet<PluginCapability> grantedCapabilities,
        TimeProvider timeProvider,
        IPluginHostLogger hostLogger)
    {
        _pluginId = manifest.Id;
        _hostLogger = hostLogger;
        PackageDirectory = packageDirectory;
        DataDirectory = dataDirectory;
        ApiVersion = apiVersion;
        Islands = islands;
        Localization = localization;
        Settings = settings;
        Storage = storage;
        Log = log;
        Dispatcher = dispatcher;
        Leases = leases;
        Commands = commands;
        Notifications = notifications;
        GrantedCapabilities = grantedCapabilities;
        Time = timeProvider;
    }

    public string PluginId => _pluginId;

    public SemanticVersion ApiVersion { get; }

    public string PackageDirectory { get; }

    public string DataDirectory { get; }

    public IIslandPublisher Islands { get; }

    public IPluginLocalization Localization { get; }

    public IPluginSettings Settings { get; }

    public IPluginStorage Storage { get; }

    public IPluginLog Log { get; }

    public IHostDispatcher Dispatcher { get; }

    public IPluginLeases Leases { get; }

    public ICommandRegistry Commands { get; }

    public INotificationSink Notifications { get; }

    public IReadOnlySet<PluginCapability> GrantedCapabilities { get; }

    public TimeProvider Time { get; }

    public CancellationToken HostStopping => _hostStopping.Token;

    public IDisposable RegisterForDisposal(Action disposeAction)
    {
        ArgumentNullException.ThrowIfNull(disposeAction);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_disposals.Count >= MaxDisposals)
            {
                throw new InvalidOperationException($"Plugin '{_pluginId}' exceeded the {MaxDisposals}-resource disposal limit.");
            }

            var registration = new DisposalRegistration(disposeAction);
            _disposals.Add(registration);
            return registration;
        }
    }

    /// <summary>
    /// Context teardown safety net, run after the plugin's own StopAsync/DisposeAsync:
    /// cancels <see cref="HostStopping"/>, disposes registered resources LIFO and clears the registration list.
    /// Exceptions are logged and contained so one bad resource cannot block unload.
    /// </summary>
    internal void DisposeContext()
    {
        DisposalRegistration[] registrations;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            registrations = [.. _disposals];
            _disposals.Clear();
        }

        try
        {
            _hostStopping.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The host already finished; cancellation is best effort.
        }

        for (var index = registrations.Length - 1; index >= 0; index--)
        {
            try
            {
                registrations[index].Invoke();
            }
            catch (Exception exception)
            {
                // Never let a plugin resource failure escape into the host teardown.
                _hostLogger.Warn(_pluginId, $"Registered resource disposal failed: {exception.GetType().FullName}: {exception.Message}");
            }
        }

        _hostStopping.Dispose();
        _hostLogger.Debug(_pluginId, "Plugin context disposed; registered resources released.");
    }

    private sealed class DisposalRegistration(Action disposeAction) : IDisposable
    {
        private int _disposed;

        internal void Invoke()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            disposeAction();
        }

        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
    }

    /// <summary>The host owns context lifetime; this exists so the CTS is deterministically releasable.</summary>
    public void Dispose() => DisposeContext();
}

/// <summary>
/// Owns all per-plugin runtime services so teardown order is explicit: leases stop first, then settings are
/// flushed, then command handlers are cleared, then the context safety net releases registered resources and
/// the host drops the context and unloads the ALC.
/// </summary>
internal sealed class PluginRuntimeRoot : IAsyncDisposable
{
    private readonly PluginSettingsStore _settings;
    private readonly PluginLeaseCoordinator _leases;
    private readonly PluginCommandRegistry _commands;
    private readonly PluginContext _context;
    private readonly Logging.IPluginHostLogger _logger;
    private readonly string _pluginId;
    private bool _disposed;

    internal PluginRuntimeRoot(
        PluginContext context,
        PluginSettingsStore settings,
        PluginLeaseCoordinator leases,
        PluginCommandRegistry commands,
        string pluginId,
        Logging.IPluginHostLogger logger)
    {
        _context = context;
        _settings = settings;
        _leases = leases;
        _commands = commands;
        _pluginId = pluginId;
        _logger = logger;
    }

    internal PluginContext Context => _context;

    internal PluginLeaseCoordinator Leases => _leases;

    internal PluginCommandRegistry Commands => _commands;

    internal PluginSettingsStore Settings => _settings;

    internal Task Completion => _leases.Completion;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 1. Stop timers/subscriptions and await their tasks: the main unload-leak mitigation.
        await _leases.DisposeAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // 2. Flush host-owned settings (never throw at unload).
        try
        {
            await _settings.SaveAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.Warn(_pluginId, $"Could not save plugin settings during unload: {exception.Message}", exception);
        }

        // 3. Drop command handlers so no delegate points back into the plugin.
        _commands.Dispose();

        // 4. Context safety net: HostStopping cancellation + LIFO disposal of registered resources.
        _context.DisposeContext();
        _settings.Dispose();
    }
}
