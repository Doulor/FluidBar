using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Policies;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// Squircle shell geometry in DIPs relative to the host window's client origin.
/// </summary>
/// <param name="XDip">Left edge.</param>
/// <param name="YDip">Top edge.</param>
/// <param name="WidthDip">Width.</param>
/// <param name="HeightDip">Height.</param>
/// <param name="CornerRadiusDip">Corner radius (the region uses <c>2×radius</c> as the ellipse diameter).</param>
public readonly record struct ShellGeometry(double XDip, double YDip, double WidthDip, double HeightDip,
    double CornerRadiusDip);

/// <summary>
/// Avalonia-free interop surface for the island host window. <c>FluidBar.App</c> is not allowed to
/// P/Invoke (spec §5.2), so every Win32 window operation the host needs at settle time lives here.
/// </summary>
/// <remarks>
/// <para>All methods operate on an existing HWND owned by the App (Avalonia creates it); none of them
/// animate window geometry. <see cref="PlaceOnMonitor"/> is the single DIP→physical placement call and
/// <see cref="UpdateWindowRegionOnSettle"/> is the only region update (never per frame).</para>
/// <para><see cref="RegisterHitTest"/> subclasses the window procedure via
/// <c>SetWindowLongPtr(GWLP_WNDPROC)</c>; call it on the UI thread after the native window exists.
/// The returned registration restores the previous procedure on dispose.</para>
/// </remarks>
public sealed class IslandWindowInterop : IDisposable
{
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly TimeProvider _timeProvider;
    private readonly object _hookGate = new();
    private readonly Dictionary<IntPtr, HitTestHook> _hooks = new();
    private long _lastTopmostAssertTimestamp;
    private int _disposed;

