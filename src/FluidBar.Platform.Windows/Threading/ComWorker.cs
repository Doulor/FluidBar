using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Threading;

/// <summary>
/// Dedicated MTA thread that owns COM objects and serializes access to them. Audio endpoints,
/// WMI and other apartment-affine COM servers are activated and released on this thread only;
/// callers interact through async invocations, so no COM call ever happens on the UI thread.
/// </summary>
/// <remarks>
/// A dedicated thread (not a thread-pool task) is mandatory: COM apartment affinity is per-thread and
/// async continuations would otherwise migrate across threads.
/// </remarks>
public sealed class ComWorker : IAsyncDisposable
{
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly BlockingCollection<Action> _queue = new(new ConcurrentQueue<Action>());

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _name;
    private Thread? _thread;
    private int _disposed;

    public ComWorker(string name, IPlatformLogger logger, PlatformCounters counters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
    }

    /// <summary>True once the worker thread has entered the MTA and is pumping work.</summary>
    public bool IsStarted => _ready.Task.IsCompletedSuccessfully;

    /// <summary>Starts the MTA worker thread. Idempotent.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_thread is not null)
        {
            await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _thread = new Thread(WorkerMain)
        {
            Name = $"FluidBar.ComWorker.{_name}",
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="func"/> on the COM thread and returns its result.</summary>
    public async Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_thread is null)
        {
            throw new InvalidOperationException("ComWorker is not started; call StartAsync first.");
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.TryAdd(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    completion.TrySetResult(func());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetException(new ObjectDisposedException(nameof(ComWorker)));
        }

        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="action"/> on the COM thread.</summary>
    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return InvokeAsync(() =>
        {
            action();
            return true;
        }, cancellationToken);
    }

    private void WorkerMain()
    {
        var initialized = false;
        try
        {
            var hr = NativeMethods.Ole32.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_MULTITHREADED);
            initialized = hr is NativeMethods.S_OK or 1;
            if (!initialized && hr != NativeMethods.RPC_E_CHANGED_MODE)
            {
                _ready.TrySetException(new InvalidOperationException(
                    $"CoInitializeEx(MTA) failed with 0x{hr:X8}."));
                return;
            }

            _ready.TrySetResult();
            _logger.LogDebug(PlatformLogCategory.Platform, "comworker.started", "COM worker thread started.",
                new Dictionary<string, object?> { ["name"] = _name });

            foreach (var action in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _counters.IncrementComFailures();
                    _logger.LogError(PlatformLogCategory.Platform, "comworker.action-failed",
                        $"Unhandled failure in COM worker '{_name}'.", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(PlatformLogCategory.Platform, "comworker.crashed",
                $"COM worker '{_name}' failed.", ex);
            _ready.TrySetException(ex);
        }
        finally
        {
            if (initialized)
            {
                NativeMethods.Ole32.CoUninitialize();
            }

            _stopped.TrySetResult();
            _logger.LogDebug(PlatformLogCategory.Platform, "comworker.stopped", "COM worker thread stopped.",
                new Dictionary<string, object?> { ["name"] = _name });
        }
    }

    /// <summary>Drains pending work, releases the MTA and joins the worker thread (bounded, non-blocking).</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.CompleteAdding();
        if (_thread is not null)
        {
            try
            {
                await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning(PlatformLogCategory.Platform, "comworker.dispose-timeout",
                    $"COM worker '{_name}' did not stop within the timeout.");
            }
        }
    }

    /// <summary>
    /// Deterministic RCW release: <see cref="Marshal.FinalReleaseComObject"/> on the COM worker thread,
    /// tolerating already-released objects.
    /// </summary>
    public static void ReleaseComObject(object? comObject, IPlatformLogger logger)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch (Exception ex)
        {
            logger.LogWarning(PlatformLogCategory.Platform, "comworker.release-failed",
                "FinalReleaseComObject failed; the RCW may already have been released.", ex);
        }
    }
}
