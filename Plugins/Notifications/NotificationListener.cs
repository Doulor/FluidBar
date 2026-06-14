using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace FluidBar;

/// <summary>
/// Isolated WinRT wrapper for UserNotificationListener.
/// Kept in a separate file so the main plugin classes don't directly
/// reference WinRT types, preventing startup JIT crashes.
/// </summary>
internal static class NotificationListenerFactory
{
    public static INotificationListener Create() => new NotificationListener();
}

internal interface INotificationListener
{
    Task<string> RequestAccessAsync();
    bool IsAccessAllowed();
    Task<IReadOnlyList<ToastNotificationInfo>> GetToastNotificationsAsync();
}

/// <summary>
/// WinRT-free snapshot of a toast notification, used as a bridge
/// between the WinRT listener and the main plugin code.
/// </summary>
internal sealed record ToastNotificationInfo(
    uint Id,
    string AppName,
    IReadOnlyList<string> Texts,
    DateTimeOffset Timestamp);

internal sealed class NotificationListener : INotificationListener
{
    public async Task<string> RequestAccessAsync()
    {
        var status = await UserNotificationListener.Current.RequestAccessAsync();
        return status.ToString();
    }

    public bool IsAccessAllowed()
    {
        return UserNotificationListener.Current.GetAccessStatus()
            == UserNotificationListenerAccessStatus.Allowed;
    }

    public async Task<IReadOnlyList<ToastNotificationInfo>> GetToastNotificationsAsync()
    {
        var listener = UserNotificationListener.Current;
        var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        var results = new List<ToastNotificationInfo>();

        foreach (var notification in notifications)
        {
            try
            {
                var appName = notification.AppInfo?.DisplayInfo.DisplayName ?? "系统通知";
                var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                var texts = binding?.GetTextElements()
                    .Select(element => element.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray() ?? Array.Empty<string>();

                results.Add(new ToastNotificationInfo(
                    notification.Id,
                    appName,
                    texts,
                    notification.CreationTime));
            }
            catch
            {
            }
        }

        return results;
    }
}
