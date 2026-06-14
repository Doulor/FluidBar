using System.Windows.Threading;

namespace FluidBar;

public sealed class NotificationsPlugin : IIslandPlugin
{
    private readonly DispatcherTimer _timer;
    private readonly HashSet<uint> _seenIds = new();
    private INotificationListener? _listener;
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

    public void Initialize()
    {
        // Lazy-load the WinRT listener; if unavailable, the plugin runs without producing events.
        try
        {
            _listener = NotificationListenerFactory.Create();
        }
        catch
        {
            _listener = null;
        }
    }

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
        if (_listener is null)
            return "Unavailable";

        try
        {
            return await _listener.RequestAccessAsync();
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
        if (_isPolling || _listener is null)
            return;

        _isPolling = true;
        try
        {
            if (!_listener.IsAccessAllowed())
                return;

            var notifications = await _listener.GetToastNotificationsAsync();
            foreach (var info in notifications.OrderBy(item => item.Timestamp))
            {
                if (!_seenIds.Add(info.Id))
                    continue;

                var snapshot = CreateSnapshot(info);
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

    private static NotificationSnapshot? CreateSnapshot(ToastNotificationInfo info)
    {
        var title = info.Texts.ElementAtOrDefault(0) ?? info.AppName;
        var body = info.Texts.Count > 1
            ? string.Join(Environment.NewLine, info.Texts.Skip(1))
            : "新的通知";

        return new NotificationSnapshot(
            info.Id,
            info.AppName,
            title,
            body,
            info.Timestamp);
    }
}
