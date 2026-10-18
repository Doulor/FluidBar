using System.Collections.Immutable;
using FluidBar.PluginHost.Logging;
using FluidBar.Sdk;

namespace FluidBar.PluginHost.Runtime;

/// <summary>
/// The plugin-facing island publisher. It validates the event against the SDK bounds, enforces that the source
/// matches the plugin id and forwards to the App-provided hub. Invalid events are dropped with a bounded,
/// per-plugin log so a buggy plugin cannot flood the log or the island pipeline.
/// </summary>
internal sealed class PluginIslandPublisher : IIslandPublisher
{
    private const int MaxLoggedInvalidEvents = 32;

    private readonly string _pluginId;
    private readonly IIslandPublisher _inner;
    private readonly IPluginHostLogger _logger;
    private readonly HashSet<string> _loggedInvalidSources = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _droppedCount;

    internal PluginIslandPublisher(string pluginId, IIslandPublisher inner, IPluginHostLogger logger)
    {
        _pluginId = pluginId;
        _inner = inner;
        _logger = logger;
    }

    internal long DroppedCount => Interlocked.Read(ref _droppedCount);

    public bool TryPublish(IslandEvent islandEvent)
    {
        var errors = IslandEventValidator.Validate(islandEvent);
        if (errors.Count > 0)
        {
            Drop(islandEvent?.Source ?? "<null>", string.Join("; ", errors));
            return false;
        }

        if (!string.Equals(islandEvent!.Source, _pluginId, StringComparison.Ordinal))
        {
            Drop(islandEvent.Source, $"Event source must equal the plugin id '{_pluginId}'.");
            return false;
        }

        if (_inner.TryPublish(islandEvent))
        {
            return true;
        }

        Interlocked.Increment(ref _droppedCount);
        return false;
    }

    public bool TryRetract(string source)
    {
        if (!string.Equals(source, _pluginId, StringComparison.Ordinal))
        {
            Drop(source, "Retract source must equal the plugin id.");
            return false;
        }

        return _inner.TryRetract(source);
    }

    private void Drop(string source, string reason)
    {
        Interlocked.Increment(ref _droppedCount);
        lock (_gate)
        {
            if (_loggedInvalidSources.Count >= MaxLoggedInvalidEvents || !_loggedInvalidSources.Add(source))
            {
                return;
            }
        }

        _logger.Warn(_pluginId, $"Dropped invalid island event from '{source}': {reason}");
    }
}

/// <summary>Plugin-facing notification sink wrapper with bounded field validation.</summary>
internal sealed class PluginNotificationSink : INotificationSink
{
    private const int MaxTitleLength = 256;
    private const int MaxBodyLength = 2_048;
    private const int MaxLoggedInvalidNotifications = 16;

    private readonly string _pluginId;
    private readonly INotificationSink _inner;
    private readonly IPluginHostLogger _logger;
    private readonly object _gate = new();
    private int _loggedInvalid;

    internal PluginNotificationSink(string pluginId, INotificationSink inner, IPluginHostLogger logger)
    {
        _pluginId = pluginId;
        _inner = inner;
        _logger = logger;
    }

    public bool TryPost(PluginNotification notification)
    {
        if (notification is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(notification.Title) || notification.Title.Length > MaxTitleLength)
        {
            LogInvalid("title must be 1-256 characters");
            return false;
        }

        if (notification.Body is { Length: > MaxBodyLength })
        {
            LogInvalid("body exceeds 2048 characters");
            return false;
        }

        return _inner.TryPost(notification);
    }

    private void LogInvalid(string reason)
    {
        lock (_gate)
        {
            if (_loggedInvalid >= MaxLoggedInvalidNotifications)
            {
                return;
            }

            _loggedInvalid++;
        }

        _logger.Warn(_pluginId, $"Dropped invalid notification: {reason}.");
    }
}

/// <summary>Discards island events (headless host/tests without an App pipeline).</summary>
internal sealed class NullIslandPublisher : IIslandPublisher
{
    internal static NullIslandPublisher Instance { get; } = new();

    public bool TryPublish(IslandEvent islandEvent) => true;

    public bool TryRetract(string source) => true;
}

/// <summary>Discards notifications (headless host/tests without an App notification service).</summary>
internal sealed class NullNotificationSink : INotificationSink
{
    internal static NullNotificationSink Instance { get; } = new();

    public bool TryPost(PluginNotification notification) => true;
}

/// <summary>
/// Inline dispatcher used by headless hosts and tests: work executes on the calling thread. The App replaces it
/// with a real UI-thread dispatcher; plugins can then rely on <see cref="IHostDispatcher"/> in both cases.
/// </summary>
public sealed class InlineHostDispatcher : IHostDispatcher
{
    public static InlineHostDispatcher Instance { get; } = new();

    private InlineHostDispatcher()
    {
    }

    public bool CheckAccess() => true;

    public bool TryPost(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
        return true;
    }

    public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return action();
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return action();
    }
}

/// <summary>Shared capability/source defaults for the runtime composition.</summary>
internal static class RuntimeDefaults
{
    internal static IReadOnlyList<string> EmptyLocales { get; } = ImmutableArray<string>.Empty;
}
