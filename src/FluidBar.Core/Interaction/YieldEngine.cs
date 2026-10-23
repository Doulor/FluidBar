using System.Diagnostics;

namespace FluidBar.Core.Interaction;

/// <summary>State of the predictive "smart yield" system (design doc §4).</summary>
public enum YieldState
{
    /// <summary>The island is interactive: clicks and wheel deltas belong to it.</summary>
    Interactive,

    /// <summary>Suspicious pointer trajectory; the engine keeps watching but does not switch input yet.</summary>
    Candidate,

    /// <summary>The island yields: it shrinks/park and stops consuming pointer input.</summary>
    Yielding,

    /// <summary>Stabilisation window after a yield, so the state cannot flap around the threshold.</summary>
    Cooldown,
}

/// <summary>How aggressively the engine yields (design doc §12).</summary>
public enum YieldSensitivity
{
    /// <summary>Yields only on clear evidence (higher thresholds, longer dwell).</summary>
    Conservative,

    /// <summary>Default balance between responsiveness and false positives.</summary>
    Balanced,

    /// <summary>Yields early; expect more false positives.</summary>
    Aggressive,
}

/// <summary>One pointer sample in screen pixels (physical) with a monotonic timestamp in milliseconds.</summary>
/// <param name="X">Screen X in physical pixels.</param>
/// <param name="Y">Screen Y in physical pixels.</param>
/// <param name="TimestampMs">Monotonic timestamp (Stopwatch-based).</param>
public readonly record struct YieldSample(double X, double Y, long TimestampMs);

