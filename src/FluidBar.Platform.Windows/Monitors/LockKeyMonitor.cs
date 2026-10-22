using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Monitors;

/// <summary>Immutable lock-key state.</summary>
public sealed record LockKeysSnapshot(bool CapsLockOn, bool NumLockOn, bool ScrollLockOn);

/// <summary>
/// Pure lock-key state tracker fed by low-level keyboard events (no polling).
/// Caps/Num/Scroll toggle on key-down; key repeat must not toggle twice, so pressed keys are tracked.
/// </summary>
public sealed class LockKeyTracker
{
    private readonly HashSet<int> _pressed = new();
    private LockKeysSnapshot _current;

    public LockKeyTracker(bool capsLockOn = false, bool numLockOn = false, bool scrollLockOn = false)
    {
        _current = new LockKeysSnapshot(capsLockOn, numLockOn, scrollLockOn);
    }

    /// <summary>Last known state.</summary>
    public LockKeysSnapshot Current => _current;

    /// <summary>Applies a raw key event. Returns the new state when it changed, otherwise null.</summary>
    public LockKeysSnapshot? HandleKeyEvent(int virtualKey, bool isKeyDown)
    {
        if (!IsLockKey(virtualKey))
        {
            return null;
        }

        if (!isKeyDown)
        {
            _pressed.Remove(virtualKey);
            return null;
        }

        if (!_pressed.Add(virtualKey))
        {
            // Auto-repeat of an already pressed toggle key does not toggle the lock state again.
            return null;
        }

        _current = virtualKey switch
        {
            NativeMethods.VK_CAPITAL => _current with { CapsLockOn = !_current.CapsLockOn },
            NativeMethods.VK_NUMLOCK => _current with { NumLockOn = !_current.NumLockOn },
            NativeMethods.VK_SCROLL => _current with { ScrollLockOn = !_current.ScrollLockOn },
            _ => _current,
        };
        return _current;
    }

    /// <summary>Resets the tracked pressed-key set (e.g. after focus loss or hook restart).</summary>
    public void ResetPressedKeys() => _pressed.Clear();

    /// <summary>Replaces the baseline state (initial read from Win32).</summary>
    public void Reset(LockKeysSnapshot snapshot) => _current = snapshot;

    /// <summary>True for Caps Lock, Num Lock and Scroll Lock virtual-key codes.</summary>
    public static bool IsLockKey(int virtualKey)
        => virtualKey is NativeMethods.VK_CAPITAL or NativeMethods.VK_NUMLOCK or NativeMethods.VK_SCROLL;
}

/// <summary>
/// Lock-key monitor: <c>WH_KEYBOARD_LL</c> state tracking (installed only while running) with a
/// ≥1 s change-only polling fallback when the hook cannot be installed. Publishes change-only snapshots.
/// </summary>
public sealed class LockKeyMonitor : PlatformMonitor<LockKeysSnapshot>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly PlatformMessageHost _host;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly TimeProvider _timeProvider;
    private readonly LockKeyTracker _tracker = new();
    private LowLevelKeyboardHookService? _hook;
    private ITimer? _pollTimer;
    private int _running;

    public LockKeyMonitor(PlatformMessageHost host, IPlatformLogger logger, PlatformCounters counters,
        TimeProvider? timeProvider = null)
        : base("lockkeys", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>True when the monitor is relying on the 1 s polling fallback instead of the keyboard hook.</summary>
    public bool IsUsingPollingFallback { get; private set; }

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _running, 1);
        IsUsingPollingFallback = false;

        var initial = await Task.Run(ReadState, cancellationToken).ConfigureAwait(false);
        _tracker.Reset(initial);
        Publish(initial);

        var hook = new LowLevelKeyboardHookService(_host, _logger, _counters);
        hook.KeyEvent += OnKeyEvent;
        _hook = hook;
        try
        {
            await hook.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            IsUsingPollingFallback = true;
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Monitor, "lockkeys.hook-failed",
                "Low-level keyboard hook could not be installed; falling back to a 1 s change-only poll.", ex);
            StartPollingFallback();
        }
    }

    private void OnKeyEvent(object? sender, LowLevelKeyEvent keyEvent)
    {
        if (Volatile.Read(ref _running) == 0)
        {
            return;
        }

        var updated = _tracker.HandleKeyEvent(keyEvent.VirtualKey, keyEvent.IsKeyDown);
        if (updated is not null)
        {
            Publish(updated);
        }
    }

    private void StartPollingFallback()
    {
        _pollTimer?.Dispose();
        _pollTimer = _timeProvider.CreateTimer(static state =>
        {
            var self = (LockKeyMonitor)state!;
            self.PollOnce();
            self.StartPollingFallback();
        }, this, PollInterval, Timeout.InfiniteTimeSpan);
    }

    private void PollOnce()
    {
        if (Volatile.Read(ref _running) == 0)
        {
            return;
        }

        try
        {
            Publish(ReadState());
        }
        catch (Exception ex)
        {
            _counters.IncrementMonitorCallbackFailures();
            _logger.LogError(PlatformLogCategory.Monitor, "lockkeys.poll-failed", "Lock-key poll failed.", ex);
        }
    }

    private static LockKeysSnapshot ReadState()
    {
        static bool IsOn(int virtualKey) => (NativeMethods.User32.GetAsyncKeyState(virtualKey) & 1) != 0;
        return new LockKeysSnapshot(IsOn(NativeMethods.VK_CAPITAL), IsOn(NativeMethods.VK_NUMLOCK),
            IsOn(NativeMethods.VK_SCROLL));
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _running, 0);
        _pollTimer?.Dispose();
        _pollTimer = null;
        _tracker.ResetPressedKeys();

        var hook = _hook;
        _hook = null;
        if (hook is not null)
        {
            hook.KeyEvent -= OnKeyEvent;
            await hook.DisposeAsync().ConfigureAwait(false);
        }
    }
}
