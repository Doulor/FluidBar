using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// OLE drop target for the layered island. Explorer and other modern drag sources use OLE rather than
/// the legacy WM_DROPFILES protocol; registering both keeps file drops working across shell versions.
/// </summary>
public sealed partial class LayeredWindowHost
{
    private const uint DropEffectCopy = 1; // DROPEFFECT_COPY
    private IDropTarget? _oleDropTarget;
    private bool _oleInitialized;

    private void EnableOleFileDrop(bool enabled)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!enabled)
        {
            if (_oleDropTarget is not null)
            {
                NativeMethods.Ole32.RevokeDragDrop(_hwnd);
                _oleDropTarget = null;
            }
            if (_oleInitialized)
            {
                NativeMethods.Ole32.OleUninitialize();
                _oleInitialized = false;
            }
            return;
        }

        if (_oleDropTarget is not null)
        {
            return;
        }

        // RegisterDragDrop requires an OLE-initialized STA. A failed registration leaves the legacy
        // DragAcceptFiles path active, so file drops still work on older shells.
        var hr = NativeMethods.Ole32.OleInitialize(IntPtr.Zero);
        if (hr < 0) // S_FALSE (already initialized) is fine, and still needs a matching uninitialize.
        {
            _logger.LogWarning(PlatformLogCategory.Platform,
                "layered-host.ole-init-failed", $"OLE initialization failed: 0x{hr:X8}.");
            return;
        }
        _oleInitialized = true;

        var target = new OleFileDropTarget(this);
        var targetPointer = Marshal.GetComInterfaceForObject(target, typeof(IDropTarget));
        int registerHr;
        try { registerHr = NativeMethods.Ole32.RegisterDragDrop(_hwnd, targetPointer); }
        finally { Marshal.Release(targetPointer); }
        if (registerHr != 0)
        {
            _logger.LogWarning(FluidBar.Platform.Windows.Diagnostics.PlatformLogCategory.Platform,
                "layered-host.ole-drop-failed", $"OLE RegisterDragDrop failed: 0x{registerHr:X8}.");
            NativeMethods.Ole32.OleUninitialize();
            _oleInitialized = false;
            return;
        }

        _oleDropTarget = target;
    }

    private static bool HasFileDrop(IDataObject data)
    {
        var format = FileDropFormat;
        try { return data.QueryGetData(ref format) == 0; }
        catch (COMException) { return false; }
    }

    private static IReadOnlyList<string> ReadFileDrop(IDataObject data)
    {
        var format = FileDropFormat;
        STGMEDIUM medium;
        try
        {
            data.GetData(ref format, out medium);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }

        try
        {
            return ReadDropFiles(medium.unionmember);
        }
        finally
        {
            NativeMethods.Ole32.ReleaseStgMedium(ref medium);
        }
    }

    private static FORMATETC FileDropFormat => new()
    {
        cfFormat = 15, // CF_HDROP
        dwAspect = DVASPECT.DVASPECT_CONTENT,
        lindex = -1,
        tymed = TYMED.TYMED_HGLOBAL,
    };

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class OleFileDropTarget(LayeredWindowHost owner) : IDropTarget
    {
        private bool _hasFiles;

        public int DragEnter(IDataObject data, uint keyState, POINTL pt, ref uint effect)
        {
            _hasFiles = (effect & DropEffectCopy) != 0 && data is not null && HasFileDrop(data);
            if (_hasFiles)
            {
                owner.FileDragEntered?.Invoke();
            }
            effect = _hasFiles ? effect & DropEffectCopy : 0;
            return 0;
        }

        public int DragOver(uint keyState, POINTL pt, ref uint effect)
        {
            effect = _hasFiles ? effect & DropEffectCopy : 0;
            return 0;
        }

        public int DragLeave()
        {
            if (_hasFiles)
            {
                owner.FileDragLeft?.Invoke();
            }
            _hasFiles = false;
            return 0;
        }

        public int Drop(IDataObject data, uint keyState, POINTL pt, ref uint effect)
        {
            var files = _hasFiles && (effect & DropEffectCopy) != 0 && HasFileDrop(data)
                ? ReadFileDrop(data) : Array.Empty<string>();
            _hasFiles = false;
            effect = files.Count > 0 ? DropEffectCopy : 0;
            if (files.Count > 0)
            {
                owner.FilesDropped?.Invoke(files);
            }
            else owner.FileDragLeft?.Invoke();

            return 0;
        }
    }

    [ComImport]
    [Guid("00000122-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTarget
    {
        [PreserveSig]
        int DragEnter([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keyState, POINTL pt, ref uint effect);

        [PreserveSig]
        int DragOver(uint keyState, POINTL pt, ref uint effect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int Drop([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keyState, POINTL pt, ref uint effect);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int x;
        public int y;
    }
}
