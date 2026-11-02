using System.Runtime.InteropServices;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Tests;

public sealed class IslandFileDropTests
{
    [Fact]
    public async Task Ole_drop_target_honors_copy_permissions_and_is_released_with_the_window()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var host = new LayeredWindowHost(new LayeredHostOptions
                { Pacing = LayeredPacingMode.None, ReassertTopmost = false }, logger: NullPlatformLogger.Instance, counters: new PlatformCounters());
                host.Create(new MonitorInfo("ole-drop-test", 0, new PixelRect(0, 0, 1920, 1080),
                    new PixelRect(0, 0, 1920, 1040), 96, 96, true), LayeredHostMode.Float);
                host.SetFileDropEnabled(true);
                var field = typeof(LayeredWindowHost).GetField("_oleDropTarget", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var target = Assert.IsAssignableFrom<object>(field.GetValue(host));
                using var native = new NativeDropTarget(target, field.FieldType);
                var original = Path.Combine(Path.GetTempPath(), "ole-reference-" + Guid.NewGuid().ToString("N") + ".txt");
                var data = new FileDataObject(original);
                var effect = 2u; // source offers Move only
                Assert.Equal(0, native.Enter(data, ref effect));
                Assert.Equal(0u, effect);
                effect = 1u;
                Assert.Equal(0, native.Enter(data, ref effect));
                Assert.Equal(1u, effect);
                IReadOnlyList<string>? dropped = null;
                host.FilesDropped += paths => dropped = paths;
                Assert.Equal(0, native.Drop(data, ref effect));
                Assert.Equal(original, Assert.Single(dropped!));
                Assert.Equal(1u, effect);
                Assert.False(File.Exists(original));
                host.Dispose();
                Assert.Null(field.GetValue(host));
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    // Cross the native CCW vtable, rather than bypassing COM through MethodInfo.Invoke.
    private sealed class NativeDropTarget : IDisposable
    {
        private readonly IntPtr _target;
        private readonly DragCallback _enter;
        private readonly DragCallback _drop;

        public NativeDropTarget(object target, Type contract)
        {
            _target = Marshal.GetComInterfaceForObject(target, contract);
            var vtable = Marshal.ReadIntPtr(_target);
            _enter = Marshal.GetDelegateForFunctionPointer<DragCallback>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
            _drop = Marshal.GetDelegateForFunctionPointer<DragCallback>(Marshal.ReadIntPtr(vtable, 6 * IntPtr.Size));
        }

        public int Enter(IDataObject data, ref uint effect) => Invoke(_enter, data, ref effect);
        public int Drop(IDataObject data, ref uint effect) => Invoke(_drop, data, ref effect);

        private int Invoke(DragCallback callback, IDataObject data, ref uint effect)
        {
            var pointer = Marshal.GetComInterfaceForObject(data, typeof(IDataObject));
            try { return callback(_target, pointer, 0, default, ref effect); }
            finally { Marshal.Release(pointer); }
        }

        public void Dispose() => Marshal.Release(_target);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DragCallback(IntPtr target, IntPtr data, uint keys, Point point, ref uint effect);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int X; public int Y; }
    }

    private sealed class FileDataObject(string path) : IDataObject
    {
        public int QueryGetData(ref FORMATETC format) => format.cfFormat == 15 ? 0 : unchecked((int)0x80040064);
        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            var bytes = Encoding.Unicode.GetBytes(path + "\0\0");
            var memory = NativeMethods.Kernel32.GlobalAlloc(0x0002, (UIntPtr)(20 + bytes.Length));
            Assert.NotEqual(IntPtr.Zero, memory);
            var pointer = NativeMethods.Kernel32.GlobalLock(memory);
            Assert.NotEqual(IntPtr.Zero, pointer);
            Marshal.Copy(new byte[20], 0, pointer, 20);
            Marshal.WriteInt32(pointer, 0, 20);
            Marshal.WriteInt32(pointer, 16, 1);
            Marshal.Copy(bytes, 0, IntPtr.Add(pointer, 20), bytes.Length);
            NativeMethods.Kernel32.GlobalUnlock(memory);
            medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
        }
        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw new NotSupportedException();
        public int GetCanonicalFormatEtc(ref FORMATETC input, out FORMATETC output) { output = input; return 1; }
        public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release) => throw new NotSupportedException();
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction) => throw new NotSupportedException();
        public int DAdvise(ref FORMATETC format, ADVF flags, IAdviseSink sink, out int connection) { connection = 0; return unchecked((int)0x80040003); }
        public void DUnadvise(int connection) { }
        public int EnumDAdvise(out IEnumSTATDATA? enumerator) { enumerator = null; return unchecked((int)0x80040003); }
    }

    [Fact]
    public void Island_file_drop_is_opt_in_and_delivers_references_without_touching_files()
    {
        if (!Environment.UserInteractive) return;
        using var host = new LayeredWindowHost(new LayeredHostOptions
        { Pacing = LayeredPacingMode.None, ReassertTopmost = false }, logger: NullPlatformLogger.Instance, counters: new PlatformCounters());
        host.Create(new MonitorInfo("drop-test", 0, new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040), 96, 96, true), LayeredHostMode.Float);
        const long acceptsFiles = 0x10;
        Assert.Equal(0, NativeMethods.User32.GetWindowLongPtr(host.Handle, NativeMethods.GWL_EXSTYLE).ToInt64() & acceptsFiles);
        host.SetFileDropEnabled(true);
        Assert.NotEqual(0, NativeMethods.User32.GetWindowLongPtr(host.Handle, NativeMethods.GWL_EXSTYLE).ToInt64() & acceptsFiles);
        IReadOnlyList<string>? dropped = null;
        host.FilesDropped += paths => dropped = paths;
        var original = Path.Combine(Path.GetTempPath(), "fluidbar-reference-only-" + Guid.NewGuid().ToString("N") + ".txt");
        var encoded = Encoding.Unicode.GetBytes(original + "\0\0");
        var memory = NativeMethods.Kernel32.GlobalAlloc(0x0002, (UIntPtr)(20 + encoded.Length));
        Assert.NotEqual(IntPtr.Zero, memory);
        var pointer = NativeMethods.Kernel32.GlobalLock(memory);
        Assert.NotEqual(IntPtr.Zero, pointer);
        Marshal.Copy(new byte[20], 0, pointer, 20);
        Marshal.WriteInt32(pointer, 0, 20);
        Marshal.WriteInt32(pointer, 16, 1);
        Marshal.Copy(encoded, 0, IntPtr.Add(pointer, 20), encoded.Length);
        NativeMethods.Kernel32.GlobalUnlock(memory);
        // WM_DROPFILES transfers ownership: the window calls DragFinish even if a consumer fails.
        NativeMethods.User32.SendMessageW(host.Handle, 0x0233, memory, IntPtr.Zero);
        Assert.Equal(original, Assert.Single(dropped!));
        Assert.False(File.Exists(original));
        host.SetFileDropEnabled(false);
        Assert.Equal(0, NativeMethods.User32.GetWindowLongPtr(host.Handle, NativeMethods.GWL_EXSTYLE).ToInt64() & acceptsFiles);
    }
}
