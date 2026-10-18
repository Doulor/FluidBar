using FluidBar.Sdk;

namespace FluidBar.Plugins.Builtin;

/// <summary>
/// Shared lifecycle implementation for the first-party plugins.
/// </summary>
/// <remarks>
/// <para>The SDK host already isolates plugin exceptions, but first-party plugins additionally degrade
/// instead of throwing: a failed start disposes partial subscriptions and leaves the plugin stopped, so a
/// missing/denied platform capability can never take the host down.</para>
/// <para>All platform subscriptions are tracked and released on <see cref="StopAsync"/>; publishing and
/// command handlers are guarded so a subscriber/command failure is logged, not propagated.</para>
/// </remarks>
public abstract class BuiltinPluginBase : IFluidBarPlugin
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _subscriptions = new();
    private IPluginContext? _context;
    private bool _started;
    private int _disposed;

    /// <summary>Host context, available after <see cref="InitializeAsync"/>.</summary>
    protected IPluginContext Context => _context ?? throw new InvalidOperationException(
        "Plugin context is not available before InitializeAsync.");

    /// <summary>Whether the plugin has a host context.</summary>
    protected bool IsInitialized => _context is not null;

    /// <summary>Whether the plugin is currently started.</summary>
    protected bool IsStarted
    {
        get
        {
            lock (_gate)
            {
                return _started;
            }
        }
    }

    /// <summary>Manifest/catalog id (stable; used as the SDK event source).</summary>
    public abstract string PluginId { get; }

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate)
        {
            if (_context is not null)
            {
                return ValueTask.CompletedTask; // initialize once (SDK contract)
            }

            _context = context;
        }

        try
        {
            OnInitialized(context);
        }
        catch (Exception ex)
        {
            context.Log.Error($"{PluginId}: initialization failed.", ex);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        if (_context is null)
        {
            throw new InvalidOperationException("InitializeAsync must be called before StartAsync.");
        }

        lock (_gate)
        {
            if (_started)
            {
                return ValueTask.CompletedTask;
            }

            _started = true;
        }

        try
        {
            OnStarted();
        }
        catch (Exception ex)
        {
            _context.Log.Error($"{PluginId}: start failed; plugin disabled for this session.", ex);
            lock (_gate)
            {
                _started = false;
            }

            DisposeSubscriptions();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_started)
            {
                return ValueTask.CompletedTask;
            }

            _started = false;
        }

        try
        {
            OnStopped();
        }
        catch (Exception ex)
        {
            _context?.Log.Error($"{PluginId}: stop failed.", ex);
        }
        finally
        {
            DisposeSubscriptions();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Registers a platform event subscription for automatic disposal on stop.</summary>
    protected void TrackSubscription(IDisposable subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        lock (_gate)
        {
            _subscriptions.Add(subscription);
        }
    }

    /// <summary>Convenience: subscribes an event handler and tracks the unsubscribe action.</summary>
    protected void TrackSubscription(Action subscribe, Action unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);
        subscribe();
        TrackSubscription(new ActionSubscription(unsubscribe));
    }

    /// <summary>Publishes an island event through the host. Returns false when dropped.</summary>
    protected bool Publish(IslandEvent islandEvent)
    {
        try
        {
            return Context.Islands.TryPublish(islandEvent);
        }
        catch (Exception ex)
        {
            Context.Log.Error($"{PluginId}: publishing failed.", ex);
            return false;
        }
    }

    /// <summary>Retracts the plugin's activity from the island.</summary>
    protected bool Retract()
    {
        try
        {
            return Context.Islands.TryRetract(PluginId);
        }
        catch (Exception ex)
        {
            Context.Log.Error($"{PluginId}: retract failed.", ex);
            return false;
        }
    }

    /// <summary>Registers a command handler; undeclared/duplicate host commands are logged, not thrown.</summary>
    protected void RegisterCommand(string commandId, Func<PluginCommandContext, CancellationToken, ValueTask> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(commandId);
        ArgumentNullException.ThrowIfNull(handler);
        try
        {
            TrackSubscription(Context.Commands.Register(commandId,
                async (context, cancellationToken) =>
                {
                    try
                    {
                        await handler(context, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Context.Log.Error($"{PluginId}: command '{commandId}' failed.", ex);
                    }
                }));
        }
        catch (Exception ex)
        {
            Context.Log.Error($"{PluginId}: command registration failed for '{commandId}'.", ex);
        }
    }

    /// <summary>Runs an async fire-and-forget operation with exception isolation (bounded by the plugin token).</summary>
    protected void RunBackground(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var token = _context?.HostStopping ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            try
            {
                await operation(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _context?.Log.Error($"{PluginId}: background operation failed.", ex);
            }
        }, CancellationToken.None);
    }

    protected virtual void OnInitialized(IPluginContext context)
    {
    }

    /// <summary>Subscribes to platform events / registers commands. Must not block.</summary>
    protected abstract void OnStarted();

    /// <summary>Releases plugin-owned state beyond the tracked subscriptions.</summary>
    protected virtual void OnStopped()
    {
    }

    private void DisposeSubscriptions()
    {
        IDisposable[] subscriptions;
        lock (_gate)
        {
            subscriptions = _subscriptions.ToArray();
            _subscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            try
            {
                subscription.Dispose();
            }
            catch (Exception ex)
            {
                _context?.Log.Warning($"{PluginId}: subscription disposal failed: {ex.GetType().Name}.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private sealed class ActionSubscription : IDisposable
    {
        private Action? _dispose;

        public ActionSubscription(Action dispose) => _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
