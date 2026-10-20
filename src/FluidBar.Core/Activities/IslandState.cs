using System.Collections.Immutable;
using FluidBar.Core.Events;

namespace FluidBar.Core.Activities;

/// <summary>The five island states (spec 2.1).</summary>
public enum IslandStateKind
{
    /// <summary>Invisible / click-through; producers may still queue activities (T9).</summary>
    Hidden,

    /// <summary>Compact pill (idle or one active surface).</summary>
    Compact,

    /// <summary>Hover preview: compact pill grown by +10 w / +6 h (T2/T3).</summary>
    Peek,

    /// <summary>Expanded 440x284 control centre (T4).</summary>
    Expanded,

    /// <summary>Collapse animation in flight; only retarget/snap inputs are accepted (T5/T12).</summary>
    Closing,
}

/// <summary>Why a transition happened; every effect list carries an explicit reason.</summary>
public enum IslandTransitionReason
{
    /// <summary>No transition (input ignored/guarded).</summary>
    NoChange,

    /// <summary>Initial state construction.</summary>
    Startup,

    /// <summary>Activity arrived and revealed the island (T1).</summary>
    ActivityArrived,

    /// <summary>Activity arrived but the island stays hidden (suppressed/disabled, T9).</summary>
    ActivityQueued,

    /// <summary>Activity content updated while compact/peeked.</summary>
    ActivityUpdated,

    /// <summary>Content update while expanded (T11; shell does not move).</summary>
    ContentUpdated,

    /// <summary>All activities ended while expanded; collapse (T5).</summary>
    ActivityEnded,

    /// <summary>Pointer entered the pill; hover intent armed (T2).</summary>
    HoverIntentArmed,

    /// <summary>Hover intent elapsed; entered Peek (T2).</summary>
    HoverPeek,

    /// <summary>Pointer left; leave debounce armed (T3).</summary>
    LeaveArmed,

    /// <summary>Leave debounce elapsed; Peek exited (T3).</summary>
    PeekExit,

    /// <summary>Peek auto-retracted after the dwell time.</summary>
    PeekDwellElapsed,

    /// <summary>Primary click expanded the island (T4).</summary>
    PrimaryClick,

    /// <summary>Toggle hotkey expanded the island.</summary>
    HotkeyExpand,

    /// <summary>Toggle hotkey started collapse (T5).</summary>
    HotkeyCollapse,

    /// <summary>Escape started collapse (T5).</summary>
    Escape,

    /// <summary>Outside click started collapse (T5; ignored while pinned).</summary>
    OutsideClick,

    /// <summary>Auto-collapse timer fired (T5).</summary>
    AutoCollapse,

    /// <summary>Auto-hide timer fired (T8).</summary>
    AutoHide,

    /// <summary>Hide hotkey (T8/T5).</summary>
    HideRequested,

    /// <summary>Show hotkey revealed the compact pill (T8 reverse).</summary>
    ShowRequested,

    /// <summary>Hover revealed a hidden island through the 10 DIP strip.</summary>
    HoverReveal,

    /// <summary>Collapse animation settled (T6/T7).</summary>
    CloseSettled,

    /// <summary>Animation settled back to compact.</summary>
    Settled,

    /// <summary>Suppression (fullscreen/lock) hid the island.</summary>
    SuppressionStarted,

    /// <summary>Suppression ended; the island restored.</summary>
    SuppressionEnded,

    /// <summary>Monitor/DPI/theme/mode change; springs snap without a transition (T10).</summary>
    EnvironmentChanged,

    /// <summary>Settings changed (may hide the island when disabled).</summary>
    SettingsChanged,

    /// <summary>Rail/page selection changed while expanded.</summary>
    PageRequested,

    /// <summary>Session pin changed.</summary>
    PinnedChanged,

    /// <summary>Pinned activity blocked an automatic collapse/preemption.</summary>
    PinnedBlocked,

    /// <summary>Drag gesture started.</summary>
    DragStarted,

    /// <summary>Drag gesture ended; placement is persisted.</summary>
    DragEnded,

    /// <summary>Interrupt while closing: click expanded again (T12).</summary>
    InterruptExpand,

    /// <summary>Input deferred because the island is closing (spec 2.1 invariant).</summary>
    InputDeferred,
}

