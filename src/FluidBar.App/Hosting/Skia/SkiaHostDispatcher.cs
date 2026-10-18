using FluidBar.Core;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Avalonia-free <see cref="IDispatcher"/> for the Skia island path. It captures the
/// <see cref="SynchronizationContext"/> of the constructing (UI) thread and posts work back to it;
/// when the host runs on a thread without a synchronization context (headless tests) work executes
/// inline, which keeps the pipeline deterministic without pulling Avalonia types into the host.
/// </summary>
public sealed class SkiaHostDispatcher : IDispatcher
{
    private readonly SynchronizationContext? _context;
    private readonly int _threadId;

    /// <summary>Creates a dispatcher bound to the calling thread.</summary>
    public SkiaHostDispatcher()
    {
        _context = SynchronizationContext.Current;
        _threadId = Environment.CurrentManagedThreadId;
    }

    /// <inheritdoc />
    public bool IsOnDispatcherThread => Environment.CurrentManagedThreadId == _threadId;

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsOnDispatcherThread)
        {
            action();
            return;
        }

        if (_context is not null)
        {
            _context.Post(static state => ((Action)state!)(), action);
            return;
        }

        // No synchronization context (headless/test host): run inline. The controller never touches
        // a window handle, so the worst case is a re-entrant reducer call, which the Core controller
        // already serialises behind its lock.
        action();
    }
}
