namespace FluidBar.Core;

/// <summary>
/// Marshals work onto the UI thread. The core never references a concrete dispatcher:
/// Platform/App inject the implementation (Avalonia dispatcher, WPF dispatcher, test pump).
/// </summary>
public interface IDispatcher
{
    /// <summary>Whether the calling thread is the dispatcher thread.</summary>
    bool IsOnDispatcherThread { get; }

    /// <summary>
    /// Queues <paramref name="action"/> for execution on the dispatcher thread.
    /// Implementations must not block the caller and must preserve FIFO ordering per caller.
    /// Exceptions thrown by the action are the host's responsibility (log + isolate).
    /// </summary>
    void Post(Action action);
}

/// <summary>
/// Dispatcher that executes posted work synchronously on the calling thread.
/// Deterministic default for unit tests and headless hosts.
/// </summary>
public sealed class InlineDispatcher : IDispatcher
{
    /// <summary>Shared instance (the dispatcher is stateless).</summary>
    public static InlineDispatcher Instance { get; } = new();

    private InlineDispatcher()
    {
    }

    /// <inheritdoc />
    public bool IsOnDispatcherThread => true;

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }
}
