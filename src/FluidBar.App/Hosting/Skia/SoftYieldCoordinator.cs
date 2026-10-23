using System.Diagnostics;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Interaction;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// "Soften" (让位) control for the island. There is no predictive logic: the user right-clicks the island
/// to soften it and right-clicks again to bring it back, and a 3 s safety timer restores it on its own.
/// The coordinator owns the fade animation and toggles input pass-through on the layered windows.
/// </summary>
internal sealed class SoftYieldCoordinator : IDisposable
{
    private readonly ManualYieldController _controller;
    private readonly IAppLogger _logger;
    private readonly Action<bool> _applyPassthrough;
    private readonly Func<long> _clock;

    private double _progress;
    private bool _animating;
    private long _animStartMs;
    private double _animFrom;
    private bool _disposed;

    /// <summary>Creates the coordinator.</summary>
    /// <param name="restoreAfterMs">Automatic restore delay (0 disables the timer).</param>
    /// <param name="applyPassthrough">Applies "the island must not consume input".</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="clock">Monotonic clock in ms (tests inject a fake).</param>
    public SoftYieldCoordinator(
        long restoreAfterMs,
        Action<bool> applyPassthrough,
        IAppLogger logger,
        Func<long>? clock = null)
    {
        _controller = new ManualYieldController(restoreAfterMs);
        _applyPassthrough = applyPassthrough ?? throw new ArgumentNullException(nameof(applyPassthrough));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? (() => (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency));
    }

    /// <summary>True while the island is softened or still fading.</summary>
    public bool IsActive => _controller.IsSoftened || _progress > 0.001;

    /// <summary>True while the island does not consume pointer input.</summary>
    public bool IsSoftened => _controller.IsSoftened;

    /// <summary>Soften-animation progress (0 = normal, 1 = fully softened).</summary>
    public double Progress => _progress;

    /// <summary>Right-click gesture: soften now, or restore when already softened.</summary>
    public void Toggle()
    {
        if (_disposed)
        {
            return;
        }

        if (_controller.IsSoftened)
        {
            Restore();
            return;
        }

        if (_controller.Soften(_clock()))
        {
            _applyPassthrough(true);
            _logger.Information(LogCategories.Render, "island.yield.soften",
                "Island softened by right-click (input pass-through on).");
        }
    }

    /// <summary>Restores the island immediately (right-click, settings change, host reset).</summary>
    public void Restore()
    {
        if (_disposed)
        {
            return;
        }

        if (_controller.Restore())
        {
            _applyPassthrough(false);
            _logger.Information(LogCategories.Render, "island.yield.restore",
                "Island restored (input pass-through off).");
        }
    }

    /// <summary>
    /// Advances the fade and the safety timer; reports whether the host should paint another frame.
    /// </summary>
    /// <param name="nowMs">Monotonic clock in milliseconds.</param>
    public bool Step(long nowMs)
    {
        if (_disposed)
        {
            return false;
        }

        if (_controller.Tick(nowMs))
        {
            _applyPassthrough(false);
            _logger.Information(LogCategories.Render, "island.yield.auto-restore",
                "Island restored automatically after the soften timeout.");
        }

        var target = _controller.IsSoftened ? 1d : 0d;
        if (Math.Abs(_progress - target) < 0.001)
        {
            _animating = false;
            _progress = target;
            return IsActive;
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

    /// <inheritdoc />
    public void Dispose() => _disposed = true;
}
