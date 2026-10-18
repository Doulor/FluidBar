using System.Collections.Immutable;
using FluidBar.Core.Activities;
using FluidBar.Core.Events;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Devices;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Monitors;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Update;

namespace FluidBar.Platform.Windows.CoreIntegration;

/// <summary>
/// Maps the platform's immutable snapshots onto the Core activity contract
/// (<see cref="ActivityPayload"/>) and builds <see cref="IslandEvent"/>s.
/// </summary>
/// <remarks>
/// <para>The mapper only fills structural/content fields. User-visible labels (for example the
/// "Volume" caption or the Chinese/English IME badge) stay in the Core presenter + i18n layer, so the
/// platform layer never hard-codes a localized string (rewrite guardrail 15).</para>
/// <para>Stable source tokens: <c>media:gsmtc</c>, <c>system:volume</c>, <c>system:brightness</c>,
/// <c>system:battery</c>, <c>system:lockkeys</c>, <c>system:network</c>, <c>system:ime</c>,
/// <c>clipboard:host</c>, <c>notif:toast</c>.</para>
/// </remarks>
public static class CoreSnapshotMapper
{
    public const string SourceMedia = "media:gsmtc";
    public const string SourceVolume = "system:volume";
    public const string SourceBrightness = "system:brightness";
    public const string SourceBattery = "system:battery";
    public const string SourceLockKeys = "system:lockkeys";
    public const string SourceNetwork = "system:network";
    public const string SourceInputMethod = "system:ime";
    public const string SourceClipboard = "clipboard:host";
    public const string SourceNotifications = "notif:toast";
    public const string SourceAgentClaude = "agent:claude";
    public const string SourceAgentCodex = "agent:codex";
    public const string SourceUsb = "system:usb";
    public const string SourceBluetooth = "system:bluetooth";
    public const string SourceUpdate = "update:check";

    /// <summary>Builds the activity key for a platform source token.</summary>
    public static ActivityKey Key(ActivityKind kind, string source) => new(kind, source);

    /// <summary>Builds a monotonic <see cref="IslandEvent"/> from a payload.</summary>
    public static IslandEvent ToEvent(ActivityKind kind, string source, IslandEventKind eventKind,
        ActivityPayload payload, long sequence, TimeSpan timestamp)
        => new(Key(kind, source), eventKind, sequence, timestamp, payload);

    /// <summary>Media snapshot → payload (title/artist/album, position, playing flag).</summary>
    public static ActivityPayload ToPayload(MediaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ActivityPayload
        {
            Title = snapshot.Title,
            Subtitle = string.IsNullOrWhiteSpace(snapshot.Artist) ? snapshot.Album : snapshot.Artist,
            SourceName = snapshot.SourceDisplayName ?? snapshot.SourceAppUserModelId,
            IsActive = snapshot.IsPlaying,
            ShowsAudioWave = snapshot.IsPlaying,
            PositionTicks = snapshot.Position.Ticks,
            EndTicks = snapshot.Duration.Ticks,
        };
    }

    /// <summary>Volume snapshot → payload (level 0–100, mute state).</summary>
    public static ActivityPayload ToPayload(VolumeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ActivityPayload
        {
            Level = snapshot.Percent,
            IsActive = !snapshot.IsMuted,
            // Icon token is a stable, non-localized identifier chosen by the presenter.
            IconToken = snapshot.IsMuted ? "volume_mute" : "volume",
        };
    }

    /// <summary>Brightness snapshot → payload (level 0–100).</summary>
    public static ActivityPayload ToPayload(BrightnessSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ActivityPayload
        {
            Level = snapshot.Percent,
            IsActive = snapshot.IsSupported,
            IconToken = "brightness",
        };
    }

    /// <summary>Battery snapshot → payload (level, charging state, remaining time).</summary>
    public static ActivityPayload ToPayload(BatterySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ActivityPayload
        {
            Level = snapshot.Percent < 0 ? null : snapshot.Percent,
            IsActive = snapshot.IsCharging,
            IconToken = snapshot.IsCharging ? "battery_charge" : snapshot.IsLow ? "battery_low" : "battery",
            DetailLines = snapshot.MinutesRemaining is { } minutes
                ? ImmutableArray.Create($"remaining_minutes:{minutes}")
                : ImmutableArray<string>.Empty,
        };
    }

    /// <summary>Lock-key state → payload (stable tokens in <see cref="ActivityPayload.DetailLines"/>).</summary>
    public static ActivityPayload ToPayload(LockKeysSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var tokens = ImmutableArray.CreateBuilder<string>(3);
        if (snapshot.CapsLockOn)
        {
            tokens.Add("capslock:on");
        }

        if (snapshot.NumLockOn)
        {
            tokens.Add("numlock:on");
        }

        if (snapshot.ScrollLockOn)
        {
            tokens.Add("scrolllock:on");
        }

        return new ActivityPayload
        {
            IconToken = "lockkey",
            DetailLines = tokens.ToImmutable(),
        };
    }

