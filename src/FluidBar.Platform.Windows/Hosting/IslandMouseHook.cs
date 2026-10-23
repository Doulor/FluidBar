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
    internal const int WmMouseMove = 0x0200;
    internal const int WmLButtonDown = 0x0201;
    internal const int WmRButtonDown = 0x0204;
    internal const int WmLButtonUp = 0x0202;
    internal const int WmMouseWheel = 0x020A;
    private readonly PlatformMessageHost _host;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly object _gate = new();
    private readonly List<Rect> _regions = [];

    // Watch-only rectangles: the hook forwards pointer movement from here but never consumes clicks,
    // so the island can reveal its preview rail when the pointer approaches (hover band) while the
    // desktop below keeps receiving every click.
    private readonly List<Rect> _watchRegions = [];
    private HookProc? _proc;
    private IntPtr _hook;
    private readonly GestureTracker _gesture = new();
    private int _disposed;
    private Timer? _watchdog;
    private long _lastEventTimestamp;
    private int _lastHookX;
    private int _lastHookY;

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

    /// <summary>
    /// Right button pressed on the island. Always consumed (never leaks to the window below) and fired
    /// even while the island yields, so a right-click can soften or restore it by hand.
    /// </summary>
    public event Action<float, float>? RightPressed;

    /// <summary>Pointer moved inside an island rectangle.</summary>
    public event Action<float, float>? PointerMoved;

    /// <summary>
    /// Every pointer move anywhere on the desktop (physical pixels). Used by the smart-yield engine to
    /// see approach trajectories; subscribers must stay cheap because this runs inside the hook callback.
    /// </summary>
    public event Action<int, int>? PointerMovedAnywhere;

    /// <summary>
    /// When true the hook stops consuming clicks and wheel deltas inside the island rectangles, so the
    /// desktop below receives them (the "yielding" state). Movement forwarding continues.
    /// </summary>
    public bool PassThrough { get; set; }

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

    /// <summary>
    /// Wheel delta in notches (+up / -down) inside an island rectangle. Fractional values are kept:
    /// precision touchpads report sub-120 deltas and truncating them swallowed the scroll entirely.
    /// </summary>
    public event Action<double>? Wheel;

    /// <summary>A left-button press that started outside every island rectangle (desktop/window click).</summary>
    public event Action? OutsidePressed;

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
        _lastHookX = cursor.X;
        _lastHookY = cursor.Y;
        _watchdog = new Timer(static state => ((IslandMouseHook)state!).CheckHealth(), this,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
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

        // Compare the cursor against the position the hook last reported, not against a sample
        // taken ten seconds ago. The old comparison fired whenever the user moved the mouse and
        // then stopped for >800 ms: the position had changed since the sample, but the hook was
        // perfectly healthy. Reinstalling a healthy hook every few seconds loses clicks.
        var movedSinceLastHook = cursor.X != _lastHookX || cursor.Y != _lastHookY;
        var idleMs = (Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastEventTimestamp))
            * 1000.0 / Stopwatch.Frequency;
        if (movedSinceLastHook && idleMs > 800)
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
                _gesture.Reset();
                GetCursorPos(out var cursor);
                _lastHookX = cursor.X;
                _lastHookY = cursor.Y;
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
    public void SetRegions(IReadOnlyList<Rect> regions) => Replace(_regions, regions);

    /// <summary>
    /// Replaces the screen-pixel rectangles that only watch the pointer. Movement inside them is
    /// forwarded (<see cref="PointerMoved"/>) but clicks and wheel deltas are passed to the desktop,
    /// so a hover band below the island never steals input.
    /// </summary>
    public void SetWatchRegions(IReadOnlyList<Rect> regions) => Replace(_watchRegions, regions);

    private void Replace(List<Rect> target, IReadOnlyList<Rect> regions)
    {
        lock (_gate)
        {
            target.Clear();
            foreach (var region in regions)
            {
                if (region.Width > 0 && region.Height > 0)
                {
                    target.Add(region);
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
        _lastHookX = x;
        _lastHookY = y;
        var message = (int)wParam;
        var wheel = message == WmMouseWheel
            ? unchecked((short)((long)data.mouseData >> 16)) / 120.0
            : 0;
        if (message == WmRButtonDown && IsInsideInteractive(x, y))
        {
            // Right-click is the manual soften/restore gesture: always ours, never passed through.
            RightPressed?.Invoke(x, y);
            return new IntPtr(1);
        }

        var route = _gesture.Route(ClassifyLocked(x, y), message, wheel);

        // Deliver in the same order the old inline switch did. A press inside/release outside now
        // always produces Released + Left, so the host can end its drag instead of leaving a phantom
        // pressed state behind.
        if (route.Pressed)
        {
            PointerPressed?.Invoke(x, y);
        }

        if (route.Released)
        {
            PointerReleased?.Invoke(x, y);
        }

        if (route.Dragged)
        {
            PointerDragged?.Invoke(x, y);
        }

        if (route.Moved)
        {
            PointerMoved?.Invoke(x, y);
        }

        if (message == WmMouseMove && PointerMovedAnywhere is { } anywhere)
        {
            anywhere(x, y);
        }

        if (route.WheelNotches is { } notches)
        {
            Wheel?.Invoke(notches);
        }

        if (route.OutsidePressed)
        {
            OutsidePressed?.Invoke();
        }

        if (route.Left)
        {
            PointerLeft?.Invoke();
        }

        return route.Consume
            ? new IntPtr(1)
            : CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private bool IsInsideInteractive(int x, int y)
    {
        lock (_gate)
        {
            return Classify(_regions, _watchRegions, x, y) == HookRegionKind.Interactive;
        }
    }

    private HookRegionKind ClassifyLocked(int x, int y)
    {
        if (PassThrough)
        {
            // Yielding: the island is visible but must not take any input.
            return HookRegionKind.Outside;
        }

        lock (_gate)
        {
            return Classify(_regions, _watchRegions, x, y);
        }
    }

    /// <summary>
    /// Classifies a screen point for the hook: interactive rectangles consume clicks and wheel deltas,
    /// watch-only rectangles only forward pointer movement (the hover band below the island), and the
    /// rest is desktop. Half-open edges keep neighbouring rectangles from overlapping.
    /// </summary>
    /// <param name="interactive">Rectangles that consume input.</param>
    /// <param name="watch">Rectangles that only forward movement.</param>
    /// <param name="x">Screen X in physical pixels.</param>
    /// <param name="y">Screen Y in physical pixels.</param>
    internal static HookRegionKind Classify(IReadOnlyList<Rect> interactive, IReadOnlyList<Rect> watch, int x, int y)
    {
        foreach (var region in interactive)
        {
            if (x >= region.Left && x < region.Right && y >= region.Top && y < region.Bottom)
            {
                return HookRegionKind.Interactive;
            }
        }

        foreach (var region in watch)
        {
            if (x >= region.Left && x < region.Right && y >= region.Top && y < region.Bottom)
            {
                return HookRegionKind.Watch;
            }
        }

        return HookRegionKind.Outside;
    }

    /// <summary>
    /// Pure gesture state machine for the low-level hook. Keeping press/inside transitions here (instead
    /// of inside the Win32 callback) makes the tricky cases testable without installing a hook:
    /// pressed-inside/released-outside, press starting on the desktop, and watch-band drags.
    /// </summary>
    internal sealed class GestureTracker
    {
        private bool _inside;
        private bool _pressed;

        /// <summary>Whether the pointer is currently over an interactive or watch rectangle.</summary>
        public bool IsInside => _inside;

        /// <summary>Whether a left-button press that started inside the island is still down.</summary>
        public bool IsPressed => _pressed;

        /// <summary>Clears all state (used when the hook is reinstalled).</summary>
        public void Reset()
        {
            _inside = false;
            _pressed = false;
        }

        /// <summary>Routes one hook message; the caller invokes the events the returned route names.</summary>
        public GestureRoute Route(HookRegionKind kind, int message, double wheelNotches = 0)
        {
            var interactive = kind == HookRegionKind.Interactive;
            var watching = kind == HookRegionKind.Watch;
            if (!interactive && !watching)
            {
                var left = _inside;
                _inside = false;

                if (message == WmLButtonDown)
                {
                    // A press that starts on the desktop is never the island's; clear any stale state
                    // left by an interrupted gesture before this one.
                    _pressed = false;
                    return new GestureRoute(Left: left, OutsidePressed: true);
                }

                if (message == WmLButtonUp && _pressed)
                {
                    // Pressed inside the island, released outside it. Deliver the release: otherwise
                    // the host keeps its drag flag and the next plain move becomes a phantom drag.
                    _pressed = false;
                    return new GestureRoute(Released: true, Left: left);
                }

                return new GestureRoute(Left: left);
            }

            _inside = true;
            if (!interactive)
            {
                // Watch-only band: let the desktop keep the click, but feed the island's hover state.
                // A drag that started inside the island keeps tracking and a release clears it here.
                if (message == WmMouseMove)
                {
                    return new GestureRoute(Dragged: _pressed, Moved: true);
                }

                if (message == WmLButtonDown)
                {
                    _pressed = false;
                    return new GestureRoute();
                }

                if (message == WmLButtonUp && _pressed)
                {
                    _pressed = false;
                    return new GestureRoute(Released: true);
                }

                return new GestureRoute();
            }

            switch (message)
            {
                case WmLButtonDown:
                    _pressed = true;
                    return new GestureRoute(Consume: true, Pressed: true);
                case WmLButtonUp:
                    _pressed = false;
                    return new GestureRoute(Consume: true, Released: true);
                case WmMouseMove:
                    return new GestureRoute(Dragged: _pressed, Moved: true);
                case WmMouseWheel:
                    return new GestureRoute(Consume: true, WheelNotches: wheelNotches);
                default:
                    return new GestureRoute();
            }
        }
    }

    /// <summary>One route decision for a hook message.</summary>
    internal readonly record struct GestureRoute(
        bool Consume = false,
        bool Pressed = false,
        bool Released = false,
        bool Moved = false,
        bool Dragged = false,
        bool Left = false,
        double? WheelNotches = null,
        bool OutsidePressed = false);

    /// <summary>How one screen point is routed by the hook.</summary>
    internal enum HookRegionKind
    {
        /// <summary>Desktop: the hook passes everything through.</summary>
        Outside,

        /// <summary>Island chrome: clicks, wheel and moves are delivered to the island.</summary>
        Interactive,

        /// <summary>Hover band: only movement is delivered; clicks and wheel stay with the desktop.</summary>
        Watch,
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
