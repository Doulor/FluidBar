namespace FluidBar.App.State;

/// <summary>Snapshot of the home quick-tile pomodoro (focus timer).</summary>
/// <param name="IsRunning">Whether the countdown is running.</param>
/// <param name="IsComplete">True for the short "finished" state before the next start.</param>
/// <param name="Remaining">Time left (or the full duration when idle).</param>
/// <param name="Duration">Configured session length.</param>
public readonly record struct PomodoroSnapshot(bool IsRunning, bool IsComplete, TimeSpan Remaining, TimeSpan Duration)
{
    /// <summary>Idle state (25 minutes, nothing running).</summary>
    public static PomodoroSnapshot Idle { get; } = new(false, false, TimeSpan.FromMinutes(PomodoroTimer.DefaultMinutes),
        TimeSpan.FromMinutes(PomodoroTimer.DefaultMinutes));

    /// <summary>Short label for the quick tile: <c>24:31</c> / <c>25:00</c> / <c>完成</c>.</summary>
    public string Text => IsComplete
        ? "完成"
        : IsRunning
            ? $"{(int)Remaining.TotalMinutes:00}:{Remaining.Seconds:00}"
            : $"{(int)Duration.TotalMinutes:00}:00";
}

/// <summary>
/// One pomodoro session driven by the island's own clock. Pure state: the host owns the ticker, so the
/// timer costs no frames beyond the one repaint per displayed second.
/// </summary>
public sealed class PomodoroTimer
{
    /// <summary>Default session length in minutes.</summary>
    public const int DefaultMinutes = 25;

    private readonly TimeSpan _duration;
    private DateTimeOffset? _endsAt;
    private bool _complete;

    /// <summary>Creates a timer.</summary>
    /// <param name="duration">Session length (defaults to 25 minutes).</param>
    public PomodoroTimer(TimeSpan? duration = null)
    {
        _duration = duration ?? TimeSpan.FromMinutes(DefaultMinutes);
        Snapshot = new PomodoroSnapshot(false, false, _duration, _duration);
    }

    /// <summary>Current snapshot.</summary>
    public PomodoroSnapshot Snapshot { get; private set; }

    /// <summary>Starts a session, stops a running one, or restarts after completion.</summary>
    /// <param name="now">Current local time.</param>
    /// <returns>True when the state changed.</returns>
    public bool Toggle(DateTimeOffset now)
    {
        if (Snapshot.IsRunning)
        {
            _endsAt = null;
            _complete = false;
            Snapshot = new PomodoroSnapshot(false, false, _duration, _duration);
            return true;
        }

        _complete = false;
        _endsAt = now + _duration;
        Snapshot = new PomodoroSnapshot(true, false, _duration, _duration);
        return true;
    }

    /// <summary>Advances the countdown to <paramref name="now"/>.</summary>
    /// <param name="now">Current local time.</param>
    /// <returns>True when the visible second (or the completion state) changed.</returns>
    public bool Tick(DateTimeOffset now)
    {
        if (Snapshot.IsRunning && _endsAt is { } endsAt)
        {
            var remaining = endsAt - now;
            if (remaining <= TimeSpan.Zero)
            {
                _endsAt = null;
                _complete = true;
                Snapshot = new PomodoroSnapshot(false, true, TimeSpan.Zero, _duration);
                return true;
            }

            var text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
            if (text == Snapshot.Text)
            {
                return false;
            }

            Snapshot = new PomodoroSnapshot(true, false, remaining, _duration);
            return true;
        }

        return false;
    }

    /// <summary>Clears the "完成" confirmation (the tile returns to its idle label).</summary>
    /// <returns>True when the state changed.</returns>
    public bool ClearCompletion()
    {
        if (!_complete)
        {
            return false;
        }

        _complete = false;
        Snapshot = new PomodoroSnapshot(false, false, _duration, _duration);
        return true;
    }
}
