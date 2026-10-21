using System.Collections.Immutable;

namespace FluidBar.Core.Activities;

/// <summary>
/// Split-island ("cell division") primary-selection policy. Pure and clock-injected: the host feeds
/// the current visible records and a timestamp, and gets the presentation order back.
/// <para>
/// User decision (2026-10-20): a split preview must not steal the main pill merely because it has a
/// higher scheduler priority. The rules are:
/// </para>
/// <list type="number">
/// <item>A just-interacted activity is primary for <see cref="UserLock"/> (opened page / clicked its controls).</item>
/// <item>Actionable short-lived events (notification, just-copied clipboard, an agent waiting for input)
/// take the main pill when their turn comes.</item>
/// <item>Persistent content (media, running agent, timer, progress, focus, low battery) becomes a ball.</item>
/// <item>On a tie the activity that appeared first keeps the main slot (stability over novelty).</item>
/// </list>
/// <para>
/// Hysteresis: the primary only changes when the activity ends, when an actionable short event arrives
/// after the <see cref="MinDwell"/> residency window, when the user interacts, or when the wheel
/// explicitly rotates the stack. A short event retains the pill for its own TTL; the previous
/// persistent primary is restored afterwards.
/// </para>
/// </summary>
public sealed class SplitStackPolicy
{
    /// <summary>How long an explicit user interaction keeps an activity in the main pill.</summary>
    public static readonly TimeSpan UserLock = TimeSpan.FromSeconds(10);

    /// <summary>Minimum time the main pill stays put before an arriving short event may preempt it.</summary>
    public static readonly TimeSpan MinDwell = TimeSpan.FromSeconds(2);

    private ActivityKey? _primaryKey;
    private DateTimeOffset _primarySince;
    private ActivityKey? _restoreKey;
    private ActivityKey? _userKey;
    private DateTimeOffset _userUntil;
    private ActivityKey? _pinnedKey;
    private ActivityKey? _pinOverrideKey;

    /// <summary>Current primary key (diagnostics/tests).</summary>
    public ActivityKey? PrimaryKey => _primaryKey;

    /// <summary>Records an explicit user interaction (opened the activity's page / clicked its controls).</summary>
    public void NoteUserInteraction(ActivityKey key, DateTimeOffset now)
    {
        _userKey = key;
        _userUntil = now + UserLock;
    }

    /// <summary>Whether <paramref name="key"/> is currently user-locked into the main pill.</summary>
    public bool IsUserLocked(ActivityKey key, DateTimeOffset now)
        => _userKey == key && now < _userUntil;

    /// <summary>The wheel-pinned primary; survives the short user-lock window until the activity ends.</summary>
    public ActivityKey? PinnedKey => _pinnedKey;

    /// <summary>Whether an activity is an actionable short-lived event (notification/copy/waiting agent).</summary>
    public static bool IsActionable(ActivityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Kind is ActivityKind.Notification or ActivityKind.Clipboard
            || (record.Kind == ActivityKind.Agent && IsAgentWaitingInput(record.Payload));
    }