/// <summary>
/// Effects are data, not delegates (spec 5.4). The UI host executes them on the dispatcher,
/// in order. This keeps the reducer fully unit-testable without a UI.
/// </summary>
public enum IslandEffectKind
{
    /// <summary>Recompute the shell/content rectangles for the current state.</summary>
    Relayout,

    /// <summary>Show the host window (no activation).</summary>
    ShowWindow,

    /// <summary>Hide the host window (leaves click-through empty set).</summary>
    HideWindow,

    /// <summary>Start requesting frames for spring motion.</summary>
    StartFrameLoop,

    /// <summary>Stop requesting frames; the island is fully at rest.</summary>
    StopFrameLoop,

    /// <summary>Snap all springs to their targets and zero velocities (T10).</summary>
    SnapSprings,

    /// <summary>Retarget a named spring; <see cref="IslandEffect.Data"/> selects the token.</summary>
    Retarget,

    /// <summary>Play the one-shot vertical nudge (−6 DIP).</summary>
    PlayNudge,

    /// <summary>Update content slots without recreating the page root (200 ms cross-fade).</summary>
    UpdateContent,

    /// <summary>Refresh the tray tooltip/menu.</summary>
    UpdateTray,

    /// <summary>Re-assert click-through/animation-region flags for the new state.</summary>
    SyncClickThrough,

    /// <summary>Move keyboard focus to the active rail item.</summary>
    FocusRail,

    /// <summary>Persist the per-monitor placement.</summary>
    PersistPlacement,

    /// <summary>Suspend event producers (lock/session switch).</summary>
    PauseProducers,

    /// <summary>Resume event producers.</summary>
    ResumeProducers,
}

/// <summary>Named spring targets carried by <see cref="IslandEffectKind.Retarget"/> effects.</summary>
public static class IslandEffectData
{
    /// <summary>Width/scale expand spring (<c>motion.expand</c>).</summary>
    public const string Expand = "expand";

    /// <summary>Height expand spring (<c>motion.expand.height</c>).</summary>
    public const string ExpandHeight = "expand.height";

    /// <summary>Collapse spring (<c>motion.collapse</c>).</summary>
    public const string Collapse = "collapse";

    /// <summary>Hover/peek spring (<c>motion.hover</c>).</summary>
    public const string Hover = "hover";

    /// <summary>Page/tab spring (<c>motion.tab</c>).</summary>
    public const string Tab = "tab";

    /// <summary>Content morph spring (<c>motion.morph</c>).</summary>
    public const string Morph = "morph";

    /// <summary>Hide/fade spring (<c>motion.hide</c>).</summary>
    public const string Hide = "hide";

    /// <summary>Press scale spring (<c>motion.press</c>).</summary>
    public const string Press = "press";

    /// <summary>Nudge spring (<c>motion.nudge</c>).</summary>
    public const string Nudge = "nudge";
}

/// <summary>One data-only effect emitted by a transition.</summary>
/// <param name="Kind">Effect kind.</param>
/// <param name="Data">Optional payload (spring token for retargets, page token, ...).</param>
public readonly record struct IslandEffect(IslandEffectKind Kind, string? Data = null)
{
    /// <summary>Creates a retarget effect for a named spring.</summary>
    public static IslandEffect Retarget(string target) => new(IslandEffectKind.Retarget, target);

    /// <inheritdoc />
    public override string ToString() => Data is null ? Kind.ToString() : $"{Kind}({Data})";
}

/// <summary>One input accepted by <see cref="IslandStateMachine"/>; producers never mutate state directly.</summary>
public enum IslandInputKind
{
    /// <summary>Aggregated activity-scheduler update (new/replace/expire/clear).</summary>
    ActivityUpdate,

    /// <summary>Pointer entered the interactive region.</summary>
    PointerEntered,

    /// <summary>Pointer left the interactive region.</summary>
    PointerLeft,

    /// <summary>Host timer: hover-intent delay elapsed.</summary>
    HoverIntentElapsed,

    /// <summary>Host timer: leave debounce elapsed.</summary>
    LeaveDebounceElapsed,

    /// <summary>Host timer: peek dwell elapsed.</summary>
    PeekDwellElapsed,

    /// <summary>Primary click on the pill.</summary>
    PrimaryClick,

    /// <summary>Click outside the island (focus loss).</summary>
    OutsideClick,

