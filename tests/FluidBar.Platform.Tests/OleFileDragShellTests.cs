using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Tests;

public sealed class OleFileDragShellTests
{
    [Fact]
    public async Task Shell_drag_loop_shows_a_visible_drag_image_over_a_real_folder_target()
    {
        if (!Environment.UserInteractive) return;
        var root = Path.Combine(Path.GetTempPath(), "fluidbar-drag-image-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(destination);
        var path = Path.Combine(root, "preview.txt");
        await File.WriteAllTextAsync(path, "visible native preview");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            IShellItem? item = null;
            IDropTarget? target = null;
            try
            {
                NativeMethods.User32.SetThreadDpiAwarenessContext(new IntPtr(-4));
                Marshal.ThrowExceptionForHR(NativeMethods.Ole32.OleInitialize(IntPtr.Zero));
                NativeMethods.User32.GetCursorPos(out var cursor);
                using var window = OleFileDragLoopTests.CreatePatch(cursor.X - 48, cursor.Y - 48);
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(destination, IntPtr.Zero, typeof(IShellItem).GUID, out item));
                item.BindToHandler(IntPtr.Zero, new Guid("3981e225-f559-11d3-8e3a-00c04f6837d5"), typeof(IDropTarget).GUID, out target);
                var pointer = Marshal.GetComInterfaceForObject(target, typeof(IDropTarget));
                try { Marshal.ThrowExceptionForHR(NativeMethods.Ole32.RegisterDragDrop(window.Handle, pointer)); }
                finally { Marshal.Release(pointer); }
                var ownerThread = NativeMethods.Kernel32.GetCurrentThreadId();
                var released = false;
                var visible = false;
                var imageWindow = IntPtr.Zero;
                var observed = new List<string>();
                var release = Task.Run(async () =>
                {
                    await OleFileDragLoopTests.PrimeNativeLoopAsync(ownerThread, started.Task,
                        new IntPtr((cursor.X & 0xffff) | (cursor.Y << 16)));
                    await Task.Delay(100);
                    NativeMethods.User32.EnumWindows((handle, _) =>
                    {
                        var name = new char[128];
                        var length = NativeMethods.User32.GetClassNameW(handle, name, name.Length);
                        var className = new string(name, 0, length);
                        NativeMethods.User32.GetWindowThreadProcessId(handle, out var process);
                        if (process == Environment.ProcessId) observed.Add(className + ":" + NativeMethods.User32.IsWindowVisible(handle));
                        if (process == Environment.ProcessId && className == "SysDragImage" && NativeMethods.User32.IsWindowVisible(handle))
                        { visible = true; imageWindow = handle; }
                        return true;
                    }, IntPtr.Zero);
                    Volatile.Write(ref released, true);
                    PostThreadMessageW(ownerThread, 0x0202, IntPtr.Zero, new IntPtr((cursor.X & 0xffff) | (cursor.Y << 16)));
                    await Task.Delay(1000);
                    if (!completion.Task.IsCompleted) PostThreadMessageW(ownerThread, 0x0012, IntPtr.Zero, IntPtr.Zero);
                });
                var copied = OleFileDrag.DragFiles([path], () => { started.TrySetResult(); return Volatile.Read(ref released); },
                    new FileDragImage(64, 32, Enumerable.Repeat((byte)255, 64 * 32 * 4).ToArray(), -12, -12));
                NativeMethods.Ole32.RevokeDragDrop(window.Handle);
                Assert.True(copied);
                Assert.True(visible, "Windows must show the Shell drag image outside the source card. " + string.Join(", ", observed));
                Assert.False(NativeMethods.User32.IsWindowVisible(imageWindow), "Completion must remove the drag image.");
                completion.SetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally
            {
                if (target is not null) Marshal.ReleaseComObject(target);
                if (item is not null) Marshal.ReleaseComObject(item);
                NativeMethods.Ole32.OleUninitialize();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        try
        {
            thread.Start();
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(6));
            Assert.Equal(await File.ReadAllTextAsync(path), await File.ReadAllTextAsync(Path.Combine(destination, "preview.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint thread, uint message, IntPtr wParam, IntPtr lParam);

    [Fact]
    public async Task Shell_drag_data_keeps_the_drag_bitmap_for_cross_window_feedback()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Marshal.ThrowExceptionForHR(NativeMethods.Ole32.OleInitialize(IntPtr.Zero));
                try
                {
                    var pixels = Enumerable.Repeat((byte)255, 64 * 32 * 4).ToArray();
                    using var data = ShellFileDragData.Create([Path.Combine(Path.GetTempPath(), "preview.txt")],
                        new FileDragImage(64, 32, pixels, -12, -12));
                    var format = new FORMATETC
                    { cfFormat = unchecked((short)NativeMethods.User32.RegisterClipboardFormatW("DragImageBits")),
                        dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
                    Assert.Equal(0, data.Data.QueryGetData(ref format));
                    data.Data.GetData(ref format, out var medium);
                    try { Assert.NotEqual(IntPtr.Zero, medium.unionmember); }
                    finally { NativeMethods.Ole32.ReleaseStgMedium(ref medium); }
                }
                finally { NativeMethods.Ole32.OleUninitialize(); }
                completion.SetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    public async Task Windows_shell_folder_copies_file_references_without_removing_the_source(uint buttons)
    {
        var root = Path.Combine(Path.GetTempPath(), "fluidbar-shell-drag-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(destination);
        var source = Path.Combine(root, "暂存文件.txt");
        await File.WriteAllTextAsync(source, "file shelf shell integration");
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            IShellItem? item = null;
            IDropTarget? target = null;
            try
            {
                Marshal.ThrowExceptionForHR(NativeMethods.Ole32.OleInitialize(IntPtr.Zero));
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(destination, IntPtr.Zero, typeof(IShellItem).GUID, out item));
                item.BindToHandler(IntPtr.Zero, new Guid("3981e225-f559-11d3-8e3a-00c04f6837d5"), typeof(IDropTarget).GUID, out target);
                using var payload = ShellFileDragData.Create([source], new FileDragImage(64, 32,
                    Enumerable.Repeat((byte)255, 64 * 32 * 4).ToArray(), -12, -12));
                var data = payload.Data;
                uint effect = 1;
                Marshal.ThrowExceptionForHR(target.DragEnter(data, buttons, default, ref effect));
                Assert.Equal(1u, effect);
                effect = 1;
                Marshal.ThrowExceptionForHR(target.Drop(data, 0, default, ref effect));
                Assert.Equal(1u, effect);
                completed.SetResult();
            }
            catch (Exception ex) { completed.TrySetException(ex); }
            finally
            {
                if (target is not null) Marshal.ReleaseComObject(target);
                if (item is not null) Marshal.ReleaseComObject(item);
                NativeMethods.Ole32.OleUninitialize();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        try
        {
            thread.Start();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var copy = Path.Combine(destination, Path.GetFileName(source));
            Assert.Equal(await File.ReadAllTextAsync(source), await File.ReadAllTextAsync(copy));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr context, in Guid handler, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IDropTarget target);
    }

    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTarget
    {
        [PreserveSig] int DragEnter(IDataObject data, uint keys, Point point, ref uint effect);
        [PreserveSig] int DragOver(uint keys, Point point, ref uint effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop(IDataObject data, uint keys, Point point, ref uint effect);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
}
