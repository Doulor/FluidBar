using System.ComponentModel;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// Window-procedure and input half of <see cref="LayeredWindowHost"/>. The host owns the window class
/// (registered once per process) and translates Win32 messages into allocation-free callbacks.
/// </summary>
public sealed partial class LayeredWindowHost
{
    private static readonly NativeMethods.WndProcHookDelegate WindowProcedureDelegate = StaticWindowProcedure;
    private static readonly object ClassGate = new();
    private static bool _classRegistered;
    private static IntPtr _classNamePointer;

    /// <summary>Registers the window class once per process (the class name pointer lives for the process).</summary>
    private static void EnsureClassRegistered()
    {
        lock (ClassGate)
        {
            if (_classRegistered)
            {
                return;
            }

            var instance = NativeMethods.Kernel32.GetModuleHandleW(null);
            if (_classNamePointer == IntPtr.Zero)
            {
                // The class keeps a pointer to this string for the lifetime of the registration.
                _classNamePointer = Marshal.StringToHGlobalUni(WindowClassName);
            }

            var windowClass = new NativeMethods.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
                style = NativeMethods.CS_DBLCLKS,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
                hInstance = instance,
                hCursor = NativeMethods.User32.LoadCursorW(IntPtr.Zero, NativeMethods.IDC_ARROW),
                lpszClassName = _classNamePointer,
            };

            var atom = NativeMethods.User32.RegisterClassExW(ref windowClass);
            if (atom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ERROR_CLASS_ALREADY_EXISTS)
                {
                    throw new Win32Exception(error, "RegisterClassExW failed for the layered island host class.");
                }
            }