    /// <summary>Network snapshot → payload (adapter name + SSID content).</summary>
    public static ActivityPayload ToPayload(NetworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ActivityPayload
        {
            SourceName = snapshot.AdapterName,
            Body = snapshot.Ssid,
            IsActive = snapshot.IsConnected,
            IconToken = snapshot.Kind == NetworkAdapterKind.Wifi ? "network_wifi" : "network",
        };
    }

    /// <summary>Input-method snapshot → payload (mode flag; the label itself is localized by the presenter).</summary>
    public static ActivityPayload ToPayload(InputMethodSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ActivityPayload
        {
            IsActive = snapshot.IsChineseMode,
            IconToken = "inputmethod",
            // Stable tokens instead of the localized 中/英 label.
            DetailLines = snapshot.IsChineseMode
                ? ImmutableArray.Create("ime:native")
                : ImmutableArray.Create("ime:latin"),
        };
    }

    /// <summary>Clipboard snapshot → payload (latest text/body + file list detail lines).</summary>
    public static ActivityPayload ToPayload(ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var latest = snapshot.Latest;
        if (latest is null)
        {
            return ActivityPayload.Empty;
        }

        var details = latest.FilePaths.IsEmpty
            ? ImmutableArray<string>.Empty
            : latest.FilePaths;
        return new ActivityPayload
        {
            Body = latest.Text,
            DetailLines = details,
            IconToken = latest.Kind switch
            {
                ClipboardContentKind.Text => "clipboard_text",
                ClipboardContentKind.Image => "clipboard_image",
                ClipboardContentKind.Files => "clipboard_files",
                _ => "clipboard",
            },
        };
    }

    /// <summary>Notifications snapshot → payload for the latest toast (with verification code).</summary>
    public static ActivityPayload ToPayload(NotificationsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var latest = snapshot.Latest;
        if (latest is null)
        {
            return ActivityPayload.Empty;
        }

        return new ActivityPayload
        {
            Title = latest.Title,
            Subtitle = latest.AppName,
            Body = latest.Body,
            VerificationCode = latest.VerificationCode,
            Badge = latest.VerificationCode,
            IconToken = "notification",
        };
    }

    /// <summary>Agent inbox snapshot → payload (latest completion/failure, stable kind token).</summary>
    public static ActivityPayload ToPayload(AgentEventSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var latest = snapshot.Latest;
        if (latest is null)
        {
            return ActivityPayload.Empty;
        }

        return new ActivityPayload
        {
            Title = latest.Title,
            Body = latest.Message,
            SourceName = latest.Agent.ToString(),
            IconToken = latest.Kind switch
            {
                AgentEventKind.Completed => "agent_completed",
                AgentEventKind.Failed => "agent_failed",
                AgentEventKind.WaitingForInput => "agent_waiting",
                _ => "agent",
            },
            DetailLines = latest.Kind switch
            {
                AgentEventKind.Completed => ["agent:completed"],
                AgentEventKind.Failed => ["agent:failed"],
                AgentEventKind.WaitingForInput => ["agent:waiting"],
                _ => ["agent:event"],
            },
        };
    }

    /// <summary>Device snapshot → payload (device name content + kind/change tokens).</summary>
    public static ActivityPayload ToPayload(DeviceEventSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var latest = snapshot.Latest;
        if (latest is null)
        {
            return ActivityPayload.Empty;
        }

        return new ActivityPayload
        {
            Title = latest.Name,
            IsActive = latest.Change == DeviceChangeKind.Connected,
            IconToken = latest.Kind == DeviceKind.Bluetooth ? "bluetooth" : "usb",
            DetailLines = latest.Kind == DeviceKind.Bluetooth
                ? [latest.Change == DeviceChangeKind.Connected ? "bluetooth:connected" : "bluetooth:disconnected"]
                : [latest.Change == DeviceChangeKind.Connected ? "usb:arrival" : "usb:removal"],
        };
    }

    /// <summary>Update state → payload (stable state token; the App decides the localized text).</summary>
    public static ActivityPayload ToPayload(UpdateStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.State is UpdateState.Idle or UpdateState.Checking or UpdateState.UpToDate)
        {
            return ActivityPayload.Empty;
        }

        return new ActivityPayload
        {
            Title = snapshot.AvailableRelease?.Version.ToString() ?? string.Empty,
            Subtitle = snapshot.AvailableRelease?.Name,
            IsActive = snapshot.State is UpdateState.Downloading or UpdateState.Available,
            IconToken = "update",
            ProgressIndeterminate = snapshot.State == UpdateState.Downloading,
            DetailLines = snapshot.State switch
            {
                UpdateState.Available => ["update:available"],
                UpdateState.Downloading => ["update:downloading"],
                UpdateState.ReadyToInstall => ["update:ready"],
                _ => ["update:failed"],
            },
        };
    }
}
