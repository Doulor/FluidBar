using FluidBar.App.Hosting.Skia;

namespace FluidBar.Ui.Tests;

public sealed class FrameClockProbeTests
{
    [Fact]
    public async Task FrameClock_TicksWithin500ms()
    {
        using var clock = new SkiaFrameClock();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clock.Start(_ => tcs.TrySetResult());
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(500));
        clock.Stop();
        Assert.Same(tcs.Task, completed);
    }
}
