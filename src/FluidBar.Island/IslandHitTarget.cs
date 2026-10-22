namespace FluidBar.Island;

/// <summary>Interactive region identities produced by <see cref="IslandRenderer.HitTest"/>.</summary>
public enum IslandHitKind
{
    /// <summary>No interactive region (click-through).</summary>
    None,

    /// <summary>Expanded shell background (not a page control).</summary>
    Shell,

    /// <summary>Compact pill (the whole pill expands; <see cref="IslandHitTarget.Control"/> refines it).</summary>
    Compact,

    /// <summary>Secondary notification pill below the primary compact pill (expands the notifications page).</summary>
    SecondaryCompact,

    /// <summary>Compact "+N" overflow chip (opens the page of the oldest background activity).</summary>
    OverflowBadge,

    /// <summary>Split-island preview ball; <see cref="IslandHitTarget.Index"/> is the preview index.</summary>
    SplitIslet,

    /// <summary>Expanded page content (not a control).</summary>
    Page,

    /// <summary>An expanded page control (transport, seek, quick toggle, media card…).</summary>
    PageControl,

    /// <summary>Page title overlay (informational; not interactive).</summary>
    Title,

    /// <summary>Collapse chevron.</summary>
    Collapse,

    /// <summary>Settings gear in the expanded header (opens the settings window).</summary>
    Settings,

    /// <summary>A detached mini-island slot (prev/current/next); <see cref="IslandHitTarget.PageIndex"/> is the page.</summary>
    RailSlot,

    /// <summary>A far-page dot on the left of the strip.</summary>
    DotLeft,

    /// <summary>A far-page dot on the right of the strip.</summary>
    DotRight,

    /// <summary>A rail overflow ellipsis; <see cref="IslandHitTarget.Index"/> 0 = left, 1 = right.</summary>
    RailOverflow,
}

/// <summary>Expanded/compact control identities.</summary>
public enum IslandPageControl
{
    /// <summary>No control.</summary>
    None,

    /// <summary>Play/pause.</summary>
    PlayPause,

    /// <summary>Previous track.</summary>
    Previous,

    /// <summary>Next track.</summary>
    Next,

    /// <summary>Seek slider.</summary>
    Seek,

    /// <summary>Quick toggle (home); <see cref="IslandHitTarget.Index"/> is the toggle index.</summary>
    QuickToggle,

    /// <summary>Home now-playing card (opens the media page).</summary>
    MediaCard,

    /// <summary>Volume slider (home).</summary>
    VolumeSlider,

    /// <summary>Mute toggle for the system output (media footer volume button).</summary>
    VolumeMute,

    /// <summary>Brightness slider (home).</summary>
    BrightnessSlider,

    /// <summary>Audio output button.</summary>
    Output,

    /// <summary>Play-queue button.</summary>
    Queue,

    /// <summary>A list row (notification/clipboard/agent); <see cref="IslandHitTarget.Index"/> is the row.</summary>
    Row,

    /// <summary>Dismiss a notification row.</summary>
    Dismiss,

    /// <summary>Clear the local notification list (header chip).</summary>
    NotificationClear,

    /// <summary>Cycle the clipboard type filter (header chip).</summary>
    ClipboardFilter,

    /// <summary>Open the clipboard search window (header chip).</summary>
    ClipboardSearch,

    /// <summary>Pin/unpin a clipboard row.</summary>
    Pin,

    /// <summary>Delete a clipboard row.</summary>
    ClipboardDelete,

    /// <summary>Copy a clipboard row.</summary>
    Copy,

    /// <summary>Toggle a widget task; <see cref="IslandHitTarget.Index"/> is the task index.</summary>
    TaskToggle,

    /// <summary>Calendar previous-month button.</summary>
    CalendarPrevious,

    /// <summary>Calendar next-month button.</summary>
    CalendarNext,

    /// <summary>Scrollable list body (wheel/touch scroll region).</summary>
    Scroll,

    /// <summary>Plugin list row; <see cref="IslandHitTarget.Index"/> is the row.</summary>
    PluginRow,

    /// <summary>Plugin list row's trailing button; <see cref="IslandHitTarget.Index"/> is the row.</summary>
    PluginRowButton,
}

/// <summary>One hit-test result: region kind, page, control and row/toggle index.</summary>
public readonly record struct IslandHitTarget(
    IslandHitKind Kind,
    int PageIndex = -1,
    IslandPageControl Control = IslandPageControl.None,
    int Index = -1)
{
    /// <summary>Whether the point hit an actionable region (the title overlay is informational).</summary>
    public bool IsInteractive => Kind is not (IslandHitKind.None or IslandHitKind.Title);

    /// <summary>Whether the hit is inside the shell (shell/page/compact/collapse/settings/title).</summary>
    public bool IsShell => Kind is IslandHitKind.Shell or IslandHitKind.Compact or IslandHitKind.Page
        or IslandHitKind.PageControl or IslandHitKind.Title or IslandHitKind.Collapse
        or IslandHitKind.Settings or IslandHitKind.OverflowBadge;

    /// <summary>Whether the hit is on the detached strip (mini-island or dot).</summary>
    public bool IsDetached => Kind is IslandHitKind.RailSlot or IslandHitKind.DotLeft or IslandHitKind.DotRight or IslandHitKind.RailOverflow;

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        IslandHitKind.PageControl => $"PageControl({Control}, page {PageIndex}, index {Index})",
        IslandHitKind.RailSlot or IslandHitKind.DotLeft or IslandHitKind.DotRight => $"{Kind}(page {PageIndex})",
        IslandHitKind.None => "None",
        _ => Kind.ToString(),
    };
}
