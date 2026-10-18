using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Monitors;

/// <summary>Immutable input-method snapshot.</summary>
/// <param name="IsChineseMode">True when the active IME is in native (Chinese) input mode.</param>
/// <param name="IsImeAvailable">True when the foreground window has a Chinese IME associated.</param>
/// <param name="KeyboardLayoutId">Low 16 bits of the foreground thread's HKL (language id).</param>
public sealed record InputMethodSnapshot(bool IsChineseMode, bool IsImeAvailable, ushort KeyboardLayoutId);

/// <summary>Pure mapping of HKL language ids to Chinese-IME detection.</summary>
public static class ImeLayoutMapper
{
    /// <summary>True for Simplified/Traditional Chinese and the Chinese IME regional variants.</summary>
    public static bool IsChineseLayout(ushort languageId) => languageId is
        0x0804 or // zh-CN
        0x0404 or // zh-TW
        0x0C04 or // zh-HK
        0x1004 or // zh-SG
        0x1404;   // zh-MO

    /// <summary>Extracts the language id (low word) from an HKL.</summary>
    public static ushort LanguageIdFromLayout(IntPtr hkl) => (ushort)(hkl.ToInt64() & 0xFFFF);
}

/// <summary>Pure checks for IMM conversion-mode bits.</summary>
public static class ImeConversionMode
{
    /// <summary><c>IME_CMODE_NATIVE</c>: the IME is in native (Chinese) mode.</summary>
    public const int NativeMode = 0x0001;

    public static bool HasNativeMode(int conversionMode) => (conversionMode & NativeMode) != 0;
}

/// <summary>Decision inputs for publishing an IME state change.</summary>
/// <param name="IsInitialized">False until the monitor has a baseline for the current foreground window.</param>
/// <param name="SameForegroundWindow">True when the foreground window did not change since the baseline.</param>
/// <param name="StateChanged">True when the Chinese/English mode differs from the baseline.</param>
public readonly record struct ImeTransition(bool IsInitialized, bool SameForegroundWindow, bool StateChanged);

/// <summary>
/// Pure publish policy: never show the island merely because the user switched windows
/// (v1 false-positive), only when the IME mode changes inside the same window.
/// </summary>
public static class ImeChangePolicy
{
    public static bool ShouldPublish(in ImeTransition transition)
        => transition.IsInitialized && transition.SameForegroundWindow && transition.StateChanged;
}

/// <summary>
/// Event-driven input-method monitor.
/// </summary>
/// <remarks>
/// <para>Events: <c>EVENT_SYSTEM_FOREGROUND</c> (window switch, 1 s debounce) and
/// <c>EVENT_OBJECT_IME_*</c> (mode change inside the window, 300 ms debounce). No polling.</para>
/// <para>Query: <c>GetKeyboardLayout</c> plus <c>SendMessageTimeout(WM_IME_CONTROL, 50 ms,
/// SMTO_ABORTIFHUNG)</c> — a hung application cannot freeze the island (v1 F-14).</para>
/// </remarks>
public sealed class InputMethodMonitor : PlatformMonitor<InputMethodSnapshot>
{
    private static readonly TimeSpan ForegroundDebounce = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ImeChangeDebounce = TimeSpan.FromMilliseconds(300);
    private const uint ImeQueryTimeoutMs = 50;

    private readonly PlatformMessageHost _host;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly TimeProvider _timeProvider;
    private readonly object _debounceGate = new();
    private PlatformWindow? _window;
    private SafeWinEventHookHandle? _foregroundHook;
    private SafeWinEventHookHandle? _imeHook;
    private NativeMethods.WinEventProc? _foregroundProc;
    private NativeMethods.WinEventProc? _imeProc;
    private ITimer? _debounceTimer;
    private IntPtr _foregroundWindow;
    private InputMethodSnapshot? _baseline;
    private int _running;

    public InputMethodMonitor(PlatformMessageHost host, IPlatformLogger logger, PlatformCounters counters,
        TimeProvider? timeProvider = null)
        : base("inputmethod", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Last observed state for the current foreground window (not necessarily published).</summary>
    public InputMethodSnapshot? Baseline => Volatile.Read(ref _baseline);

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _running, 1);
        _window = await _host.CreateWindowAsync("inputmethod", PlatformWindowKind.HiddenTopLevel,
            static (_, _, _, _) => null, cancellationToken).ConfigureAwait(false);

        var foregroundProc = new NativeMethods.WinEventProc(OnForegroundChanged);
        var imeProc = new NativeMethods.WinEventProc(OnImeChanged);
        _foregroundProc = foregroundProc;
        _imeProc = imeProc;
        (_foregroundHook, _imeHook) = await _host.InvokeAsync(() =>
        {
            var fg = SafeWinEventHookHandle.Create(foregroundProc, NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND);
            var ime = SafeWinEventHookHandle.Create(imeProc, NativeMethods.EVENT_OBJECT_IME_SHOW,
                NativeMethods.EVENT_OBJECT_IME_CHANGE);
            return (fg, ime);
        }, cancellationToken).ConfigureAwait(false);

