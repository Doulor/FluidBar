using System.Collections.Immutable;
using FluidBar.Core.Activities;
using FluidBar.Platform.Windows.CoreIntegration;
using FluidBar.Plugins.Builtin;
using FluidBar.Sdk;
using CoreEvent = FluidBar.Core.Events.IslandEvent;
using CoreEventKind = FluidBar.Core.Events.IslandEventKind;
using SdkEvent = FluidBar.Sdk.IslandEvent;

namespace FluidBar.Integration.Tests.TestSupport;

/// <summary>
/// The SDK → Core mapping the App must implement (test adapter; the contract is documented in
/// docs/rewrite/contracts/integration.md). Built-in plugin ids map to the stable Core source tokens;
/// generic plugin activities use the plugin id as their own stable source.
/// </summary>
/// <remarks>
/// <para>Kind mapping: SDK <see cref="IslandEventKind.Media"/> → <see cref="ActivityKind.Media"/>,
/// notification → notification, agent → agent, progress → progress; status/text/auto events are
/// classified by icon token (clipboard/media/notification/agent/usb/bluetooth/volume/brightness/
/// lockkey/battery/timer), falling back to <see cref="ActivityKind.Notification"/> for generic
/// transient text.</para>
/// <para>Event kind mapping: the first event for a key is <c>New</c>; subsequent media events for the
/// same track become <c>Incremental</c> so the hub coalesces progress/lyrics updates; other kinds stay
/// <c>New</c> (same-key in-place replacement).</para>
/// </remarks>
public sealed class SdkCoreEventAdapter
{
    private readonly Dictionary<ActivityKey, long> _sequences = new();
    private readonly Dictionary<ActivityKey, string> _mediaTokens = new();
    private long _timestampTicks;

    /// <summary>Maps one SDK event to one Core event.</summary>
    public CoreEvent Adapt(SdkEvent islandEvent)
    {
        ArgumentNullException.ThrowIfNull(islandEvent);
        var (kind, source) = MapActivity(islandEvent);
        var key = new ActivityKey(kind, source);
        var sequence = _sequences.TryGetValue(key, out var current) ? current + 1 : 1;
        _sequences[key] = sequence;

        var payload = MapPayload(islandEvent, kind);
        var eventKind = CoreEventKind.New;
        if (kind == ActivityKind.Media)
        {
            var token = MediaTrackToken(islandEvent);
            if (_mediaTokens.TryGetValue(key, out var previous) && string.Equals(previous, token, StringComparison.Ordinal))
            {
                eventKind = CoreEventKind.Incremental;
            }

            _mediaTokens[key] = token;
        }

        _timestampTicks += TimeSpan.TicksPerMillisecond * 10;
        return new CoreEvent(key, eventKind, sequence, TimeSpan.FromTicks(_timestampTicks), payload);
    }

    /// <summary>Maps a batch of SDK events.</summary>
    public ImmutableArray<CoreEvent> AdaptMany(IEnumerable<SdkEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var builder = ImmutableArray.CreateBuilder<CoreEvent>();
        foreach (var islandEvent in events)
        {
            builder.Add(Adapt(islandEvent));
        }

        return builder.ToImmutable();
    }

    /// <summary>Maps SDK plugin/event identity to a Core activity kind + stable source token.</summary>
    public static (ActivityKind Kind, string Source) MapActivity(SdkEvent islandEvent)
    {
        ArgumentNullException.ThrowIfNull(islandEvent);
        var fields = islandEvent.Payload?.Fields;

        if (string.Equals(islandEvent.Source, BuiltinIds.MediaPlugin, StringComparison.Ordinal))
        {
            return (ActivityKind.Media, CoreSnapshotMapper.SourceMedia);
        }

        if (string.Equals(islandEvent.Source, BuiltinIds.ClipboardPlugin, StringComparison.Ordinal))
        {
            return (ActivityKind.Clipboard, CoreSnapshotMapper.SourceClipboard);
        }

        if (string.Equals(islandEvent.Source, BuiltinIds.NotificationsPlugin, StringComparison.Ordinal))
        {
            return (ActivityKind.Notification, CoreSnapshotMapper.SourceNotifications);
        }

        if (string.Equals(islandEvent.Source, BuiltinIds.AgentPlugin, StringComparison.Ordinal))
        {
            var agentToken = Field(fields, "agent") ?? islandEvent.Payload?.Subtitle;
            var source = string.Equals(agentToken, "codex", StringComparison.OrdinalIgnoreCase)
                ? CoreSnapshotMapper.SourceAgentCodex
                : CoreSnapshotMapper.SourceAgentClaude;
            return (ActivityKind.Agent, source);
        }

        if (string.Equals(islandEvent.Source, BuiltinIds.DevicesPlugin, StringComparison.Ordinal))
        {
            var sourceToken = Field(fields, "source");
            if (string.Equals(sourceToken, CoreSnapshotMapper.SourceBluetooth, StringComparison.Ordinal))
            {
                return (ActivityKind.Bluetooth, CoreSnapshotMapper.SourceBluetooth);
            }

            return (ActivityKind.Device, sourceToken ?? CoreSnapshotMapper.SourceUsb);
        }

        return (InferKind(islandEvent), "plugin:" + islandEvent.Source);
    }

    /// <summary>Infers a Core kind for generic plugin events from SDK kind/icon token.</summary>
    public static ActivityKind InferKind(SdkEvent islandEvent)
    {
        ArgumentNullException.ThrowIfNull(islandEvent);
        switch (islandEvent.Kind)
        {
            case IslandEventKind.Media:
                return ActivityKind.Media;
            case IslandEventKind.Notification:
                return ActivityKind.Notification;
            case IslandEventKind.Agent:
                return ActivityKind.Agent;
            case IslandEventKind.Progress:
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

    private static string MediaTrackToken(SdkEvent islandEvent)
        => string.Join('|', islandEvent.Title, islandEvent.Payload?.Subtitle ?? string.Empty);

    private static string? Field(IReadOnlyDictionary<string, string?>? fields, string key)
        => fields is not null && fields.TryGetValue(key, out var value) ? value : null;
}
