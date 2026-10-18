using System.Collections.Concurrent;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Threading;

/// <summary>Kind of native window hosted by <see cref="PlatformMessageHost"/>.</summary>
public enum PlatformWindowKind
{
    /// <summary>
    /// Message-only window (<c>HWND_MESSAGE</c>): receives directed messages such as
    /// <c>WM_HOTKEY</c>, <c>WM_CLIPBOARDUPDATE</c>, tray callbacks and device notifications.
    /// Does <b>not</b> receive broadcast messages.
    /// </summary>
    MessageOnly,

    /// <summary>
    /// Zero-size, never-shown top-level tool window. Receives broadcast messages
    /// (<c>WM_SETTINGCHANGE</c>, <c>WM_DISPLAYCHANGE</c>, <c>WM_POWERBROADCAST</c>,
    /// <c>WM_DEVICECHANGE</c>, <c>WM_WTSSESSION_CHANGE</c>) in addition to directed ones.
    /// </summary>
    HiddenTopLevel,
}

/// <summary>Callback invoked on the host thread for a native window message. Return <c>null</c> to defer to DefWindowProc.</summary>
public delegate IntPtr? PlatformWindowMessageHandler(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

/// <summary>
/// A native window owned by a <see cref="PlatformMessageHost"/>. Must be destroyed through the host
/// (windows can only be destroyed by the thread that created them), which is what
/// <see cref="DisposeAsync"/> does.
/// </summary>
public sealed class PlatformWindow : IAsyncDisposable
{
    private readonly PlatformMessageHost _host;
    private int _disposed;

    internal PlatformWindow(PlatformMessageHost host, IntPtr handle, string name)
    {
        _host = host;
        Handle = handle;
        Name = name;
    }

    /// <summary>Native HWND. Valid until disposal.</summary>
    public IntPtr Handle { get; }

    /// <summary>Diagnostic name ("tray", "clipboard", …).</summary>
    public string Name { get; }

    /// <summary>Posts a message to this window from any thread.</summary>
    public void PostMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        if (Volatile.Read(ref _disposed) == 0 && Handle != IntPtr.Zero)
        {
            NativeMethods.User32.PostMessageW(Handle, message, wParam, lParam);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        return _host.DestroyWindowAsync(this);
    }
}

/// <summary>
/// Owns one dedicated STA background thread that pumps Win32 messages for every platform window
/// (hotkeys, tray, clipboard, power/session/device notifications, WinEvent hooks).
/// </summary>
/// <remarks>
/// <para>Everything runs off the UI thread by construction; the host thread never performs blocking
/// I/O of its own — handlers are expected to hand heavy work to the thread pool.</para>
/// <para>Lifecycle: <see cref="StartAsync"/> → <see cref="CreateWindowAsync"/> × n →
/// <see cref="DisposeAsync"/>. Dispose posts a shutdown action, destroys the windows on the host
/// thread, quits the pump and awaits thread completion (bounded, never blocks the caller).</para>
/// </remarks>
public sealed class PlatformMessageHost : IAsyncDisposable
{
    private const uint WM_APP_RUN_ACTION = NativeMethods.WM_APP + 1;
    private const uint WM_APP_SHUTDOWN = NativeMethods.WM_APP + 2;

    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly ConcurrentQueue<Action> _pendingActions = new();
    private readonly ConcurrentDictionary<IntPtr, WindowEntry> _windows = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _className = $"FluidBar.Platform.MessageHost.{Guid.NewGuid():N}";
    private readonly NativeMethods.WindowProc _wndProcDelegate;

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _dispatcherWindow;
    private int _disposed;

    public PlatformMessageHost(IPlatformLogger logger, PlatformCounters counters)
    {
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _wndProcDelegate = WindowProc;
    }

    /// <summary>True when the caller is the host thread (callbacks run here).</summary>
    public bool IsHostThread => _threadId != 0 && NativeMethods.Kernel32.GetCurrentThreadId() == _threadId;

    /// <summary>Host thread id (0 before start).</summary>
    public uint ThreadId => _threadId;

    /// <summary>Starts the pump thread and waits (asynchronously) until it is ready. Idempotent.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_thread is not null)
        {
            await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _thread = new Thread(PumpThreadMain)
        {
            Name = "FluidBar.Platform.MessageHost",
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a native window on the host thread and registers its message handler.</summary>
    public async Task<PlatformWindow> CreateWindowAsync(string name, PlatformWindowKind kind,
        PlatformWindowMessageHandler handler, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        var handle = await InvokeAsync(() => CreateWindowCore(name, kind, handler), cancellationToken)
            .ConfigureAwait(false);
        return new PlatformWindow(this, handle, name);
    }

    private IntPtr CreateWindowCore(string name, PlatformWindowKind kind, PlatformWindowMessageHandler handler)
    {
        var hInstance = NativeMethods.Kernel32.GetModuleHandleW(null);
        var exStyle = 0;
        IntPtr parent;
        var style = 0;
        if (kind == PlatformWindowKind.MessageOnly)
        {
            parent = NativeMethods.HWND_MESSAGE;
        }
        else
        {
            parent = IntPtr.Zero;
            style = NativeMethods.WS_POPUP;
            exStyle = NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        }

        var handle = NativeMethods.User32.CreateWindowExW((uint)exStyle, _className, name, style,
            0, 0, 0, 0, parent, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            var error = Win32Error.LastError;
            _counters.IncrementWin32Failures();
            throw Win32Error.CreateException($"CreateWindowEx('{name}')", error);
        }

        _windows[handle] = new WindowEntry(name, handler);
        _logger.LogDebug(PlatformLogCategory.Platform, "messagehost.window-created", "Native window created.",
            new Dictionary<string, object?> { ["name"] = name, ["kind"] = kind.ToString(), ["hwnd"] = $"0x{handle:X}" });
        return handle;
    }

    /// <summary>Queues an action for the host thread. Fire-and-forget; exceptions are logged.</summary>
    public void Post(Action action)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _pendingActions.Enqueue(action);
        if (_dispatcherWindow != IntPtr.Zero)
        {
            NativeMethods.User32.PostMessageW(_dispatcherWindow, WM_APP_RUN_ACTION, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Executes <paramref name="action"/> on the host thread and returns its result.</summary>
    public async Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsHostThread)
        {
            return action();
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0 || _thread is null, this);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Executes <paramref name="action"/> on the host thread.</summary>
    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return InvokeAsync(() =>
        {
            action();
            return true;
        }, cancellationToken);
    }

    internal ValueTask DestroyWindowAsync(PlatformWindow window)
    {
        if (Volatile.Read(ref _disposed) != 0 || _thread is null || !_thread.IsAlive)
        {
            _windows.TryRemove(window.Handle, out _);
            return ValueTask.CompletedTask;
        }

        return new ValueTask(InvokeAsync(() => DestroyWindowCore(window.Handle), CancellationToken.None));
    }

    private bool DestroyWindowCore(IntPtr handle)
    {
        if (_windows.TryRemove(handle, out _) && NativeMethods.User32.IsWindow(handle))
        {
            NativeMethods.User32.DestroyWindow(handle);
        }

        return true;
    }

    private void PumpThreadMain()
    {
        _threadId = NativeMethods.Kernel32.GetCurrentThreadId();
        var comInitialized = false;
        try
        {
            var hr = NativeMethods.Ole32.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_APARTMENTTHREADED);
            comInitialized = hr is NativeMethods.S_OK or 1 /* S_FALSE */;
            if (hr == NativeMethods.RPC_E_CHANGED_MODE)
            {
                _logger.LogWarning(PlatformLogCategory.Platform, "messagehost.sta-rejected",
                    "Host thread could not enter STA; continuing in the existing apartment.");
            }

            if (!RegisterWindowClass())
            {
                _ready.TrySetException(new InvalidOperationException(
                    $"RegisterClassEx('{_className}') failed: {Win32Error.Format(Win32Error.LastError)}"));
                return;
            }

            _dispatcherWindow = NativeMethods.User32.CreateWindowExW(0, _className, "FluidBar.Platform.Dispatcher",
                0, 0, 0, 0, 0, NativeMethods.HWND_MESSAGE, IntPtr.Zero,
                NativeMethods.Kernel32.GetModuleHandleW(null), IntPtr.Zero);
            if (_dispatcherWindow == IntPtr.Zero)
            {
                _ready.TrySetException(new InvalidOperationException(
                    $"Could not create dispatcher window: {Win32Error.Format(Win32Error.LastError)}"));
                return;
            }

            _windows[_dispatcherWindow] = new WindowEntry("dispatcher", DispatcherWindowProc);
            _ready.TrySetResult();
            _logger.LogInfo(PlatformLogCategory.Platform, "messagehost.started", "Platform message host started.",
                new Dictionary<string, object?> { ["threadId"] = _threadId });

            while (true)
            {
                var result = NativeMethods.User32.GetMessageW(out var msg, IntPtr.Zero, 0, 0);
                if (result == 0)
                {
                    break; // WM_QUIT
                }

                if (result < 0)
                {
                    _counters.IncrementWin32Failures();
                    _logger.LogError(PlatformLogCategory.Platform, "messagehost.getmessage-failed",
                        $"GetMessage failed: {Win32Error.Format(Win32Error.LastError)}");
                    break;
                }

                NativeMethods.User32.TranslateMessage(ref msg);
                NativeMethods.User32.DispatchMessageW(ref msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(PlatformLogCategory.Platform, "messagehost.crashed",
                "Platform message host thread failed.", ex);
            _ready.TrySetException(ex);
        }
        finally
        {
            foreach (var handle in _windows.Keys)
            {
                if (NativeMethods.User32.IsWindow(handle))
                {
                    NativeMethods.User32.DestroyWindow(handle);
                }
            }

            _windows.Clear();
            NativeMethods.User32.UnregisterClassW(_className, NativeMethods.Kernel32.GetModuleHandleW(null));
            if (comInitialized)
            {
                NativeMethods.Ole32.CoUninitialize();
            }

            _shutdown.TrySetResult();
            _logger.LogInfo(PlatformLogCategory.Platform, "messagehost.stopped", "Platform message host stopped.");
        }
    }

    private bool RegisterWindowClass()
    {
        var wc = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = NativeMethods.Kernel32.GetModuleHandleW(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = IntPtr.Zero,
            lpszClassName = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(_className),
            hIconSm = IntPtr.Zero,
        };

        try
        {
            var atom = NativeMethods.User32.RegisterClassExW(ref wc);
            if (atom != 0)
            {
                return true;
            }

            return Win32Error.LastError == NativeMethods.ERROR_CLASS_ALREADY_EXISTS;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(wc.lpszClassName);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (_windows.TryGetValue(hwnd, out var entry))
            {
                var handled = entry.Handler(hwnd, message, wParam, lParam);
                if (handled.HasValue)
                {
                    return handled.Value;
                }
            }
        }
        catch (Exception ex)
        {
            _counters.IncrementMonitorCallbackFailures();
            _logger.LogError(PlatformLogCategory.Platform, "messagehost.handler-failed",
                $"Unhandled exception in window handler for hwnd 0x{hwnd:X}.", ex);
        }

        return NativeMethods.User32.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private IntPtr? DispatcherWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WM_APP_RUN_ACTION:
                while (_pendingActions.TryDequeue(out var action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        _counters.IncrementMonitorCallbackFailures();
                        _logger.LogError(PlatformLogCategory.Platform, "messagehost.action-failed",
                            "Unhandled exception in a posted host action.", ex);
                    }
                }

                return IntPtr.Zero;

            case WM_APP_SHUTDOWN:
                NativeMethods.User32.PostQuitMessage(0);
                return IntPtr.Zero;

            default:
                return null;
        }
    }

    /// <summary>Destroys all windows, quits the pump and awaits thread shutdown. Idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_thread is { IsAlive: true } && !IsHostThread)
        {
            try
            {
                NativeMethods.User32.PostMessageW(_dispatcherWindow, WM_APP_SHUTDOWN, IntPtr.Zero, IntPtr.Zero);
                await _shutdown.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                _thread.Join(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning(PlatformLogCategory.Platform, "messagehost.dispose-timeout",
                    "Platform message host did not shut down within the timeout.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(PlatformLogCategory.Platform, "messagehost.dispose-failed",
                    "Platform message host shutdown failed.", ex);
            }
        }
        else if (IsHostThread)
        {
            NativeMethods.User32.PostQuitMessage(0);
        }
    }

    private sealed record WindowEntry(string Name, PlatformWindowMessageHandler Handler);
}
