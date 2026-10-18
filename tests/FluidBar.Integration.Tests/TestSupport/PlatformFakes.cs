using System.Collections.Immutable;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Devices;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Threading;
using FluidBar.Sdk;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Integration.Tests.TestSupport;

/// <summary>Deterministic virtual clock (no ambient time in tests).</summary>
public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset start) => _utcNow = start;

    public static TestTimeProvider StartingAtEpoch() => new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _utcNow.UtcTicks;

    public DateTimeOffset UtcNow => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}

/// <summary>Captures plugin-published SDK events and retracts.</summary>
public sealed class RecordingPublisher : IIslandPublisher
{
    private readonly object _gate = new();
    private readonly List<IslandEvent> _events = [];
    private readonly List<string> _retracted = [];

    public IReadOnlyList<IslandEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public IReadOnlyList<string> Retracted
    {
        get
        {
            lock (_gate)
            {
                return [.. _retracted];
            }
        }
    }

    public bool TryPublish(IslandEvent islandEvent)
    {
        lock (_gate)
        {
            _events.Add(islandEvent);
        }

        return true;
    }

    public bool TryRetract(string source)
    {
        lock (_gate)
        {
            _retracted.Add(source);
        }

        return true;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
            _retracted.Clear();
        }
    }
}

/// <summary>Captures command handlers so tests can invoke them exactly like the host would.</summary>
public sealed class RecordingCommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, Func<PluginCommandContext, CancellationToken, ValueTask>> _handlers =
        new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Func<PluginCommandContext, CancellationToken, ValueTask>> Handlers => _handlers;

    public bool IsRegistered(string commandId) => _handlers.ContainsKey(commandId);

    public IDisposable Register(string commandId, Func<PluginCommandContext, CancellationToken, ValueTask> handler)
    {
        _handlers[commandId] = handler;
        return new ActionDisposable(() => _handlers.Remove(commandId));
    }

    public Task InvokeAsync(string commandId, PluginCommandContext context, CancellationToken cancellationToken = default)
    {
        var handler = _handlers[commandId];
        return handler(context, cancellationToken).AsTask();
    }
}

/// <summary>In-memory plugin settings (host-owned store contract).</summary>
public sealed class RecordingPluginSettings : IPluginSettings
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => _values.Keys;

    public int SaveCount { get; private set; }

    public bool TryGetBoolean(string key, out bool value)
    {
        if (_values.TryGetValue(key, out var raw) && raw is bool boolean)
        {
            value = boolean;
            return true;
        }

        value = false;
        return false;
    }

    public bool TryGetInt32(string key, out int value)
    {
        if (_values.TryGetValue(key, out var raw) && raw is int integer)
        {
            value = integer;
            return true;
        }

        value = 0;
        return false;
    }

    public bool TryGetDouble(string key, out double value)
    {
        if (_values.TryGetValue(key, out var raw) && raw is double number)
        {
            value = number;
            return true;
        }

        value = 0;
        return false;
    }

    public bool TryGetString(string key, out string? value)
    {
        if (_values.TryGetValue(key, out var raw) && raw is string text)
        {
            value = text;
            return true;
        }

        value = null;
        return false;
    }

    public bool TryGetStringArray(string key, out IReadOnlyList<string>? value)
    {
        if (_values.TryGetValue(key, out var raw) && raw is IReadOnlyList<string> list)
        {
            value = list;
            return true;
        }

        value = null;
        return false;
    }

    public bool GetBoolean(string key, bool fallback) => TryGetBoolean(key, out var value) ? value : fallback;

    public int GetInt32(string key, int fallback) => TryGetInt32(key, out var value) ? value : fallback;

    public double GetDouble(string key, double fallback) => TryGetDouble(key, out var value) ? value : fallback;

    public string GetString(string key, string fallback) => TryGetString(key, out var value) ? value ?? fallback : fallback;

    public IReadOnlyList<string> GetStringArray(string key, IReadOnlyList<string>? fallback = null)
        => TryGetStringArray(key, out var value) ? value! : fallback ?? [];

    public void SetBoolean(string key, bool value) => _values[key] = value;

    public void SetInt32(string key, int value) => _values[key] = value;

    public void SetDouble(string key, double value) => _values[key] = value;

    public void SetString(string key, string? value) => _values[key] = value;

    public void SetStringArray(string key, IReadOnlyList<string> values) => _values[key] = values;

    public bool Remove(string key) => _values.Remove(key);

    public IDisposable Subscribe(Action<string> onChanged) => new ActionDisposable(() => { });

    public ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Recording plugin log.</summary>
