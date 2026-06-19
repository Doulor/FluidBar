using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace FluidBar.Monitors;

/// <summary>
/// Windows 通知监控 - 监听系统 toast 通知
/// </summary>
public sealed class NotificationMonitor : ISystemMonitor
{
    public string Id => "notifications";
    public string Name => "Windows 通知";
    public string Description => "监听 Windows toast 通知，在灵动岛上显示应用来源、标题和正文";
    public string Icon => "\uE7F4";
    public bool Enabled { get; set; } = true;
    public event Action<IslandEvent>? EventTriggered;

    private readonly HashSet<uint> _seenIds = new();
    private System.Windows.Threading.DispatcherTimer? _timer;
    private bool _isRunning;
    private DateTimeOffset _startTime;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _startTime = DateTimeOffset.Now;
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _timer.Tick += (_, _) => _ = SafePollAsync();
        _timer.Start();
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _timer?.Stop();
        _timer = null;
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
        if (!_isRunning) return;
        try { await PollAsync(); } catch { }
    }

    private async Task PollAsync()
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

            if (notification.CreationTime < _startTime)
                continue;

            var snapshot = TryCreateSnapshot(notification);
            if (snapshot is not null)
                EventTriggered?.Invoke(NotificationIslandEventFactory.FromSnapshot(snapshot));
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

    public void Dispose() => Stop();
}
