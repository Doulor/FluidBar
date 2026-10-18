namespace FluidBar.Soak;

/// <summary>Deterministic dispatcher for the harness: posts queue until the scenario pumps them.</summary>
public sealed class ManualDispatcher : FluidBar.Core.IDispatcher
{
    private readonly Queue<Action> _pending = new();

    /// <inheritdoc />
    public bool IsOnDispatcherThread { get; private set; }

    /// <summary>Number of queued callbacks.</summary>
    public int PendingCount
    {
        get
        {
            lock (_pending)
            {
                return _pending.Count;
            }
        }
    }

    /// <inheritdoc />
    public void Post(Action action)
    {
        lock (_pending)
        {
            _pending.Enqueue(action);
        }
    }

    /// <summary>Runs every queued callback on the simulated dispatcher thread.</summary>
    public int RunAll()
    {
        var ran = 0;
        IsOnDispatcherThread = true;
        try
        {
            while (true)
            {
                Action action;
                lock (_pending)
                {
                    if (_pending.Count == 0)
                    {
                        break;
                    }

                    action = _pending.Dequeue();
                }

                action();
                ran++;
            }
        }
        finally
        {
            IsOnDispatcherThread = false;
        }

        return ran;
    }
}