public sealed class RecordingPluginLog : IPluginLog
{
    private readonly List<(PluginLogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(PluginLogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public int CountOf(PluginLogLevel level)
    {
        lock (_entries)
        {
            return _entries.Count(entry => entry.Level == level);
        }
    }

    public bool IsEnabled(PluginLogLevel level) => true;

    public void Log(PluginLogLevel level, string message, Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        lock (_entries)
        {
            _entries.Add((level, message));
        }
    }
}

/// <summary>Key-returning localization (host fallback chain is exercised in the real host; SDK events are data only).</summary>
public sealed class PassthroughPluginLocalization : IPluginLocalization
{
    public string CurrentLocale => "en-US";

    public string DefaultLocale => "en-US";

    public IReadOnlyList<string> AvailableLocales => ["en-US"];

    public string Get(string key) => key switch
    {
        "island.title" => "Sample title",
        _ => key,
    };

    public bool TryGet(string key, out string value)
    {
        value = key;
        return false;
    }

    public string Format(string key, IReadOnlyDictionary<string, object?> arguments) => key;

    public string Plural(string key, long count) => key;
}

/// <summary>Recording notification sink (host-rendered notifications posted by plugins).</summary>
public sealed class RecordingNotificationSink : INotificationSink
{
    private readonly List<PluginNotification> _notifications = [];

    public IReadOnlyList<PluginNotification> Notifications
    {
        get
        {
            lock (_notifications)
            {
                return [.. _notifications];
            }
        }
    }

    public bool TryPost(PluginNotification notification)
    {
        lock (_notifications)
        {
            _notifications.Add(notification);
        }

        return true;
    }
}

/// <summary>Inline dispatcher for the SDK contract.</summary>
public sealed class InlinePluginDispatcher : IHostDispatcher
{
    public static InlinePluginDispatcher Instance { get; } = new();

    public bool CheckAccess() => true;

    public bool TryPost(Action action)
    {
        action();
        return true;
    }

    public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken) => action();

    public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken) => action();
}

/// <summary>No-op lease factory for plugins that do not need host timers.</summary>
public sealed class NoopPluginLeases : IPluginLeases
{
    public bool IsDisposed => false;

    public IPluginLease CreateLease(string name) => new NoopLease(name);

    private sealed class NoopLease(string name) : IPluginLease
    {
        public string Name { get; } = name;

        public CancellationToken CancellationToken => CancellationToken.None;

        public bool IsDisposed { get; private set; }

        public IDisposable ScheduleOnce(TimeSpan dueTime, Func<CancellationToken, ValueTask> callback) => new ActionDisposable(() => { });

        public IDisposable SchedulePeriodic(TimeSpan period, Func<CancellationToken, ValueTask> callback, TimeSpan? initialDelay = null)
            => new ActionDisposable(() => { });

        public void Dispose() => IsDisposed = true;
    }
}

/// <summary>
/// Test <see cref="IPluginContext"/>: stands in for the App-owned host context so built-in plugins can
/// be driven end-to-end without the real host. Mirrors the same contract the real host provides.
/// </summary>
public sealed class TestPluginContext : IPluginContext, IDisposable
{
    private readonly CancellationTokenSource _hostStopping = new();

    public TestPluginContext(string pluginId, TimeProvider time)
    {
        PluginId = pluginId;
        Time = time;
    }

    public string PluginId { get; }

    public SemanticVersion ApiVersion => new(2, 0, 0);

    public string PackageDirectory => Path.GetTempPath();

    public string DataDirectory => Path.GetTempPath();

    public RecordingPublisher Publisher { get; } = new();

    public IIslandPublisher Islands => Publisher;

    public IPluginLocalization Localization { get; } = new PassthroughPluginLocalization();

    public RecordingPluginSettings SettingsStore { get; } = new();

    public IPluginSettings Settings => SettingsStore;

    public IPluginStorage Storage { get; } = new InMemoryPluginStorage();

    public RecordingPluginLog LogSink { get; } = new();

