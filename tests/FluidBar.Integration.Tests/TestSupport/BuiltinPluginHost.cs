using FluidBar.Plugins.Builtin;
using FluidBar.Sdk;
using SdkEvent = FluidBar.Sdk.IslandEvent;

namespace FluidBar.Integration.Tests.TestSupport;

/// <summary>One running built-in plugin plus its test host context.</summary>
/// <param name="Descriptor">Catalog descriptor.</param>
/// <param name="Plugin">Plugin instance.</param>
/// <param name="Context">Test host context.</param>
public sealed record BuiltinPluginRun(
    BuiltinPluginDescriptor Descriptor,
    IFluidBarPlugin Plugin,
    TestPluginContext Context);

/// <summary>
/// Drives <see cref="BuiltinPluginCatalog"/> end-to-end: constructs the plugin set from fake platform
/// services, runs the real SDK lifecycle (Initialize/Start/Stop/DisposeAsync) with a test host context,
/// and exposes the published SDK events.
/// </summary>
public sealed class BuiltinPluginHost : IAsyncDisposable
{
    private readonly List<BuiltinPluginRun> _runs;

    private BuiltinPluginHost(List<BuiltinPluginRun> runs) => _runs = runs;

    /// <summary>All running plugins (catalog order).</summary>
    public IReadOnlyList<BuiltinPluginRun> Plugins => _runs;

    /// <summary>All SDK events published by every plugin, in publication order.</summary>
    public IReadOnlyList<SdkEvent> PublishedEvents
        => [.. _runs.SelectMany(run => run.Context.Publisher.Events)];

    /// <summary>Constructs and starts every plugin whose platform dependencies are available.</summary>
    public static async Task<BuiltinPluginHost> StartAsync(BuiltinPlatformServices services, TimeProvider time)
    {
        var instances = BuiltinPluginCatalog.CreateAll(services);
        var runs = new List<BuiltinPluginRun>(instances.Count);
        var host = new BuiltinPluginHost(runs);
        try
        {
            foreach (var instance in instances)
            {
                var context = new TestPluginContext(instance.Descriptor.Id, time);
                var plugin = instance.Plugin;
                await plugin.InitializeAsync(context, CancellationToken.None).ConfigureAwait(false);
                await plugin.StartAsync(CancellationToken.None).ConfigureAwait(false);
                runs.Add(new BuiltinPluginRun(instance.Descriptor, plugin, context));
            }

            return host;
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Finds the running plugin by catalog id; throws when it was not constructed.</summary>
    public BuiltinPluginRun Get(string pluginId)
        => _runs.Single(run => string.Equals(run.Descriptor.Id, pluginId, StringComparison.Ordinal));

    /// <summary>Events published by one plugin.</summary>
    public IReadOnlyList<SdkEvent> EventsOf(string pluginId) => Get(pluginId).Context.Publisher.Events;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        for (var i = _runs.Count - 1; i >= 0; i--)
        {
            var run = _runs[i];
            await run.Plugin.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await run.Plugin.DisposeAsync().ConfigureAwait(false);
            run.Context.Dispose();
        }

        _runs.Clear();
    }
}