    /// <summary>Escape pressed.</summary>
    Escape,

    /// <summary>Toggle hotkey (Ctrl+Alt+D).</summary>
    ToggleHotkey,

    /// <summary>Hide hotkey (Ctrl+Alt+H).</summary>
    HideHotkey,

    /// <summary>Show hotkey.</summary>
    ShowHotkey,

    /// <summary>Host timer: auto-collapse deadline elapsed.</summary>
    AutoCollapseElapsed,

    /// <summary>Host timer: auto-hide deadline elapsed.</summary>
    AutoHideElapsed,

    /// <summary>Collapse animation settled.</summary>
    CloseSettled,

    /// <summary>Fullscreen/lock/focus suppression changed.</summary>
    SuppressionChanged,

    /// <summary>Monitor/DPI/theme change.</summary>
    DisplayChanged,

    /// <summary>Island mode changed (float/flush/dock).</summary>
    ModeChanged,

    /// <summary>Settings snapshot changed.</summary>
    SettingsChanged,

    /// <summary>Rail page selection changed.</summary>
    PageRequested,

    /// <summary>Session pin changed.</summary>
    PinnedChanged,

    /// <summary>Drag gesture started.</summary>
    DragStarted,

    /// <summary>Drag gesture ended.</summary>
    DragEnded,
}

/// <summary>Value-type input envelope; static factories keep call sites readable.</summary>
public readonly record struct IslandInput
{
    /// <summary>Input class.</summary>
    public IslandInputKind Kind { get; init; }

    /// <summary>Primary activity key, when applicable.</summary>
    public ActivityKey? Key { get; init; }

    /// <summary>Primary activity kind, when applicable.</summary>
    public ActivityKind? ActivityKind { get; init; }

    /// <summary>Requested rail page, when applicable.</summary>
    public PageRef? Page { get; init; }

    /// <summary>Scheduler outcome class (for activity updates).</summary>
    public ActivityUpdateKind UpdateKind { get; init; }

    /// <summary>Whether content changed (activity updates).</summary>
    public bool ContentChanged { get; init; }

    /// <summary>Whether the primary activity changed (activity updates).</summary>
    public bool PrimaryChanged { get; init; }

    /// <summary>Live activity count (activity updates).</summary>
    public int ActivityCount { get; init; }

    /// <summary>Hidden activity count / <c>+N</c> badge (activity updates).</summary>
    public int BadgeCount { get; init; }

    /// <summary>Whether the primary activity is pinned.</summary>
    public bool IsPinned { get; init; }

    /// <summary>Suppression flag (suppression input).</summary>
    public bool Suppressed { get; init; }

    /// <summary>Builds an activity-update input from a scheduler update.</summary>
    public static IslandInput ForActivityUpdate(ActivitySchedulerUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return new IslandInput
        {
            Kind = IslandInputKind.ActivityUpdate,
            Key = update.Key,
            ActivityKind = update.State.PrimaryKind,
            Page = update.State.HasActivities ? update.State.PrimaryPage : null,
            UpdateKind = update.Kind,
            ContentChanged = update.ContentChanged,
            PrimaryChanged = update.PrimaryChanged,
            ActivityCount = update.State.ActivityCount,
            BadgeCount = update.State.BadgeCount,
            IsPinned = update.State.Primary?.IsPinned ?? false,
        };
    }

    /// <summary>Pointer entered the interactive region.</summary>
    public static IslandInput PointerEntered() => new() { Kind = IslandInputKind.PointerEntered };

    /// <summary>Pointer left the interactive region.</summary>
    public static IslandInput PointerLeft() => new() { Kind = IslandInputKind.PointerLeft };

    /// <summary>Hover-intent timer elapsed.</summary>
    public static IslandInput HoverIntentElapsed() => new() { Kind = IslandInputKind.HoverIntentElapsed };

    /// <summary>Leave-debounce timer elapsed.</summary>
    public static IslandInput LeaveDebounceElapsed() => new() { Kind = IslandInputKind.LeaveDebounceElapsed };

    /// <summary>Peek-dwell timer elapsed.</summary>
    public static IslandInput PeekDwellElapsed() => new() { Kind = IslandInputKind.PeekDwellElapsed };

    /// <summary>Primary click.</summary>
    public static IslandInput PrimaryClick() => new() { Kind = IslandInputKind.PrimaryClick };

    /// <summary>Outside click / focus loss.</summary>
    public static IslandInput OutsideClick() => new() { Kind = IslandInputKind.OutsideClick };

    /// <summary>Escape pressed.</summary>
    public static IslandInput Escape() => new() { Kind = IslandInputKind.Escape };

    /// <summary>Toggle hotkey.</summary>
    public static IslandInput ToggleHotkey() => new() { Kind = IslandInputKind.ToggleHotkey };

    /// <summary>Hide hotkey.</summary>
    public static IslandInput HideHotkey() => new() { Kind = IslandInputKind.HideHotkey };

    /// <summary>Show hotkey.</summary>
    public static IslandInput ShowHotkey() => new() { Kind = IslandInputKind.ShowHotkey };

    /// <summary>Auto-collapse timer elapsed.</summary>
    public static IslandInput AutoCollapseElapsed() => new() { Kind = IslandInputKind.AutoCollapseElapsed };

    /// <summary>Auto-hide timer elapsed.</summary>
    public static IslandInput AutoHideElapsed() => new() { Kind = IslandInputKind.AutoHideElapsed };

    /// <summary>Collapse settled.</summary>
    public static IslandInput CloseSettled() => new() { Kind = IslandInputKind.CloseSettled };

    /// <summary>Suppression changed (fullscreen/lock).</summary>
    public static IslandInput SuppressionChanged(bool suppressed) => new() { Kind = IslandInputKind.SuppressionChanged, Suppressed = suppressed };

    /// <summary>Display/DPI/theme change.</summary>
    public static IslandInput DisplayChanged() => new() { Kind = IslandInputKind.DisplayChanged };

    /// <summary>Island mode change.</summary>
    public static IslandInput ModeChanged() => new() { Kind = IslandInputKind.ModeChanged };

    /// <summary>Settings changed.</summary>
    public static IslandInput SettingsChanged() => new() { Kind = IslandInputKind.SettingsChanged };

    /// <summary>Rail page request.</summary>
    public static IslandInput PageRequested(PageRef page) => new() { Kind = IslandInputKind.PageRequested, Page = page };

    /// <summary>Pin changed.</summary>
    public static IslandInput PinnedChanged(bool pinned) => new() { Kind = IslandInputKind.PinnedChanged, IsPinned = pinned };

    /// <summary>Drag started.</summary>
    public static IslandInput DragStarted() => new() { Kind = IslandInputKind.DragStarted };

    /// <summary>Drag ended.</summary>
    public static IslandInput DragEnded() => new() { Kind = IslandInputKind.DragEnded };
}