    public IPluginLog Log => LogSink;

    public IHostDispatcher Dispatcher { get; } = InlinePluginDispatcher.Instance;

    public IPluginLeases Leases { get; } = new NoopPluginLeases();

    public RecordingCommandRegistry CommandRegistry { get; } = new();

    public ICommandRegistry Commands => CommandRegistry;

    public RecordingNotificationSink NotificationSink { get; } = new();

    public INotificationSink Notifications => NotificationSink;

    public IReadOnlySet<PluginCapability> GrantedCapabilities => PluginContextDefaults.NoCapabilities;

    public TimeProvider Time { get; }

    public CancellationToken HostStopping => _hostStopping.Token;

    public List<IDisposable> Registered { get; } = [];

    public IDisposable RegisterForDisposal(Action disposeAction)
    {
        var disposable = new ActionDisposable(disposeAction);
        Registered.Add(disposable);
        return disposable;
    }

    public void Dispose()
    {
        if (!_hostStopping.IsCancellationRequested)
        {
            _hostStopping.Cancel();
        }

        _hostStopping.Dispose();
    }

    private sealed class InMemoryPluginStorage : IPluginStorage
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
        private readonly string _root = Path.Combine(Path.GetTempPath(), "fluidbar-integration-plugin-data");

        public string DataDirectory => _root;

        public string GetDataPath(string relativePath) => Path.Combine(_root, relativePath);

        public bool Exists(string relativePath) => _files.ContainsKey(relativePath);

        public Task<string?> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult(_files.TryGetValue(relativePath, out var text) ? text : null);

        public Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken = default)
        {
            _files[relativePath] = content;
            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadBytesAsync(string relativePath, int maxBytes, CancellationToken cancellationToken = default)
            => Task.FromResult<byte[]?>(null);

        public Task WriteBytesAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            _files.Remove(relativePath);
            return Task.CompletedTask;
        }
    }
}

/// <summary>Disposable wrapper over an action.</summary>
public sealed class ActionDisposable(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;

    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}

/// <summary>Fake GSMTC media source with the production interface shape.</summary>
public sealed class FakeMediaSource : IMediaSessionSource
{
    public MediaSnapshot? Current { get; private set; }

    public ArtworkCache ArtworkCache { get; } = new();

    public List<string> TransportCalls { get; } = [];

    public int SnapshotSubscriberCount => SnapshotChanged?.GetInvocationList().Length ?? 0;

    public int ArtworkSubscriberCount => ArtworkAvailable?.GetInvocationList().Length ?? 0;

    public event EventHandler<SnapshotEventArgs<MediaSnapshot>>? SnapshotChanged;

    public event EventHandler<MediaArtworkEventArgs>? ArtworkAvailable;

    public bool TryGetArtwork(string trackKey, out ArtworkEntry? artwork) => ArtworkCache.TryGet(trackKey, out artwork);

    public void Emit(MediaSnapshot snapshot)
    {
        Current = snapshot;
        SnapshotChanged?.Invoke(this, new SnapshotEventArgs<MediaSnapshot>(snapshot));
    }

    public void EmitArtwork(string trackKey)
    {
        ArtworkCache.Add(trackKey, [1, 2, 3]);
        ArtworkAvailable?.Invoke(this, new MediaArtworkEventArgs(trackKey, new ArtworkEntry([1, 2, 3], "image/png", default)));
    }

    public Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default)
    {
        TransportCalls.Add("toggle");
        return Task.FromResult(true);
    }

    public Task<bool> NextAsync(CancellationToken cancellationToken = default)
    {
        TransportCalls.Add("next");
        return Task.FromResult(true);
    }

    public Task<bool> PreviousAsync(CancellationToken cancellationToken = default)
    {
        TransportCalls.Add("previous");
        return Task.FromResult(true);
    }

    public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        TransportCalls.Add($"seek:{position.TotalSeconds:0.###}");
        return Task.FromResult(true);
    }
}

/// <summary>Fake clipboard source with bounded history (production model reused).</summary>
public sealed class FakeClipboardSource : IClipboardSource
{
    private readonly ClipboardHistory _history = new(50);

    public ClipboardSnapshot? Current { get; private set; } = ClipboardSnapshot.Empty;

    public List<string> WrittenTexts { get; } = [];

