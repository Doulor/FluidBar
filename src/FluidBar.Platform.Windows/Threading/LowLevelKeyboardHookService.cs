using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Threading;

/// <summary>One low-level keyboard event, delivered off the hook thread.</summary>
/// <param name="VirtualKey">Virtual-key code.</param>
/// <param name="IsKeyDown">True for key-down events.</param>
/// <param name="IsSystemKey">True for Alt-combination (WM_SYSKEY*) events.</param>
/// <param name="IsInjected">True when the event was synthesized (<c>LLKHF_INJECTED</c>).</param>
public readonly record struct LowLevelKeyEvent(int VirtualKey, bool IsKeyDown, bool IsSystemKey, bool IsInjected);

/// <summary>
/// <c>WH_KEYBOARD_LL</c> hook service used for lock-key state tracking and hold-to-hide.
/// </summary>
/// <remarks>
/// <para>Installed only while explicitly started (the design forbids a permanent global keyboard hook).
/// The hook never swallows keys: it always calls <c>CallNextHookEx</c> and never injects input.</para>
/// <para>Callbacks are dispatched to the thread pool; subscribers must be fast.</para>
/// </remarks>
public sealed class LowLevelKeyboardHookService : IAsyncDisposable
{
    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLKHF_ALTDOWN = 0x20;

    private readonly PlatformMessageHost _host;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private SafeHookHandle? _hook;
    private int _disposed;

    public LowLevelKeyboardHookService(PlatformMessageHost host, IPlatformLogger logger, PlatformCounters counters)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _proc = HookCallback;
    }

    /// <summary>Raised for every key event while the hook is installed. Raised on a thread-pool thread.</summary>
    public event EventHandler<LowLevelKeyEvent>? KeyEvent;

    /// <summary>Whether the hook is currently installed.</summary>
    public bool IsRunning => _hook is { IsInvalid: false };

    /// <summary>Installs the hook on the platform host thread (needs a message loop).</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (IsRunning)
        {
            return;
        }

        var module = NativeMethods.Kernel32.GetModuleHandleW(null);
        _hook = await _host.InvokeAsync(() => SafeHookHandle.Create(_proc, module), cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInfo(PlatformLogCategory.Shell, "keyboard-hook.started", "Low-level keyboard hook installed.");
    }

    /// <summary>Removes the hook. Idempotent and safe from any thread.</summary>
    public Task StopAsync()
    {
        var hook = _hook;
        _hook = null;
        if (hook is null)
        {
            return Task.CompletedTask;
        }

        hook.Dispose();
        _logger.LogInfo(PlatformLogCategory.Shell, "keyboard-hook.stopped", "Low-level keyboard hook removed.");
        return Task.CompletedTask;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode == NativeMethods.HC_ACTION && wParam.ToInt64() is
                NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var isKeyDown = wParam.ToInt64() is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                var isSystemKey = wParam.ToInt64() is NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_SYSKEYUP;
                var keyEvent = new LowLevelKeyEvent((int)info.vkCode, isKeyDown, isSystemKey,
                    (info.flags & LLKHF_INJECTED) != 0);
                DispatchKeyEvent(keyEvent);
            }
        }
        catch (Exception ex)
        {
            _counters.IncrementMonitorCallbackFailures();
            _logger.LogError(PlatformLogCategory.Shell, "keyboard-hook.callback-failed",
                "Low-level keyboard hook callback failed.", ex);
        }

        return NativeMethods.User32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void DispatchKeyEvent(LowLevelKeyEvent keyEvent)
    {
        var handler = KeyEvent;
        if (handler is null)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(static state =>
        {
            var (self, h, e) = ((LowLevelKeyboardHookService, EventHandler<LowLevelKeyEvent>, LowLevelKeyEvent))state!;
            try
            {
                h(self, e);
            }
            catch (Exception ex)
            {
                self._counters.IncrementMonitorCallbackFailures();
                self._logger.LogError(PlatformLogCategory.Shell, "keyboard-hook.handler-failed",
                    "A keyboard hook subscriber threw.", ex);
            }
        }, (this, handler, keyEvent));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
    }
}
