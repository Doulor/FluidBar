using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Lifecycle of the app's single notification listener: the active-instance counter is what proves the
/// page and plugin paths share one listener, and disposal must release it exactly once.
/// </summary>
public sealed class NotificationServiceLifecycleTests
{
    [Fact]
    public async Task Disposal_IsIdempotent_AndReleasesTheSharedListenerOnce()
    {
        var service = new NotificationService(NullPlatformLogger.Instance, new PlatformCounters());
        Assert.Equal(PlatformMonitorState.Stopped, service.State);
        Assert.Equal(0, NotificationService.ActiveInstanceCount);

        await service.DisposeAsync();
        await service.DisposeAsync();

        Assert.Equal(PlatformMonitorState.Disposed, service.State);
        Assert.Equal(0, NotificationService.ActiveInstanceCount);
    }

    [Fact]
    public async Task Disposal_AfterStop_DoesNotReleaseTwice()
    {
        var service = new NotificationService(NullPlatformLogger.Instance, new PlatformCounters());
        await service.StopAsync();
        await service.DisposeAsync();

        Assert.Equal(0, NotificationService.ActiveInstanceCount);
        Assert.Equal(PlatformMonitorState.Disposed, service.State);
    }

    [Fact]
    public void SeededIdsAndPrimedState_StartEmpty()
    {
        var service = new NotificationService(NullPlatformLogger.Instance, new PlatformCounters());

        Assert.False(service.IsPrimed);
        Assert.Empty(service.SeededIds);
        Assert.Null(service.Current);
    }
}
