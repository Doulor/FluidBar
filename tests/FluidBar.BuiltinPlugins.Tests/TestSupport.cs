using System.Collections.Immutable;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Devices;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Threading;
using FluidBar.Sdk;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Versioning;

namespace FluidBar.BuiltinPlugins.Tests;

/// <summary>Captures published/retracted SDK events.</summary>
public sealed class FakeIslandPublisher : IIslandPublisher
{
    private readonly object _gate = new();
    private readonly List<IslandEvent> _published = new();

    public IReadOnlyList<IslandEvent> Published
    {
        get
        {
            lock (_gate)
            {
                return _published.ToArray();
            }
        }
    }

    public List<string> Retracted { get; } = new();

    public bool AutoAccept { get; set; } = true;

    public bool TryPublish(IslandEvent islandEvent)
    {
        lock (_gate)
        {
            _published.Add(islandEvent);
        }

        return AutoAccept;
    }

    public bool TryRetract(string source)
    {
        lock (Retracted)
        {
            Retracted.Add(source);
        }

        return true;
    }

    public IslandEvent LastPublished
    {
        get
        {
            lock (_gate)
            {
                return _published[^1];
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _published.Count;
            }
        }
    }
}

/// <summary>Captures command handlers so tests can invoke them like the host would.</summary>
public sealed class FakeCommandRegistry : ICommandRegistry
{
    public Dictionary<string, Func<PluginCommandContext, CancellationToken, ValueTask>> Handlers { get; } =
        new(StringComparer.Ordinal);

    public bool ThrowOnRegister { get; set; }

    public bool IsRegistered(string commandId) => Handlers.ContainsKey(commandId);

    public IDisposable Register(string commandId, Func<PluginCommandContext, CancellationToken, ValueTask> handler)
    {
        if (ThrowOnRegister)
        {
            throw new InvalidOperationException("simulated host command failure");
        }

        Handlers[commandId] = handler;
        return new ActionDisposable(() => Handlers.Remove(commandId));
    }

    /// <summary>Invokes a registered handler (fails the test when absent).</summary>
    public Task InvokeAsync(string commandId, PluginCommandContext context, CancellationToken cancellationToken = default)
    {
        var handler = Handlers[commandId];
        return handler(context, cancellationToken).AsTask();
    }
}

public sealed class FakePluginSettings : IPluginSettings
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>Atomically-persisted snapshot of the last save (for save assertions).</summary>
    public Dictionary<string, object?> Saved { get; } = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => _values.Keys;

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
        => TryGetStringArray(key, out var value) ? value! : fallback ?? Array.Empty<string>();

    public void SetBoolean(string key, bool value) => _values[key] = value;
    public void SetInt32(string key, int value) => _values[key] = value;
    public void SetDouble(string key, double value) => _values[key] = value;
    public void SetString(string key, string? value) => _values[key] = value;
    public void SetStringArray(string key, IReadOnlyList<string> values) => _values[key] = values;

    public bool Remove(string key) => _values.Remove(key);

    public IDisposable Subscribe(Action<string> onChanged) => new ActionDisposable(() => { });

    public ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        Saved.Clear();
        foreach (var pair in _values)
        {
            Saved[pair.Key] = pair.Value;
        }

        return ValueTask.CompletedTask;
    }
}

public sealed class FakePluginLog : IPluginLog
{
    public List<(PluginLogLevel Level, string Message)> Entries { get; } = new();

    public bool IsEnabled(PluginLogLevel level) => true;

    public void Log(PluginLogLevel level, string message, Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        lock (Entries)
        {
            Entries.Add((level, message));
        }
    }

    public int CountOf(PluginLogLevel level)
    {
        lock (Entries)
        {
            return Entries.Count(entry => entry.Level == level);
        }
    }
}

public sealed class FakeLocalization : IPluginLocalization
{
    public string CurrentLocale => "en-US";
    public string DefaultLocale => "en-US";
    public IReadOnlyList<string> AvailableLocales => ["en-US"];

    public string Get(string key) => key;

    public bool TryGet(string key, out string value)
    {
        value = key;
        return false;
    }