    /// <summary>
    /// Whether an agent activity is waiting for user input. The agent plugin reports the state through
    /// the payload badge; silent/running agents are persistent content and become preview balls.
    /// </summary>
    public static bool IsAgentWaitingInput(ActivityPayload? payload)
    {
        if (payload?.Badge is not { Length: > 0 } badge)
        {
            return false;
        }

        return badge.Equals("waiting", StringComparison.OrdinalIgnoreCase)
            || badge.Equals("waiting-input", StringComparison.OrdinalIgnoreCase)
            || badge.Equals("needs-input", StringComparison.OrdinalIgnoreCase)
            || badge.Equals("input", StringComparison.OrdinalIgnoreCase)
            || badge.Equals("需要输入", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a record is persistent content (preview ball material) rather than actionable.</summary>
    public static bool IsPersistentContent(ActivityRecord record)
        => record.Kind is ActivityKind.Media or ActivityKind.Timer or ActivityKind.BatteryLow
            or ActivityKind.Progress or ActivityKind.Focus
            || (record.Kind == ActivityKind.Agent && !IsAgentWaitingInput(record.Payload));

    private enum Layer
    {
        User = 0,
        Actionable = 1,
        Persistent = 2,
        Idle = 3,
    }

    private Layer Classify(ActivityRecord record, DateTimeOffset now)
    {
        if (IsUserLocked(record.Key, now) || _pinnedKey == record.Key)
        {
            return Layer.User;
        }

        if (IsActionable(record))
        {
            return Layer.Actionable;
        }

        return IsPersistentContent(record) ? Layer.Persistent : Layer.Idle;
    }

    /// <summary>
    /// Orders the visible stack for split presentation (index 0 = main pill, the rest = preview balls).
    /// </summary>
    /// <param name="visible">Scheduler-ordered visible records.</param>
    /// <param name="now">Current time (dwell/user-lock comparisons).</param>
    /// <param name="wheelSteps">Wheel override: rotate the main pill by this many positions (0 = none).</param>
    public ImmutableArray<ActivityRecord> Order(
        ImmutableArray<ActivityRecord> visible,
        DateTimeOffset now,
        int wheelSteps = 0)
    {
        // A pin is owned by the user, not by a TTL. Release it only when the target disappears.
        if (_pinnedKey is { } pinned && Find(visible, pinned) is null)
        {
            _pinnedKey = null;
            _pinOverrideKey = null;
        }

        if (_pinOverrideKey is { } overrideKey)
        {
            var overrideRecord = Find(visible, overrideKey);
            if (overrideRecord is null || !IsActionable(overrideRecord))
            {
                _pinOverrideKey = null;
                var pinnedRecord = _pinnedKey is { } pk ? Find(visible, pk) : null;
                if (pinnedRecord is not null)
                {
                    SetPrimary(pinnedRecord, now);
                    return Arrange(pinnedRecord, visible);
                }
            }
            else
            {
                SetPrimary(overrideRecord, now);
                return Arrange(overrideRecord, visible);
            }
        }

        if (visible.Length <= 1)
        {
            if (visible.Length == 1)
            {
                SetPrimary(visible[0], now);
            }

            return visible;
        }

        if (wheelSteps != 0)
        {
            var currentIndex = _primaryKey is { } key ? FindIndex(visible, key) : 0;
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            var next = ((currentIndex - wheelSteps) % visible.Length + visible.Length) % visible.Length;
            var rotated = Rotate(visible, next);
            SetPrimary(rotated[0], now);
            _pinnedKey = rotated[0].Key;
            _pinOverrideKey = null;
            _restoreKey = null;
            return rotated;
        }

        var current = _primaryKey is { } currentKey ? Find(visible, currentKey) : null;
        var currentLayer = current is null ? Layer.Idle : Classify(current, now);

        // Pick the highest layer, and inside it the activity that appeared first (stability).
        var best = visible[0];
        var bestLayer = Classify(best, now);
        foreach (var record in visible)
        {
            var layer = Classify(record, now);
            if (layer < bestLayer
                || (layer == bestLayer && record.ReceivedAt < best.ReceivedAt))
            {
                best = record;
                bestLayer = layer;
            }
        }

        if (current is null)
        {
            // Primary ended (TTL/retract): restore the persistent primary that a short event displaced.
            var restored = _restoreKey is { } restoreKey ? Find(visible, restoreKey) : null;
            var chosen = restored ?? best;
            SetPrimary(chosen, now);
            return Arrange(chosen, visible);
        }

        var dwellElapsed = now - _primarySince >= MinDwell;
        var userWants = _userKey is { } userKey && Find(visible, userKey) is not null
            && IsUserLocked(userKey, now) && current.Key != userKey;
        var shortEventCanPreempt = bestLayer < currentLayer
            && bestLayer == Layer.Actionable
            && dwellElapsed;
        var higherUser = bestLayer == Layer.User && currentLayer != Layer.User;

        if (userWants || higherUser)
        {
            var chosen = bestLayer == Layer.User ? best : Find(visible, _userKey!.Value) ?? best;
            SetPrimary(chosen, now);
            return Arrange(chosen, visible);
        }

        if (_pinnedKey == current.Key && dwellElapsed)
        {
            // The pinned item is classified as User, so the normal "best" search would pick the pin
            // again. Look explicitly for an actionable event that may borrow the pill; the pin stays
            // as the restore target until that event disappears.
            ActivityRecord? actionable = null;
            foreach (var record in visible)
            {
                if (IsActionable(record) && (actionable is null || record.ReceivedAt < actionable.ReceivedAt))
                {
                    actionable = record;
                }
            }

            if (actionable is not null)
            {
                _pinOverrideKey = actionable.Key;
                SetPrimary(actionable, now);
                return Arrange(actionable, visible);
            }
        }

        if (shortEventCanPreempt)
        {
            // Remember the persistent primary so the pill returns to it when the short event expires.
            _restoreKey = currentLayer == Layer.Persistent ? current.Key : _restoreKey;
            SetPrimary(best, now);
            return Arrange(best, visible);
        }

        // Stability: keep the current primary. Passive re-sorting must never flip the main pill.
        return Arrange(current, visible);
    }

    /// <summary>Primary first; the remaining preview balls follow by importance (rank, then arrival).</summary>
    private static ImmutableArray<ActivityRecord> Arrange(
        ActivityRecord primary, ImmutableArray<ActivityRecord> visible)
    {
        var rest = visible
            .Where(record => record.Key != primary.Key)
            .OrderBy(record => ActivityPriorityPolicy.Rank(record.Kind))
            .ThenBy(record => record.ReceivedAt)
            .ToImmutableArray();
        return rest.Insert(0, primary);
    }

    private void SetPrimary(ActivityRecord record, DateTimeOffset now)
    {
        if (_primaryKey != record.Key)
        {
            LogPrimaryChange(_primaryKey, record.Key, _pinnedKey is { } pin && pin == record.Key ? "pin" : "order");
            _primaryKey = record.Key;
            _primarySince = now;
        }
    }

    private static void LogPrimaryChange(ActivityKey? oldKey, ActivityKey newKey, string reason)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_PIN_DIAG"), "1", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var frame = Environment.StackTrace.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(3);
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "fluidbar-pin-diag.log"),
                $"{DateTimeOffset.Now:O} | {oldKey?.ToString() ?? "none"} -> {newKey} | {reason} | {string.Join(" <- ", frame)}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    private static ActivityRecord? Find(ImmutableArray<ActivityRecord> visible, ActivityKey key)
    {
        foreach (var record in visible)
        {
            if (record.Key == key)
            {
                return record;
            }
        }

        return null;
    }

    private static int FindIndex(ImmutableArray<ActivityRecord> visible, ActivityKey key)
    {
        for (var i = 0; i < visible.Length; i++)
        {
            if (visible[i].Key == key)
            {
                return i;
            }
        }

        return -1;
    }

    private static ImmutableArray<ActivityRecord> MoveFirst(
        ImmutableArray<ActivityRecord> visible, ActivityRecord primary)
    {
        if (visible[0].Key == primary.Key)
        {
            return visible;
        }

        return visible.Remove(primary).Insert(0, primary);
    }

    private static ImmutableArray<ActivityRecord> Rotate(ImmutableArray<ActivityRecord> visible, int start)
    {
        if (start <= 0)
        {
            return visible;
        }

        return visible.Skip(start).Concat(visible.Take(start)).ToImmutableArray();
    }
}
