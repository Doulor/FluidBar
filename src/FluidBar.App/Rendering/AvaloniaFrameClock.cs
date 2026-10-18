using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using FluidBar.Core.Motion;

namespace FluidBar.App.Rendering;

/// <summary>
/// Avalonia frame hook for the spring loop (spec 4.4, spike §2): <see cref="TopLevel.RequestAnimationFrame"/>
/// is one-shot, so every tick re-arms it; the elapsed time is measured with <see cref="Stopwatch"/>
/// (the callback's TimeSpan is only a hint). A render-priority <see cref="DispatcherTimer"/> watchdog
/// takes over if the compositor never delivers frames. When stopped, zero frame callbacks fire.
/// </summary>
public sealed class AvaloniaFrameClock : IFrameClock, IDisposable
{
    private readonly TopLevel _topLevel;
    private readonly Dispatcher _dispatcher;
    private Action<TimeSpan>? _onFrame;
    private DispatcherTimer? _watchdog;
    private long _lastTimestamp;
    private long _firstRequestTimestamp;
    private bool _usingFallback;
    private bool _running;
    private bool _disposed;

    /// <summary>Creates a frame clock for a top level.</summary>
    public AvaloniaFrameClock(TopLevel topLevel)
    {
        _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));
        _dispatcher = Dispatcher.UIThread;
    }

    /// <inheritdoc />
    public bool IsRunning => _running;

    /// <summary>Whether the render-timer fallback is driving frames (diagnostics).</summary>
    public bool IsUsingFallback => _usingFallback;

    /// <inheritdoc />
    public void Start(Action<TimeSpan> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _onFrame = onFrame;
        if (_running)
        {
            return;
        }

        _running = true;
        _usingFallback = false;
        _lastTimestamp = Stopwatch.GetTimestamp();
        _firstRequestTimestamp = _lastTimestamp;
        RequestFrame();
        StartWatchdog();
    }

    /// <inheritdoc />
    public void Stop()
    {
        _running = false;
        _onFrame = null;
        _watchdog?.Stop();
        _watchdog = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void RequestFrame()
    {
        if (!_running)
        {
            return;
        }

        _topLevel.RequestAnimationFrame(OnCompositorFrame);
    }

    private void OnCompositorFrame(TimeSpan _)
    {
        if (!_running)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsed = _lastTimestamp == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((now - _lastTimestamp) / (double)Stopwatch.Frequency);
        _lastTimestamp = now;
        if (elapsed <= TimeSpan.Zero)
        {
            elapsed = TimeSpan.FromSeconds(1.0 / 120.0);
        }

        _onFrame?.Invoke(elapsed);
        RequestFrame();
    }

    private void StartWatchdog()
    {
        _watchdog?.Stop();
        _watchdog = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _watchdog.Tick += OnWatchdogTick;
        _watchdog.Start();
    }

    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (!_running)
        {
            return;
        }

        // If no compositor frame arrived within 250 ms of the request, fall back to a render timer.
        var stalled = Stopwatch.GetElapsedTime(_firstRequestTimestamp) > TimeSpan.FromMilliseconds(250)
            && Stopwatch.GetElapsedTime(_lastTimestamp) > TimeSpan.FromMilliseconds(250);
        if (!stalled)
        {
            return;
        }

        _watchdog?.Stop();
        _watchdog = null;
        _usingFallback = true;
        _lastTimestamp = Stopwatch.GetTimestamp();
        var fallback = new DispatcherTimer(DispatcherPriority.Render, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(1),
        };
        fallback.Tick += OnFallbackTick;
        fallback.Start();
        _fallbackTimer = fallback;
    }

    private DispatcherTimer? _fallbackTimer;

    private void OnFallbackTick(object? sender, EventArgs e)
    {
        if (!_running)
        {
            _fallbackTimer?.Stop();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsed = TimeSpan.FromSeconds((now - _lastTimestamp) / (double)Stopwatch.Frequency);
        _lastTimestamp = now;
        _onFrame?.Invoke(elapsed);
        if (!_running)
        {
            _fallbackTimer?.Stop();
        }
    }
}