/// <summary>Behaviour switches the reducer needs; mapped from settings by the host.</summary>
public sealed record IslandBehaviorOptions
{
    /// <summary>Shared default options.</summary>
    public static IslandBehaviorOptions Default { get; } = new();

    /// <summary>Global island enable (T1 guard).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Hover peek enabled (T2 guard).</summary>
    public bool HoverPeek { get; init; } = true;

    /// <summary>Hovering the hidden 10 DIP strip reveals the compact pill.</summary>
    public bool HoverReveal { get; init; } = true;

    /// <summary>Auto-collapse delay in ms; 0 disables auto-collapse.</summary>
    public int AutoCollapseMs { get; init; } = 2600;

    /// <summary>Auto-hide delay in ms; 0 keeps the compact pill visible (T8).</summary>
    public int AutoHideMs { get; init; }

    /// <summary>Hover-intent delay in ms (spec: 90).</summary>
    public int HoverIntentMs { get; init; } = 90;

    /// <summary>Leave debounce in ms (spec: 120).</summary>
    public int LeaveDebounceMs { get; init; } = 120;

    /// <summary>Peek auto-retract dwell in ms (spec: 2400).</summary>
    public int PeekDwellMs { get; init; } = 2400;

    /// <summary>Reduced-motion preference (affects motion policy only).</summary>
    public bool ReducedMotion { get; init; }

    /// <summary>When false, primary clicks do not expand (settings "click to expand" off).</summary>
    public bool ExpandOnClick { get; init; } = true;

    /// <summary>Maximum deferred inputs while closing; the oldest are dropped beyond this bound.</summary>
    public int MaxDeferredInputs { get; init; } = 8;
}