        // Establish the baseline silently: switching to the app must not emit an IME activity.
        var initial = await Task.Run(TryQuery, cancellationToken).ConfigureAwait(false);
        if (initial is not null)
        {
            Interlocked.Exchange(ref _baseline, initial);
            _foregroundWindow = NativeMethods.User32.GetForegroundWindow();
        }

        // Keep the hook delegates reachable for the lifetime of the hooks.
        GC.KeepAlive(foregroundProc);
        GC.KeepAlive(imeProc);
        _logger.LogInfo(PlatformLogCategory.Monitor, "inputmethod.started",
            "Input-method monitor started (event-driven).");    }

    private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (Volatile.Read(ref _running) == 0)
        {
            return;
        }

        ScheduleRefresh(ForegroundDebounce);
    }

    private void OnImeChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (Volatile.Read(ref _running) == 0)
        {
            return;
        }

        ScheduleRefresh(ImeChangeDebounce);
    }

    private void ScheduleRefresh(TimeSpan delay)
    {
        lock (_debounceGate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = _timeProvider.CreateTimer(static state =>
            {
                var self = (InputMethodMonitor)state!;
                _ = self.RefreshSafeAsync();
            }, this, delay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RefreshSafeAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _counters.IncrementMonitorCallbackFailures();
            _logger.LogError(PlatformLogCategory.Monitor, "inputmethod.refresh-failed",
                "Input-method refresh failed.", ex);
        }
    }

    /// <summary>Queries the current IME state off-thread and publishes only genuine mode changes.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await Task.Run(TryQuery, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return;
        }

        var foreground = NativeMethods.User32.GetForegroundWindow();
        var baseline = Volatile.Read(ref _baseline);
        var sameWindow = foreground == _foregroundWindow;

        if (baseline is null || !sameWindow)
        {
            // Window changed: reset the baseline silently (v1 false-positive fix).
            Interlocked.Exchange(ref _baseline, snapshot);
            _foregroundWindow = foreground;
            return;
        }

        var transition = new ImeTransition(true, true, snapshot.IsChineseMode != baseline.IsChineseMode);
        Interlocked.Exchange(ref _baseline, snapshot);
        if (ImeChangePolicy.ShouldPublish(transition))
        {
            Publish(snapshot);
        }
    }

    private InputMethodSnapshot? TryQuery()
    {
        try
        {
            var foreground = NativeMethods.User32.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return null;
            }

            var threadId = NativeMethods.User32.GetWindowThreadProcessId(foreground, out _);
            var hkl = NativeMethods.User32.GetKeyboardLayout(threadId);
            var languageId = ImeLayoutMapper.LanguageIdFromLayout(hkl);
            if (!ImeLayoutMapper.IsChineseLayout(languageId))
            {
                return new InputMethodSnapshot(false, false, languageId);
            }

            var imeWindow = NativeMethods.Imm32.ImmGetDefaultIMEWnd(foreground);
            if (imeWindow == IntPtr.Zero)
            {
                return new InputMethodSnapshot(false, false, languageId);
            }

            var openStatus = NativeMethods.User32.SendMessageTimeoutW(imeWindow, NativeMethods.WM_IME_CONTROL,
                (IntPtr)NativeMethods.IMC_GETOPENSTATUS, IntPtr.Zero,
                NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_BLOCK, ImeQueryTimeoutMs, out var openResult);
            if (openStatus == IntPtr.Zero)
            {
                // Hung target application: keep the previous state instead of lying or blocking.
                _logger.LogDebug(PlatformLogCategory.Monitor, "inputmethod.query-timeout",
                    "SendMessageTimeout(IMC_GETOPENSTATUS) timed out; keeping previous IME state.");
                return Baseline;
            }

            if (openResult == IntPtr.Zero)
            {
                // IME is closed (English mode on a Chinese layout).
                return new InputMethodSnapshot(false, true, languageId);
            }

            var conversionStatus = NativeMethods.User32.SendMessageTimeoutW(imeWindow,
                NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero,
                NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_BLOCK, ImeQueryTimeoutMs, out var conversion);
            if (conversionStatus == IntPtr.Zero)
            {
                return Baseline;
            }

            return new InputMethodSnapshot(ImeConversionMode.HasNativeMode(conversion.ToInt32()), true, languageId);
        }
        catch (Exception ex)
        {
            _counters.IncrementMonitorCallbackFailures();
            _logger.LogWarning(PlatformLogCategory.Monitor, "inputmethod.query-failed",
                "Input-method query failed.", ex);
            return null;
        }
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _running, 0);
        lock (_debounceGate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        var foregroundHook = _foregroundHook;
        var imeHook = _imeHook;
        _foregroundHook = null;
        _imeHook = null;
        if (foregroundHook is not null || imeHook is not null)
        {
            try
            {
                await _host.InvokeAsync(() =>
                {
                    foregroundHook?.Dispose();
                    imeHook?.Dispose();
                    return true;
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(PlatformLogCategory.Monitor, "inputmethod.unhook-failed",
                    "Failed to remove input-method WinEvent hooks.", ex);
            }
        }

        var window = _window;
        _window = null;
        if (window is not null)
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }

        Interlocked.Exchange(ref _baseline, null);
        _foregroundWindow = IntPtr.Zero;
    }
}
