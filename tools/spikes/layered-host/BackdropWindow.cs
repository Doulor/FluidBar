using System.Runtime.InteropServices;

namespace FluidBar.Spikes.LayeredHost;

/// <summary>
/// A deterministic checkerboard window placed behind the layered host. Transparent pixels of the host
/// must show this pattern; that is what makes the PNG evidence and the pixel assertions meaningful
/// (a uniform desktop wallpaper would not prove per-pixel alpha).
/// </summary>
internal sealed class BackdropWindow : IDisposable
{
    /// <summary>Checker colour A (COLORREF 0x00BBGGRR): rose RGB(225,29,72).</summary>
    public const uint ColorRefA = 0x00481DE1;

    /// <summary>Checker colour B: sky RGB(14,165,233).</summary>
    public const uint ColorRefB = 0x00E9A50E;

    /// <summary>Checker colour A as (R,G,B).</summary>
    public static readonly (byte R, byte G, byte B) RgbA = (225, 29, 72);

    /// <summary>Checker colour B as (R,G,B).</summary>
    public static readonly (byte R, byte G, byte B) RgbB = (14, 165, 233);

    /// <summary>Checker cell size in physical pixels.</summary>
    public const int CellSize = 32;

    private const string ClassName = "FluidBar.Spike.CheckerBackdrop";

    private static readonly SpikeNative.WindowProc WindowProcedure = StaticWindowProcedure;
    private static BackdropWindow? _instance;
    private static int _paintCount;

    /// <summary>Diagnostic sink for the paint path.</summary>
    internal static Action<string>? Trace;

    private readonly IntPtr _brushA;
    private readonly IntPtr _brushB;
    private IntPtr _hwnd;
    private int _disposed;

    private BackdropWindow(IntPtr brushA, IntPtr brushB)
    {
        _brushA = brushA;
        _brushB = brushB;
    }

    /// <summary>Window handle.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>Creates and shows the backdrop at an absolute physical rectangle.</summary>
    public static BackdropWindow Create(int x, int y, int width, int height)
    {
        var instance = new BackdropWindow(
            SpikeNative.CreateSolidBrush(ColorRefA),
            SpikeNative.CreateSolidBrush(ColorRefB));
        _instance = instance;

        var module = SpikeNative.GetModuleHandleW(null);
        var windowClass = new SpikeNative.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<SpikeNative.WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProcedure),
            hInstance = module,
            hCursor = SpikeNative.LoadCursorW(IntPtr.Zero, new IntPtr(32512)),
            lpszClassName = Marshal.StringToHGlobalUni(ClassName),
        };
        SpikeNative.RegisterClassExW(ref windowClass);

        instance._hwnd = SpikeNative.CreateWindowExW(SpikeNative.WS_EX_TOOLWINDOW, ClassName, "spike-backdrop",
            SpikeNative.WS_POPUP, x, y, width, height, IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);
        if (instance._hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Backdrop CreateWindowExW failed: {SpikeNative.GetLastError()}");
        }

        SpikeNative.ShowWindow(instance._hwnd, SpikeNative.SW_SHOWNOACTIVATE);
        SpikeNative.UpdateWindow(instance._hwnd);

        // Topmost so that the backdrop is the window directly below the topmost host: WindowFromPoint
        // then proves click-through without interference from unrelated overlapping windows.
        // SWP_SHOWWINDOW as well: the first ShowWindow call of a process is overridden by
        // STARTUPINFO, which would otherwise leave the backdrop hidden for the whole run.
        SpikeNative.SetWindowPos(instance._hwnd, SpikeNative.HwndTopmost, 0, 0, 0, 0,
            SpikeNative.SwpNoMove | SpikeNative.SwpNoSize | SpikeNative.SwpNoActivate | SpikeNative.SwpShowWindow);
        return instance;
    }

    private static IntPtr StaticWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        var instance = _instance;
        if (instance is null || instance._hwnd != hwnd)
        {
            return SpikeNative.DefWindowProcW(hwnd, message, wParam, lParam);
        }

        switch (message)
        {
            case SpikeNative.WM_PAINT:
                instance.Paint(hwnd);
                return IntPtr.Zero;

            case SpikeNative.WM_ERASEBKGND:
                return new IntPtr(1);

            case SpikeNative.WM_CLOSE:
                SpikeNative.DestroyWindow(hwnd);
                return IntPtr.Zero;

            case SpikeNative.WM_DESTROY:
                return IntPtr.Zero;

            default:
                return SpikeNative.DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private void Paint(IntPtr hwnd)
    {
        var paint = SpikeNative.BeginPaint(hwnd, out var paintStruct);
        SpikeNative.GetWindowRect(hwnd, out var windowRect);
        var width = windowRect.Width;
        var height = windowRect.Height;

        for (var y = 0; y < height; y += CellSize)
        {
            for (var x = 0; x < width; x += CellSize)
            {
                var even = (((x / CellSize) + (y / CellSize)) & 1) == 0;
                var cell = new SpikeNative.RECT
                {
                    Left = x,
                    Top = y,
                    Right = Math.Min(x + CellSize, width),
                    Bottom = Math.Min(y + CellSize, height),
                };
                var filled = SpikeNative.FillRect(paint, ref cell, even ? _brushA : _brushB);
                if (y == 0 && x == 0)
                {
                    Trace?.Invoke($"backdrop paint #{++_paintCount}: hdc=0x{paint:X} brushA=0x{_brushA:X} brushB=0x{_brushB:X} " +
                        $"fillRect={filled} size={width}x{height}");
                }
            }
        }

        SpikeNative.EndPaint(hwnd, ref paintStruct);
    }

    /// <summary>Destroys the window and its brushes.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_hwnd != IntPtr.Zero)
        {
            SpikeNative.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_brushA != IntPtr.Zero)
        {
            SpikeNative.DeleteObject(_brushA);
        }

        if (_brushB != IntPtr.Zero)
        {
            SpikeNative.DeleteObject(_brushB);
        }

        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }
}
