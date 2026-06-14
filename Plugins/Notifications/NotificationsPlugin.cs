using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace FluidBar;

public sealed class NotificationsPlugin : IIslandPlugin
{
    private readonly DispatcherTimer _timer;
    private readonly HashSet<uint> _seenIds = new();
    private bool _isPolling;
    private bool _disposed;

    public string Id => "notifications";
    public string Name => "Windows 通知";
    public string Description => "监听 Windows toast 通知，在灵动岛上显示应用来源、标题和正文";
    public string Icon => "\uE7F4";
    public bool Enabled { get; set; } = true;
    public IPluginConfig? Config => null;
    public event Action<IslandEvent>? EventTriggered;

    public NotificationsPlugin()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _timer.Tick += (_, _) => _ = SafePollAsync();
    }

    public void Initialize() { }

    public void Start()
    {
        if (_disposed || _timer.IsEnabled)
            return;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }

    public async Task<string> RequestAccessAsync()
    {
        try
        {
            var status = await UserNotificationListener.Current.RequestAccessAsync();
            return status.ToString();
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    private async Task SafePollAsync()
    {
        if (_disposed)
            return;

        try
        {
            await PollAsync();
        }
        catch
        {
        }
    }

    private async Task PollAsync()
    {
        if (_isPolling)
            return;

        _isPolling = true;
        try
        {
            var listener = UserNotificationListener.Current;
            var access = listener.GetAccessStatus();
            if (access != UserNotificationListenerAccessStatus.Allowed)
                return;

            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var notification in notifications.OrderBy(item => item.CreationTime))
            {
                if (!_seenIds.Add(notification.Id))
                    continue;

                var snapshot = TryCreateSnapshot(notification);
                if (snapshot is not null)
                    EventTriggered?.Invoke(NotificationIslandEventFactory.FromSnapshot(snapshot));
            }
        }
        catch
        {
        }
        finally
        {
            _isPolling = false;
        }
    }

    private static NotificationSnapshot? TryCreateSnapshot(UserNotification notification)
    {
        try
        {
            var appName = notification.AppInfo?.DisplayInfo.DisplayName ?? "系统通知";
            var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements()
                .Select(element => element.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray() ?? Array.Empty<string>();
            var title = texts.ElementAtOrDefault(0) ?? appName;
            var body = texts.Length > 1
                ? string.Join(Environment.NewLine, texts.Skip(1))
                : "新的通知";

            return new NotificationSnapshot(
                notification.Id,
                appName,
                title,
                body,
                notification.CreationTime);
        }
        catch
        {
            return null;
        }
    }
}
