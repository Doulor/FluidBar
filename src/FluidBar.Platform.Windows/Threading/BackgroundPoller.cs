using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Threading;

/// <summary>
/// Dedicated background thread for work that needs a stable apartment and a periodic cadence
/// (device queries such as DDC/CI brightness). The tick runs on the poll thread; the thread is
/// signal-driven so <see cref="TriggerNow"/> wakes it immediately and cancellation never blocks.
/// </summary>
internal sealed class BackgroundPoller : IDisposable
{
    private readonly string _name;
    private volatile int _intervalMs;
    private readonly Action _onTick;
    private readonly Action? _onStarted;
    private readonly Action? _onStopped;
    private readonly IPlatformLogger _logger;
    private readonly ManualResetEventSlim _signal = new(false);
    private readonly Queue<(Action Action, TaskCompletionSource Completion)> _invocations = new();
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private int _disposed;

    public BackgroundPoller(string name, TimeSpan interval, Action onTick, IPlatformLogger logger,
        Action? onStarted = null, Action? onStopped = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // 200 ms floor: the media fallback poller reads one accessibility glyph and the user-visible
        // play/pause icon must follow a tap within a couple of frames. Everything else still polls at
        // >= 1 s; polling faster than 5 Hz is rejected as before.
        if (interval < TimeSpan.FromMilliseconds(200))
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Background polls must be at least 200 ms apart.");
        }

        _name = name;
        _intervalMs = (int)interval.TotalMilliseconds;
        _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
        _onStarted = onStarted;
        _onStopped = onStopped;
        _logger = logger ?? NullPlatformLogger.Instance;
    }

    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Current poll interval (diagnostics).</summary>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(Volatile.Read(ref _intervalMs));

    /// <summary>Changes the cadence immediately; the poll thread wakes and uses the new interval.</summary>
    public void SetInterval(TimeSpan interval)
    {
        if (interval < TimeSpan.FromMilliseconds(200))
        {
            interval = TimeSpan.FromMilliseconds(200);
        }

        var milliseconds = (int)interval.TotalMilliseconds;
        if (Interlocked.Exchange(ref _intervalMs, milliseconds) == milliseconds)
        {
            return;
        }

        _signal.Set();
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(PollThreadMain)
        {
            Name = $"FluidBar.Poller.{_name}",
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    /// <summary>Wakes the poll thread so it ticks (or runs queued invocations) immediately.</summary>
    public void TriggerNow() => _signal.Set();

    /// <summary>Runs <paramref name="action"/> on the poll thread.</summary>
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_invocations)
        {
            _invocations.Enqueue((action, completion));
        }

        _signal.Set();
        return completion.Task;
    }

    private void PollThreadMain()
    {
        try
        {
            _onStarted?.Invoke();
            while (!_cts.IsCancellationRequested)
            {
                DrainInvocations();
                if (_cts.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    _onTick();
                }
                catch (Exception ex)
                {
                    _logger.LogError(PlatformLogCategory.Monitor, "poller.tick-failed",
                        $"Background poll '{_name}' failed.", ex);
                }

                DrainInvocations();
                if (_cts.IsCancellationRequested)
                {
                    break;
                }

                _signal.Wait(TimeSpan.FromMilliseconds(Volatile.Read(ref _intervalMs)));
                _signal.Reset();
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(PlatformLogCategory.Monitor, "poller.crashed",
                $"Background poll '{_name}' crashed.", ex);
        }
        finally
        {
            while (true)
            {
                (Action Action, TaskCompletionSource Completion)? pending = null;
                lock (_invocations)
                {
                    if (_invocations.Count > 0)
                    {
                        pending = _invocations.Dequeue();
                    }
                }

                if (pending is null)
                {
                    break;
                }

                try
                {
                    pending.Value.Action();
                    pending.Value.Completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    pending.Value.Completion.TrySetException(ex);
                }
            }

            try
            {
                _onStopped?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(PlatformLogCategory.Monitor, "poller.cleanup-failed",
                    $"Cleanup for background poll '{_name}' failed.", ex);
            }
        }
    }

    private void DrainInvocations()
    {
        while (true)
        {
            (Action Action, TaskCompletionSource Completion) invocation;
            lock (_invocations)
            {
                if (_invocations.Count == 0)
                {
                    return;
                }

                invocation = _invocations.Dequeue();
            }

            try
            {
                invocation.Action();
                invocation.Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                invocation.Completion.TrySetException(ex);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        _signal.Set();
        _thread?.Join(TimeSpan.FromSeconds(5));
        _cts.Dispose();
        _signal.Dispose();
    }
}
