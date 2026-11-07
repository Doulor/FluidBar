using FluidBar.PluginHost.Logging;
using FluidBar.PluginHost.Runtime;

namespace FluidBar.PluginHost.Tests;

public sealed class PluginLeaseTests
{
    [Fact]
    public async Task Cancelled_schedules_release_the_active_quota_before_their_callbacks_finish()
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lease = new PluginLease("timer", "test", NullPluginHostLogger.Instance, TimeProvider.System, CancellationToken.None);
        try
        {
            for (var i = 0; i < 100; i++)
                lease.SchedulePeriodic(TimeSpan.FromSeconds(1), _ => new ValueTask(finish.Task), TimeSpan.Zero).Dispose();
            Assert.False(lease.Completion.IsCompleted); // Unload must still await the cancelled callbacks.
        }
        finally { finish.TrySetResult(); await lease.Completion.WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    [Fact]
    public async Task Active_schedules_still_have_a_bounded_quota()
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lease = new PluginLease("timer", "test", NullPluginHostLogger.Instance, TimeProvider.System, CancellationToken.None);
        var handles = new List<IDisposable>();
        try
        {
            for (var i = 0; i < 64; i++)
                handles.Add(lease.ScheduleOnce(TimeSpan.Zero, _ => new ValueTask(finish.Task)));
            Assert.Throws<InvalidOperationException>(() => lease.ScheduleOnce(TimeSpan.Zero, _ => ValueTask.CompletedTask));
        }
        finally
        {
            foreach (var handle in handles) handle.Dispose();
            finish.TrySetResult();
            await lease.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public async Task Disposing_an_already_completed_schedule_is_safe()
    {
        using var lease = new PluginLease("timer", "test", NullPluginHostLogger.Instance, TimeProvider.System, CancellationToken.None);
        var schedule = lease.ScheduleOnce(TimeSpan.Zero, _ => ValueTask.CompletedTask);
        await lease.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        schedule.Dispose();
        schedule.Dispose();
    }
}
