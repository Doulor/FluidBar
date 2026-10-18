namespace FluidBar.Core.Activities;

/// <summary>
/// Pure hover-intent / peek guards (spec 5.4 <c>PeekPolicy</c>). Used by
/// <see cref="IslandStateMachine"/>; exposed so hosts can drive their hover timers from one
/// source of truth and unit-test the policy without the reducer.
/// </summary>
public static class PeekPolicy
{
    /// <summary>Whether the current compact state may arm/enter Peek (T2 guard).</summary>
    public static bool CanEnterPeek(in IslandState state, IslandBehaviorOptions options)
        => state.Kind == IslandStateKind.Compact
           && options.Enabled
           && options.HoverPeek
           && !state.IsPinned
           && !state.IsDragging
           && !state.IsSuppressed
           && state.IsHovering;

    /// <summary>Deadline for the 90 ms hover-intent timer.</summary>
    public static DateTimeOffset HoverIntentDeadline(DateTimeOffset now, IslandBehaviorOptions options)
        => now.AddMilliseconds(Math.Max(0, options.HoverIntentMs));

    /// <summary>Whether the hover-intent timer elapsed and Peek should be entered.</summary>
    public static bool HoverIntentElapsed(in IslandState state, IslandBehaviorOptions options, DateTimeOffset now)
        => CanEnterPeek(state, options)
           && state.HoverIntentDeadline is { } deadline
           && now >= deadline;

    /// <summary>Deadline for the 120 ms leave-debounce timer.</summary>
    public static DateTimeOffset LeaveDeadline(DateTimeOffset now, IslandBehaviorOptions options)
        => now.AddMilliseconds(Math.Max(0, options.LeaveDebounceMs));

    /// <summary>Whether Peek should leave back to Compact (leave debounce elapsed, pointer gone).</summary>
    public static bool ShouldExitPeek(in IslandState state, DateTimeOffset now)
        => state.Kind == IslandStateKind.Peek
           && !state.IsHovering
           && (state.LeaveDeadline is null || now >= state.LeaveDeadline);

    /// <summary>Whether Peek should auto-retract after the 2400 ms dwell.</summary>
    public static bool ShouldAutoRetractPeek(in IslandState state, DateTimeOffset now)
        => state.Kind == IslandStateKind.Peek
           && !state.IsHovering
           && state.PeekDwellDeadline is { } deadline
           && now >= deadline;
}

/// <summary>
/// Pure collapse/hide guards (spec 5.4 <c>CollapsePolicy</c>): auto-collapse, auto-hide,
/// activity reveal and click-outside suppression.
/// </summary>
public static class CollapsePolicy
{
    /// <summary>Whether the expanded auto-collapse deadline elapsed and is allowed (T5).</summary>
    public static bool ShouldAutoCollapse(in IslandState state, DateTimeOffset now)
        => state.Kind == IslandStateKind.Expanded
           && !state.IsPinned
           && state.AutoCollapseDeadline is { } deadline
           && now >= deadline;

    /// <summary>Whether the compact auto-hide deadline is active (T8).</summary>
    public static bool ShouldAutoHide(in IslandState state, IslandBehaviorOptions options)
        => state.Kind == IslandStateKind.Compact
           && options.AutoHideMs > 0
           && !state.IsPinned
           && !state.IsHovering
           && !state.IsDragging
           && state.AutoHideDeadline is not null;

    /// <summary>Whether an arriving activity may reveal a hidden island (T1 guard).</summary>
    public static bool ShouldRevealOnActivity(in IslandState state, IslandBehaviorOptions options, ActivityKind kind)
        => options.Enabled
           && !state.IsSuppressed
           && ActivityPriorityPolicy.CanRevealWhenHidden(kind);

    /// <summary>Whether an outside click must be ignored because the activity is pinned.</summary>
    public static bool BlocksOutsideClickClose(in IslandState state) => state.IsPinned;

    /// <summary>Whether auto-collapse must be skipped/dropped because the activity is pinned.</summary>
    public static bool IsAutoCollapseBlockedByPin(in IslandState state) => state.IsPinned;
}
