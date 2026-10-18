using System.ComponentModel;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// A 32bpp top-down premultiplied-BGRA <c>CreateDIBSection</c> plus the memory DC that has the
/// section selected into it. This is the exact surface <c>UpdateLayeredWindow(ULW_ALPHA)</c> expects:
/// the bytes are BGRA, alpha is straight-to-premultiplied by the renderer, and row 0 is the top row.
/// </summary>
/// <remarks>
/// <para>The surface is created once per size and reused for every frame: no per-frame allocation, no
/// copy, no readback. <see cref="Pixels"/> is a view over the section's memory, so a SkiaSharp surface
/// created on <see cref="Bits"/> draws directly into the memory the compositor uploads.</para>
/// <para>Instances are not thread-safe and must be used on the thread that created them (they are
/// owned by <see cref="LayeredWindowHost"/>, which enforces its owning thread).</para>
/// </remarks>
public sealed class LayeredSurface : IDisposable
{
    /// <summary>Bytes per pixel of the DIB.</summary>
    public const int BytesPerPixel = 4;

    private IntPtr _dib;
    private IntPtr _hdc;
    private IntPtr _oldBitmap;
    private IntPtr _bits;
    private int _widthPx;
    private int _heightPx;
    private int _stride;
    private int _disposed;

    private LayeredSurface()
    {
    }

    /// <summary>Creates a surface of the given pixel size.</summary>
    /// <param name="widthPx">Width in pixels (1..16384).</param>
    /// <param name="heightPx">Height in pixels (1..16384).</param>
    /// <exception cref="Win32Exception">The DIB section or memory DC could not be created.</exception>
    public static LayeredSurface Create(int widthPx, int heightPx)
    {
        var surface = new LayeredSurface();
        surface.Recreate(widthPx, heightPx);
        return surface;
    }

    /// <summary>Width in pixels.</summary>
    public int WidthPx => _widthPx;

    /// <summary>Height in pixels.</summary>
    public int HeightPx => _heightPx;

    /// <summary>Row stride in bytes (<c>WidthPx * 4</c>).</summary>
    public int Stride => _stride;

    /// <summary>Total size of the pixel buffer in bytes.</summary>
    public int ByteCount => _stride * _heightPx;

    /// <summary>Pointer to the first pixel (top-left).</summary>
    public IntPtr Bits => _bits;

    /// <summary>Memory DC with the DIB section selected into it (the <c>hdcSrc</c> of an update).</summary>
    public IntPtr Hdc => _hdc;

    /// <summary>The DIB section handle.</summary>
    public IntPtr DibHandle => _dib;

    /// <summary>Writable pixel buffer (premultiplied BGRA, top-down).</summary>
    public unsafe Span<byte> Pixels => _disposed == 0 && _bits != IntPtr.Zero
        ? new Span<byte>((void*)_bits, ByteCount)
        : [];

    /// <summary>Writable pixel buffer as 32-bit words (<c>0xAARRGGBB</c> in memory as BGRA).</summary>
    public unsafe Span<uint> Pixels32 => MemoryMarshal.Cast<byte, uint>(Pixels);

    /// <summary>Pixel size.</summary>
    public LayeredPixelSize Size => new(_widthPx, _heightPx);

    /// <summary>Sets every byte to zero (fully transparent, premultiplied black).</summary>
    public void Clear() => Pixels.Clear();

    /// <summary>Copies a caller buffer into the surface (must be at least <see cref="ByteCount"/> bytes).</summary>
    public void CopyFrom(ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(source.Length, ByteCount, nameof(source));
        source[..ByteCount].CopyTo(Pixels);
    }

    /// <summary>Copies a single row into the surface (top-down row index).</summary>
    public void CopyRow(int y, ReadOnlySpan<byte> row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, _heightPx);
        ArgumentOutOfRangeException.ThrowIfLessThan(row.Length, _stride);
        row[.._stride].CopyTo(Pixels.Slice(y * _stride, _stride));
    }

    /// <summary>Reads one pixel as premultiplied BGRA (<c>0xAARRGGBB</c>).</summary>
    public unsafe uint GetPixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, _widthPx);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, _heightPx);
        return ((uint*)_bits)[(y * _widthPx) + x];
    }

    /// <summary>Writes one pixel as premultiplied BGRA (<c>0xAARRGGBB</c>).</summary>
    public unsafe void SetPixel(int x, int y, uint bgra)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, _widthPx);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, _heightPx);
        ((uint*)_bits)[(y * _widthPx) + x] = bgra;
    }

    /// <summary>
    /// Recreates the DIB at a new size, releasing the old one. Content is not preserved: the next
    /// frame must be a full frame.
    /// </summary>
    public void Recreate(int widthPx, int heightPx)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(widthPx, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(widthPx, 16384);
        ArgumentOutOfRangeException.ThrowIfLessThan(heightPx, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(heightPx, 16384);

        if (widthPx == _widthPx && heightPx == _heightPx && _dib != IntPtr.Zero)
        {
            return;
        }

        var screenDc = NativeMethods.User32.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC(NULL) failed.");
        }

        try
        {
            var hdc = NativeMethods.Gdi32.CreateCompatibleDC(screenDc);
            if (hdc == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateCompatibleDC failed.");
            }

            var bmi = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = widthPx,
                    // Negative height = top-down rows, which is what every rasterizer expects.
                    biHeight = -heightPx,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeMethods.BI_RGB,
                },
            };

            var dib = NativeMethods.Gdi32.CreateDIBSection(screenDc, ref bmi, NativeMethods.DIB_RGB_COLORS,
                out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                NativeMethods.Gdi32.DeleteDC(hdc);
                throw new Win32Exception(error, "CreateDIBSection failed.");
            }

            var oldBitmap = NativeMethods.Gdi32.SelectObject(hdc, dib);
            if (oldBitmap == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                NativeMethods.Gdi32.DeleteObject(dib);
                NativeMethods.Gdi32.DeleteDC(hdc);
                throw new Win32Exception(error, "SelectObject(DIB) failed.");
            }

            ReleaseNative();

            _dib = dib;
            _hdc = hdc;
            _oldBitmap = oldBitmap;
            _bits = bits;
            _widthPx = widthPx;
            _heightPx = heightPx;
            _stride = widthPx * BytesPerPixel;
        }
        finally
        {
            _ = NativeMethods.User32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Releases the DIB and its DC (idempotent).</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ReleaseNative();
    }

    private void ReleaseNative()
    {
        if (_hdc != IntPtr.Zero)
        {
            if (_oldBitmap != IntPtr.Zero)
            {
                NativeMethods.Gdi32.SelectObject(_hdc, _oldBitmap);
            }

            NativeMethods.Gdi32.DeleteDC(_hdc);
        }

        if (_dib != IntPtr.Zero)
        {
            NativeMethods.Gdi32.DeleteObject(_dib);
        }

        _dib = IntPtr.Zero;
        _hdc = IntPtr.Zero;
        _oldBitmap = IntPtr.Zero;
        _bits = IntPtr.Zero;
        _widthPx = 0;
        _heightPx = 0;
        _stride = 0;
    }
}
