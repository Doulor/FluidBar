using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>OLE-thread-only Shell fallback; normal pointer tracking never calls the Shell image helper.</summary>
internal sealed class FileDragFeedback(ShellFileDragData data, FileDragImage image, Task<bool> initialization) : IDisposable
{
    private IDropTargetHelper? _helper;
    private bool _attempted;
    private bool _entered;
    private bool _disposed;

    internal void Update(uint effect)
    {
        if (_disposed || !initialization.IsCompletedSuccessfully || initialization.Result) return;
        try
        {
            if (!_attempted)
            {
                _attempted = true;
                data.AttachImage(image);
                Marshal.ThrowExceptionForHR(NativeMethods.Ole32.CoCreateInstance(new Guid("4657278a-411b-11d2-839a-00c04fd918d0"),
                    IntPtr.Zero, 1, typeof(IDropTargetHelper).GUID, out var pointer));
                try { _helper = (IDropTargetHelper)Marshal.GetObjectForIUnknown(pointer); }
                finally { Marshal.Release(pointer); }
            }
            if (_helper is null || !NativeMethods.User32.GetCursorPos(out var point)) return;
            if (!_entered)
                _entered = _helper.DragEnter(NativeMethods.User32.WindowFromPoint(point), data.Data, ref point, effect) >= 0;
            else _helper.DragOver(ref point, effect);
            if (_entered) _helper.Show(true);
        }
        catch (ExternalException) { Dispose(); } // Cosmetic failures must not abort the file copy.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_helper is null) return;
        try { if (_entered) _helper.DragLeave(); }
        finally { Marshal.ReleaseComObject(_helper); }
    }

    [ComImport, Guid("4657278b-411b-11d2-839a-00c04fd918d0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTargetHelper
    {
        [PreserveSig] int DragEnter(IntPtr window, IDataObject data, ref NativeMethods.POINT point, uint effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int DragOver(ref NativeMethods.POINT point, uint effect);
        [PreserveSig] int Drop(IDataObject data, ref NativeMethods.POINT point, uint effect);
        [PreserveSig] int Show([MarshalAs(UnmanagedType.Bool)] bool show);
    }
}
