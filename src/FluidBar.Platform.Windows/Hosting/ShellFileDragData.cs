using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>Unpremultiplied BGRA drag bitmap, with the cursor offset from its top-left corner.</summary>
public sealed record FileDragImage(int Width, int Height, byte[] Pixels, int OffsetX, int OffsetY);

/// <summary>Shell owns file references, private drag-image formats and their media for the native drag lifetime.</summary>
internal sealed class ShellFileDragData : IDisposable
{
    public IDataObject Data { get; }
    private int _disposed;
    private ShellFileDragData(IDataObject data) => Data = data;

    public static ShellFileDragData Create(IReadOnlyList<string> paths, FileDragImage? image = null)
    {
        var inner = OleFileDrag.CreateDataObject(paths);
        Marshal.ThrowExceptionForHR(SHCreateDataObject(IntPtr.Zero, 0, IntPtr.Zero, null, typeof(IDataObject).GUID, out var data));
        var result = new ShellFileDragData(data);
        try
        {
            var formats = new FORMATETC[1];
            var fetched = new int[1];
            var enumerator = inner.EnumFormatEtc(DATADIR.DATADIR_GET);
            while (enumerator.Next(1, formats, fetched) == 0)
            {
                var format = formats[0];
                inner.GetData(ref format, out var medium);
                try { data.SetData(ref format, ref medium, true); }
                catch { NativeMethods.Ole32.ReleaseStgMedium(ref medium); throw; }
            }
            if (image is not null) AttachImage(data, image);
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static void AttachImage(IDataObject data, FileDragImage image)
    {
        if (image.Width is <= 0 or > 1024 || image.Height is <= 0 or > 1024
            || image.Pixels.Length != checked(image.Width * image.Height * 4))
            throw new ArgumentException("A drag image requires a bounded 32-bit BGRA bitmap.", nameof(image));
        var helperClass = new Guid("4657278a-411b-11d2-839a-00c04fd918d0");
        Marshal.ThrowExceptionForHR(NativeMethods.Ole32.CoCreateInstance(helperClass, IntPtr.Zero, 1,
            typeof(IDragSourceHelper).GUID, out var pointer));
        var helper = (IDragSourceHelper)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var info = new NativeMethods.BITMAPINFO { bmiHeader = new NativeMethods.BITMAPINFOHEADER
            { biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(), biWidth = image.Width,
                biHeight = -image.Height, biPlanes = 1, biBitCount = 32, biCompression = NativeMethods.BI_RGB } };
            bitmap = NativeMethods.Gdi32.CreateDIBSection(IntPtr.Zero, ref info, NativeMethods.DIB_RGB_COLORS,
                out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Marshal.Copy(image.Pixels, 0, bits, image.Pixels.Length);
            var dragImage = new DragImage
            { Size = new NativeMethods.SIZE(image.Width, image.Height),
                Offset = new NativeMethods.POINT { X = image.OffsetX, Y = image.OffsetY }, Bitmap = bitmap, ColorKey = 0xffffffff };
            // The helper serializes a copy into IDataObject. Keep arbitrary formats in the Shell wrapper.
            Marshal.ThrowExceptionForHR(helper.InitializeFromBitmap(ref dragImage, data));
        }
        finally
        {
            if (bitmap != IntPtr.Zero) NativeMethods.Gdi32.DeleteObject(bitmap);
            Marshal.ReleaseComObject(helper);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) Marshal.ReleaseComObject(Data);
    }

    [DllImport("shell32.dll")]
    private static extern int SHCreateDataObject(IntPtr folder, uint count, IntPtr children, IDataObject? inner,
        in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IDataObject data);

    [StructLayout(LayoutKind.Sequential)]
    private struct DragImage
    { public NativeMethods.SIZE Size; public NativeMethods.POINT Offset; public IntPtr Bitmap; public uint ColorKey; }

    [ComImport, Guid("de5bf786-477a-11d2-839d-00c04fd918d0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDragSourceHelper
    {
        [PreserveSig] int InitializeFromBitmap(ref DragImage image, IDataObject data);
        [PreserveSig] int InitializeFromWindow(IntPtr window, ref NativeMethods.POINT point, IDataObject data);
    }
}
