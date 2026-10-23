namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Semantic page-control actions raised by the Skia island. The controller never talks to a platform
/// service directly: it maps a hit target to one of these actions and dispatches it through
/// <see cref="IIslandPlatformActions"/>, so the whole input path is testable headless with a fake.
/// </summary>
public enum IslandPlatformActionKind
{
    /// <summary>Play/pause the active media session.</summary>
    MediaTogglePlay,

    /// <summary>Skip to the previous track.</summary>
    MediaPrevious,

    /// <summary>Skip to the next track.</summary>
    MediaNext,

    /// <summary>Seek the active media session; <see cref="IslandPlatformAction.Value"/> is 0..1.</summary>
    MediaSeek,

    /// <summary>Set the system volume; <see cref="IslandPlatformAction.Value"/> is 0..1.</summary>
    VolumeSet,

    /// <summary>Toggle the system output mute (media footer volume button).</summary>
    VolumeMute,

    /// <summary>Set the display brightness; <see cref="IslandPlatformAction.Value"/> is 0..1.</summary>
    BrightnessSet,

    /// <summary>Toggle a home quick toggle; <see cref="IslandPlatformAction.Toggle"/> identifies it.</summary>
    QuickToggle,

    /// <summary>Open the audio-output picker (media page).</summary>
    MediaOutput,

    /// <summary>Open the play queue (media page).</summary>
    MediaQueue,

    /// <summary>Activate a notification row; <see cref="IslandPlatformAction.Index"/> is the row.</summary>
    NotificationActivate,

    /// <summary>Dismiss a notification row; <see cref="IslandPlatformAction.Index"/> is the row.</summary>
    NotificationDismiss,

    /// <summary>Copy a notification's verification code; <see cref="IslandPlatformAction.Text"/> is the code.</summary>
    NotificationCopyCode,

    /// <summary>Mark every currently listed notification as read (opening the notification page).</summary>
    NotificationMarkAllRead,

    /// <summary>Clear the island's local notification list (Action Center is not modified).</summary>
    NotificationClearAll,

    /// <summary>Copy a clipboard row; <see cref="IslandPlatformAction.Text"/> is the row text.</summary>
    ClipboardCopy,

    /// <summary>Pin/unpin a clipboard row; <see cref="IslandPlatformAction.Index"/> is the row.</summary>
    ClipboardPin,

    /// <summary>Clear the island's clipboard history (the system clipboard is untouched).</summary>
    ClipboardClearAll,

    /// <summary>Remove one clipboard row; <see cref="IslandPlatformAction.Index"/> is the row.</summary>
    ClipboardRemove,

    /// <summary>Cycle the clipboard type filter (all → text → image → files).</summary>
    ClipboardCycleFilter,

    /// <summary>Open the clipboard text-search window.</summary>
    ClipboardSearch,

    /// <summary>Activate an agent session row; <see cref="IslandPlatformAction.Index"/> is the row.</summary>
    AgentActivate,

    /// <summary>Toggle a widget task; <see cref="IslandPlatformAction.Index"/> is the task row.</summary>
    TaskToggle,

    /// <summary>Navigate the widgets calendar; <see cref="IslandPlatformAction.Value"/> is -1 or +1.</summary>
    CalendarNavigate,
}

/// <summary>Home quick-toggle identity (order matches the renderer's four toggle slots).</summary>
public enum IslandQuickToggle
{
    /// <summary>Wi-Fi / network toggle (slot 0).</summary>
    Wifi = 0,

    /// <summary>Bluetooth toggle (slot 1).</summary>
    Bluetooth = 1,

    /// <summary>App/island theme toggle (slot 2, sun/moon).</summary>
    Theme = 2,
}

/// <summary>
/// One semantic page-control action.
/// </summary>
/// <param name="Kind">Action identity.</param>
/// <param name="Index">Row index, quick-toggle slot or -1 when not row-specific.</param>
/// <param name="Value">0..1 slider fraction, or -1/+1 for calendar navigation.</param>
/// <param name="Toggle">Quick-toggle identity (only for <see cref="IslandPlatformActionKind.QuickToggle"/>).</param>
/// <param name="Text">Clipboard row text for <see cref="IslandPlatformActionKind.ClipboardCopy"/>.</param>
public readonly record struct IslandPlatformAction(
    IslandPlatformActionKind Kind,
    int Index = -1,
    double Value = 0,
    IslandQuickToggle Toggle = IslandQuickToggle.Wifi,
    string? Text = null)
{
    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        IslandPlatformActionKind.QuickToggle => $"{Kind}({Toggle})",
        IslandPlatformActionKind.CalendarNavigate => $"{Kind}({Value:+0;-0})",
        IslandPlatformActionKind.VolumeSet or IslandPlatformActionKind.BrightnessSet
            or IslandPlatformActionKind.MediaSeek => $"{Kind}({Value:0.###})",
        IslandPlatformActionKind.ClipboardCopy => $"{Kind}(row {Index})",
        _ => Index >= 0 ? $"{Kind}(row {Index})" : Kind.ToString(),
    };
}

/// <summary>How a platform seam handled one action.</summary>
public enum IslandActionOutcome
{
    /// <summary>The action was dispatched to a real service or applied to settings.</summary>
    Handled,

    /// <summary>The action was raised as an App event for a later owner; nothing was executed.</summary>
    Deferred,

    /// <summary>No service exists for this action; the controller logs <c>control-unsupported</c>.</summary>
    Unsupported,
}

/// <summary>Result of <see cref="IIslandPlatformActions.Invoke"/>.</summary>
/// <param name="Outcome">Handled, deferred or unsupported.</param>
/// <param name="Reason">Exact missing service/reason for deferred/unsupported actions.</param>
public readonly record struct IslandActionResult(IslandActionOutcome Outcome, string? Reason = null)
{
    /// <summary>A handled action.</summary>
    public static IslandActionResult Handled { get; } = new(IslandActionOutcome.Handled);

    /// <summary>An action emitted as an App event for later.</summary>
    public static IslandActionResult Deferred(string reason) => new(IslandActionOutcome.Deferred, reason);

    /// <summary>An action with no backing service (logged as <c>control-unsupported</c>).</summary>
    public static IslandActionResult Unsupported(string reason) => new(IslandActionOutcome.Unsupported, reason);
}

/// <summary>
/// Narrow App-owned seam between the Skia island controller and the platform/App services. The
/// controller only maps hit targets to <see cref="IslandPlatformAction"/> values and calls
/// <see cref="Invoke"/>; implementations must never block the UI thread (dispatch async work and
/// return immediately). A null seam falls back to the legacy <c>MediaCommandRequested</c> event for
/// media transport and reports every other control as unsupported.
/// </summary>
public interface IIslandPlatformActions
{
    /// <summary>Dispatches one semantic action without blocking the caller.</summary>
    /// <param name="action">Action to dispatch.</param>
    /// <returns>Whether a service handled it, it was deferred, or no service exists.</returns>
    IslandActionResult Invoke(in IslandPlatformAction action);

}
