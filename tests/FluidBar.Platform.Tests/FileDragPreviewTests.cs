using System.Diagnostics;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Interop;
using Xunit.Abstractions;

namespace FluidBar.Platform.Tests;

public sealed class FileDragPreviewTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Preview_tracks_pointer_while_the_source_thread_is_blocked_without_ole_feedback()
    {
        var cursorX = -28000;
        await using var preview = new FileDragPreview(Image(), () => new NativeMethods.POINT(Volatile.Read(ref cursorX), -28000));
        Assert.True(await preview.Initialization.WaitAsync(TimeSpan.FromSeconds(3)));
        var handle = preview.Handle;
        AssertPosition(handle, -27984, -27984);
        var probe = Task.Run(async () =>
        {
            // No GiveFeedback calls or source-thread dispatch during this sequence.
            var timings = new List<double>();
            foreach (var x in new[] { -27800, -27600, -27400 })
            {
                var watch = Stopwatch.StartNew();
                Volatile.Write(ref cursorX, x);
                while (watch.Elapsed < TimeSpan.FromMilliseconds(250))
                {
                    Assert.True(NativeMethods.User32.GetWindowRect(handle, out var rect));
                    if (rect.Left == x + 16) break;
                    await Task.Delay(2);
                }
                AssertPosition(handle, x + 16, -27984);
                timings.Add(watch.Elapsed.TotalMilliseconds);
            }
            output.WriteLine("Pointer-to-window latency during source stall (ms): " + string.Join(", ", timings.Select(t => t.ToString("F2"))));
        });
        // Deliberately reproduce a busy OLE/source UI thread. The native preview has its own pump.
        Thread.Sleep(350);
        await probe;
        var styles = NativeMethods.User32.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        Assert.NotEqual(0, styles & NativeMethods.WS_EX_TRANSPARENT);
        Assert.NotEqual(0, styles & NativeMethods.WS_EX_NOACTIVATE);
        Assert.Equal(new IntPtr(NativeMethods.HTTRANSPARENT),
            NativeMethods.User32.SendMessageW(handle, NativeMethods.WM_NCHITTEST, IntPtr.Zero, IntPtr.Zero));
        preview.Dispose();
        await preview.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(NativeMethods.User32.IsWindow(handle));
        preview.Dispose();
    }

    [Fact]
    public async Task Creation_failure_releases_the_preview_window_and_pump()
    {
        await using var preview = new FileDragPreview(Image(),
            () => throw new InvalidOperationException("Unavailable cursor"));
        Assert.False(await preview.Initialization.WaitAsync(TimeSpan.FromSeconds(3)));
        await preview.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(IntPtr.Zero, FindWindowW(null, FileDragPreview.WindowTitle));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_is_removed_on_release_or_escape_before_target_copying_starts(bool escape)
    {
        await using var preview = new FileDragPreview(Image(), () => new NativeMethods.POINT(-28000, -28000));
        Assert.True(await preview.Initialization.WaitAsync(TimeSpan.FromSeconds(3)));
        var handle = preview.Handle;
        var released = false;
        var source = new OleFileDrag.FileDropSource(() => released, preview.Dispose);
        Assert.Equal(0, source.QueryContinueDrag(false, 1));
        Assert.True(NativeMethods.User32.IsWindowVisible(handle));
        released = true;
        Assert.Equal(escape ? OleFileDrag.Cancel : OleFileDrag.Drop, source.QueryContinueDrag(escape, 0));
        await preview.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(NativeMethods.User32.IsWindow(handle));
    }

    [Fact]
    public async Task Disposal_during_startup_never_shows_a_late_preview()
    {
        await using var preview = new FileDragPreview(Image(), () => new NativeMethods.POINT(-28000, -28000));
        preview.Dispose();
        await preview.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(NativeMethods.User32.IsWindow(preview.Handle));
    }

    [Fact]
    public async Task Slow_preview_initialization_does_not_block_the_source_or_its_dispose_callback()
    {
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        await using var preview = new FileDragPreview(Image(), () =>
        {
            entered.Set();
            if (!resume.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
            return new NativeMethods.POINT(-28000, -28000);
        });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            // With the worker deliberately blocked, a synchronous source wait would time out here.
            await Task.Run(preview.Dispose).WaitAsync(TimeSpan.FromMilliseconds(250));
        }
        finally { resume.Set(); }
        await preview.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(NativeMethods.User32.IsWindow(preview.Handle));
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1025, 10, 0)]
    [InlineData(10, 10, 8)]
    public void Invalid_bitmap_is_rejected_before_a_native_window_is_started(int width, int height, int bytes)
        => Assert.Throws<ArgumentException>(() => new FileDragPreview(new FileDragImage(width, height, new byte[bytes], 0, 0)));

    private static FileDragImage Image() => new(64, 32, Enumerable.Repeat((byte)180, 64 * 32 * 4).ToArray(), -16, -16);

    private static void AssertPosition(IntPtr handle, int x, int y)
    {
        Assert.True(NativeMethods.User32.GetWindowRect(handle, out var rect));
        Assert.Equal(x, rect.Left);
        Assert.Equal(y, rect.Top);
        Assert.Equal(64, rect.Right - rect.Left);
        Assert.Equal(32, rect.Bottom - rect.Top);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? className, string? title);
}