    public string Format(string key, IReadOnlyDictionary<string, object?> arguments) => key;

    public string Plural(string key, long count) => key;
}

public sealed class FakeNotificationSink : INotificationSink
{
    public List<PluginNotification> Posted { get; } = new();

    public bool TryPost(PluginNotification notification)
    {
        lock (Posted)
        {
            Posted.Add(notification);
        }

        return true;
    }
}

/// <summary>Minimal host context for offline plugin tests (no UI, no real host).</summary>
public sealed class FakePluginContext : IPluginContext
{
    public FakePluginContext(string pluginId = "test-plugin")
    {
        PluginId = pluginId;
        HostStoppingSource = new CancellationTokenSource();
    }

    public string PluginId { get; }
    public SemanticVersion ApiVersion => new(2, 0, 0);
    public string PackageDirectory => Path.GetTempPath();
    public string DataDirectory => Path.GetTempPath();
    public FakeIslandPublisher Publisher { get; } = new();
    public IIslandPublisher Islands => Publisher;
    public IPluginLocalization Localization { get; } = new FakeLocalization();
    public FakePluginSettings SettingsStore { get; } = new();
    public IPluginSettings Settings => SettingsStore;
    public IPluginStorage Storage => throw new NotSupportedException();
    public FakePluginLog LogSink { get; } = new();
    public IPluginLog Log => LogSink;
    public IHostDispatcher Dispatcher => throw new NotSupportedException();
    public IPluginLeases Leases => throw new NotSupportedException();
    public FakeCommandRegistry CommandRegistry { get; } = new();
    public ICommandRegistry Commands => CommandRegistry;
    public FakeNotificationSink NotificationSink { get; } = new();
    public INotificationSink Notifications => NotificationSink;
    public IReadOnlySet<PluginCapability> GrantedCapabilities => PluginContextDefaults.NoCapabilities;
    public TimeProvider Time { get; init; } = TimeProvider.System;
    public CancellationTokenSource HostStoppingSource { get; }
    public CancellationToken HostStopping => HostStoppingSource.Token;
    public List<IDisposable> Registered { get; } = new();

    public IDisposable RegisterForDisposal(Action disposeAction)
    {
        var disposable = new ActionDisposable(disposeAction);
        Registered.Add(disposable);
        return disposable;
    }
}

/// <summary>Fake platform media source with controllable snapshots/artwork.</summary>
public sealed class FakeMediaSource : IMediaSessionSource
{
    public MediaSnapshot? Current { get; private set; }
    public ArtworkCache ArtworkCache { get; } = new();
    public int SnapshotSubscribers { get; private set; }
    public int ArtworkSubscribers { get; private set; }
    public List<string> TransportCalls { get; } = new();

    public event EventHandler<SnapshotEventArgs<MediaSnapshot>>? SnapshotChanged;
    public event EventHandler<MediaArtworkEventArgs>? ArtworkAvailable;

    public bool TryGetArtwork(string trackKey, out ArtworkEntry? artwork) => ArtworkCache.TryGet(trackKey, out artwork);

    public void Emit(MediaSnapshot snapshot)
    {
        Current = snapshot;
        SnapshotChanged?.Invoke(this, new SnapshotEventArgs<MediaSnapshot>(snapshot));
    }

    public void EmitArtwork(string trackKey, ArtworkEntry artwork)
    {
        ArtworkCache.Add(trackKey, artwork.Data);
        ArtworkAvailable?.Invoke(this, new MediaArtworkEventArgs(trackKey, artwork));
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
        TransportCalls.Add($"seek:{position.TotalSeconds}");
        return Task.FromResult(true);
    }

    internal void TrackHandlers()
    {
        SnapshotChanged += (_, _) => { };
    }

    public int CountSubscribers() => SnapshotChanged?.GetInvocationList().Length ?? 0;
}

public sealed class FakeClipboardSource : IClipboardSource
{
    public ClipboardSnapshot? Current { get; private set; }
    public ClipboardHistory History { get; } = new(50);
    public List<string> WrittenTexts { get; } = new();

    public event EventHandler<SnapshotEventArgs<ClipboardSnapshot>>? SnapshotChanged;

