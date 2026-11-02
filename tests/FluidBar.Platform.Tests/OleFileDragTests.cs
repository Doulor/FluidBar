using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

public sealed class OleFileDragTests
{
    [Fact]
    public void Native_data_object_vtable_returns_a_standard_HDROP_payload()
    {
        var path = Path.Combine(Path.GetTempPath(), "COM-拖出.txt");
        var data = OleFileDrag.CreateDataObject([path]);
        var pointer = Marshal.GetComInterfaceForObject(data, typeof(IDataObject));
        try
        {
            var get = Marshal.GetDelegateForFunctionPointer<GetData>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), 3 * IntPtr.Size));
            var format = new FORMATETC { cfFormat = 15, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
            Assert.Equal(0, get(pointer, ref format, out var medium));
            try
            {
                var buffer = new char[2049];
                var read = NativeMethods.Shell32.DragQueryFileW(medium.unionmember, 0, buffer, (uint)buffer.Length);
                Assert.Equal(path, new string(buffer, 0, (int)read));
            }
            finally { NativeMethods.Ole32.ReleaseStgMedium(ref medium); }
        }
        finally { Marshal.Release(pointer); }
    }

    [Fact]
    public void Data_object_exposes_unicode_files_folders_and_copy_effect_without_touching_originals()
    {
        var paths = new[] { Path.Combine(Path.GetTempPath(), "拖出-" + Guid.NewGuid().ToString("N") + ".txt"),
            Path.Combine(Path.GetTempPath(), "文件夹-" + Guid.NewGuid().ToString("N")) };
        var data = OleFileDrag.CreateDataObject(paths);
        var enumerator = data.EnumFormatEtc(DATADIR.DATADIR_GET);
        var formats = new FORMATETC[2];
        var fetched = new int[1];
        Assert.Equal(0, enumerator.Next(2, formats, fetched));
        Assert.Equal(2, fetched[0]);
        Assert.Equal(15, formats[0].cfFormat);
        data.GetData(ref formats[0], out var medium);
        try
        {
            Assert.Equal(2u, NativeMethods.Shell32.DragQueryFileW(medium.unionmember, uint.MaxValue, null, 0));
            for (uint i = 0; i < paths.Length; i++)
            {
                var buffer = new char[2049];
                var read = NativeMethods.Shell32.DragQueryFileW(medium.unionmember, i, buffer, (uint)buffer.Length);
                Assert.Equal(paths[i], new string(buffer, 0, (int)read));
            }
        }
        finally { NativeMethods.Ole32.ReleaseStgMedium(ref medium); }
        data.GetData(ref formats[1], out medium);
        try
        {
            var pointer = NativeMethods.Kernel32.GlobalLock(medium.unionmember);
            try { Assert.Equal(1, Marshal.ReadInt32(pointer)); }
            finally { NativeMethods.Kernel32.GlobalUnlock(medium.unionmember); }
        }
        finally { NativeMethods.Ole32.ReleaseStgMedium(ref medium); }
        Assert.False(File.Exists(paths[0]));
        Assert.False(Directory.Exists(paths[1]));
        Assert.Equal(1, enumerator.Next(1, new FORMATETC[1], fetched));
        Assert.Equal(0, enumerator.Reset());
        Assert.Equal(0, enumerator.Skip(1));
        enumerator.Clone(out var clone);
        var last = new FORMATETC[1];
        Assert.Equal(0, clone.Next(1, last, fetched));
        Assert.Equal(formats[1].cfFormat, last[0].cfFormat);
    }

    [Theory]
    [InlineData(true, 1u, OleFileDrag.Cancel)]
    [InlineData(false, 1u, 0)]
    [InlineData(false, 0u, OleFileDrag.Drop)]
    public void Native_drop_source_handles_escape_and_release(bool escape, uint keys, int expected)
    {
        var source = new OleFileDrag.FileDropSource(null);
        var pointer = Marshal.GetComInterfaceForObject(source, typeof(OleFileDrag.IDropSource));
        try
        {
            var callback = Marshal.GetDelegateForFunctionPointer<ContinueDrag>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), 3 * IntPtr.Size));
            Assert.Equal(expected, callback(pointer, escape ? 1 : 0, keys));
        }
        finally { Marshal.Release(pointer); }
    }

    [Fact]
    public async Task Native_drag_scope_hands_release_to_OLE_and_restores_normal_clicks_without_a_phantom_press()
    {
        await using var host = new PlatformMessageHost(NullPlatformLogger.Instance, new PlatformCounters());
        using var hook = new IslandMouseHook(host, NullPlatformLogger.Instance, new PlatformCounters());
        hook.SetRegions([new IslandMouseHook.Rect(100, 100, 300, 300)]);
        hook.RouteHookMessage(150, 150, IslandMouseHook.WmLButtonDown);
        using (var scope = hook.TrySuspendForNativeDrag()!)
        {
            Assert.NotNull(scope);
            var source = new OleFileDrag.FileDropSource(() => scope.LeftButtonReleased);
            Assert.Equal(0, source.QueryContinueDrag(false, 0)); // The initiating hook consumed the native button-down.
            Assert.False(hook.RouteHookMessage(150, 150, IslandMouseHook.WmRButtonDown).Consume);
            Assert.False(hook.RouteHookMessage(150, 150, IslandMouseHook.WmMouseWheel, 1).Consume);
            var up = hook.RouteHookMessage(150, 150, IslandMouseHook.WmLButtonUp);
            Assert.False(up.Consume);
            Assert.False(up.Released);
            Assert.True(scope.LeftButtonReleased);
            Assert.Equal(OleFileDrag.Drop, source.QueryContinueDrag(false, 0));
            Assert.Equal(OleFileDrag.Cancel, source.QueryContinueDrag(true, 1));
        }
        Assert.False(hook.RouteHookMessage(150, 150, IslandMouseHook.WmMouseMove).Dragged);
        Assert.True(hook.RouteHookMessage(150, 150, IslandMouseHook.WmLButtonDown).Pressed);
        Assert.True(hook.RouteHookMessage(150, 150, IslandMouseHook.WmLButtonUp).Released);
        Assert.Null(hook.TrySuspendForNativeDrag()); // A queued move after release must not hang in DoDragDrop.
    }

    [Fact]
    public void Rejects_unbounded_or_non_absolute_file_lists()
    {
        Assert.Throws<ArgumentException>(() => OleFileDrag.CreateDataObject([]));
        Assert.Throws<ArgumentException>(() => OleFileDrag.CreateDataObject(["relative.txt"]));
        Assert.Throws<ArgumentException>(() => OleFileDrag.CreateDataObject([Path.GetTempPath() + "bad\0.txt"]));
        Assert.Throws<ArgumentException>(() => OleFileDrag.CreateDataObject(Enumerable.Repeat(Path.GetTempPath(), 33).ToArray()));
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ContinueDrag(IntPtr source, int escape, uint keys);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetData(IntPtr data, ref FORMATETC format, out STGMEDIUM medium);
}
