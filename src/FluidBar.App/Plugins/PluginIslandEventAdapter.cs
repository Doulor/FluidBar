using System.Collections.Immutable;
using FluidBar.Core.Activities;
using FluidBar.App.Hosting.Pages;
using FluidBar.Core.Events;
using FluidBar.Plugins.Builtin;
using FluidBar.Sdk;
using FluidBar.Sdk.Pages;
using CoreEvent = FluidBar.Core.Events.IslandEvent;
using CoreEventKind = FluidBar.Core.Events.IslandEventKind;
using SdkEvent = FluidBar.Sdk.IslandEvent;
using SdkEventKind = FluidBar.Sdk.IslandEventKind;

namespace FluidBar.App.Plugins;

/// <summary>
/// Adapts SDK <see cref="SdkEvent"/> values produced by plugins onto the Core event hub model. Built-in
/// plugin ids map to the stable Core source tokens; community plugins use <c>plugin:&lt;id&gt;</c> so two
/// plugins can never collide.
/// </summary>
/// <remarks>
/// This is the App-owned boundary required by <c>docs/rewrite/contracts/plugin.md</c> §2/§8: the SDK never
/// references Core, and the App decides how a plugin event becomes an island activity.
/// </remarks>
public sealed class PluginIslandEventAdapter
{
    private readonly Dictionary<ActivityKey, long> _sequences = new();
    private readonly Dictionary<ActivityKey, string> _mediaTokens = new();
    private readonly object _gate = new();
    private long _timestampTicks;

    /// <summary>Maps one SDK event to one Core event (thread-safe; callers may publish from any thread).</summary>
    public CoreEvent Adapt(SdkEvent islandEvent)
    {
        ArgumentNullException.ThrowIfNull(islandEvent);
        var (kind, source) = MapActivity(islandEvent);
        var key = new ActivityKey(kind, source);

        lock (_gate)
        {
            var sequence = _sequences.TryGetValue(key, out var current) ? current + 1 : 1;
            _sequences[key] = sequence;

            var eventKind = CoreEventKind.New;
            if (kind == ActivityKind.Media)
            {
                var token = string.Join('|', islandEvent.Title, islandEvent.Payload?.Subtitle ?? string.Empty);
                if (_mediaTokens.TryGetValue(key, out var previous) && string.Equals(previous, token, StringComparison.Ordinal))
                {
                    eventKind = CoreEventKind.Incremental;
                }

                _mediaTokens[key] = token;
            }

            _timestampTicks += TimeSpan.TicksPerMillisecond * 10;
            return new CoreEvent(key, eventKind, sequence, TimeSpan.FromTicks(_timestampTicks), MapPayload(islandEvent, kind));
        }
    }

    /// <summary>Maps a plugin retraction to a Core retract event for the plugin's activity key.</summary>
    public CoreEvent Retract(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var (kind, coreSource) = MapSource(source);
        var key = new ActivityKey(kind, coreSource);
        lock (_gate)
        {
            var sequence = _sequences.TryGetValue(key, out var current) ? current + 1 : 1;
            _sequences[key] = sequence;
            _timestampTicks += TimeSpan.TicksPerMillisecond * 10;
            return CoreEvent.Retract(key, sequence, TimeSpan.FromTicks(_timestampTicks));
        }
    }

    /// <summary>Maps SDK plugin/event identity to a Core activity kind + stable source token.</summary>
    public static (ActivityKind Kind, string Source) MapActivity(SdkEvent islandEvent)
    {
        ArgumentNullException.ThrowIfNull(islandEvent);
        var (kind, source) = MapSource(islandEvent.Source);
        return kind == ActivityKind.Idle ? (InferKind(islandEvent), source) : (kind, source);
    }

    private static (ActivityKind Kind, string Source) MapSource(string source)
    {
        if (string.Equals(source, BuiltinIds.MediaPlugin, StringComparison.Ordinal))
        {
            return (ActivityKind.Media, "media:gsmtc");
        }

        if (string.Equals(source, BuiltinIds.ClipboardPlugin, StringComparison.Ordinal))
        {
            return (ActivityKind.Clipboard, "clipboard:host");
        }

        if (string.Equals(source, BuiltinIds.NotificationsPlugin, StringComparison.Ordinal))
        {
            return (ActivityKind.Notification, "notif:toast");
        }

        if (string.Equals(source, BuiltinIds.AgentPlugin, StringComparison.Ordinal))
        {
            return (ActivityKind.Agent, "agent:claude");
        }

        if (string.Equals(source, BuiltinIds.DevicesPlugin, StringComparison.Ordinal))
        {
            return (ActivityKind.Device, "system:usb");
        }

        return (ActivityKind.Idle, "plugin:" + source);
    }

