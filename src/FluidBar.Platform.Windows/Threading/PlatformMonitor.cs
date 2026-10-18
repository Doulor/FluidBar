using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Threading;

/// <summary>Explicit lifecycle state shared by every platform monitor and shell service.</summary>
public enum PlatformMonitorState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
    Disposed,
}

/// <summary>
/// Contract consumed by the App layer: read the latest immutable snapshot, subscribe to changes,
/// drive an explicit start/stop lifecycle.
/// </summary>
public interface IPlatformMonitor<TSnapshot> : IAsyncDisposable
    where TSnapshot : class, IEquatable<TSnapshot>
{
    /// <summary>Stable id used in logs/telemetry ("volume", "brightness", …).</summary>
    string MonitorId { get; }

    /// <summary>Current lifecycle state.</summary>
    PlatformMonitorState State { get; }

    /// <summary>Latest snapshot, or <c>null</c> before the first observation.</summary>
    TSnapshot? Current { get; }

    /// <summary>Thread-pool notification for change-only snapshots.</summary>
    event EventHandler<SnapshotEventArgs<TSnapshot>>? SnapshotChanged;

    /// <summary>Starts the monitor. Idempotent; failures transition to <see cref="PlatformMonitorState.Failed"/>.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the monitor and releases its resources. Idempotent.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class for event-driven monitors. Guarantees:
/// <list type="bullet">
/// <item>single-flight lifecycle (concurrent Start/Stop cannot interleave);</item>
/// <item>change-only snapshots through <see cref="SnapshotPublisher{TSnapshot}"/> (off the UI thread);</item>
/// <item>start failures are logged and counted, never silently swallowed;</item>
/// <item>disposal stops the monitor first.</item>
/// </list>
/// </summary>
public abstract class PlatformMonitor<TSnapshot> : IPlatformMonitor<TSnapshot>
    where TSnapshot : class, IEquatable<TSnapshot>
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SnapshotPublisher<TSnapshot> _publisher;
    private int _state = (int)PlatformMonitorState.Stopped;
    private int _disposed;

    protected PlatformMonitor(string monitorId, IPlatformLogger logger, PlatformCounters counters, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);
        MonitorId = monitorId;
        Logger = logger ?? NullPlatformLogger.Instance;
        Counters = counters ?? new PlatformCounters();
        TimeProvider = timeProvider ?? TimeProvider.System;
        _publisher = new SnapshotPublisher<TSnapshot>(monitorId, Logger, Counters);
    }

    public string MonitorId { get; }

    public PlatformMonitorState State => (PlatformMonitorState)Volatile.Read(ref _state);

    public TSnapshot? Current => _publisher.Current;

    public event EventHandler<SnapshotEventArgs<TSnapshot>>? SnapshotChanged
    {
        add => _publisher.Changed += value;
        remove => _publisher.Changed -= value;
    }

    protected IPlatformLogger Logger { get; }

    protected PlatformCounters Counters { get; }

    protected TimeProvider TimeProvider { get; }

    /// <summary>Publishes a snapshot if it differs from the last one (coalesced, thread-safe).</summary>
    protected bool Publish(TSnapshot snapshot) => _publisher.Publish(snapshot);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is PlatformMonitorState.Running or PlatformMonitorState.Starting)
            {
                return;
            }

            SetState(PlatformMonitorState.Starting);
            try
            {
                await OnStartAsync(cancellationToken).ConfigureAwait(false);
                SetState(PlatformMonitorState.Running);
                Logger.LogInfo(PlatformLogCategory.Monitor, $"{MonitorId}.started", "Monitor started.",
                    new Dictionary<string, object?> { ["monitor"] = MonitorId });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SetState(PlatformMonitorState.Stopped);
                throw;
            }
            catch (Exception ex)
            {
                SetState(PlatformMonitorState.Failed);
                Counters.IncrementMonitorStartFailures();
                Logger.LogError(PlatformLogCategory.Monitor, $"{MonitorId}.start-failed",
                    $"Monitor '{MonitorId}' failed to start.", ex);
                await SafeOnStartCleanupAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is PlatformMonitorState.Stopped or PlatformMonitorState.Stopping)
            {
                return;
            }

            SetState(PlatformMonitorState.Stopping);
            try
            {
                await OnStopAsync(cancellationToken).ConfigureAwait(false);
                SetState(PlatformMonitorState.Stopped);
                Logger.LogInfo(PlatformLogCategory.Monitor, $"{MonitorId}.stopped", "Monitor stopped.",
                    new Dictionary<string, object?> { ["monitor"] = MonitorId });
            }
            catch (Exception ex)
            {
                Counters.IncrementMonitorStopFailures();
                Logger.LogError(PlatformLogCategory.Monitor, $"{MonitorId}.stop-failed",
                    $"Monitor '{MonitorId}' failed to stop cleanly.", ex);
                SetState(PlatformMonitorState.Failed);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>Starts the producer. Must not block; must wire up event-driven sources.</summary>
    protected abstract ValueTask OnStartAsync(CancellationToken cancellationToken);

    /// <summary>Unsubscribes and releases everything acquired in <see cref="OnStartAsync"/>. Must tolerate partial starts.</summary>
    protected abstract ValueTask OnStopAsync(CancellationToken cancellationToken);

    /// <summary>Called after a failed start; default delegates to <see cref="OnStopAsync"/> for cleanup.</summary>
    protected virtual async ValueTask SafeOnStartCleanupAsync()
    {
        try
        {
            await OnStopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Monitor, $"{MonitorId}.start-cleanup-failed",
                $"Cleanup after failed start of '{MonitorId}' also failed.", ex);
        }
    }

    private void SetState(PlatformMonitorState state) => Volatile.Write(ref _state, (int)state);

    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _publisher.Dispose();
            SetState(PlatformMonitorState.Disposed);
            _lifecycleGate.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
