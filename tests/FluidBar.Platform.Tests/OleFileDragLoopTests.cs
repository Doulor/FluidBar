using System.Runtime.InteropServices;
using FluidBar.Core.Layout;
using FluidBar.Platform.Windows.Diagnostics;
using MonitorInfo = FluidBar.Platform.Windows.Display.MonitorInfo;
using PixelRect = FluidBar.Platform.Windows.Display.PixelRect;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

public sealed class OleFileDragLoopTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Windows_drag_loop_leaves_source_card_and_delivers_to_another_window(int buttonState)
    {
        if (!Environment.UserInteractive) return;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                NativeMethods.User32.SetThreadDpiAwarenessContext(new IntPtr(-4));
                NativeMethods.User32.GetCursorPos(out var cursor);
                using var source = CreatePatch(cursor.X - 48, cursor.Y - 48);
                using var target = CreatePatch(cursor.X - 48, cursor.Y + 112);
                source.SetFileDropEnabled(true);
                target.SetFileDropEnabled(true);
                var sourceDrops = 0;
                source.FilesDropped += _ => sourceDrops++;
                var switched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                source.ActivateRequested += () =>
                {
                    NativeMethods.User32.SetWindowPos(source.Handle, IntPtr.Zero, cursor.X - 250, cursor.Y - 48, 0, 0,
                        NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                    NativeMethods.User32.SetWindowPos(target.Handle, NativeMethods.HWND_TOPMOST, cursor.X - 48, cursor.Y - 48, 0, 0,
                        NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    switched.TrySetResult();
                };
                IReadOnlyList<string>? dropped = null;
                target.FilesDropped += paths => dropped = paths;
                var path = Path.Combine(Path.GetTempPath(), "cross-window-" + Guid.NewGuid().ToString("N") + ".txt");
                var released = false;
                var ownerThread = NativeMethods.Kernel32.GetCurrentThreadId();
                _ = Task.Run(async () =>
                {
                    var destination = new IntPtr((cursor.X & 0xffff) | (cursor.Y << 16));
                    await PrimeNativeLoopAsync(ownerThread, started.Task, destination);
                    // Change the native window under a stationary pointer. Shell imagery itself queries
                    // GetCursorPos; posting fictitious coordinates alone cannot model that transition.
                    NativeMethods.User32.PostMessageW(source.Handle, LayeredWindowHost.ActivateMessage, IntPtr.Zero, IntPtr.Zero);
                    await switched.Task;
                    PostThreadMessageW(ownerThread, 0x0200, new IntPtr(buttonState), destination);
                    await Task.Delay(120);
                    Volatile.Write(ref released, true);
                    PostThreadMessageW(ownerThread, 0x0202, IntPtr.Zero, destination);
                    await Task.Delay(1000);
                    if (!completed.Task.IsCompleted) PostThreadMessageW(ownerThread, 0x0012, IntPtr.Zero, IntPtr.Zero);
                });
                var copied = OleFileDrag.DragFiles([path], () =>
                { started.TrySetResult(); return Volatile.Read(ref released); },
                    new FileDragImage(64, 32, Enumerable.Repeat((byte)255, 64 * 32 * 4).ToArray(), -12, -12));
                Assert.True(copied);
                Assert.Equal(0, sourceDrops);
                Assert.Equal(path, Assert.Single(dropped!));
                completed.SetResult();
            }
            catch (Exception ex) { completed.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(6));
    }

    internal static LayeredWindowHost CreatePatch(int x, int y)
    {
        var host = new LayeredWindowHost(new LayeredHostOptions
        { Pacing = LayeredPacingMode.None, ReassertTopmost = false, EnableDrag = false });
        host.Create(new MonitorInfo("cross-window-copy", 0, new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040), 96, 96, true));
        host.HitRegion.SetShell(new VisualRect(0, 0, 96, 96), CornerRadii.Uniform(0));
        var frame = host.BeginFrame();
        frame.Pixels.Clear();
        for (var row = 0; row < 96; row++) frame.Pixels.Slice(row * frame.Stride, 96 * 4).Fill(255);
        host.CommitFrame();
        host.Show();
        NativeMethods.User32.SetWindowPos(host.Handle, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        return host;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows_drag_loop_delivers_a_copy_on_release_and_cancels_on_escape(bool cancel)
    {
        if (!Environment.UserInteractive) return;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? release = null;
        var thread = new Thread(() =>
        {
            try
            {
                NativeMethods.User32.SetThreadDpiAwarenessContext(new IntPtr(-4));
                using var target = new LayeredWindowHost(new LayeredHostOptions
                { Pacing = LayeredPacingMode.None, ReassertTopmost = false, EnableDrag = false });
                target.Create(new MonitorInfo("native-copy-loop", 0, new PixelRect(0, 0, 1920, 1080),
                    new PixelRect(0, 0, 1920, 1040), 96, 96, true));
                target.HitRegion.SetShell(new VisualRect(0, 0, 96, 96), CornerRadii.Uniform(0));
                target.SetFileDropEnabled(true);
                NativeMethods.User32.GetCursorPos(out var cursor);
                var frame = target.BeginFrame();
                frame.Pixels.Clear();
                for (var y = 0; y < 96; y++) frame.Pixels.Slice(y * frame.Stride, 96 * 4).Fill(255);
                target.CommitFrame();
                target.Show();
                NativeMethods.User32.SetWindowPos(target.Handle, NativeMethods.HWND_TOPMOST,
                    cursor.X - 48, cursor.Y - 48, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                var path = Path.Combine(Path.GetTempPath(), "native-loop-" + Guid.NewGuid().ToString("N") + ".txt");
                var entered = 0;
                target.FileDragEntered += () => entered++;
                var hitBefore = NativeMethods.User32.WindowFromPoint(cursor);
                IReadOnlyList<string>? dropped = null;
                target.FilesDropped += paths => dropped = paths;
                var messageHost = new PlatformMessageHost(NullPlatformLogger.Instance, new PlatformCounters());
                using var hook = new IslandMouseHook(messageHost, NullPlatformLogger.Instance, new PlatformCounters());
                hook.SetRegions([new IslandMouseHook.Rect(0, 0, 10, 10)]);
                hook.RouteHookMessage(1, 1, IslandMouseHook.WmLButtonDown);
                using var input = hook.TrySuspendForNativeDrag()!;
                var ownerThread = NativeMethods.Kernel32.GetCurrentThreadId();
                release = Task.Run(async () =>
                {
                    await PrimeNativeLoopAsync(ownerThread, started.Task,
                        new IntPtr((cursor.X & 0xffff) | (cursor.Y << 16)));
                    await Task.Delay(80);
                    if (cancel) PostThreadMessageW(ownerThread, 0x0100, new IntPtr(0x1b), IntPtr.Zero);
                    else
                    {
                        hook.RouteHookMessage(1, 1, IslandMouseHook.WmLButtonUp);
                        PostThreadMessageW(ownerThread, 0x0202, IntPtr.Zero,
                            new IntPtr((cursor.X & 0xffff) | (cursor.Y << 16)));
                    }
                    await Task.Delay(1000);
                    // Bound the probe even if a native drag loop regresses. No real desktop input is injected.
                    if (!completed.Task.IsCompleted) PostThreadMessageW(ownerThread, 0x0012, IntPtr.Zero, IntPtr.Zero);
                });
                var copied = OleFileDrag.DragFiles([path], () =>
                { started.TrySetResult(); return input.LeftButtonReleased; });
                if (cancel) { Assert.False(copied); Assert.Null(dropped); }
                else
                {
                    Assert.True(copied, $"Entered={entered}; hit=0x{hitBefore:X}; target=0x{target.Handle:X}; dropped={dropped?.Count ?? 0}");
                    Assert.Equal(path, Assert.Single(dropped!));
                }
                Assert.False(File.Exists(path));
                messageHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
                completed.SetResult();
            }
            catch (Exception ex) { completed.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(6));
        if (release is not null) await release;
    }

    internal static async Task PrimeNativeLoopAsync(uint ownerThread, Task started, IntPtr coordinates)
    {
        // SHDoDragDrop may wait for its first queued mouse message before querying the source.
        // Prime only this test STA queue; never depend on, move, or inject the user's real pointer.
        for (var attempt = 0; attempt < 80 && !started.IsCompleted; attempt++)
        {
            PostThreadMessageW(ownerThread, 0x0200, new IntPtr(1), coordinates);
            await Task.WhenAny(started, Task.Delay(25));
        }
        await started.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint thread, uint message, IntPtr wParam, IntPtr lParam);
}
