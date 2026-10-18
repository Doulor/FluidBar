using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Notifications;

/// <summary>Notification contract consumed by first-party plugins. Implemented by <see cref="NotificationService"/>.</summary>
public interface INotificationSource
{
    NotificationsSnapshot? Current { get; }

    event EventHandler<SnapshotEventArgs<NotificationsSnapshot>>? SnapshotChanged;

    NotificationAccessState AccessState { get; }

    Task<NotificationAccessState> RequestAccessAsync(CancellationToken cancellationToken = default);
}