            _classRegistered = true;
        }
    }

    private static IntPtr StaticWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        var host = ResolveHost(hwnd);
        if (host is null)
        {
            return NativeMethods.User32.DefWindowProcW(hwnd, message, wParam, lParam);
        }

        try
        {
            return host.HandleMessage(hwnd, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            // A managed exception must never unwind through the native window procedure.
            host.OnMessageException(ex);
            return NativeMethods.User32.DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private static LayeredWindowHost? ResolveHost(IntPtr hwnd)
    {
        var raw = NativeMethods.User32.GetWindowLongPtr(hwnd, NativeMethods.GWLP_USERDATA);
        if (raw == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return GCHandle.FromIntPtr(raw).Target as LayeredWindowHost;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private IntPtr HandleMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case NativeMethods.WM_NCHITTEST:
                return new IntPtr(HitTest(lParam));

            case NativeMethods.WM_MOUSEACTIVATE:
                // Clicking the island must never steal focus from the user's application.
                return new IntPtr(NativeMethods.MA_NOACTIVATE);

            case NativeMethods.WM_GETOBJECT:
                return OnGetObject(hwnd, wParam, lParam);

            case NativeMethods.WM_GETDLGCODE:
                return Volatile.Read(ref _uiaKeyboard) is null
                    ? NativeMethods.User32.DefWindowProcW(hwnd, message, wParam, lParam)
                    : new IntPtr(NativeMethods.DLGC_WANTARROWS | NativeMethods.DLGC_WANTTAB
                        | NativeMethods.DLGC_WANTALLKEYS);

            case NativeMethods.WM_KEYDOWN:
                return OnKeyDown(wParam, lParam);

            case NativeMethods.WM_SETFOCUS:
                OnWindowFocusChanged(true);
                return IntPtr.Zero;

            case NativeMethods.WM_KILLFOCUS:
                OnWindowFocusChanged(false);
                return IntPtr.Zero;

            case NativeMethods.WM_SETCURSOR:
                return OnSetCursor(wParam, lParam);

            case NativeMethods.WM_MOUSEMOVE:
                OnMouseMove(wParam, lParam);
                return IntPtr.Zero;

            case NativeMethods.WM_MOUSELEAVE:
                OnMouseLeave();
                return IntPtr.Zero;

            case NativeMethods.WM_LBUTTONDOWN:
                OnLeftButtonDown(wParam, lParam);
                return IntPtr.Zero;

            case NativeMethods.WM_LBUTTONDBLCLK:
                OnLeftButtonDown(wParam, lParam, isDoubleClick: true);
                return IntPtr.Zero;

            case NativeMethods.WM_LBUTTONUP:
                OnLeftButtonUp(wParam, lParam);
                return IntPtr.Zero;

            case NativeMethods.WM_MOUSEWHEEL:
                OnWheel(wParam, lParam, isHorizontal: false);
                return IntPtr.Zero;

            case NativeMethods.WM_MOUSEHWHEEL:
                OnWheel(wParam, lParam, isHorizontal: true);
                return IntPtr.Zero;

            case NativeMethods.WM_CAPTURECHANGED:
                OnCaptureChanged();
                return IntPtr.Zero;

            case NativeMethods.WM_DPICHANGED:
                OnDpiChanged(wParam, lParam);
                return IntPtr.Zero;

            case NativeMethods.WM_ERASEBKGND:
                // The layered surface is the only content: never erase with a class brush.
                return new IntPtr(1);

            case NativeMethods.WM_PAINT:
                ValidatePaint(hwnd);
                return IntPtr.Zero;

            case NativeMethods.WM_NCDESTROY:
                NativeMethods.User32.SetWindowLongPtr(hwnd, NativeMethods.GWLP_USERDATA, IntPtr.Zero);
                return NativeMethods.User32.DefWindowProcW(hwnd, message, wParam, lParam);

            default:
                return NativeMethods.User32.DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private int HitTest(IntPtr lParam)
    {
        if (!_visible)
        {
            return NativeMethods.HTTRANSPARENT;
        }

        var packed = lParam.ToInt64();
        var point = new NativeMethods.POINT
        {
            X = unchecked((short)(packed & 0xFFFF)),
            Y = unchecked((short)((packed >> 16) & 0xFFFF)),
        };
        if (!NativeMethods.User32.ScreenToClient(_hwnd, ref point))
        {
            return NativeMethods.HTTRANSPARENT;
        }

        return _hitRegion.IsInteractivePixels(point.X, point.Y, _scale)
            ? NativeMethods.HTCLIENT
            : NativeMethods.HTTRANSPARENT;
    }

    private IntPtr OnSetCursor(IntPtr wParam, IntPtr lParam)
    {
        var hitTest = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        if (hitTest != NativeMethods.HTCLIENT)
        {
            return NativeMethods.User32.DefWindowProcW(_hwnd, NativeMethods.WM_SETCURSOR, wParam, lParam);
        }

        NativeMethods.User32.SetCursor(CursorHandle(_cursorKind));
        return new IntPtr(1);
    }

    private void OnMouseMove(IntPtr wParam, IntPtr lParam)
    {
        var pointer = BuildPointerFromClient(wParam, ClientX(lParam), ClientY(lParam));
        _pointerInside = true;
        if (!_trackingLeave)
        {
            TrackLeave();
        }

        PointerMoved?.Invoke(pointer);

        if (_dragging && (pointer.Buttons & LayeredPointerButtons.Left) != 0)
        {
            RaiseDrag(LayeredDragPhase.Moved, pointer);
        }
    }

    private void OnMouseLeave()
    {
        _trackingLeave = false;
        _pointerInside = false;
        PointerLeft?.Invoke();
    }

    private void OnLeftButtonDown(IntPtr wParam, IntPtr lParam, bool isDoubleClick = false)
    {
        var pointer = BuildPointerFromClient(wParam, ClientX(lParam), ClientY(lParam));
        PointerPressed?.Invoke(pointer);

        if (isDoubleClick)
        {
            // A double click is also a press: without this, the second click of a double click
            // would silently skip press/drag handling (CS_DBLCLKS replaces WM_LBUTTONDOWN).
            PointerDoubleClicked?.Invoke(pointer);
        }

        if (!_options.EnableDrag)
        {
            return;
        }

        // SetCapture returns the *previous* capture window (usually NULL), so success is checked
        // with GetCapture, not with the return value.
        NativeMethods.User32.SetCapture(_hwnd);
        if (NativeMethods.User32.GetCapture() != _hwnd)
        {
            _logger.LogDebug(PlatformLogCategory.Platform, "layered-host.capture-failed",
                "SetCapture failed; drag disabled for this press.");
            return;
        }

        _dragging = true;
        _dragStartScreenX = pointer.ScreenX;
        _dragStartScreenY = pointer.ScreenY;
        _lastDragScreenX = pointer.ScreenX;
        _lastDragScreenY = pointer.ScreenY;
        Dragged?.Invoke(new LayeredDragEvent(LayeredDragPhase.Started, pointer, 0, 0, 0, 0));
    }

    private void OnLeftButtonUp(IntPtr wParam, IntPtr lParam)
    {
        var pointer = BuildPointerFromClient(wParam, ClientX(lParam), ClientY(lParam));
        PointerReleased?.Invoke(pointer);

        if (_dragging)
        {
            EndDrag(LayeredDragPhase.Ended, pointer);
        }
    }

    private void OnWheel(IntPtr wParam, IntPtr lParam, bool isHorizontal)
    {
        if (!_options.EnableWheel)
        {
            return;
        }

        var packed = wParam.ToInt64();
        var delta = unchecked((short)((packed >> 16) & 0xFFFF));
        var pointer = BuildPointerFromScreen(wParam, ScreenX(lParam), ScreenY(lParam));
        PointerWheel?.Invoke(new LayeredWheelEvent(pointer, delta, isHorizontal));
    }

    private void OnCaptureChanged()
    {
        if (!_dragging)
        {
            return;
        }

        // Re-capturing to ourselves (SetCapture on a window that already has capture) still sends
        // WM_CAPTURECHANGED; only a change to a *different* window cancels the drag.
        if (NativeMethods.User32.GetCapture() == _hwnd)
        {
            return;
        }

        EndDrag(LayeredDragPhase.Cancelled, _lastPointer);
        PointerCaptureLost?.Invoke();
    }

    private void OnDpiChanged(IntPtr wParam, IntPtr lParam)
    {
        var (dpiX, _) = DisplayMetrics.DecodeDpiChangedWParam(wParam);
        var suggested = Marshal.PtrToStructure<NativeMethods.RECT>(lParam);
        var changed = dpiX > 0 && dpiX != _dpi;
        var surfaceSize = LayeredSurfaceSize.ForMode(_mode);
        var scale = changed ? DisplayMetrics.ScaleFromDpi(dpiX) : _scale;
        var sizePx = surfaceSize.ToPixels(scale);
        LayeredDpiChange? notification = null;

        if (changed)
        {
            var oldDpi = _dpi;
            var oldScale = _scale;
            _dpi = dpiX;
            _scale = scale;
            RebuildSurface(sizePx);
            _needsFullFrame = true;
            _geometryDirty = true;
            _pacer?.Reset();
            notification = new LayeredDpiChange(oldDpi, dpiX, oldScale, _scale, sizePx);
        }

        // Follow the OS suggestion for the origin but always keep our fixed surface size. This must
        // happen BEFORE LayeredDpiChanged: the App handler re-places from Core's WindowPlacement, and
        // its SetWindowPos must win over the OS suggestion (otherwise a DPI change would undo the
        // anchor/dock placement by one monitor-step).
        var hasSuggestion = suggested.Right > suggested.Left && suggested.Bottom > suggested.Top;
        var originX = hasSuggestion ? suggested.Left : _placement.WindowPixel.X;
        var originY = hasSuggestion ? suggested.Top : _placement.WindowPixel.Y;
        var rect = new LayeredRect(originX, originY, sizePx.Width, sizePx.Height);
        ApplyWindowRect(rect);
        _placement = new LayeredHostPlacement(rect, sizePx, surfaceSize, _scale,
            OffsetToPixels(LayeredSurfaceSize.ShellLocalOffset(_mode), _scale));
        UpdateUiaGeometry();

        if (notification is { } change)
        {
            LayeredDpiChanged?.Invoke(change);
        }
    }

    private static void ValidatePaint(IntPtr hwnd)
    {
        NativeMethods.User32.BeginPaint(hwnd, out var paint);
        NativeMethods.User32.EndPaint(hwnd, ref paint);
    }

    private void OnMessageException(Exception exception)
    {
        _counters.IncrementWin32Failures();
        _logger.LogError(PlatformLogCategory.Platform, "layered-host.wndproc-failed",
            "Layered host window procedure threw; the message was ignored.", exception);
    }

    private void TrackLeave()
    {
        var track = new NativeMethods.TRACKMOUSEEVENT
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.TRACKMOUSEEVENT>(),
            dwFlags = NativeMethods.TME_LEAVE,
            hwndTrack = _hwnd,
        };
        if (NativeMethods.User32.TrackMouseEvent(ref track))
        {
            _trackingLeave = true;
        }
    }

    private LayeredPointerEvent BuildPointerFromClient(IntPtr wParam, int clientX, int clientY)
    {
        // Screen coordinates come from the cursor, not from ClientToScreen(client): a drag moves the
        // window while the message is being handled, so a client-to-screen conversion would feed the
        // window's own movement back into the drag delta (classic drag jitter/drift).
        if (!NativeMethods.User32.GetCursorPos(out var screen))
        {
            screen = new NativeMethods.POINT(clientX, clientY);
            NativeMethods.User32.ClientToScreen(_hwnd, ref screen);
        }

        return CreatePointer(wParam, clientX, clientY, screen.X, screen.Y);
    }

    private LayeredPointerEvent BuildPointerFromScreen(IntPtr wParam, int screenX, int screenY)
    {
        var client = new NativeMethods.POINT(screenX, screenY);
        NativeMethods.User32.ScreenToClient(_hwnd, ref client);
        return CreatePointer(wParam, client.X, client.Y, screenX, screenY);
    }

    private LayeredPointerEvent CreatePointer(IntPtr wParam, int clientX, int clientY, int screenX, int screenY)
    {
        var scale = _scale <= 0 ? 1.0 : _scale;
        var pointer = new LayeredPointerEvent(
            clientX,
            clientY,
            clientX / scale,
            clientY / scale,
            screenX,
            screenY,
            DecodeButtons(wParam),
            DecodeModifiers(wParam),
            _timeProvider.GetTimestamp());
        _lastPointer = pointer;
        return pointer;
    }

    private void RaiseDrag(LayeredDragPhase phase, in LayeredPointerEvent pointer)
    {
        var totalX = pointer.ScreenX - _dragStartScreenX;
        var totalY = pointer.ScreenY - _dragStartScreenY;
        var stepX = pointer.ScreenX - _lastDragScreenX;
        var stepY = pointer.ScreenY - _lastDragScreenY;
        _lastDragScreenX = pointer.ScreenX;
        _lastDragScreenY = pointer.ScreenY;
        Dragged?.Invoke(new LayeredDragEvent(phase, pointer, totalX, totalY, stepX, stepY));
    }

    private void EndDrag(LayeredDragPhase phase, in LayeredPointerEvent pointer)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (NativeMethods.User32.GetCapture() == _hwnd)
        {
            NativeMethods.User32.ReleaseCapture();
        }

        RaiseDrag(phase, pointer);
    }

    private static LayeredPointerButtons DecodeButtons(IntPtr wParam)
    {
        var value = wParam.ToInt32();
        var buttons = LayeredPointerButtons.None;
        if ((value & NativeMethods.MK_LBUTTON) != 0)
        {
            buttons |= LayeredPointerButtons.Left;
        }

        if ((value & NativeMethods.MK_RBUTTON) != 0)
        {
            buttons |= LayeredPointerButtons.Right;
        }

        if ((value & NativeMethods.MK_MBUTTON) != 0)
        {
            buttons |= LayeredPointerButtons.Middle;
        }

        if ((value & NativeMethods.MK_XBUTTON1) != 0)
        {
            buttons |= LayeredPointerButtons.XButton1;
        }

        if ((value & NativeMethods.MK_XBUTTON2) != 0)
        {
            buttons |= LayeredPointerButtons.XButton2;
        }

        return buttons;
    }

    private static LayeredKeyModifiers DecodeModifiers(IntPtr wParam)
    {
        var value = wParam.ToInt32();
        var modifiers = LayeredKeyModifiers.None;
        if ((value & NativeMethods.MK_SHIFT) != 0)
        {
            modifiers |= LayeredKeyModifiers.Shift;
        }

        if ((value & NativeMethods.MK_CONTROL) != 0)
        {
            modifiers |= LayeredKeyModifiers.Control;
        }

        // WM_* wParam never carries Alt; query the async key state instead.
        if ((NativeMethods.User32.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0)
        {
            modifiers |= LayeredKeyModifiers.Alt;
        }

        return modifiers;
    }

    private static int ClientX(IntPtr lParam) => unchecked((short)(lParam.ToInt64() & 0xFFFF));

    private static int ClientY(IntPtr lParam) => unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

    private static int ScreenX(IntPtr lParam) => unchecked((short)(lParam.ToInt64() & 0xFFFF));

    private static int ScreenY(IntPtr lParam) => unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
}
