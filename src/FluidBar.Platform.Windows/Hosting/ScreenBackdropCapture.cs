using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>One captured desktop region (top-down BGRA, premultiplied-free: alpha is 255).</summary>
/// <param name="Width">Pixel width.</param>
/// <param name="Height">Pixel height.</param>
/// <param name="Stride">Bytes per row (width * 4).</param>
/// <param name="Pixels">BGRA pixels, top-down.</param>
public readonly record struct BackdropFrame(int Width, int Height, int Stride, byte[] Pixels);

/// <summary>
/// Captures the desktop *behind* the island so the liquid-glass material can blur real content.
/// <para>
/// A layered window sits on top of the composited desktop, so a naive <c>BitBlt</c> would capture the
/// island itself. The host therefore marks its window with <c>WDA_EXCLUDEFROMCAPTURE</c> (Windows 10
/// 2004+); the compositor then leaves the window out of GDI/desktop captures and the pixels behind it
/// come back instead (verified on this machine: 255,0,0 → desktop 67,69,74).
/// </para>
/// </summary>
public static class ScreenBackdropCapture
{
    private static readonly WgcBackdropCapture Wgc = new();

    /// <summary><c>WDA_EXCLUDEFROMCAPTURE</c>: the window is not included in captures at all.</summary>
    public const uint WdaExcludeFromCapture = 0x11;

    /// <summary>Marks a window as excluded from screen captures (no-op on older builds).</summary>
    /// <param name="hwnd">Window handle owned by this process.</param>
    /// <returns>True when the flag was applied.</returns>
    public static bool TryExcludeFromCapture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NativeMethods.User32.SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Captures a screen region in physical pixels. Returns null when the region is empty or GDI fails
    /// (locked session, secure desktop, remote session without capture rights).
    /// </summary>
    /// <param name="x">Virtual-screen X in physical pixels.</param>
    /// <param name="y">Virtual-screen Y in physical pixels.</param>
    /// <param name="width">Width in physical pixels.</param>
    /// <param name="height">Height in physical pixels.</param>
    public static BackdropFrame? TryCapture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 8192 || height > 4096)
        {
            return null;
        }

        // Windows.Graphics.Capture reads the *composed* desktop (windows included). GDI cannot see DWM
        // windows, so it is only the fallback for machines/sessions where WGC is unavailable.
        if (Wgc.TryCapture(x, y, width, height, out var composed))
        {
            return composed;
        }

        var screenDc = NativeMethods.User32.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        var memoryDc = IntPtr.Zero;
        var dib = IntPtr.Zero;
        try
        {
            memoryDc = NativeMethods.Gdi32.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                return null;
            }

            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            var info = new NativeMethods.BITMAPINFO { bmiHeader = header };
            dib = NativeMethods.Gdi32.CreateDIBSection(screenDc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return null;
            }

            NativeMethods.Gdi32.SelectObject(memoryDc, dib);
            if (!NativeMethods.Gdi32.BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, NativeMethods.SRCCOPY))
            {
                return null;
            }

            NativeMethods.Gdi32.GdiFlush();
            var stride = width * 4;
            var pixels = new byte[stride * height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return new BackdropFrame(width, height, stride, pixels);
        }
        catch (Exception)
        {
            // Capture is best-effort: the renderer falls back to the tinted fill.
            return null;
        }
        finally
        {
            if (dib != IntPtr.Zero)
            {
                NativeMethods.Gdi32.DeleteObject(dib);
            }

            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.Gdi32.DeleteDC(memoryDc);
            }

            NativeMethods.User32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
