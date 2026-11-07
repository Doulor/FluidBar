using System.ComponentModel;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// A cached, input-transparent drag bitmap on its own native pump. Pointer tracking must not wait
/// for OLE GiveFeedback: slow drop targets and windows without a drop target can delay that callback.
/// </summary>
internal sealed class FileDragPreview : IDisposable, IAsyncDisposable
{
    internal const string WindowTitle = "FluidBar.FileDragPreview";
    private readonly PlatformMessageHost _host = new(NullPlatformLogger.Instance, new PlatformCounters());
    private readonly Func<NativeMethods.POINT> _cursor;
    private readonly Action _move;
    private readonly FileDragImage _image;
    private readonly object _lifecycle = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PlatformWindow? _window;
    private LayeredSurface? _surface;
    private Timer? _timer;
    private NativeMethods.POINT _lastPosition;
    private int _queued;
    private int _disposed;
    private bool _timerResolution;

    internal IntPtr Handle => _window?.Handle ?? IntPtr.Zero;
    internal Task<bool> Initialization => _ready.Task;
    internal Task Completion { get; }

    internal FileDragPreview(FileDragImage image, Func<NativeMethods.POINT>? cursor = null)
    {
        image.Validate();
        _image = image;
        _cursor = cursor ?? ReadCursor;
        _move = Move;
        Completion = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            await _host.StartAsync().ConfigureAwait(false);
            await _host.InvokeAsync(() => NativeMethods.User32.SetThreadDpiAwarenessContext(new IntPtr(-4)))
                .ConfigureAwait(false);
            _window = await _host.CreateWindowAsync(WindowTitle, PlatformWindowKind.HiddenTopLevel,
                (_, message, _, _) => message switch
                {
                    NativeMethods.WM_NCHITTEST => new IntPtr(NativeMethods.HTTRANSPARENT),
                    NativeMethods.WM_MOUSEACTIVATE => new IntPtr(NativeMethods.MA_NOACTIVATE),
                    _ => null,
                }).ConfigureAwait(false);
            var initialized = await _host.InvokeAsync(() =>
            {
                if (Volatile.Read(ref _disposed) != 0) return false;
                Initialize();
                lock (_lifecycle)
                {
                    if (_disposed != 0) return false;
                    NativeMethods.User32.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
                    _timerResolution = timeBeginPeriod(1) == 0;
                    _timer = new Timer(_ => QueueMove(), null, 8, 8);
                    return true;
                }
            }).ConfigureAwait(false);
            _ready.TrySetResult(initialized);
            await _stop.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Preview failure must never abort the native file operation or leave an unobserved task.
            NullPlatformLogger.Instance.LogWarning(PlatformLogCategory.Platform, "file-drag.preview-failed",
                "The optional native file drag preview failed.", ex);
        }
        finally
        {
            _ready.TrySetResult(false);
            _timer?.Dispose();
            try
            {
                if (_surface is not null)
                    await _host.InvokeAsync(() => _surface.Dispose()).ConfigureAwait(false);
            }
            finally
            {
                try { await _host.DisposeAsync().ConfigureAwait(false); }
                finally { if (_timerResolution) timeEndPeriod(1); }
            }
        }
    }

    internal static FileDragPreview? TryCreate(FileDragImage image)
    {
        try { return new FileDragPreview(image); }
        // A cosmetic preview must never prevent the actual native file drag.
        catch (Exception ex) when (ex is Win32Exception or ExternalException or InvalidOperationException or TimeoutException)
        { return null; }
    }

    private static NativeMethods.POINT ReadCursor()
    {
        NativeMethods.User32.GetCursorPos(out var cursor);
        return cursor;
    }

    private void Initialize()
    {
        NativeMethods.User32.SetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE,
            new IntPtr(NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST));
        _surface = LayeredSurface.Create(_image.Width, _image.Height);
        var pixels = _surface.Pixels;
        // IDragSourceHelper takes straight alpha; UpdateLayeredWindow needs premultiplied BGRA.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = _image.Pixels[i + 3];
            pixels[i] = (byte)((_image.Pixels[i] * alpha + 127) / 255);
            pixels[i + 1] = (byte)((_image.Pixels[i + 1] * alpha + 127) / 255);
            pixels[i + 2] = (byte)((_image.Pixels[i + 2] * alpha + 127) / 255);
            pixels[i + 3] = alpha;
        }
        var point = _cursor();
        var destination = new NativeMethods.POINT(point.X - _image.OffsetX, point.Y - _image.OffsetY);
        var size = new NativeMethods.SIZE(_image.Width, _image.Height);
        var source = new NativeMethods.POINT();
        var blend = new NativeMethods.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 };
        if (!NativeMethods.User32.UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref size,
            _surface.Hdc, ref source, 0, ref blend, NativeMethods.ULW_ALPHA))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _lastPosition = point;
        NativeMethods.User32.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void QueueMove()
    {
        if (Volatile.Read(ref _disposed) == 0 && Interlocked.CompareExchange(ref _queued, 1, 0) == 0)
            _host.Post(_move);
    }

    private void Move()
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var point = _cursor();
            if (point.X == _lastPosition.X && point.Y == _lastPosition.Y) return;
            NativeMethods.User32.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST,
                point.X - _image.OffsetX, point.Y - _image.OffsetY, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            _lastPosition = point;
        }
        finally { Volatile.Write(ref _queued, 0); }
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _timer?.Dispose();
            // Do not wait for a worker thread from OLE's UI callback. The worker owns final cleanup.
            if (Handle != IntPtr.Zero) ShowWindowAsync(Handle, 0);
        }
        _stop.TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Completion.ConfigureAwait(false);
    }

    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);
}