    /// <summary>Infers a Core kind for generic plugin events from the SDK kind/icon token.</summary>
    public static ActivityKind InferKind(SdkEvent islandEvent)
    {
        ArgumentNullException.ThrowIfNull(islandEvent);
        switch (islandEvent.Kind)
        {
            case SdkEventKind.Media:
                return ActivityKind.Media;
            case SdkEventKind.Notification:
                return ActivityKind.Notification;
            case SdkEventKind.Agent:
                return ActivityKind.Agent;
            case SdkEventKind.Progress:
                return ActivityKind.Progress;
        }

        return (islandEvent.IconKind ?? string.Empty).ToLowerInvariant() switch
        {
            "media" => ActivityKind.Media,
            "clipboard" or "clipboard_text" or "clipboard_image" or "clipboard_files" => ActivityKind.Clipboard,
            "notification" => ActivityKind.Notification,
            "agent" => ActivityKind.Agent,
            "usb" => ActivityKind.Device,
            "bluetooth" => ActivityKind.Bluetooth,
            "volume" or "volume_mute" => ActivityKind.Volume,
            "brightness" => ActivityKind.Brightness,
            "lockkey" or "lockkeys" => ActivityKind.LockKeys,
            "battery" or "battery_low" or "battery_charge" => ActivityKind.BatteryLow,
            "timer" => ActivityKind.Timer,
            "progress" or "update" => ActivityKind.Progress,
            _ => ActivityKind.Notification,
        };
    }

    /// <summary>Maps the SDK payload onto the Core immutable payload.</summary>
    public static ActivityPayload MapPayload(SdkEvent islandEvent, ActivityKind kind)
    {
        ArgumentNullException.ThrowIfNull(islandEvent);
        var payload = islandEvent.Payload;
        var lyricLines = payload?.LyricLines;
        var progressPercent = payload?.Progress is { } progress
            ? Math.Clamp((int)Math.Round(progress * 100, MidpointRounding.AwayFromZero), 0, 100)
            : (int?)null;

        return new ActivityPayload
        {
            Title = islandEvent.Title,
            Subtitle = payload?.Subtitle,
            Body = islandEvent.Content,
            Badge = payload?.Badge,
            SourceName = payload?.SourceName,
            ProgressPercent = progressPercent,
            IsActive = payload?.IsActive,
            ShowsAudioWave = payload is not null
                ? payload.AudioWave is { Count: > 0 } || (kind == ActivityKind.Media && payload.IsActive)
                : kind == ActivityKind.Media,
            AlbumArtPath = payload?.AlbumArtPath,
            AppIconPath = payload?.AppIconPath,
            LyricLine = lyricLines is { Count: > 0 } ? lyricLines[0] : null,
            SecondaryLyricLine = lyricLines is { Count: > 1 } ? lyricLines[1] : null,
            DetailLines = payload?.DetailLines is { } lines ? [.. lines] : ImmutableArray<string>.Empty,
            PositionTicks = payload?.PlaybackTicks ?? 0,
            VerificationCode = payload?.VerificationCode,
        };
    }
}

/// <summary>One plugin's publisher: SDK event → Core event → the island host's event hub.</summary>
public sealed class AppPluginIslandPublisher : IIslandPublisher
{
    private readonly string _pluginId;
    private readonly PluginIslandEventAdapter _adapter;
    private readonly Action<CoreEvent> _publish;
    private readonly PageRegistry? _pages;

    /// <summary>Creates the publisher for one plugin.</summary>
    public AppPluginIslandPublisher(
        string pluginId,
        PluginIslandEventAdapter adapter,
        Action<CoreEvent> publish,
        PageRegistry? pages = null)
    {
        _pluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _pages = pages;
    }

    /// <inheritdoc />
    public bool RegisterPage(PageDescriptor descriptor)
        => descriptor is not null && _pages is not null
           && _pages.RegisterPage(_pluginId, descriptor, out _);

    /// <inheritdoc />
    public bool PublishPage(PageData data)
        => data is not null && _pages is not null && _pages.PublishPage(_pluginId, data, out _);

    /// <inheritdoc />
    public bool UnregisterPage(string pageId)
        => !string.IsNullOrWhiteSpace(pageId) && _pages is not null && _pages.UnregisterPage(_pluginId, pageId);

    /// <summary>Host-used cleanup when the plugin stops or unloads.</summary>
    public bool UnregisterAllPages() => _pages?.UnregisterOwner(_pluginId) == true;

    /// <inheritdoc />
    public bool TryPublish(SdkEvent islandEvent)
    {
        if (islandEvent is null)
        {
            return false;
        }

        try
        {
            _publish(_adapter.Adapt(islandEvent));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryRetract(string source)
    {
        if (!string.Equals(source, _pluginId, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            _publish(_adapter.Retract(source));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
