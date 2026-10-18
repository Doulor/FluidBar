using FluidBar.Sdk;

namespace FluidBar.Plugins.SampleMinimal;

/// <summary>
/// The smallest useful FluidBar v2 plugin: initialize, publish one declarative-layout island event,
/// own a lease timer, stop cleanly. Everything it needs arrives through <see cref="IPluginContext"/>.
/// </summary>
public sealed class MinimalPlugin : IFluidBarPlugin
{
    private IPluginContext? _context;
    private IPluginLease? _lease;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        context.Log.Info("SampleMinimal initialized.");
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("InitializeAsync was not called.");
        _lease = context.Leases.CreateLease("sample-minimal.timer");
        _lease.SchedulePeriodic(
            TimeSpan.FromHours(12),
            _ =>
            {
                Publish(context, context.Localization.Get("island.body"));
                return ValueTask.CompletedTask;
            });

        Publish(context, context.Localization.Get("island.body"));
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _lease?.Dispose();
        _lease = null;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);

    private static void Publish(IPluginContext context, string body)
    {
        var title = context.Localization.Get("island.title");
        var published = context.Islands.TryPublish(new IslandEvent
        {
            Source = context.PluginId,
            Title = title,
            Content = body,
            Kind = IslandEventKind.Text,
            LayoutId = "sample-minimal.island",
            Payload = new IslandEventPayload
            {
                Fields = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["title"] = title,
                    ["subtitle"] = body,
                },
            },
        });

        if (!published)
        {
            context.Log.Warning("SampleMinimal island event was dropped by the host queue.");
        }
    }
}
