using System.Diagnostics;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// Low-level mouse hook (WH_MOUSE_LL) used as the deterministic input path for the layered island.
/// Layered windows are unreliable hit-test targets on some DPI/composition paths; the hook sees the
/// pointer before any window and consumes clicks inside the island's screen rectangles, then forwards
/// them to the App. Outside those rectangles it always calls the next hook, so click-through is kept.
/// </summary>
public sealed class IslandMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseWheel = 0x020A;
    private readonly PlatformMessageHost _host;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly object _gate = new();
    private readonly List<Rect> _regions = [];
    private HookProc? _proc;
    private IntPtr _hook;
    private bool _inside;
    private bool _pressed;
    private int _disposed;
    private Timer? _watchdog;
    private long _lastEventTimestamp;
    private int _cursorX;
    private int _cursorY;

    /// <summary>Creates the hook; call <see cref="StartAsync"/> once.</summary>
    public IslandMouseHook(PlatformMessageHost host, IPlatformLogger logger, PlatformCounters counters)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
    }

    /// <summary>Pointer pressed inside an island rectangle (screen physical pixels).</summary>
    public event Action<float, float>? PointerPressed;

    /// <summary>Pointer released inside an island rectangle.</summary>
    public event Action<float, float>? PointerReleased;

    /// <summary>Pointer moved inside an island rectangle.</summary>
    public event Action<float, float>? PointerMoved;

    /// <summary>
    /// Pointer moved inside an island rectangle with the left button held. Layered windows only get a
    /// drag when they receive the press and capture the mouse; the hook consumes the press, so the
    /// host rebuilds the drag gesture from these moves.
    /// </summary>
    public event Action<float, float>? PointerDragged;

    /// <summary>
    /// Pointer moved from inside an island rectangle to outside it. The hook is the only input path
    /// that sees the pointer leave a layered island (the window itself may never receive
    /// <c>WM_MOUSELEAVE</c> when hit-testing is unreliable), so hover/auto-collapse depends on it.
    /// </summary>
    public event Action? PointerLeft;

    /// <summary>Wheel notch (+up / -down) inside an island rectangle.</summary>
    public event Action<int>? Wheel;

    /// <summary>True when the hook is installed.</summary>
    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>Installs the hook on the platform message thread.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _proc = HookCallback;
        _hook = await _host.InvokeAsync(
            () => SetWindowsHookExW(WhMouseLl, _proc, GetModuleHandleW(null), 0),
            cancellationToken).ConfigureAwait(false);
        if (_hook == IntPtr.Zero)
        {
            _logger.LogWarning(PlatformLogCategory.Platform, "island.mouse-hook-failed",
                "SetWindowsHookEx(WH_MOUSE_LL) failed: " + Marshal.GetLastWin32Error());
            return;
        }

        // Windows silently removes a low-level hook whose callback misses its timeout (a busy message
        // thread is enough). The island must never end up deaf, so a watchdog compares the real cursor
        // position with the last callback: if the pointer moved but no callback arrived, the hook was
        // dropped and is reinstalled on the host thread.
        _lastEventTimestamp = Stopwatch.GetTimestamp();
        GetCursorPos(out var cursor);
        _cursorX = cursor.X;
        _cursorY = cursor.Y;
        _watchdog = new Timer(static state => ((IslandMouseHook)state!).CheckHealth(), this,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void CheckHealth()
    {
        if (Volatile.Read(ref _disposed) != 0 || _hook == IntPtr.Zero)
        {
            return;
        }

        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var moved = cursor.X != _cursorX || cursor.Y != _cursorY;
        _cursorX = cursor.X;
        _cursorY = cursor.Y;
        var idleMs = (Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastEventTimestamp))
            * 1000.0 / Stopwatch.Frequency;
        if (moved && idleMs > 800)
        {
            Reinstall("the pointer moved without any hook callback");
        }
    }

    private void Reinstall(string reason)
    {
        _ = ReinstallAsync(reason);
    }

    private async Task ReinstallAsync(string reason)
    {
        try
        {
            await _host.InvokeAsync(() =>
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                var previous = _hook;
                if (previous != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(previous);
                    _hook = IntPtr.Zero;
                }

                _proc ??= HookCallback;
                var reinstalled = SetWindowsHookExW(WhMouseLl, _proc, GetModuleHandleW(null), 0);
                if (reinstalled == IntPtr.Zero)
                {
                    _logger.LogWarning(PlatformLogCategory.Platform, "island.mouse-hook-reinstall-failed",
                        "Reinstalling WH_MOUSE_LL failed: " + Marshal.GetLastWin32Error());
                    return;
                }

                if (Volatile.Read(ref _disposed) != 0)
                {
                    UnhookWindowsHookEx(reinstalled);
                    return;
                }

                _hook = reinstalled;
                Interlocked.Exchange(ref _lastEventTimestamp, Stopwatch.GetTimestamp());
                _logger.LogWarning(PlatformLogCategory.Platform, "island.mouse-hook-reinstalled",
                    "Reinstalled WH_MOUSE_LL because " + reason + ".");
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Platform, "island.mouse-hook-reinstall-failed",
                "Reinstalling WH_MOUSE_LL failed: " + ex.Message);
        }
    }

    /// <summary>Replaces the screen-pixel rectangles that should consume mouse input.</summary>
    public void SetRegions(IReadOnlyList<Rect> regions)
    {
        lock (_gate)
        {
            _regions.Clear();
            foreach (var region in regions)
            {
                if (region.Width > 0 && region.Height > 0)
                {
                    _regions.Add(region);
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _watchdog?.Dispose();
        _watchdog = null;
        var hook = _hook;
        _hook = IntPtr.Zero;
        if (hook != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(hook); } catch { }
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || _disposed != 0)
        {
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        Interlocked.Exchange(ref _lastEventTimestamp, Stopwatch.GetTimestamp());
        var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
        var x = data.pt.X;
        var y = data.pt.Y;
        var inside = Contains(x, y);
        if (!inside)
        {
            if (_inside)
            {
                _inside = false;
                PointerLeft?.Invoke();
            }

            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        _inside = true;
        switch ((int)wParam)
        {
            case WmLButtonDown:
                _pressed = true;
                PointerPressed?.Invoke(x, y);
                return new IntPtr(1);
            case WmLButtonUp:
                _pressed = false;
                PointerReleased?.Invoke(x, y);
                return new IntPtr(1);
            case WmMouseMove:
                if (_pressed)
                {
                    PointerDragged?.Invoke(x, y);
                }

                PointerMoved?.Invoke(x, y);
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            case WmMouseWheel:
                Wheel?.Invoke(unchecked((short)((long)data.mouseData >> 16)) / 120);
                return new IntPtr(1);
            default:
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }
    }

    private bool Contains(int x, int y)
    {
        lock (_gate)
        {
            foreach (var region in _regions)
            {
                if (x >= region.Left && x < region.Right && y >= region.Top && y < region.Bottom)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Screen-pixel rectangle consumed by the hook.</summary>
    public readonly record struct Rect(double Left, double Top, double Right, double Bottom)
    {
        public double Width => Right - Left;
        public double Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int hookId, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}
