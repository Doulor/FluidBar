using System.Collections.Immutable;
using System.Reflection;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

public sealed class NotificationUndoTests
{
    [Fact]
    public async Task Clear_undo_preserves_read_state_and_concurrent_arrivals()
    {
        await using var service = new NotificationService(NullPlatformLogger.Instance, new PlatformCounters());
        var older = new NotificationEntry(1, "Mail", "First", "Body", DateTimeOffset.UtcNow.AddMinutes(-3), null);
        var newer = older with { Id = 2, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2) };
        Seed(service, [newer, older]);
        await Changed(service, () => service.MarkRead(older.Id));
        await Changed(service, service.ClearRecent);
        Assert.True(service.CanUndo);
        Assert.Empty(service.Current!.Recent);
        var arrival = older with { Id = 3, CreatedAt = DateTimeOffset.UtcNow };
        Seed(service, [arrival]);
        await Changed(service, service.UndoLocalRemoval);
        Assert.True(service.Current!.IsLocalUpdate);
        Assert.Equal(new uint[] { 3, 2, 1 }, service.Current!.Recent.Select(item => item.Id));
        Assert.Contains((uint)1, service.ReadIds);
        Assert.DoesNotContain((uint)3, service.ReadIds);
        Assert.False(service.CanUndo);
        service.UndoLocalRemoval();
        Assert.Equal(3, service.Current.Recent.Length);
    }

    [Fact]
    public async Task Dismiss_undo_restores_only_the_removed_notification_without_duplicates()
    {
        await using var service = new NotificationService(NullPlatformLogger.Instance, new PlatformCounters());
        var item = new NotificationEntry(10, "Mail", "First", "Body", DateTimeOffset.UtcNow, null);
        Seed(service, [item, item with { Id = 11 }]);
        await Changed(service, () => Assert.True(service.RemoveLocally(10)));
        Assert.False(service.RemoveLocally(999));
        await Changed(service, service.UndoLocalRemoval);
        Assert.Equal(2, service.Current!.Recent.Length);
        Assert.Single(service.Current.Recent.Where(entry => entry.Id == 10));
    }

    [Fact]
    public async Task Closed_toast_invalidates_undo_and_publishes_even_when_recent_stays_empty()
    {
        await using var service = new NotificationService(NullPlatformLogger.Instance, new PlatformCounters());
        Seed(service, [new NotificationEntry(10, "Mail", "Title", "Body", DateTimeOffset.UtcNow, null)]);
        var cleared = await Changed(service, service.ClearRecent);
        Assert.True(service.CanUndo);
        var refreshed = await Changed(service, () => Assert.True(Refresh(service, new HashSet<uint>())));
        Assert.False(service.CanUndo);
        Assert.Empty(refreshed.Recent);
        Assert.True(refreshed.LocalRevision > cleared.LocalRevision);
        Assert.False(Refresh(service, new HashSet<uint>()));
    }

    [Fact]
    public async Task Mark_all_read_publishes_when_toast_content_has_not_changed()
    {
        await using var service = new NotificationService(NullPlatformLogger.Instance, new PlatformCounters());
        var first = new NotificationEntry(10, "Mail", "Title", "Body", DateTimeOffset.UtcNow, null);
        Seed(service, [first, first with { Id = 11 }]);
        var singleRead = await Changed(service, () => service.MarkRead(10));
        var allRead = await Changed(service, service.MarkAllRead);
        Assert.True(allRead.LocalRevision > singleRead.LocalRevision);
        Assert.Equal(2, service.ReadIds.Count);
    }

    private static bool Refresh(NotificationService service, IReadOnlySet<uint> liveIds)
    {
        object?[] args = [liveIds, Array.Empty<NotificationEntry>(), 0];
        return (bool)typeof(NotificationService).GetMethod("ApplyLiveNotifications", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, args)!;
    }

    private static async Task<NotificationsSnapshot> Changed(NotificationService service, Action action)
    {
        var completion = new TaskCompletionSource<NotificationsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, SnapshotEventArgs<NotificationsSnapshot> args) => completion.TrySetResult(args.Snapshot);
        service.SnapshotChanged += OnChanged;
        try
        {
            action();
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { service.SnapshotChanged -= OnChanged; }
    }

    private static void Seed(NotificationService service, ImmutableArray<NotificationEntry> entries)
        => typeof(NotificationService).GetField("_recent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, entries);
}