    public int SnapshotSubscriberCount => SnapshotChanged?.GetInvocationList().Length ?? 0;

    public event EventHandler<SnapshotEventArgs<ClipboardSnapshot>>? SnapshotChanged;

    public void Emit(ClipboardEntry entry)
    {
        _history.TryAdd(entry, out var snapshot);
        Current = snapshot;
        SnapshotChanged?.Invoke(this, new SnapshotEventArgs<ClipboardSnapshot>(snapshot));
    }

    public IReadOnlyList<ClipboardEntry> GetHistory() => Current?.History ?? ImmutableArray<ClipboardEntry>.Empty;

    public Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        WrittenTexts.Add(text);
        return Task.FromResult(true);
    }

    public void ClearHistory()
    {
        _history.Clear();
        Current = ClipboardSnapshot.Empty;
    }
}

/// <summary>Fake Windows notification listener.</summary>
public sealed class FakeNotificationSource : INotificationSource
{
    public NotificationsSnapshot? Current { get; private set; } = NotificationsSnapshot.Empty;

    public NotificationAccessState AccessState { get; set; } = NotificationAccessState.Allowed;

    public int RequestAccessCalls { get; private set; }

    public int SnapshotSubscriberCount => SnapshotChanged?.GetInvocationList().Length ?? 0;

    public event EventHandler<SnapshotEventArgs<NotificationsSnapshot>>? SnapshotChanged;

    public void Emit(NotificationEntry entry)
    {
        Current = new NotificationsSnapshot(AccessState, [entry]);
        SnapshotChanged?.Invoke(this, new SnapshotEventArgs<NotificationsSnapshot>(Current));
    }

    public Task<NotificationAccessState> RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        RequestAccessCalls++;
        AccessState = NotificationAccessState.Allowed;
        return Task.FromResult(AccessState);
    }
}

/// <summary>Fake agent inbox (hook-written JSON snapshots).</summary>
public sealed class FakeAgentInboxSource : IAgentInboxSource
{
    private readonly AgentEventHistory _history = new(100);

    public AgentEventSnapshot? Current { get; private set; } = AgentEventSnapshot.Empty;

    public string InboxDirectory => Path.GetTempPath();

    public int SnapshotSubscriberCount => SnapshotChanged?.GetInvocationList().Length ?? 0;

    public event EventHandler<SnapshotEventArgs<AgentEventSnapshot>>? SnapshotChanged;

    public void Emit(AgentEventEntry entry)
    {
        Current = _history.Add(entry);
        SnapshotChanged?.Invoke(this, new SnapshotEventArgs<AgentEventSnapshot>(Current));
    }
}

/// <summary>Fake consent-gated hook installer; the plugin must never call it without consent.</summary>
public sealed class FakeHookInstaller : IAgentHookInstaller
{
    public int InstallCalls { get; private set; }

    public int UninstallCalls { get; private set; }

    public bool Installed { get; private set; }

    public HookInstallResult Result { get; set; } = new(true, true, null, null);

    public Task<HookInstallResult> InstallAsync(bool userConfirmed, CancellationToken cancellationToken = default)
    {
        InstallCalls++;
        Installed = userConfirmed && Result.Success;
        return Task.FromResult(Result);
    }

    public Task<HookInstallResult> UninstallAsync(bool userConfirmed, CancellationToken cancellationToken = default)
    {
        UninstallCalls++;
        Installed = false;
        return Task.FromResult(Result);
    }

    public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) => Task.FromResult(Installed);
}

/// <summary>Fake USB/Bluetooth device source.</summary>
public sealed class FakeDeviceEventSource(string sourceToken) : IDeviceEventSource
{
    private readonly DeviceEventHistory _history = new(50);

    public DeviceEventSnapshot? Current { get; private set; } = DeviceEventSnapshot.Empty;

    public string SourceToken { get; } = sourceToken;

    public int SnapshotSubscriberCount => SnapshotChanged?.GetInvocationList().Length ?? 0;

    public event EventHandler<SnapshotEventArgs<DeviceEventSnapshot>>? SnapshotChanged;

    public void Emit(DeviceEventEntry entry)
    {
        Current = _history.Add(entry);
        SnapshotChanged?.Invoke(this, new SnapshotEventArgs<DeviceEventSnapshot>(Current));
    }
}
