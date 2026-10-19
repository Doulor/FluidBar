using System.Collections.Immutable;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

/// <summary>
/// The arrivals-only view that lets the page and the plugin share one listener without replaying the
/// seeded backlog as a stale compact activity.
/// </summary>
public sealed class NewArrivalsNotificationSourceTests
{
    private sealed class FakeSource : INotificationSource
    {
        private NotificationsSnapshot? _current;

        public NotificationsSnapshot? Current => _current;

        public NotificationAccessState AccessState { get; set; } = NotificationAccessState.Allowed;

        public int RequestAccessCalls { get; private set; }

        public event EventHandler<SnapshotEventArgs<NotificationsSnapshot>>? SnapshotChanged;

        public Task<NotificationAccessState> RequestAccessAsync(CancellationToken cancellationToken = default)
        {
            RequestAccessCalls++;
            return Task.FromResult(AccessState);
        }

        public void Set(NotificationsSnapshot? snapshot, bool raise = true)
        {
            _current = snapshot;
            if (raise && snapshot is not null)
            {
                SnapshotChanged?.Invoke(this, new SnapshotEventArgs<NotificationsSnapshot>(snapshot));
            }
        }
    }

    private static NotificationEntry Entry(uint id)
        => new(id, "App", $"title-{id}", "body", DateTimeOffset.UnixEpoch.AddSeconds(id), null);

    private static NotificationsSnapshot Snapshot(params uint[] ids)
        => new(NotificationAccessState.Allowed, ids.Select(Entry).ToImmutableArray());

    [Fact]
    public void View_KeepsTheSharedInnerInstance()
    {
        var inner = new FakeSource();
        var view = new NewArrivalsNotificationSource(inner);

        Assert.Same(inner, view.Inner);
    }

    [Fact]
    public void Baseline_HidesTheToastsPresentWhenTheViewWasCreated()
    {
        var inner = new FakeSource();
        inner.Set(Snapshot(1, 2));
        var view = new NewArrivalsNotificationSource(inner);

        Assert.Equal(2, view.BaselineCount);
        Assert.Empty(view.Current!.Recent);

        inner.Set(Snapshot(1, 2, 3));
        Assert.Equal([3u], view.Current!.Recent.Select(entry => entry.Id).ToArray());
        Assert.Equal(3u, view.Current.Latest!.Id);
    }

    [Fact]
    public void Baseline_WaitsForTheFirstSnapshotWhenTheListenerIsStillStarting()
    {
        var inner = new FakeSource();
        var view = new NewArrivalsNotificationSource(inner);

        Assert.Null(view.Current);
        Assert.False(view.IsBaselineReady);

        // The listener's first publish is the seed: it becomes the backlog, not an arrival.
        inner.Set(Snapshot(5, 6));
        Assert.True(view.IsBaselineReady);
        Assert.Empty(view.Current!.Recent);

        inner.Set(Snapshot(5, 6, 7));
        Assert.Equal([7u], view.Current!.Recent.Select(entry => entry.Id).ToArray());
    }

    [Fact]
    public void EmptyBacklog_LetsTheFirstArrivalThrough()
    {
        var inner = new FakeSource();
        inner.Set(Snapshot());
        var view = new NewArrivalsNotificationSource(inner);
        Assert.Equal(0, view.BaselineCount);

        inner.Set(Snapshot(9));
        Assert.Equal([9u], view.Current!.Recent.Select(entry => entry.Id).ToArray());
    }

    [Fact]
    public void Events_ForwardOnlyTheArrivals()
    {
        var inner = new FakeSource();
        inner.Set(Snapshot(1, 2));
        var view = new NewArrivalsNotificationSource(inner);
        NotificationsSnapshot? forwarded = null;
        view.SnapshotChanged += (_, args) => forwarded = args.Snapshot;

        inner.Set(Snapshot(1, 2, 3, 4));

        Assert.NotNull(forwarded);
        Assert.Equal([3u, 4u], forwarded!.Recent.Select(entry => entry.Id).ToArray());
        Assert.Equal(NotificationAccessState.Allowed, forwarded.Access);
    }

    [Fact]
    public void Events_AreSuppressedWhileTheBaselineIsUnknown()
    {
        var inner = new FakeSource();
        var view = new NewArrivalsNotificationSource(inner);
        var raised = 0;
        view.SnapshotChanged += (_, _) => raised++;

        // First publish only establishes the baseline; it must not reach the plugin as an arrival.
        inner.Set(Snapshot(1));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Project_ReturnsTheSameSnapshotWhenNothingIsFiltered()
    {
        var snapshot = Snapshot(3, 4);

        Assert.Same(snapshot, NewArrivalsNotificationSource.Project(snapshot, new HashSet<uint> { 1, 2 }));
        Assert.Same(snapshot, NewArrivalsNotificationSource.Project(snapshot, new HashSet<uint>()));
    }

    [Fact]
    public async Task AccessAndPermissionRequest_AreForwardedToTheSharedListener()
    {
        var inner = new FakeSource { AccessState = NotificationAccessState.Allowed };
        var view = new NewArrivalsNotificationSource(inner);

        Assert.Equal(NotificationAccessState.Allowed, view.AccessState);
        var access = await view.RequestAccessAsync();
        Assert.Equal(NotificationAccessState.Allowed, access);
        Assert.Equal(1, inner.RequestAccessCalls);
    }

    [Fact]
    public void AThrowingConsumer_CannotBreakTheSharedListener()
    {
        var inner = new FakeSource();
        inner.Set(Snapshot(1));
        var view = new NewArrivalsNotificationSource(inner);
        view.SnapshotChanged += (_, _) => throw new InvalidOperationException("plugin path failed");

        // The plugin handler throws, yet the shared listener's publisher must not see the exception.
        var exception = Record.Exception(() => inner.Set(Snapshot(1, 2)));

        Assert.Null(exception);
    }

    [Fact]
    public void View_HidesOnlyTheSeededEntriesOfTheSharedSnapshot()
    {
        var inner = new FakeSource();
        inner.Set(Snapshot(10, 11, 12));
        var view = new NewArrivalsNotificationSource(inner);

        inner.Set(Snapshot(10, 11, 12, 13));

        Assert.Equal(4, view.Inner.Current!.Recent.Length);
        Assert.Single(view.Current!.Recent);
        Assert.Equal(13u, view.Current.Latest!.Id);
    }
}