/// <summary>The island shell rectangle in screen pixels (physical).</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct YieldShell(double X, double Y, double Width, double Height)
{
    /// <summary>Right edge.</summary>
    public double Right => X + Width;

    /// <summary>Bottom edge.</summary>
    public double Bottom => Y + Height;

    /// <summary>Whether a point lies inside the rectangle, optionally padded.</summary>
    /// <param name="x">Point X.</param>
    /// <param name="y">Point Y.</param>
    /// <param name="padding">Extra margin in pixels.</param>
    public bool Contains(double x, double y, double padding = 0)
        => x >= X - padding && x <= Right + padding && y >= Y - padding && y <= Bottom + padding;

    /// <summary>Distance from a point to the rectangle (0 when inside).</summary>
    /// <param name="x">Point X.</param>
    /// <param name="y">Point Y.</param>
    public double DistanceTo(double x, double y)
    {
        var dx = Math.Max(Math.Max(X - x, 0), x - Right);
        var dy = Math.Max(Math.Max(Y - y, 0), y - Bottom);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

/// <summary>Context signals from the window/input layer (design doc §5.1).</summary>
/// <param name="ForegroundMaximized">The foreground window is maximized and overlaps the island.</param>
/// <param name="ForegroundFullscreen">A fullscreen app (game/video/presentation) owns the monitor.</param>
/// <param name="IsDragging">The user is dragging the island or a window.</param>
/// <param name="PointerInsideInteractive">The pointer is over an interactive island control.</param>
/// <param name="RecentlyInteracted">The user clicked the island within the interaction window.</param>
/// <param name="ManualRestoreRequested">A manual restore arrived; forces Interactive.</param>
public readonly record struct YieldContext(
    bool ForegroundMaximized = false,
    bool ForegroundFullscreen = false,
    bool IsDragging = false,
    bool PointerInsideInteractive = false,
    bool RecentlyInteracted = false,
    bool ManualRestoreRequested = false);

/// <summary>Transition produced by <see cref="YieldEngine.Update"/>.</summary>
/// <param name="Previous">State before the update.</param>
/// <param name="Current">State after the update.</param>
/// <param name="Score">Current intent score (-100..100).</param>
/// <param name="ShouldPassThrough">Whether the window must stop consuming pointer input.</param>
public readonly record struct YieldTransition(YieldState Previous, YieldState Current, double Score, bool ShouldPassThrough)
{
    /// <summary>True when the state changed in this update.</summary>
    public bool Changed => Previous != Current;
}

/// <summary>Tuning knobs for <see cref="YieldEngine"/> (design doc §5.2 and §12).</summary>
public sealed record YieldOptions
{
    /// <summary>Sensitivity preset.</summary>
    public YieldSensitivity Sensitivity { get; init; } = YieldSensitivity.Balanced;

    /// <summary>Trajectory samples kept for speed/heading (bounded).</summary>
    public int HistorySize { get; init; } = 12;

    /// <summary>How far ahead the heading is extrapolated, in ms.</summary>
    public int LookaheadMs { get; init; } = 220;

    /// <summary>Speed (px/ms) that counts as a fast pass-through; 2.0 ≈ 2000 px/s.</summary>
    public double FastApproachSpeed { get; init; } = 2.0;

    /// <summary>Dwell (ms) inside the shell that counts as "wants the island".</summary>
    public int DwellInsideMs { get; init; } = 180;

    /// <summary>How long a recent island click keeps suppressing a yield, in ms.</summary>
    public int RecentInteractionMs { get; init; } = 900;

    /// <summary>Restore the island after this long without pointer activity near it, in ms.</summary>
    public int RestoreAfterMs { get; init; } = 3000;

    /// <summary>Radius around the shell (px) that still counts as "interacting with the island".</summary>
    public double ActivityRadiusPx { get; init; } = 56;

    /// <summary>Cooldown after a yield, in ms.</summary>
    public int CooldownMs { get; init; } = 400;

    /// <summary>Candidate state times out after this long without confirmation, in ms.</summary>
    public int CandidateTimeoutMs { get; init; } = 800;

    /// <summary>Hard cap on a single yield, in ms (protects against a stuck pointer sitting on the shell).</summary>
    public int MaxYieldMs { get; init; } = 30_000;

    /// <summary>Score at or above which the engine starts watching (Candidate).</summary>
    public int CandidateThreshold => Sensitivity switch
    {
        YieldSensitivity.Conservative => 30,
        YieldSensitivity.Aggressive => 12,
        _ => 20,
    };

    /// <summary>Score at or above which the engine yields.</summary>
    public int YieldThreshold => Sensitivity switch
    {
        YieldSensitivity.Conservative => 70,
        YieldSensitivity.Aggressive => 42,
        _ => 55,
    };

    /// <summary>Score at or below which a yield may end (hysteresis).</summary>
    public int ExitThreshold => Sensitivity switch
    {
        YieldSensitivity.Conservative => 20,
        YieldSensitivity.Aggressive => 35,
        _ => 28,
    };
}

/// <summary>
/// Rule-based intent scorer + hysteresis state machine for the smart-yield system (design doc §4–§5).
/// Pure logic: no timers, no Win32, no allocation on the hot path — the caller feeds pointer samples.
/// </summary>
public sealed class YieldEngine
{
    private readonly YieldOptions _options;
    private readonly Queue<YieldSample> _history;

    private YieldState _state = YieldState.Interactive;
    private long _stateSinceMs;
    private long _lastInsideMs = long.MinValue;
    private long _insideSinceMs = long.MinValue;
    private long _lastNearMs = long.MinValue;
    private long _lastInteractionMs = long.MinValue;
    private double _score;

    /// <summary>Creates an engine with the given options.</summary>
    /// <param name="options">Tuning knobs; defaults to the balanced preset.</param>
    public YieldEngine(YieldOptions? options = null)
    {
        _options = options ?? new YieldOptions();
        _history = new Queue<YieldSample>(Math.Max(4, _options.HistorySize));
    }

    /// <summary>Current state.</summary>
    public YieldState State => _state;

    /// <summary>Current intent score (-100..100, positive = yield).</summary>
    public double Score => _score;

    /// <summary>Number of samples currently buffered (bounded by <see cref="YieldOptions.HistorySize"/>).</summary>
    public int HistoryCount => _history.Count;

    /// <summary>Timestamp of the last pointer activity near the shell (diagnostics/tests).</summary>
    public long LastNearMs => _lastNearMs;

    /// <summary>The island should stop consuming pointer input while yielding.</summary>
    public bool ShouldPassThrough => _state == YieldState.Yielding;

    /// <summary>Pushes one pointer sample and advances the state machine.</summary>
    /// <param name="sample">Pointer position + monotonic timestamp.</param>
    /// <param name="shell">Island shell rectangle in the same coordinate space.</param>
    /// <param name="context">Window/interaction context.</param>
    /// <param name="nowMs">Monotonic "now" (same clock as the sample timestamps).</param>
    public YieldTransition Update(in YieldSample sample, in YieldShell shell, in YieldContext context, long nowMs)
    {
        var previous = _state;

        PushSample(sample);
        if (context.RecentlyInteracted)
        {
            _lastInteractionMs = nowMs;
        }

        if (shell.DistanceTo(sample.X, sample.Y) <= _options.ActivityRadiusPx)
        {
            _lastNearMs = nowMs;
        }

        if (shell.Contains(sample.X, sample.Y))
        {
            _lastInsideMs = nowMs;
            if (_insideSinceMs == long.MinValue)
            {
                _insideSinceMs = nowMs;
            }
        }
        else
        {
            _insideSinceMs = long.MinValue;
        }

        _score = ComputeScore(sample, shell, context, nowMs);
        AdvanceCore(context, nowMs);

        return new YieldTransition(previous, _state, _score, ShouldPassThrough);
    }

    /// <summary>Forces a yield (right-click "soften now"), held until the restore timer expires.</summary>
    /// <param name="nowMs">Monotonic "now".</param>
    public void ForceYield(long nowMs)
    {
        _state = YieldState.Yielding;
        _stateSinceMs = nowMs;
        _lastNearMs = nowMs;
    }

    /// <summary>
    /// Advances only the time-based rules (restore timer, cooldown) without a new pointer sample. A tick
    /// must **not** count as pointer activity, otherwise a pointer parked on the island would refresh the
    /// "3 s without activity" window forever and the island could never come back.
    /// </summary>
    /// <param name="shell">Island shell rectangle.</param>
    /// <param name="context">Window/interaction context.</param>
    /// <param name="nowMs">Monotonic "now".</param>
    public YieldTransition Tick(in YieldShell shell, in YieldContext context, long nowMs)
    {
        var previous = _state;
        if (context.ManualRestoreRequested)
        {
            ForceInteractive(nowMs);
        }
        else if (_state != YieldState.Interactive)
        {
            // Never start a *new* yield from a tick: the score is only refreshed by real pointer samples,
            // so a stale high score would immediately re-yield the island right after it came back.
            AdvanceCore(context, nowMs);
        }

        return new YieldTransition(previous, _state, _score, ShouldPassThrough);
    }

    /// <summary>Forces the engine back to Interactive (manual restore, settings change, host reset).</summary>
    /// <param name="nowMs">Monotonic "now".</param>
    public void ForceInteractive(long nowMs)
    {
        _state = YieldState.Interactive;
        _stateSinceMs = nowMs;
        _score = 0;
        _history.Clear();
    }

    /// <summary>Marks a user interaction with the island (click, drag start, wheel).</summary>
    /// <param name="nowMs">Monotonic "now".</param>
    public void NoteInteraction(long nowMs) => _lastInteractionMs = nowMs;

    private void PushSample(in YieldSample sample)
    {
        var limit = Math.Max(4, _options.HistorySize);
        _history.Enqueue(sample);
        while (_history.Count > limit)
        {
            _history.Dequeue();
        }
    }

    private double ComputeScore(in YieldSample sample, in YieldShell shell, in YieldContext context, long nowMs)
    {
        if (context.IsDragging)
        {
            return -100;
        }

        var score = 0d;
        var speed = Speed(sample);
        var inside = shell.Contains(sample.X, sample.Y);

        // Trajectory extrapolated into the shell: the pointer is heading at the island's area.
        if (HeadingHitsShell(sample, shell, speed))
        {
            score += 25;
        }

        // Fast pass-through: moving quickly, not dwelling inside.
        var dwellMs = _lastInsideMs == long.MinValue ? 0 : nowMs - _lastInsideMs;
        if (!inside && speed >= _options.FastApproachSpeed && dwellMs < _options.DwellInsideMs)
        {
            score += 20;
        }

        if (context.ForegroundFullscreen)
        {
            score += 30;
        }
        else if (context.ForegroundMaximized)
        {
            score += 15;
        }

        if (context.PointerInsideInteractive)
        {
            score -= 20;
        }

        // Dwelling inside the shell is evidence *for* the island, not against it: measure the total
        // time inside, not the time since the last sample (which is always zero).
        var insideMs = _insideSinceMs == long.MinValue ? 0 : nowMs - _insideSinceMs;
        if (inside && insideMs >= _options.DwellInsideMs)
        {
            score -= 35;
        }

        // long.MinValue is the "never" sentinel: subtracting it would overflow and look like a fresh
        // interaction, which silently suppressed every yield.
        var interactedRecently = _lastInteractionMs != long.MinValue
            && nowMs - _lastInteractionMs <= _options.RecentInteractionMs;
        if (context.RecentlyInteracted || interactedRecently)
        {
            // Stronger than the design doc's -40: a user who just clicked the island must not even enter
            // Candidate, otherwise a fast exit flickers the state machine on every interaction.
            score -= 60;
        }

        return Math.Clamp(score, -100, 100);
    }

    private void AdvanceCore(in YieldContext context, long nowMs)
    {
        if (context.ManualRestoreRequested)
        {
            ForceInteractive(nowMs);
            return;
        }

        switch (_state)
        {
            case YieldState.Interactive:
                if (_score >= _options.CandidateThreshold)
                {
                    Transition(YieldState.Candidate, nowMs);
                }

                break;

            case YieldState.Candidate:
                if (_score >= _options.YieldThreshold)
                {
                    Transition(YieldState.Yielding, nowMs);
                }
                else if (nowMs - _stateSinceMs >= _options.CandidateTimeoutMs || _score < _options.CandidateThreshold / 2d)
                {
                    Transition(YieldState.Interactive, nowMs);
                }

                break;

            case YieldState.Yielding:
                // The island comes back 3 s after the last pointer activity near it (user rule), with a
                // hard cap so a pointer parked on the shell cannot hold it forever.
                var lastActivity = Math.Max(
                    _stateSinceMs,
                    _lastNearMs == long.MinValue ? _stateSinceMs : _lastNearMs);
                if (nowMs - _stateSinceMs >= _options.MaxYieldMs
                    || nowMs - lastActivity >= _options.RestoreAfterMs)
                {
                    Transition(YieldState.Cooldown, nowMs);
                }

                break;

            case YieldState.Cooldown:
                if (nowMs - _stateSinceMs >= _options.CooldownMs)
                {
                    Transition(YieldState.Interactive, nowMs);
                }

                break;
        }
    }

    private void Transition(YieldState next, long nowMs)
    {
        _state = next;
        _stateSinceMs = nowMs;
    }

    private double Speed(in YieldSample sample)
    {
        if (_history.Count < 2)
        {
            return 0;
        }

        var samples = _history.ToArray();
        var previous = samples[^1];
        var before = samples[^2];
        var dt = sample.TimestampMs - before.TimestampMs;
        if (dt <= 0)
        {
            return 0;
        }

        var dx = previous.X - before.X;
        var dy = previous.Y - before.Y;
        return Math.Sqrt((dx * dx) + (dy * dy)) / dt;
    }

    private bool HeadingHitsShell(in YieldSample sample, in YieldShell shell, double speed)
    {
        if (_history.Count < 2 || speed <= 0.05)
        {
            return false;
        }

        var samples = _history.ToArray();
        var last = samples[^1];
        var prev = samples[^2];
        var vx = last.X - prev.X;
        var vy = last.Y - prev.Y;
        var length = Math.Sqrt((vx * vx) + (vy * vy));
        if (length < 0.5)
        {
            return false;
        }

        var look = Math.Max(1, _options.LookaheadMs);
        var targetX = last.X + (vx / length * speed * look);
        var targetY = last.Y + (vy / length * speed * look);

        // Segment-vs-rect (slab method): a fast pointer overshoots the shell, so testing only the
        // extrapolated end point would miss the island completely.
        var minX = shell.X - 6;
        var maxX = shell.Right + 6;
        var minY = shell.Y - 6;
        var maxY = shell.Bottom + 6;
        var t0 = 0d;
        var t1 = 1d;
        return Clip(-vx, last.X - minX, ref t0, ref t1)
            && Clip(vx, maxX - last.X, ref t0, ref t1)
            && Clip(-vy, last.Y - minY, ref t0, ref t1)
            && Clip(vy, maxY - last.Y, ref t0, ref t1);
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) < 1e-6)
        {
            return q >= 0;
        }

        var r = q / p;
        if (p < 0)
        {
            if (r > t1)
            {
                return false;
            }

            if (r > t0)
            {
                t0 = r;
            }
        }
        else
        {
            if (r < t0)
            {
                return false;
            }

            if (r < t1)
            {
                t1 = r;
            }
        }

        return true;
    }
}
