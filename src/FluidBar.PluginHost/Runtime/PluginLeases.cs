using System.Collections.Immutable;
using FluidBar.PluginHost.Logging;
using FluidBar.Sdk;
using FluidBar.Sdk.Capabilities;

namespace FluidBar.PluginHost.Runtime;

/// <summary>
/// Owns every <see cref="IPluginLease"/> of one plugin. The host disposes this coordinator before unloading
/// the AssemblyLoadContext: timers stop, tasks are awaited, then no delegate can pin plugin types.
/// </summary>
internal sealed class PluginLeaseCoordinator : IPluginLeases, IAsyncDisposable
{
    private readonly string _pluginId;
    private readonly IPluginHostLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<PluginLease> _leases = [];
    private readonly object _gate = new();
    private bool _disposed;

    internal PluginLeaseCoordinator(string pluginId, IPluginHostLogger logger, TimeProvider timeProvider)
    {
        _pluginId = pluginId;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    public IPluginLease CreateLease(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_leases.Count >= 32)
            {
                throw new InvalidOperationException($"Plugin '{_pluginId}' exceeded the 32-lease limit.");
            }

            var lease = new PluginLease(name, _pluginId, _logger, _timeProvider, _shutdown.Token);
            _leases.Add(lease);
            return lease;
        }
    }

    /// <summary>A task that completes when every lease task has finished (never faults).</summary>
    internal Task Completion
    {
        get
        {
            PluginLease[] leases;
            lock (_gate)
            {
                leases = [.. _leases];
            }

            return leases.Length == 0 ? Task.CompletedTask : Task.WhenAll(leases.Select(static lease => lease.Completion));
        }
    }

    /// <summary>Cancels and waits for every lease task, bounded by <paramref name="timeout"/>.</summary>
    internal async ValueTask DisposeAsync(TimeSpan timeout)
    {
        PluginLease[] leases;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            leases = [.. _leases];
            _leases.Clear();
        }

        foreach (var lease in leases)
        {
            lease.Dispose();
        }

        var completion = Task.WhenAll(leases.Select(static lease => lease.Completion));
        try
        {
            await completion.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.Warn(_pluginId, $"Timed out waiting {timeout} for plugin lease tasks to finish.");
        }
        finally
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
        }
    }

    /// <summary>Interface-compatible dispose used by the host; awaiting the coordinator is preferred.</summary>
    public void Dispose() => _shutdown.Cancel();

    /// <summary>IAsyncDisposable fallback; the host uses the timeout overload below.</summary>
    ValueTask IAsyncDisposable.DisposeAsync() => DisposeAsync(TimeSpan.FromSeconds(5));
}

/// <summary>
/// One lease. Timers never overlap (sequential awaits), exceptions are logged and contained, and schedule
/// tasks are awaited by the host before unload so delegates cannot pin the plugin AssemblyLoadContext.
/// </summary>
internal sealed class PluginLease : IPluginLease
{
    private const int MaxSchedules = 64;

    private readonly string _pluginId;
    private readonly IPluginHostLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _lifetime;
    private readonly List<Task> _tasks = [];
    private readonly object _gate = new();
    private long _disposed;

    internal PluginLease(
        string name,
        string pluginId,
        IPluginHostLogger logger,
        TimeProvider timeProvider,
        CancellationToken coordinatorToken)
    {
        Name = name;
        _pluginId = pluginId;
        _logger = logger;
        _timeProvider = timeProvider;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(coordinatorToken);
    }

    public string Name { get; }

    public CancellationToken CancellationToken => _lifetime.Token;

    public bool IsDisposed => Interlocked.Read(ref _disposed) != 0;

    internal Task Completion
    {
        get
        {
            Task[] tasks;
            lock (_gate)
            {
                tasks = [.. _tasks];
            }

            return tasks.Length == 0 ? Task.CompletedTask : Task.WhenAll(tasks);
        }
    }

    public IDisposable ScheduleOnce(TimeSpan dueTime, Func<CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (dueTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dueTime), "A schedule delay must not be negative.");
        }

        return AddSchedule(async token =>
        {
            await Task.Delay(dueTime, _timeProvider, token).ConfigureAwait(false);
            await callback(token).ConfigureAwait(false);
        });
    }

    public IDisposable SchedulePeriodic(TimeSpan period, Func<CancellationToken, ValueTask> callback, TimeSpan? initialDelay = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "A periodic timer needs a positive period.");
        }

        var delay = initialDelay ?? period;
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDelay), "A schedule delay must not be negative.");
        }

        return AddSchedule(async token =>
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, token).ConfigureAwait(false);
            }

            while (!token.IsCancellationRequested)
            {
                await callback(token).ConfigureAwait(false);
                await Task.Delay(period, _timeProvider, token).ConfigureAwait(false);
            }
        });
    }

    private IDisposable AddSchedule(Func<CancellationToken, ValueTask> body)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var scheduleCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var task = RunAsync(body, scheduleCancellation);
        lock (_gate)
        {
            if (_tasks.Count >= MaxSchedules)
            {
                scheduleCancellation.Dispose();
                throw new InvalidOperationException($"Plugin '{_pluginId}' exceeded the {MaxSchedules}-schedule lease limit.");
            }

            _tasks.Add(task);
        }

        return new ScheduleHandle(scheduleCancellation);
    }

    private async Task RunAsync(Func<CancellationToken, ValueTask> body, CancellationTokenSource scheduleCancellation)
    {
        try
        {
            await body(scheduleCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (scheduleCancellation.IsCancellationRequested)
        {
            // Expected on lease/schedule disposal: cancellation is the documented way to stop the timer.
        }
        catch (Exception exception)
        {
            _logger.Error(_pluginId, $"Lease '{Name}' callback failed: {exception.Message}", exception);
        }
        finally
        {
            scheduleCancellation.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed class ScheduleHandle(CancellationTokenSource cancellation) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            cancellation.Cancel();
            cancellation.Dispose();
        }
    }
}

/// <summary>Immutable granted-capability snapshot used by a context.</summary>
internal static class PluginCapabilityGrants
{
    internal static IReadOnlySet<Sdk.Capabilities.PluginCapability> Resolve(
        Sdk.Manifest.PluginManifest manifest,
        IReadOnlySet<Sdk.Capabilities.PluginCapability>? granted)
    {
        if (granted is not null)
        {
            return granted;
        }

        return manifest.Capabilities.GetDeclaredCapabilities().ToImmutableHashSet();
    }
}
