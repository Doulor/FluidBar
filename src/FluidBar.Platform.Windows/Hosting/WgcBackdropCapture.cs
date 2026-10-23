using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// Captures the composed desktop (windows included) through <c>Windows.Graphics.Capture</c>.
/// <para>
/// GDI <c>BitBlt</c> only returns the wallpaper layer on modern DWM desktops, so anything floating above
/// a window would blur the wrong content. WGC reads the same composed surface the user sees. One session
/// is kept alive per monitor; frames arrive only when the desktop changes, so the last frame is cached and
/// re-used (the island re-captures it on its own 400 ms cadence).
/// </para>
/// </summary>
internal sealed class WgcBackdropCapture : IDisposable
{
    private const uint MonitorDefaultToPrimary = 1;

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _frameReady = new(false);

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private Direct3D11CaptureFrame? _latest;
    private IntPtr _monitor;
    private NativeMethods.RECT _monitorRect;
    private int _framesArrived;
    private long _lastRequestTicks;
    private bool _disposed;

    /// <summary>Stops the capture session when nothing asked for a frame for this long (ms).</summary>
    private const int IdleStopMs = 5000;

    /// <summary>Whether the platform supports window/desktop capture (Windows 10 1903+ with WGC).</summary>
    public static bool IsSupported
    {
        get
        {
            try
            {
                return GraphicsCaptureSession.IsSupported();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Number of frames delivered by the capture session (diagnostics).</summary>
    public int FramesArrived => _framesArrived;

    /// <summary>
    /// Captures a screen region in physical pixels. Returns false when no frame is available yet or the
    /// session cannot start (the caller falls back to GDI).
    /// </summary>
    public bool TryCapture(int screenX, int screenY, int width, int height, out BackdropFrame frame)
    {
        frame = default;
        if (_disposed || width <= 0 || height <= 0)
        {
            return false;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var sinceLastMs = _lastRequestTicks == 0
            ? 0
            : (now - _lastRequestTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _lastRequestTicks = now;
        if (_session is not null && sinceLastMs > IdleStopMs)
        {
            lock (_gate)
            {
                StopLocked();
            }
        }

        var monitor = NativeMethods.User32.MonitorFromPoint(
            new NativeMethods.POINT { X = screenX, Y = screenY }, MonitorDefaultToPrimary);
        if (monitor == IntPtr.Zero || !EnsureStarted(monitor))
        {
            return false;
        }

        if (_latest is null && !_frameReady.Wait(120))
        {
            return false;
        }

        lock (_gate)
        {
            var current = _latest;
            if (current is null)
            {
                return false;
            }

            return TryCopy(current, screenX - _monitorRect.Left, screenY - _monitorRect.Top, width, height, out frame);
        }
    }

    private bool EnsureStarted(IntPtr monitor)
    {
        lock (_gate)
        {
            if (_session is not null && _monitor == monitor)
            {
                return true;
            }

            StopLocked();
            _monitor = monitor;
            var monitorInfo = new NativeMethods.MONITORINFOEXW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEXW>(),
                szDevice = string.Empty,
            };
            if (!NativeMethods.User32.GetMonitorInfoW(monitor, ref monitorInfo))
            {
                return false;
            }

            _monitorRect = monitorInfo.rcMonitor;

            try
            {
                var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
                D3D11.D3D11CreateDevice(
                    (IDXGIAdapter?)null,
                    DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport,
                    featureLevels,
                    out ID3D11Device device,
                    out ID3D11DeviceContext context);
                _device = device;
                _context = context;

                using var dxgi = device.QueryInterface<IDXGIDevice>();
                var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var inspectable);
                if (hr != 0 || inspectable == IntPtr.Zero)
                {
                    StopLocked();
                    return false;
                }

                var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                var item = CreateItemForMonitor(monitor);
                if (item is null)
                {
                    StopLocked();
                    return false;
                }

                _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
                _session = _pool.CreateCaptureSession(item);
                _session.IsCursorCaptureEnabled = false;
                try
                {
                    _session.IsBorderRequired = false;
                }
                catch (Exception)
                {
                    // Older builds do not expose the border toggle.
                }

                _pool.FrameArrived += OnFrameArrived;
                _session.StartCapture();
                _frameReady.Reset();
                return true;
            }
            catch (Exception)
            {
                StopLocked();
                return false;
            }
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Direct3D11CaptureFrame? frame = null;
        try
        {
            frame = sender.TryGetNextFrame();
        }
        catch (Exception)
        {
            return;
        }

        if (frame is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                frame.Dispose();
                return;
            }

            _latest?.Dispose();
            _latest = frame;
            _framesArrived++;
            _frameReady.Set();
        }
    }

    private bool TryCopy(Direct3D11CaptureFrame frame, int x, int y, int width, int height, out BackdropFrame result)
    {
        result = default;
        if (_device is null || _context is null)
        {
            return false;
        }

        if (x < 0 || y < 0 || x + width > frame.ContentSize.Width || y + height > frame.ContentSize.Height)
        {
            return false;
        }

        try
        {
            var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
            var iid = typeof(ID3D11Texture2D).GUID;
            if (access.GetInterface(ref iid, out var texturePtr) != 0 || texturePtr == IntPtr.Zero)
            {
                return false;
            }

            using var source = new ID3D11Texture2D(texturePtr);
            var desc = source.Description;
            using var staging = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = desc.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None,
            });

            _context.CopySubresourceRegion(
                staging, 0, 0, 0, 0, source, 0,
                new Box(x, y, 0, x + width, y + height, 1));

            var mapped = _context.Map(staging, 0, MapMode.Read);
            try
            {
                var stride = width * 4;
                var pixels = new byte[stride * height];
                for (var row = 0; row < height; row++)
                {
                    Marshal.Copy(mapped.DataPointer + (row * (int)mapped.RowPitch), pixels, row * stride, stride);
                }

                result = new BackdropFrame(width, height, stride, pixels);
                return true;
            }
            finally
            {
                _context.Unmap(staging, 0);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static GraphicsCaptureItem? CreateItemForMonitor(IntPtr monitor)
    {
        var interopIid = typeof(IGraphicsCaptureItemInterop).GUID;
        var hr = RoGetActivationFactory(
            NewHString("Windows.Graphics.Capture.GraphicsCaptureItem"), ref interopIid, out var factory);
        if (hr != 0 || factory == IntPtr.Zero)
        {
            return null;
        }

        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
        var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        return interop.CreateForMonitor(monitor, ref itemIid, out var itemPtr) == 0 && itemPtr != IntPtr.Zero
            ? MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr)
            : null;
    }

    private static IntPtr NewHString(string value)
    {
        WindowsCreateString(value, value.Length, out var hstring);
        return hstring;
    }

    private void StopLocked()
    {
        try
        {
            _session?.Dispose();
        }
        catch (Exception)
        {
            // Ignore teardown races.
        }

        _session = null;
        _pool?.Dispose();
        _pool = null;
        _latest?.Dispose();
        _latest = null;
        _context?.Dispose();
        _context = null;
        _device?.Dispose();
        _device = null;
        _frameReady.Reset();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopLocked();
        }

        _frameReady.Dispose();
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, int length, out IntPtr hstring);

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig] int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr item);
        [PreserveSig] int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr item);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig] int GetInterface(ref Guid iid, out IntPtr result);
    }
}
