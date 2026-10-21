namespace FluidBar.Core.Activities;

/// <summary>
/// Priority, persistence and default TTL policy for activity kinds (spec 2.3).
/// Lower rank wins. Rank 10 is the Agent extension from the lead's priority brief;
/// every other relative order matches the spec's table.
/// </summary>
public static class ActivityPriorityPolicy
{
    /// <summary>Rank of <see cref="ActivityKind.Lookup"/> (highest).</summary>
    public const int LookupRank = 1;

    /// <summary>Rank of <see cref="ActivityKind.Clipboard"/>.</summary>
    public const int ClipboardRank = 2;

    /// <summary>Rank of <see cref="ActivityKind.LockKeys"/>.</summary>
    public const int LockKeysRank = 3;

    /// <summary>Rank of <see cref="ActivityKind.Device"/>.</summary>
    public const int DeviceRank = 4;

    /// <summary>Rank of <see cref="ActivityKind.Bluetooth"/>.</summary>
    public const int BluetoothRank = 5;

    /// <summary>Rank of <see cref="ActivityKind.Focus"/> (first ambient kind).</summary>
    public const int FocusRank = 6;

    /// <summary>Rank of <see cref="ActivityKind.Volume"/>.</summary>
    public const int VolumeRank = 7;

    /// <summary>Rank of <see cref="ActivityKind.Brightness"/>.</summary>
    public const int BrightnessRank = 8;

    /// <summary>Rank of <see cref="ActivityKind.Notification"/>.</summary>
    public const int NotificationRank = 9;

    /// <summary>Rank of <see cref="ActivityKind.Agent"/> (lead-brief extension).</summary>
    public const int AgentRank = 10;

    /// <summary>Rank of <see cref="ActivityKind.BatteryLow"/>.</summary>
    public const int BatteryLowRank = 11;

    /// <summary>Rank of <see cref="ActivityKind.Progress"/>.</summary>
    public const int ProgressRank = 12;

    /// <summary>Rank of <see cref="ActivityKind.Timer"/>.</summary>
    public const int TimerRank = 13;

    /// <summary>Rank of <see cref="ActivityKind.Media"/>.</summary>
    public const int MediaRank = 14;

    /// <summary>Rank of the idle surface (lowest; not produced by events).</summary>
    public const int IdleRank = 15;

    /// <summary>Highest rank that counts as "user-initiated discrete feedback" (rules 1-5).</summary>
    public const int UserInitiatedMaxRank = BluetoothRank;

    /// <summary>Returns the rank of <paramref name="kind"/>; lower is more important.</summary>
    public static int Rank(ActivityKind kind) => kind switch
    {
        ActivityKind.Lookup => LookupRank,
        ActivityKind.Clipboard => ClipboardRank,
        ActivityKind.LockKeys => LockKeysRank,
        ActivityKind.Device => DeviceRank,
        ActivityKind.Bluetooth => BluetoothRank,
        ActivityKind.Focus => FocusRank,
        ActivityKind.Volume => VolumeRank,
        ActivityKind.Brightness => BrightnessRank,
        ActivityKind.Notification => NotificationRank,
        ActivityKind.Agent => AgentRank,
        ActivityKind.BatteryLow => BatteryLowRank,
        ActivityKind.Progress => ProgressRank,
        ActivityKind.Timer => TimerRank,
        ActivityKind.Media => MediaRank,
        ActivityKind.Idle => IdleRank,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown activity kind."),
    };

    /// <summary>
    /// "Persistent" kinds (spec 2.3 rule 2): when preempted they stay live in the background
    /// layer instead of being discarded.
    /// </summary>
    public static bool IsPersistent(ActivityKind kind, ActivityPayload? payload = null) => kind switch
    {
        ActivityKind.Focus => true,
        ActivityKind.BatteryLow => true,
        ActivityKind.Progress => true,
        ActivityKind.Timer => true,
        // PlaybackStatus=Playing is authoritative; accept wave/badge fallback when a player omits
        // IsActive on a heartbeat but still reports playing content. Paused media stays ordinary.
        ActivityKind.Media => payload is null || payload.IsActive == true || payload.ShowsAudioWave == true ||
            string.Equals(payload.Badge, "playing", StringComparison.OrdinalIgnoreCase),
        // A running Agent session is persistent content; a waiting/idle agent is a short event.
        ActivityKind.Agent => payload?.IsActive == true,
        _ => false,
    };

    /// <summary>
    /// Default TTL; <see langword="null"/> means the activity lives until it is retracted
    /// or replaced (persistent kinds, lookup). Payload-level <c>Ttl</c> overrides this.
    /// </summary>
    public static TimeSpan? DefaultTtl(ActivityKind kind) => kind switch
    {
        ActivityKind.Lookup => null,
        ActivityKind.Clipboard => TimeSpan.FromSeconds(4),
        ActivityKind.LockKeys => TimeSpan.FromSeconds(1.6),
        ActivityKind.Device => TimeSpan.FromSeconds(3),
        ActivityKind.Bluetooth => TimeSpan.FromSeconds(3),
        ActivityKind.Focus => null,
        ActivityKind.Volume => TimeSpan.FromSeconds(1.6),
        ActivityKind.Brightness => TimeSpan.FromSeconds(1.6),
        ActivityKind.Notification => TimeSpan.FromSeconds(5),
        // Completed/failed Agent rows stay visible for a 3 s completion beat, then expire.
        ActivityKind.Agent => TimeSpan.FromSeconds(3),
        ActivityKind.BatteryLow => null,
        ActivityKind.Progress => null,
        ActivityKind.Timer => null,
        ActivityKind.Media => null,
        ActivityKind.Idle => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown activity kind."),
    };

    /// <summary>Whether the kind is user-initiated discrete feedback (priority band 1).</summary>
    public static bool IsUserInitiatedDiscrete(ActivityKind kind) => Rank(kind) <= UserInitiatedMaxRank;

    /// <summary>Whether the kind is an ambient/system status (priority band 3, below media-user feedback).</summary>
    public static bool IsAmbient(ActivityKind kind) => kind is ActivityKind.Focus or ActivityKind.BatteryLow
        or ActivityKind.Progress or ActivityKind.Timer;

    /// <summary>Whether the kind may reveal a <c>Hidden</c> island on arrival (T1 guard).</summary>
    public static bool CanRevealWhenHidden(ActivityKind kind) => kind != ActivityKind.Idle;

    /// <summary>Resolves the effective rank for an activity record, preferring its snapshot rank.</summary>
    public static int RankOf(ActivityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.PriorityRank;
    }
}
