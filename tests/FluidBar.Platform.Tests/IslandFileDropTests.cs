using System.Runtime.InteropServices;
using System.Text;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Tests;

public sealed class IslandFileDropTests
{
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
