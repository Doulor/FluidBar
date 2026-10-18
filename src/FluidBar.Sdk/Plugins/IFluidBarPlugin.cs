namespace FluidBar.Sdk;

/// <summary>
/// The plugin lifecycle contract (frozen v2.0 surface). The host calls the methods in order:
/// <see cref="InitializeAsync"/> once, <see cref="StartAsync"/> once per activation,
/// <see cref="StopAsync"/> before deactivation and <see cref="IAsyncDisposable.DisposeAsync"/> on unload.
/// Every method is async, cancellable and subject to a host timeout; exceptions are caught, logged and
/// isolate the plugin (the host itself always survives).
/// </summary>
public interface IFluidBarPlugin : IAsyncDisposable
{
    /// <summary>Receives the host-owned services. Must not block; called with a 5 second host timeout.</summary>
    ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken);

    /// <summary>Starts work. Must not block; called with a 5 second host timeout.</summary>
    ValueTask StartAsync(CancellationToken cancellationToken);

    /// <summary>Stops work and releases leases/timers. Called with a 5 second host timeout.</summary>
    ValueTask StopAsync(CancellationToken cancellationToken);
}
