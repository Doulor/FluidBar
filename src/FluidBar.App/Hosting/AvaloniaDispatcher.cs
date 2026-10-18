using Avalonia.Threading;
using FluidBar.Core;
using CoreDispatcher = FluidBar.Core.IDispatcher;

namespace FluidBar.App.Hosting;

/// <summary>Marshals Core event-hub callbacks onto the Avalonia UI thread (spec 5.3 thread model).</summary>
public sealed class AvaloniaDispatcher : CoreDispatcher
{
    /// <summary>Shared instance.</summary>
    public static AvaloniaDispatcher Instance { get; } = new();

    private AvaloniaDispatcher()
    {
    }

    /// <inheritdoc />
    public bool IsOnDispatcherThread => Dispatcher.UIThread.CheckAccess();

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }
}
