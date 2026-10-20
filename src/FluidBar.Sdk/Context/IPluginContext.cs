using System.Collections.Immutable;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Pages;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Sdk;

/// <summary>
/// Host-owned services exposed to a plugin. The surface is deliberately free of UI types so the same
/// contract can later be served over IPC for the out-of-process lane.
/// </summary>
public interface IPluginContext
{
    /// <summary>The manifest id; the host enforces that events/commands use this namespace.</summary>
    string PluginId { get; }

    /// <summary>The SDK ABI version the host implements.</summary>
    SemanticVersion ApiVersion { get; }

    /// <summary>Read-only plugin package directory (code). Never write here.</summary>
    string PackageDirectory { get; }

    /// <summary>Writable per-plugin data directory (<c>$PLUGIN_DATA</c>).</summary>
    string DataDirectory { get; }

    /// <summary>Event publishing abstraction. Non-blocking; returns <see langword="false"/> when dropped.</summary>
    IIslandPublisher Islands { get; }

    /// <summary>Plugin-scoped localization with the documented fallback chain.</summary>
    IPluginLocalization Localization { get; }

    /// <summary>Host-owned, schema-validated, per-plugin settings store.</summary>
    IPluginSettings Settings { get; }

    /// <summary>Per-plugin file storage rooted at <see cref="DataDirectory"/> with traversal protection.</summary>
    IPluginStorage Storage { get; }

    /// <summary>Structured per-plugin log.</summary>
    IPluginLog Log { get; }

    /// <summary>The only supported way to marshal work onto the host UI thread.</summary>
    IHostDispatcher Dispatcher { get; }

    /// <summary>Leases that own timers/subscriptions and are released on stop/unload.</summary>
    IPluginLeases Leases { get; }

    /// <summary>Binds handlers to manifest-declared commands.</summary>
    ICommandRegistry Commands { get; }

    /// <summary>Posts a host-rendered notification.</summary>
    INotificationSink Notifications { get; }

    /// <summary>Capabilities the user consented to. Advisory in-process; enforceable out-of-process.</summary>
    IReadOnlySet<PluginCapability> GrantedCapabilities { get; }

    /// <summary>Host clock abstraction (tests can virtualize time).</summary>
    TimeProvider Time { get; }

    /// <summary>
    /// Cancelled while the host is stopping/unloading this plugin. Long-running plugin work should observe it.
    /// The loader cannot abort a hung in-process call; plugins must never block and must release their own threads.
    /// </summary>
    CancellationToken HostStopping { get; }

    /// <summary>
    /// Registers a resource (timer, thread handle, subscription, native handle) for disposal by the loader.
    /// Disposal is LIFO after <c>StopAsync</c>/<c>DisposeAsync</c> when the context is torn down.
    /// </summary>
    IDisposable RegisterForDisposal(Action disposeAction);
}

/// <summary>Non-blocking island publishing abstraction implemented by the host.</summary>
public interface IIslandPublisher
{
    /// <summary>Publishes an event; returns <see langword="false"/> when the host queue dropped it.</summary>
    bool TryPublish(IslandEvent islandEvent);

    /// <summary>Retracts the current event for a source (e.g. media playback stopped).</summary>
    bool TryRetract(string source);

    /// <summary>
    /// Registers one host-rendered page. Default implementation returns false so existing in-process
    /// publisher implementations (and older plugins) do not have to be rewritten in lockstep.
    /// </summary>
    bool RegisterPage(PageDescriptor descriptor) => false;

    /// <summary>Publishes new template data for a page previously registered by this plugin.</summary>
    bool PublishPage(PageData data) => false;

    /// <summary>Removes a page previously registered by this plugin (host also does this on stop).</summary>
    bool UnregisterPage(string pageId) => false;

    /// <summary>
    /// Publishes a generic Status/Progress compact activity through the shared host template. Default
    /// implementation forwards to <see cref="TryPublish"/> so existing publishers keep working.
    /// </summary>
    bool PublishActivity(IslandActivity activity)
    {
        if (activity is null || string.IsNullOrWhiteSpace(activity.Source) || string.IsNullOrWhiteSpace(activity.Title))
        {
            return false;
        }

        return TryPublish(new IslandEvent
        {
            Source = activity.Source,
            Title = activity.Title,
            Content = activity.Body,
            IconKind = activity.IconToken,
            Kind = activity.Kind == IslandActivityKind.Progress ? IslandEventKind.Progress : IslandEventKind.Status,
            Payload = new IslandEventPayload
            {
                Subtitle = activity.Subtitle,
                Badge = activity.Badge,
                Progress = activity.Progress,
                IsActive = activity.IsActive,
                DetailLines = activity.DetailLines,
                Fields = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["status"] = activity.StatusText,
                    ["title"] = activity.Title,
                    ["subtitle"] = activity.Subtitle,
                    ["icon"] = activity.IconToken,
                    ["badge"] = activity.Badge,
                },
            },
        });
    }
}

/// <summary>
/// Host UI-thread marshalling abstraction. Plugins must never touch host UI objects directly; the App
/// supplies the concrete implementation.
/// </summary>
public interface IHostDispatcher
{
    /// <summary>Returns <see langword="true"/> when the caller is already on the host UI thread.</summary>
    bool CheckAccess();

    /// <summary>Fire-and-forget post; returns <see langword="false"/> when the host is shutting down.</summary>
    bool TryPost(Action action);

    /// <summary>Invokes a function on the host UI thread and awaits the result.</summary>
    Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken);

    /// <summary>Invokes a function on the host UI thread and awaits the result.</summary>
    Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken);
}

/// <summary>Empty capability set for hosts/tests that do not grant anything.</summary>
public static class PluginContextDefaults
{
    /// <summary>An immutable empty capability set.</summary>
    public static IReadOnlySet<PluginCapability> NoCapabilities { get; } = ImmutableHashSet<PluginCapability>.Empty;
}
