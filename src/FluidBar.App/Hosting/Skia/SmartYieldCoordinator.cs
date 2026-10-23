using System.Diagnostics;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Interaction;
using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Connects the pure <see cref="YieldEngine"/> to the live island (design doc §3): feeds pointer samples
/// from the low-level mouse hook, resolves the foreground-window context and toggles input pass-through
/// on the layered hosts. All decisions stay in Core; this class is glue and never swallows a click.
/// </summary>
internal sealed class SmartYieldCoordinator : IDisposable
{
    private readonly YieldEngine _engine;
    private readonly IAppLogger _logger;
    private readonly Func<((int X, int Y, int Width, int Height) Shell, bool Dragging)?> _shellProvider;
    private readonly Action<bool> _applyPassthrough;
    private readonly Func<long> _clock;
    private readonly Func<(int X, int Y, int Width, int Height), bool, YieldContext> _contextFactory;

    private YieldState _lastState = YieldState.Interactive;
    private double _progress;
    private bool _animating;
    private long _animStartMs;
    private double _animFrom;
    private long? _contextStampMs;
    private YieldContext _cachedContext;
    private bool _disposed;

    /// <summary>Creates the coordinator.</summary>
    /// <param name="options">Engine tuning (sensitivity etc.).</param>
    /// <param name="shellProvider">Returns the island shell rect in physical screen pixels (null when hidden).</param>
    /// <param name="applyPassthrough">Applies the "island must not consume input" state.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="clock">Monotonic clock in ms (tests inject a fake; defaults to Stopwatch).</param>
    /// <param name="contextFactory">Context resolver (tests inject; defaults to the Win32 probe).</param>
    public SmartYieldCoordinator(
        YieldOptions options,
        Func<((int X, int Y, int Width, int Height) Shell, bool Dragging)?> shellProvider,
        Action<bool> applyPassthrough,
        IAppLogger logger,
        Func<long>? clock = null,
        Func<(int X, int Y, int Width, int Height), bool, YieldContext>? contextFactory = null)
    {
        _clock = clock ?? (() => (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency));
        _contextFactory = contextFactory ?? DefaultContext;
        _engine = new YieldEngine(options);
        _shellProvider = shellProvider ?? throw new ArgumentNullException(nameof(shellProvider));
        _applyPassthrough = applyPassthrough ?? throw new ArgumentNullException(nameof(applyPassthrough));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Current engine state (diagnostics/tests).</summary>
    public YieldState State => _engine.State;

    /// <summary>Current intent score (diagnostics).</summary>
    public double Score => _engine.Score;

    /// <summary>Soften-animation progress (0 = interactive, 1 = fully yielded).</summary>
    public double Progress => _progress;

    /// <summary>
    /// Advances the soften animation towards the engine's target and reports whether another frame is
    /// needed. Materials are irrelevant here: the renderer applies the effect to the whole scene.
    /// </summary>
    /// <param name="nowMs">Monotonic clock in milliseconds.</param>
    public bool Step(long nowMs)
    {
        if (_disposed)
        {
            return false;
        }

        var target = _engine.ShouldPassThrough ? 1d : 0d;
        if (Math.Abs(_progress - target) < 0.001)
        {
            _animating = false;
            _progress = target;
            return false;
        }

        if (!_animating)
        {
            _animating = true;
            _animStartMs = nowMs;
            _animFrom = _progress;
        }

        var duration = target > _progress
            ? FluidBar.Island.YieldTokens.SoftenDurationMs
            : FluidBar.Island.YieldTokens.RestoreDurationMs;
        var t = Math.Clamp((nowMs - _animStartMs) / duration, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        _progress = _animFrom + ((target - _animFrom) * eased);
        if (t >= 1)
        {
            _progress = target;
            _animating = false;
        }

        return true;
    }

    /// <summary>Feeds one pointer position in physical screen pixels.</summary>
    /// <param name="x">Pointer X.</param>
    /// <param name="y">Pointer Y.</param>
    public void PointerMoved(int x, int y)
    {
        if (_disposed)
        {
            return;
        }

        var now = _clock();
        var resolved = _shellProvider();
        if (resolved is not { } info)
        {
            if (_engine.State != YieldState.Interactive)
            {
                Restore();
            }

            return;
        }

        var context = Context(info.Shell, info.Dragging, now);
        var sample = new YieldSample(x, y, now);
        var shell = new YieldShell(info.Shell.X, info.Shell.Y, info.Shell.Width, info.Shell.Height);
        var transition = _engine.Update(sample, shell, context, now);
        if (transition.Changed)
        {
            Apply(transition);
        }
    }

    /// <summary>Marks a click/wheel on the island so the engine suppresses a yield for a moment.</summary>
    public void NoteInteraction()
    {
        if (!_disposed)
        {
            _engine.NoteInteraction(_clock());
        }
    }

    /// <summary>Manual restore (tray command, settings change, host reset).</summary>
    public void Restore()
    {
        if (_disposed)
        {
            return;
        }

        _engine.ForceInteractive(_clock());
        _animating = false;
        if (_lastState != YieldState.Interactive)
        {
            _lastState = YieldState.Interactive;
            _applyPassthrough(false);
            _logger.Information(LogCategories.Render, "island.yield.restore",
                "Smart yield restored island input.");
        }
    }

    private void Apply(in YieldTransition transition)
    {
        _lastState = transition.Current;
        _animating = false;
        _applyPassthrough(transition.ShouldPassThrough);
        _logger.Information(LogCategories.Render, "island.yield.transition",
            "Smart yield state changed.",
            data: new Dictionary<string, object?>
            {
                ["from"] = transition.Previous.ToString(),
                ["to"] = transition.Current.ToString(),
                ["score"] = Math.Round(transition.Score, 1),
                ["passThrough"] = transition.ShouldPassThrough,
            });
    }

    private YieldContext Context((int X, int Y, int Width, int Height) shell, bool dragging, long nowMs)
    {
        // The Win32 queries are cheap but not free; refresh them at most every 100 ms while the pointer
        // is moving, which is well inside the reaction time the state machine needs.
        if (_contextStampMs is null || nowMs - _contextStampMs.Value >= 100)
        {
            _contextStampMs = nowMs;
            _cachedContext = _contextFactory(shell, dragging);
        }

        return _cachedContext with { IsDragging = dragging };
    }

    private static YieldContext DefaultContext((int X, int Y, int Width, int Height) shell, bool dragging)
        => new(
            ForegroundMaximized: ForegroundWindowProbe.IsForegroundMaximizedOver(
                shell.X, shell.Y, shell.Width, shell.Height),
            ForegroundFullscreen: ForegroundWindowProbe.IsForegroundFullscreen(),
            IsDragging: dragging);

    private long NowMs() => _clock();

    /// <inheritdoc />
    public void Dispose() => _disposed = true;
}
