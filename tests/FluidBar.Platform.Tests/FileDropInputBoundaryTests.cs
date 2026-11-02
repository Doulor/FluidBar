using System.Reflection;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

public sealed class FileDropInputBoundaryTests
{
    [Fact]
    public async Task Hook_callback_does_not_swallow_an_external_drop_or_dispatch_a_card_click()
    {
        var counters = new PlatformCounters();
        await using var host = new PlatformMessageHost(NullPlatformLogger.Instance, counters);
        using var hook = new IslandMouseHook(host, NullPlatformLogger.Instance, counters);
        hook.SetRegions([new IslandMouseHook.Rect(100, 100, 300, 300)]);
        var released = 0;
        hook.PointerReleased += (_, _) => released++;
        var callback = typeof(IslandMouseHook).GetMethod("HookCallback", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var data = Marshal.AllocHGlobal(32); // MSLLHOOKSTRUCT, with no actual desktop input injected.
        try
        {
            Marshal.Copy(new byte[32], 0, data, 32);
            Marshal.WriteInt32(data, 0, 50);
            Marshal.WriteInt32(data, 4, 50);
            callback.Invoke(hook, [0, new IntPtr(IslandMouseHook.WmLButtonDown), data]);
            Marshal.WriteInt32(data, 0, 150);
            Marshal.WriteInt32(data, 4, 150);
            callback.Invoke(hook, [0, new IntPtr(IslandMouseHook.WmMouseMove), data]);
            var result = callback.Invoke(hook, [0, new IntPtr(IslandMouseHook.WmLButtonUp), data]);
            Assert.Equal(IntPtr.Zero, result); // Let the drag source/OLE see the button release.
            Assert.Equal(0, released);
        }
        finally { Marshal.FreeHGlobal(data); }
    }
}