    public void Emit(ClipboardEntry entry)
    {
        History.TryAdd(entry, out var snapshot);
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
        History.Clear();
        Current = ClipboardSnapshot.Empty;
    }

    public int CountSubscribers() => SnapshotChanged?.GetInvocationList().Length ?? 0;
}

public sealed class FakeNotificationSource : INotificationSource
{
    public NotificationsSnapshot? Current { get; private set; }
    public NotificationAccessState AccessState { get; set; } = NotificationAccessState.Allowed;
    public int RequestAccessCalls { get; private set; }

    public event EventHandler<SnapshotEventArgs<NotificationsSnapshot>>? SnapshotChanged;

    public void Emit(NotificationEntry entry)
    {
        Current = new NotificationsSnapshot(AccessState, ImmutableArray.Create(entry));
        SnapshotChanged?.Invoke(this, new SnapshotEventArgs<NotificationsSnapshot>(Current));
    }

    public Task<NotificationAccessState> RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        RequestAccessCalls++;
        AccessState = NotificationAccessState.Allowed;
        return Task.FromResult(AccessState);
    }

    public int CountSubscribers() => SnapshotChanged?.GetInvocationList().Length ?? 0;
}

public sealed class FakeAgentInboxSource : IAgentInboxSource
{
    public AgentEventSnapshot? Current { get; private set; }
    public string InboxDirectory => Path.GetTempPath();

    public event EventHandler<SnapshotEventArgs<AgentEventSnapshot>>? SnapshotChanged;

    public void Emit(AgentEventEntry entry)
    {
        var history = new AgentEventHistory();
        if (Current is not null)
        {
            foreach (var existing in Current.Recent)
            {
                history.Add(existing);
            }
        }

        Current = history.Add(entry);
        SnapshotChanged?.Invoke(this, new SnapshotEventArgs<AgentEventSnapshot>(Current));
    }

    public int CountSubscribers() => SnapshotChanged?.GetInvocationList().Length ?? 0;
}

public sealed class FakeHookInstaller : IAgentHookInstaller
{
    public int InstallCalls { get; private set; }
    public int UninstallCalls { get; private set; }
    public bool Installed { get; set; }
    public HookInstallResult Result { get; set; } = new(true, true, null, null);

    public Task<HookInstallResult> InstallAsync(bool userConfirmed, CancellationToken cancellationToken = default)
    {
        InstallCalls++;
        Installed = Result.Success;
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

public sealed class FakeDeviceEventSource : IDeviceEventSource
{
    public FakeDeviceEventSource(string sourceToken) => SourceToken = sourceToken;

    public DeviceEventSnapshot? Current { get; private set; }
    public string SourceToken { get; }

    public event EventHandler<SnapshotEventArgs<DeviceEventSnapshot>>? SnapshotChanged;

    public void Emit(DeviceEventEntry entry, bool primeOnly = false)
    {
        var history = new DeviceEventHistory();
        if (Current is not null)
        {
            foreach (var existing in Current.Recent)
            {
                history.Add(existing);
            }
        }

        Current = history.Add(entry);
        if (!primeOnly)
        {
            SnapshotChanged?.Invoke(this, new SnapshotEventArgs<DeviceEventSnapshot>(Current));
        }
    }

    public int CountSubscribers() => SnapshotChanged?.GetInvocationList().Length ?? 0;
}

internal sealed class ActionDisposable : IDisposable
{
    private Action? _dispose;

    public ActionDisposable(Action dispose) => _dispose = dispose;

    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}

public static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    /// <summary>Builds a minimal valid PNG header with the given dimensions (no pixel data needed).</summary>
    public static byte[] PngHeader(int width, int height)
    {
        var png = new byte[128];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        png[8] = 0; png[9] = 0; png[10] = 0; png[11] = 13;
        new byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(png, 12);
        png[16] = (byte)(width >> 24); png[17] = (byte)(width >> 16);
        png[18] = (byte)(width >> 8); png[19] = (byte)width;
        png[20] = (byte)(height >> 24); png[21] = (byte)(height >> 16);
        png[22] = (byte)(height >> 8); png[23] = (byte)height;
        return png;
    }
}