    public IslandWindowInterop(IPlatformLogger? logger = null, PlatformCounters? counters = null,
        TimeProvider? timeProvider = null)
    {
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Applies the island extended window styles: <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW |
    /// WS_EX_NOREDIRECTIONBITMAP</c> (composition-hosted, no taskbar entry, no focus stealing).
    /// </summary>
    public void ApplyIslandStyles(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("HWND must not be zero.", nameof(hwnd));
        }

        var exStyle = NativeMethods.User32.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        var desired = exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_NOREDIRECTIONBITMAP;
        if (desired == exStyle)
        {
            return;
        }

        NativeMethods.User32.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(desired));
        var error = Win32Error.LastError;
        if (error != 0)
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "interop.styles-failed",
                $"SetWindowLongPtr(GWL_EXSTYLE) failed. {Win32Error.Format(error)}");
        }
    }

    /// <summary>
    /// Toggles the whole-window click-through style. Enable only when no part of the window is
    /// interactive; per-point click-through is handled by <see cref="RegisterHitTest"/>.
    /// </summary>
    public void SetClickThrough(IntPtr hwnd, bool clickThrough)
    {
        var exStyle = NativeMethods.User32.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        var desired = clickThrough
            ? exStyle | NativeMethods.WS_EX_TRANSPARENT
            : exStyle & ~(long)NativeMethods.WS_EX_TRANSPARENT;
        if (desired == exStyle)
        {
            return;
        }

        NativeMethods.User32.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(desired));
        var error = Win32Error.LastError;
        if (error != 0)
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "interop.click-through-failed",
                $"SetWindowLongPtr(WS_EX_TRANSPARENT) failed. {Win32Error.Format(error)}");
        }
    }

    /// <summary>
    /// Temporarily clears <c>WS_EX_NOACTIVATE</c> so a text field can take keyboard focus.
    /// Callers must restore it on focus loss; the design forbids a permanently activatable island.
    /// </summary>
    public void SetNoActivate(IntPtr hwnd, bool enabled)
    {
        var exStyle = NativeMethods.User32.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        var desired = enabled
            ? exStyle | NativeMethods.WS_EX_NOACTIVATE
            : exStyle & ~(long)NativeMethods.WS_EX_NOACTIVATE;
        if (desired != exStyle)
        {
            NativeMethods.User32.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(desired));
            var error = Win32Error.LastError;
            if (error != 0)
            {
                _counters.IncrementWin32Failures();
                _logger.LogWarning(PlatformLogCategory.Platform, "interop.no-activate-failed",
                    $"SetWindowLongPtr(WS_EX_NOACTIVATE) failed. {Win32Error.Format(error)}");
            }
        }
    }

    /// <summary>
    /// Installs the <c>WM_NCHITTEST</c>/<c>WM_MOUSEACTIVATE</c> policy for the island window:
    /// <c>HTCLIENT</c> only where <paramref name="isInteractiveInClientPixels"/> is true,
    /// <c>HTTRANSPARENT</c> everywhere else, and <c>MA_NOACTIVATE</c> on mouse activation.
    /// </summary>
    /// <returns>Disposable registration that restores the original window procedure. Idempotent.</returns>
    public IDisposable RegisterHitTest(IntPtr hwnd, Func<int, int, bool> isInteractiveInClientPixels)
    {
        ArgumentNullException.ThrowIfNull(isInteractiveInClientPixels);
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("HWND must not be zero.", nameof(hwnd));
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lock (_hookGate)
        {
            if (_hooks.Remove(hwnd, out var existing))
            {
                existing.Dispose();
            }

            var hook = new HitTestHook(this, hwnd, isInteractiveInClientPixels);
            _hooks[hwnd] = hook;
            _logger.LogDebug(PlatformLogCategory.Platform, "interop.hittest-registered",
                "Hit-test hook installed.", new Dictionary<string, object?> { ["hwnd"] = $"0x{hwnd:X}" });
            return hook;
        }
    }

    /// <summary>True when a hit-test hook is active for the window.</summary>
    public bool IsHitTestRegistered(IntPtr hwnd)
    {
        lock (_hookGate)
        {
            return _hooks.ContainsKey(hwnd);
        }
    }

    /// <summary>Makes the window topmost without activating it (<c>SWP_NOACTIVATE</c>).</summary>
    public void SetTopmost(IntPtr hwnd)
    {
        if (!NativeMethods.User32.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE))
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "interop.topmost-failed",
                "SetWindowPos(HWND_TOPMOST) failed. " + Win32Error.Format(Win32Error.LastError));
            return;
        }

        _lastTopmostAssertTimestamp = _timeProvider.GetTimestamp();
    }

    /// <summary>
    /// Re-asserts topmost only when policy allows it: never above the Windows shell foreground window
    /// (Start menu/taskbar/task view/overflow/tooltips), never while suppressed by full-screen/presentation
    /// mode, and throttled to ≤1 Hz.
    /// </summary>
    /// <returns>True when the window was raised.</returns>
    public bool TryReassertTopmost(IntPtr hwnd)
    {
        var foregroundClass = GetForegroundWindowClass();
        var sinceLast = _lastTopmostAssertTimestamp == 0
            ? TimeSpan.MaxValue
            : _timeProvider.GetElapsedTime(_lastTopmostAssertTimestamp);

        var context = new TopmostContext(foregroundClass, IslandVisible: IsWindowVisible(hwnd),
            UserNotificationState: QueryUserNotificationState(), TimeSinceLastAssert: sinceLast);
        var decision = TopmostPolicy.Evaluate(context);
        if (decision != TopmostDecision.Reassert)
        {
            _logger.LogDebug(PlatformLogCategory.Platform, "interop.topmost-skipped",
                $"Topmost re-assert skipped: {decision}.",
                new Dictionary<string, object?>
                {
                    ["decision"] = decision.ToString(),
                    ["foregroundClass"] = foregroundClass,
                });
            return false;
        }

        if (!NativeMethods.User32.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE))
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "interop.topmost-failed",
                "SetWindowPos(HWND_TOPMOST) failed. " + Win32Error.Format(Win32Error.LastError));
            return false;
        }

        _lastTopmostAssertTimestamp = _timeProvider.GetTimestamp();
        return true;
    }

    /// <summary>
    /// Places the island window at an absolute physical-pixel rectangle in one <c>SetWindowPos</c>.
    /// No clamping: the padded host window (shell + 24 DIP bleed on both axes) is allowed to have a
    /// negative origin. This is the API the App host uses with Core's
    /// <c>WindowPlacementConverter.PlaceWindow</c> output; never called per frame.
    /// </summary>
    public void PlacePhysical(IntPtr hwnd, int xPx, int yPx, int widthPx, int heightPx)
    {
        if (widthPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPx), widthPx, "Window width must be positive.");
        }

        if (heightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightPx), heightPx, "Window height must be positive.");
        }

        if (!NativeMethods.User32.SetWindowPos(hwnd, IntPtr.Zero, xPx, yPx, widthPx, heightPx,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER))
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "interop.place-physical-failed",
                "SetWindowPos(PlacePhysical) failed. " + Win32Error.Format(Win32Error.LastError));
        }
    }

    /// <summary>
    /// Places the island window in one <c>SetWindowPos</c>: DIP size → physical pixels using the
    /// monitor scale, anchored inside the monitor work area, clamped so it never leaves the work area.
    /// Convenience API; the App host should prefer <see cref="PlacePhysical"/> with Core's converter.
    /// </summary>
    public void PlaceOnMonitor(IntPtr hwnd, MonitorInfo monitor, int widthDip, int heightDip, IslandAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var rect = DisplayMetrics.PlaceAtAnchor(monitor, anchor, widthDip, heightDip);
        if (!NativeMethods.User32.SetWindowPos(hwnd, IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER))
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "interop.place-failed",
                "SetWindowPos(placement) failed. " + Win32Error.Format(Win32Error.LastError));
        }
    }

    /// <summary>
    /// Applies a rounded window region from the final shell geometry. Called once when the spring
    /// settles (never per frame); keeps clicks outside the visible squircle with the window below.
    /// </summary>
    public void UpdateWindowRegionOnSettle(IntPtr hwnd, ShellGeometry geometry, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (geometry.WidthDip <= 0 || geometry.HeightDip <= 0)
        {
            return;
        }

        var scale = monitor.Scale;
        var left = (int)Math.Round(geometry.XDip * scale, MidpointRounding.AwayFromZero);
        var top = (int)Math.Round(geometry.YDip * scale, MidpointRounding.AwayFromZero);
        var right = left + (int)Math.Ceiling(geometry.WidthDip * scale);
        var bottom = top + (int)Math.Ceiling(geometry.HeightDip * scale);
        var radius = (int)Math.Round(Math.Max(0, geometry.CornerRadiusDip) * scale, MidpointRounding.AwayFromZero);
        var diameter = Math.Max(1, radius * 2);

        var region = NativeMethods.Gdi32.CreateRoundRectRgn(left, top, right, bottom, diameter, diameter);
        if (region == IntPtr.Zero)
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "interop.region-failed",
                "CreateRoundRectRgn failed. " + Win32Error.Format(Win32Error.LastError));
            return;
        }

        // On success the system owns the region; on failure we must delete it.
        if (NativeMethods.User32.SetWindowRgn(hwnd, region, bRedraw: false) == 0)
        {
            NativeMethods.Gdi32.DeleteObject(region);
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "interop.region-failed",
                "SetWindowRgn failed. " + Win32Error.Format(Win32Error.LastError));
        }
    }

    /// <summary>
    /// Core-layout overload: applies the rounded region from Core's <c>ShellGeometry</c>
    /// (work-area-relative DIP, per-corner radii) using the default host padding.
    /// </summary>
    public void UpdateWindowRegionOnSettle(IntPtr hwnd, FluidBar.Core.Layout.ShellGeometry geometry,
        FluidBar.Core.Layout.MonitorInfo monitor, double hostPaddingDip = Display.CoreLayoutConversions.DefaultHostPaddingDip)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(monitor);
        UpdateWindowRegionOnSettle(hwnd, geometry.ToHostingShellGeometry(hostPaddingDip), monitor.ToPlatform());
    }

    /// <summary>
    /// Core-layout overload: places the host with the work-area clamp/anchor convenience using Core's
    /// monitor + anchor types. Prefer <see cref="PlacePhysical"/> with Core's <c>WindowPlacement</c>.
    /// </summary>
    public void PlaceOnMonitor(IntPtr hwnd, FluidBar.Core.Layout.MonitorInfo monitor, int widthDip, int heightDip,
        FluidBar.Core.Layout.IslandAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        PlaceOnMonitor(hwnd, monitor.ToPlatform(), widthDip, heightDip, anchor.ToPlatform());
    }

    /// <summary>Class name of the current foreground window, or null when there is none.</summary>
    public static string? GetForegroundWindowClass()
    {
        var hwnd = NativeMethods.User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var buffer = new char[256];
        var length = NativeMethods.User32.GetClassNameW(hwnd, buffer, buffer.Length);
        return length <= 0 ? null : new string(buffer, 0, Math.Min(length, buffer.Length));
    }

    /// <summary>True when the shell owns the foreground (Start menu/taskbar/task view/tooltips/menus).</summary>
    public static bool IsShellForeground() => ShellWindowClasses.IsShellWindowClass(GetForegroundWindowClass());

    /// <summary>Queries <c>SHQueryUserNotificationState</c> for full-screen/presentation suppression.</summary>
    public static int? QueryUserNotificationState()
    {
        var result = NativeMethods.Shell32.SHQueryUserNotificationState(out var state);
        return result == 0 ? state : null;
    }

    private static bool IsWindowVisible(IntPtr hwnd)
        => hwnd != IntPtr.Zero && NativeMethods.User32.IsWindowVisible(hwnd);

    internal IPlatformLogger Logger => _logger;

    internal PlatformCounters Counters => _counters;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_hookGate)
        {
            foreach (var hook in _hooks.Values)
            {
                hook.Dispose();
            }

            _hooks.Clear();
        }
    }

    /// <summary>One window-procedure subclass; restores the original procedure on dispose.</summary>
    private sealed class HitTestHook : IDisposable
    {
        private readonly IslandWindowInterop _owner;
        private readonly IntPtr _hwnd;
        private readonly Func<int, int, bool> _isInteractive;
        private readonly NativeMethods.WndProcHookDelegate _hookDelegate;
        private readonly IntPtr _originalProc;
        private int _disposed;

        public HitTestHook(IslandWindowInterop owner, IntPtr hwnd, Func<int, int, bool> isInteractive)
        {
            _owner = owner;
            _hwnd = hwnd;
            _isInteractive = isInteractive;
            _hookDelegate = HookProc;
            var newProc = Marshal.GetFunctionPointerForDelegate(_hookDelegate);
            _originalProc = NativeMethods.User32.SetWindowLongPtr(hwnd, NativeMethods.GWLP_WNDPROC, newProc);
            if (_originalProc == IntPtr.Zero)
            {
                _owner._counters.IncrementWin32Failures();
                _owner._logger.LogWarning(PlatformLogCategory.Platform, "interop.hittest-install-failed",
                    "SetWindowLongPtr(GWLP_WNDPROC) failed. " + Win32Error.Format(Win32Error.LastError));
            }
        }

        private IntPtr HookProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                switch (message)
                {
                    case NativeMethods.WM_NCHITTEST:
                    {
                        var packed = lParam.ToInt64();
                        var screenPoint = new NativeMethods.POINT
                        {
                            X = unchecked((short)(packed & 0xFFFF)),
                            Y = unchecked((short)((packed >> 16) & 0xFFFF)),
                        };
                        if (!NativeMethods.User32.ScreenToClient(hwnd, ref screenPoint))
                        {
                            return new IntPtr(NativeMethods.HTTRANSPARENT);
                        }

                        return _isInteractive(screenPoint.X, screenPoint.Y)
                            ? new IntPtr(NativeMethods.HTCLIENT)
                            : new IntPtr(NativeMethods.HTTRANSPARENT);
                    }

                    case NativeMethods.WM_MOUSEACTIVATE:
                        // Clicking the island must never steal focus from the user's application.
                        return new IntPtr(NativeMethods.MA_NOACTIVATE);

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                // A UI-thread predicate bug must not crash the window procedure: degrade to click-through.
                _owner._counters.IncrementWin32Failures();
                _owner._logger.LogError(PlatformLogCategory.Platform, "interop.hittest-failed",
                    "Hit-test predicate threw; defaulting to HTTRANSPARENT.", ex);
                return new IntPtr(NativeMethods.HTTRANSPARENT);
            }

            return _originalProc == IntPtr.Zero
                ? NativeMethods.User32.DefWindowProcW(hwnd, message, wParam, lParam)
                : NativeMethods.User32.CallWindowProcW(_originalProc, hwnd, message, wParam, lParam);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_originalProc != IntPtr.Zero)
            {
                NativeMethods.User32.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_WNDPROC, _originalProc);
            }

            // Keep the delegate alive until after the procedure has been restored.
            GC.KeepAlive(_hookDelegate);
        }
    }
}
