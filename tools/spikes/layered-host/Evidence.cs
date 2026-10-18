using System.Runtime.InteropServices;
using SkiaSharp;

namespace FluidBar.Spikes.LayeredHost;

/// <summary>One screen capture as top-down BGRA bytes (what the compositor actually shows).</summary>
internal sealed class CapturedFrame
{
    public CapturedFrame(int x, int y, int width, int height, byte[] bgra)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    /// <summary>Screen X of the capture origin.</summary>
    public int X { get; }

    /// <summary>Screen Y of the capture origin.</summary>
    public int Y { get; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Top-down BGRA pixels.</summary>
    public byte[] Bgra { get; }

    /// <summary>Reads a pixel as (R,G,B) in capture coordinates.</summary>
    public (byte R, byte G, byte B) Pixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        var index = ((y * Width) + x) * 4;
        return (Bgra[index + 2], Bgra[index + 1], Bgra[index]);
    }

    /// <summary>Reads a pixel in absolute screen coordinates.</summary>
    public (byte R, byte G, byte B) PixelAtScreen(int screenX, int screenY) => Pixel(screenX - X, screenY - Y);

    /// <summary>Checks a pixel against the backdrop palette with a tolerance per channel.</summary>
    public bool IsBackdrop(int screenX, int screenY, int tolerance = 10)
    {
        var (r, g, b) = PixelAtScreen(screenX, screenY);
        return Matches(r, g, b, BackdropWindow.RgbA, tolerance) || Matches(r, g, b, BackdropWindow.RgbB, tolerance);
    }

    /// <summary>Checks a pixel against an expected colour with a tolerance per channel.</summary>
    public bool Is(int screenX, int screenY, (byte R, byte G, byte B) expected, int tolerance = 10)
    {
        var (r, g, b) = PixelAtScreen(screenX, screenY);
        return Matches(r, g, b, expected, tolerance);
    }

    /// <summary>Checks whether a pixel is "dark island content" (panel/pill fill), not the backdrop.</summary>
    public bool IsDarkIsland(int screenX, int screenY)
    {
        var (r, g, b) = PixelAtScreen(screenX, screenY);
        return r < 90 && g < 90 && b < 100;
    }

    private static bool Matches(byte r, byte g, byte b, (byte R, byte G, byte B) expected, int tolerance)
        => Math.Abs(r - expected.R) <= tolerance
            && Math.Abs(g - expected.G) <= tolerance
            && Math.Abs(b - expected.B) <= tolerance;
}

/// <summary>Screen capture + PNG evidence writer.</summary>
internal static class Evidence
{
    /// <summary>Copies a screen rectangle through a DIB section (<c>BitBlt(SRCCOPY)</c>).</summary>
    public static CapturedFrame Capture(int x, int y, int width, int height)
    {
        var screenDc = SpikeNative.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetDC(NULL) failed.");
        }

        var memoryDc = SpikeNative.CreateCompatibleDC(screenDc);
        var bitmapInfo = new SpikeNative.BITMAPINFO
        {
            bmiHeader = new SpikeNative.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<SpikeNative.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = SpikeNative.BI_RGB,
            },
        };
        var dib = SpikeNative.CreateDIBSection(screenDc, ref bitmapInfo, SpikeNative.DIB_RGB_COLORS, out var bits,
            IntPtr.Zero, 0);
        try
        {
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw new InvalidOperationException($"CreateDIBSection failed: {SpikeNative.GetLastError()}");
            }

            SpikeNative.SelectObject(memoryDc, dib);
            if (!SpikeNative.BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SpikeNative.SRCCOPY))
            {
                throw new InvalidOperationException($"BitBlt failed: {SpikeNative.GetLastError()}");
            }

            var bytes = new byte[width * height * 4];
            Marshal.Copy(bits, bytes, 0, bytes.Length);
            return new CapturedFrame(x, y, width, height, bytes);
        }
        finally
        {
            SpikeNative.SelectObject(memoryDc, IntPtr.Zero);
            SpikeNative.DeleteObject(dib);
            SpikeNative.DeleteDC(memoryDc);
            SpikeNative.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Writes a capture to a PNG file (Skia encodes; no System.Drawing dependency).</summary>
    public static void SavePng(CapturedFrame frame, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var handle = GCHandle.Alloc(frame.Bgra, GCHandleType.Pinned);
        try
        {
            var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var bitmap = new SKBitmap();
            bitmap.InstallPixels(info, handle.AddrOfPinnedObject(), frame.Width * 4);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(path);
            data.SaveTo(stream);
        }
        finally
        {
            handle.Free();
        }
    }
}
