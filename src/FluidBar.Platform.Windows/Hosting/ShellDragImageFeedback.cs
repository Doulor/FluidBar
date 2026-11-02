using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>Keeps source feedback visible even over applications that do not draw Shell drag images.</summary>
internal sealed class ShellDragImageFeedback : IDisposable
{
    private readonly IDataObject _data;
    private readonly IDropTargetHelper _helper;
    private bool _entered;

    public ShellDragImageFeedback(IDataObject data)
    {
        _data = data;
        Marshal.ThrowExceptionForHR(NativeMethods.Ole32.CoCreateInstance(new Guid("4657278a-411b-11d2-839a-00c04fd918d0"),
            IntPtr.Zero, 1, typeof(IDropTargetHelper).GUID, out var pointer));
        try { _helper = (IDropTargetHelper)Marshal.GetObjectForIUnknown(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public void Update(uint effect)
    {
        if (!NativeMethods.User32.GetCursorPos(out var point)) return;
        if (!_entered)
        {
            _entered = _helper.DragEnter(NativeMethods.User32.WindowFromPoint(point), _data, ref point, effect) >= 0;
        }
        else _helper.DragOver(ref point, effect);
        if (_entered) _helper.Show(true);
    }

    public void Dispose()
    {
        if (_entered) _helper.DragLeave();
        Marshal.ReleaseComObject(_helper);
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