/// <summary>Immutable island state. Every field is explicit (no implicit bool combinations).</summary>
public sealed record IslandState
{
    /// <summary>The initially hidden, idle island.</summary>
    public static IslandState Hidden { get; } = new();

    /// <summary>Current state kind.</summary>
    public IslandStateKind Kind { get; init; } = IslandStateKind.Hidden;

    /// <summary>Selected rail page (persists across collapse/expand).</summary>
    public PageRef Page { get; init; } = PageRef.Home;

    /// <summary>Primary activity identity, <see langword="null"/> for the idle surface.</summary>
    public ActivityKey? PrimaryKey { get; init; }

    /// <summary>Primary activity kind, <see langword="null"/> for the idle surface.</summary>
    public ActivityKind? ContentKind { get; init; }

    /// <summary>Whether the primary activity is pinned.</summary>
    public bool IsPinned { get; init; }

    /// <summary>Live activity count (visible + hidden); 0 means idle.</summary>
    public int ActivityCount { get; init; }

    /// <summary>Hidden activity count, shown as the <c>+N</c> badge.</summary>
    public int BadgeCount { get; init; }

    /// <summary>Whether fullscreen/lock suppression is active.</summary>
    public bool IsSuppressed { get; init; }

    /// <summary>Whether a drag gesture is in flight.</summary>
    public bool IsDragging { get; init; }

    /// <summary>Whether the pointer is inside the interactive region.</summary>
    public bool IsHovering { get; init; }

    /// <summary>Where a <see cref="IslandStateKind.Closing"/> animation settles.</summary>
    public IslandStateKind ClosingReturnKind { get; init; } = IslandStateKind.Compact;

    /// <summary>Inputs received while closing; replayed after the collapse settles (bounded).</summary>
    public ImmutableArray<IslandInput> DeferredInputs { get; init; } = [];

    /// <summary>Deadline for the hover-intent timer, when armed.</summary>
    public DateTimeOffset? HoverIntentDeadline { get; init; }

    /// <summary>Deadline for the leave-debounce timer, when armed.</summary>
    public DateTimeOffset? LeaveDeadline { get; init; }

    /// <summary>Deadline for the peek auto-retract timer.</summary>
    public DateTimeOffset? PeekDwellDeadline { get; init; }

    /// <summary>Deadline for the expanded auto-collapse timer.</summary>
    public DateTimeOffset? AutoCollapseDeadline { get; init; }

    /// <summary>Deadline for the compact auto-hide timer.</summary>
    public DateTimeOffset? AutoHideDeadline { get; init; }

    /// <summary>Whether any activity is live.</summary>
    public bool HasActivities => ActivityCount > 0;

    /// <summary>Short deterministic fingerprint used by tests and logs.</summary>
    public override string ToString()
        => $"{Kind}(page={Page}, kind={ContentKind?.ToString() ?? "idle"}, n={ActivityCount}, badge={BadgeCount}"
           + $"{(IsPinned ? ", pinned" : string.Empty)}{(IsSuppressed ? ", suppressed" : string.Empty)})";
}

/// <summary>Result of one reducer step: new state, explicit reason and data-only effects.</summary>
public sealed record IslandTransition
{
    /// <summary>State before the input was applied.</summary>
    public required IslandState Previous { get; init; }

    /// <summary>State after the input was applied.</summary>
    public required IslandState State { get; init; }

    /// <summary>Explicit reason for the transition.</summary>
    public IslandTransitionReason Reason { get; init; } = IslandTransitionReason.NoChange;

    /// <summary>Ordered effects for the host to execute on the dispatcher.</summary>
    public ImmutableArray<IslandEffect> Effects { get; init; } = [];

    /// <summary>Whether the state kind changed.</summary>
    public bool KindChanged => Previous.Kind != State.Kind;

    /// <summary>Whether any input was deferred.</summary>
    public bool Deferred => Reason == IslandTransitionReason.InputDeferred;

    /// <summary>Creates a no-change transition.</summary>
    public static IslandTransition None(IslandState state, IslandTransitionReason reason = IslandTransitionReason.NoChange)
        => new() { Previous = state, State = state, Reason = reason };

    /// <summary>Whether the effect list contains <paramref name="kind"/>.</summary>
    public bool HasEffect(IslandEffectKind kind) => Effects.Any(e => e.Kind == kind);
}

