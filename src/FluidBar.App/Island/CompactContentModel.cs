using FluidBar.App.Controls;
using FluidBar.Core.Activities;

namespace FluidBar.App.Island;

/// <summary>Compact surface style selector (one visual per activity class).</summary>
public enum CompactStyle
{
    /// <summary>Clock + battery idle pill.</summary>
    Idle,

    /// <summary>Artwork + title/artist + transport.</summary>
    Media,

    /// <summary>Single-row volume/brightness HUD.</summary>
    Hud,

    /// <summary>Notification summary.</summary>
    Notification,

    /// <summary>Clipboard preview.</summary>
    Clipboard,

    /// <summary>Agent session status.</summary>
    Agent,

    /// <summary>Countdown timer.</summary>
    Timer,

    /// <summary>Low-battery warning.</summary>
    Battery,

    /// <summary>Caps/Num/Scroll lock feedback.</summary>
    LockKeys,

    /// <summary>USB device plug/unplug.</summary>
    Device,

    /// <summary>Bluetooth connect/disconnect.</summary>
    Bluetooth,

    /// <summary>Focus assist indicator.</summary>
    Focus,

    /// <summary>Long-running task progress.</summary>
    Progress,

    /// <summary>Search / lookup (clamped to host width; the focused lookup window is separate).</summary>
    Lookup,
}

/// <summary>
/// Immutable data for one compact island surface (the UI-side projection of
/// <c>FluidBar.Core.Presentation.ActivityViewModel</c> plus live clock/battery values).
/// </summary>
public sealed record CompactContentModel
{
    /// <summary>Which compact surface to draw.</summary>
    public required CompactStyle Style { get; init; }

    /// <summary>Primary text (user data, never translated).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Secondary text.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Body/preview text.</summary>
    public string? Body { get; init; }

    /// <summary>Small badge text (verification code, source).</summary>
    public string? Badge { get; init; }

    /// <summary>Source application name.</summary>
    public string? SourceName { get; init; }

    /// <summary>Icon identity for the surface.</summary>
    public IconKind Icon { get; init; } = IconKind.Clock;

    /// <summary>Clock text for the idle pill (HH:mm).</summary>
    public string? ClockText { get; init; }

    /// <summary>Localized short date for idle peek.</summary>
    public string? DateText { get; init; }

    /// <summary>Battery percentage for the idle pill (0..100), or null when unknown.</summary>
    public int? BatteryPercent { get; init; }

    /// <summary>Whether the battery is charging.</summary>
    public bool IsCharging { get; init; }

    /// <summary>Determinate progress 0..100.</summary>
    public int? ProgressPercent { get; init; }

    /// <summary>Indeterminate progress (spinner unavailable: draws a pulsing track).</summary>
    public bool ProgressIndeterminate { get; init; }

    /// <summary>Playback/charging/active flag.</summary>
    public bool IsActive { get; init; }

    /// <summary>Muted flag (volume HUD).</summary>
    public bool IsMuted { get; init; }

    /// <summary>Level 0..100 for HUD surfaces.</summary>
    public int? Level { get; init; }

    /// <summary>Whether the media wave should show.</summary>
    public bool ShowsWave { get; init; }

    /// <summary>Detected notification verification code.</summary>
    public string? VerificationCode { get; init; }

    /// <summary>Accent hex extracted from artwork/plugin payload.</summary>
    public string? AccentHex { get; init; }

    /// <summary>Countdown text for the timer surface (e.g. <c>24:59</c>).</summary>
    public string? TimerText { get; init; }

    /// <summary>Remaining time text (battery/timer).</summary>
    public string? RemainingText { get; init; }

    /// <summary>Activity kind this surface represents; <see langword="null"/> for idle.</summary>
    public ActivityKind? Kind { get; init; }

    /// <summary>Localization key for the primary kind label.</summary>
    public string? KindLabelKey { get; init; }

    /// <summary>Whether the pill is currently in the hover peek form.</summary>
    public bool IsPeeked { get; init; }

    /// <summary>Localized status text (agent/session state, battery state, ...).</summary>
    public string? StatusText { get; init; }

    /// <summary>Semantic tone for the status text/dot.</summary>
    public StatusTone Tone { get; init; } = StatusTone.Neutral;
}
