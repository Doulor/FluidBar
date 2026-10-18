using FluidBar.Plugins.SampleRich.Core;
using FluidBar.Sdk;

namespace FluidBar.Plugins.SampleRich;

/// <summary>
/// Rich reference sample: a local dependency, host-owned settings, declarative layouts/settings/commands/
/// context-menu/widget contributions, localization with plurals, and command handlers bound at runtime.
/// It contains no UI code and no XAML: the host renders every contribution.
/// </summary>
public sealed class RichPlugin : IFluidBarPlugin
{
    private readonly List<IDisposable> _registrations = [];
    private IPluginContext? _context;
    private IPluginLease? _lease;
    private IDisposable? _registeredResource;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (!context.Settings.TryGetBoolean("showDetails", out _))
        {
            context.Settings.SetBoolean("showDetails", true);
        }

        if (!context.Settings.TryGetInt32("refreshMinutes", out _))
        {
            context.Settings.SetInt32("refreshMinutes", 5);
        }

        if (!context.Settings.TryGetString("greetingStyle", out _))
        {
            context.Settings.SetString("greetingStyle", "friendly");
        }

        if (!context.Settings.TryGetString("status.lastRefresh", out _))
        {
            context.Settings.SetString("status.lastRefresh", "never");
        }

        if (!context.Settings.TryGetInt32("status.refreshCount", out _))
        {
            context.Settings.SetInt32("status.refreshCount", 0);
        }

        context.Log.Info($"SampleRich initialized (helper {TrackFormatter.LibraryVersion}).");
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("InitializeAsync was not called.");

        _registrations.Add(context.Commands.Register("sample-rich.refresh", (_, _) =>
        {
            Publish(context);
            return ValueTask.CompletedTask;
        }));
        _registrations.Add(context.Commands.Register("sample-rich.clear-cache", (_, _) =>
        {
            context.Settings.Remove("lastRefresh");
            context.Settings.SetString("status.lastRefresh", "cleared");
            context.Settings.SetInt32("status.refreshCount", 0);
            context.Log.Info("SampleRich cache cleared.");
            Publish(context);
            return ValueTask.CompletedTask;
        }));

        // Explicit resource registration: even if StopAsync is skipped, the loader releases this on unload.
        _registeredResource = context.RegisterForDisposal(() =>
        {
            context.Log.Info("SampleRich registered resource released by the loader.");
        });

        _lease = context.Leases.CreateLease("sample-rich.refresh");
        _lease.SchedulePeriodic(
            TimeSpan.FromMinutes(Math.Clamp(context.Settings.GetInt32("refreshMinutes", 5), 1, 60)),
            _ =>
            {
                Publish(context);
                return ValueTask.CompletedTask;
            });

        Publish(context);
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
        _registeredResource?.Dispose();
        _registeredResource = null;
        _lease?.Dispose();
        _lease = null;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);

    private static void Publish(IPluginContext context)
    {
        var title = context.Localization.Get("island.title");
        var track = context.Localization.Get("track.title");
        var artist = context.Localization.Get("track.artist");
        var subtitle = TrackFormatter.Format(track, artist);
        var body = context.Localization.Format(
            "island.body",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["count"] = context.Settings.GetBoolean("showDetails", true) ? 2 : 0,
                ["style"] = context.Settings.GetString("greetingStyle", "friendly"),
            });
        var tracksLabel = context.Localization.Plural("tracks.count", 2);
        var refreshedAt = DateTimeOffset.UtcNow.ToString("O");
        context.Settings.SetString("lastRefresh", refreshedAt);
        // Read-only status row: the plugin writes the displayed value into the host-owned settings store and the
        // host re-renders the status control (see docs/plugin/declarative-ui.md).
        context.Settings.SetString("status.lastRefresh", refreshedAt);
        context.Settings.SetInt32("status.refreshCount", context.Settings.GetInt32("status.refreshCount", 0) + 1);

        var published = context.Islands.TryPublish(new IslandEvent
        {
            Source = context.PluginId,
            Title = title,
            Content = body,
            Kind = IslandEventKind.Media,
            LayoutId = "sample-rich.compact",
            Payload = new IslandEventPayload
            {
                Subtitle = subtitle,
                Badge = TrackFormatter.Badge(track),
                SourceName = "FluidBar Sample Rich",
                IsActive = true,
                Progress = 0.42,
                Fields = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["title"] = title,
                    ["subtitle"] = subtitle,
                    ["badge"] = TrackFormatter.Badge(track),
                    ["tracksLabel"] = tracksLabel,
                },
            },
        });

        if (!published)
        {
            context.Log.Warning("SampleRich island event was dropped.");
        }
    }
}
