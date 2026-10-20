namespace FluidBar.Core.Activities;

/// <summary>
/// The 14 activity classes of FluidBar v2 (spec 2.3) plus <c>Agent</c>, which the
/// lead's priority brief adds between notifications and ambient system status.
/// </summary>
public enum ActivityKind
{
    /// <summary>Search / dictionary lookup. Rank 1; the only activity allowed to take focus.</summary>
    Lookup,

    /// <summary>Clipboard text/image preview. Rank 2, 4 s TTL.</summary>
    Clipboard,

    /// <summary>Caps/Num/Scroll lock key feedback. Rank 3, 1.6 s TTL.</summary>
    LockKeys,

    /// <summary>USB device plugged/unplugged. Rank 4, 3 s TTL.</summary>
    Device,

    /// <summary>Bluetooth connect/disconnect + battery. Rank 5, 3 s TTL.</summary>
    Bluetooth,

    /// <summary>Focus assist / do-not-disturb indicator. Rank 6, persistent.</summary>
    Focus,

    /// <summary>Volume HUD. Rank 7, 1.6 s TTL, single-line 40 DIP tall.</summary>
    Volume,

    /// <summary>Brightness HUD. Rank 8, 1.6 s TTL, single-line 40 DIP tall.</summary>
    Brightness,

    /// <summary>Notification toast summary. Rank 9, 5 s TTL.</summary>
    Notification,

    /// <summary>Agent (Claude/Codex) completion or failure feedback. Rank 10 (lead brief extension).</summary>
    Agent,

    /// <summary>Low battery warning. Rank 11, persistent.</summary>
    BatteryLow,

    /// <summary>Long-running task progress (update download...). Rank 12, persistent.</summary>
    Progress,

    /// <summary>Pomodoro/timer. Rank 13, persistent.</summary>
    Timer,

    /// <summary>Now-playing media. Rank 14, persistent; never auto-expands.</summary>
    Media,

    /// <summary>Default idle pill (clock + battery + page dots). Rank 15; never scheduled from an event.</summary>
    Idle,
}

/// <summary>
/// Stable identity of an activity: its <see cref="ActivityKind"/> plus the producing source.
/// "Same key again" means in-place replacement (spec 2.3 rule 1) and is also the coalescing
/// identity of the event bus.
/// </summary>
public readonly record struct ActivityKey
{
    /// <summary>Creates a key. <paramref name="source"/> must be non-empty (trimmed).</summary>
    /// <param name="kind">Activity class.</param>
    /// <param name="source">Stable producer identity, e.g. <c>gsmtc:Spotify.exe</c> or <c>notif:toast</c>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="source"/> is null/whitespace.</exception>
    public ActivityKey(ActivityKind kind, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Kind = kind;
        Source = source.Trim();
    }

    /// <summary>Activity class.</summary>
    public ActivityKind Kind { get; }

    /// <summary>Stable producer identity.</summary>
    public string Source { get; }

    /// <summary>Whether this key was constructed (the default struct value is invalid).</summary>
    public bool IsValid => !string.IsNullOrEmpty(Source);

    /// <summary>Stable token form: <c>kind:source</c>, e.g. <c>media:gsmtc</c>.</summary>
    public override string ToString() => IsValid ? $"{Kind.ToToken()}:{Source}" : "<invalid>";
}

/// <summary>Helpers for activity kinds.</summary>
public static class ActivityKindExtensions
{
    /// <summary>
    /// Spec activityKey token (spec 2.3): lowercase, no separators, <c>battery.low</c> exceptional.
    /// </summary>
    public static string ToToken(this ActivityKind kind) => kind switch
    {
        ActivityKind.Lookup => "lookup",
        ActivityKind.Clipboard => "clipboard",
        ActivityKind.LockKeys => "lockkeys",
        ActivityKind.Device => "device",
        ActivityKind.Bluetooth => "bluetooth",
        ActivityKind.Focus => "focus",
        ActivityKind.Volume => "volume",
        ActivityKind.Brightness => "brightness",
        ActivityKind.Notification => "notification",
        ActivityKind.Agent => "agent",
        ActivityKind.BatteryLow => "battery.low",
        ActivityKind.Progress => "progress",
        ActivityKind.Timer => "timer",
        ActivityKind.Media => "media",
        ActivityKind.Idle => "idle",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown activity kind."),
    };

    /// <summary>Parses a spec activityKey token; unknown tokens return <see langword="false"/>.</summary>
    public static bool TryParseToken(string? token, out ActivityKind kind)
    {
        kind = token switch
        {
            "lookup" => ActivityKind.Lookup,
            "clipboard" => ActivityKind.Clipboard,
            "lockkeys" => ActivityKind.LockKeys,
            "device" => ActivityKind.Device,
            "bluetooth" => ActivityKind.Bluetooth,
            "focus" => ActivityKind.Focus,
            "volume" => ActivityKind.Volume,
            "brightness" => ActivityKind.Brightness,
            "notification" => ActivityKind.Notification,
            "agent" => ActivityKind.Agent,
            "battery.low" or "battery" => ActivityKind.BatteryLow,
            "progress" => ActivityKind.Progress,
            "timer" => ActivityKind.Timer,
            "media" => ActivityKind.Media,
            "idle" => ActivityKind.Idle,
            _ => ActivityKind.Idle,
        };

        return token is not null && token is "lookup" or "clipboard" or "lockkeys" or "device" or "bluetooth"
            or "focus" or "volume" or "brightness" or "notification" or "agent" or "battery.low" or "battery"
            or "progress" or "timer" or "media" or "idle";
    }

    /// <summary>The expanded page this activity opens when the user clicks the compact pill.</summary>
    public static PageRef ToPage(this ActivityKind kind) => kind switch
    {
        ActivityKind.Media => PageRef.Media,
        ActivityKind.Notification => PageRef.Notifications,
        ActivityKind.Clipboard => PageRef.Clipboard,
        ActivityKind.Agent => PageRef.Agent,
        _ => PageRef.Home,
    };

    /// <summary>Whether the activity is a transient (TTL-driven) notification-style surface.</summary>
    public static bool IsTransientFeedback(this ActivityKind kind) => kind switch
    {
        ActivityKind.Clipboard or ActivityKind.LockKeys or ActivityKind.Volume or ActivityKind.Brightness
            or ActivityKind.Notification or ActivityKind.Agent or ActivityKind.Device or ActivityKind.Bluetooth => true,
        _ => false,
    };
}

