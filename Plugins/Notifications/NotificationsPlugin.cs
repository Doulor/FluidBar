using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace FluidBar;

/// <summary>
/// Windows notification plugin.
/// Uses a lightweight Win32 window-title polling fallback when the WinRT
/// UserNotificationListener is unavailable (requires net10.0-windows10.0.19041.0 TFM).
/// </summary>
public sealed class NotificationsPlugin : IIslandPlugin
{
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _seenKeys = new();
    private bool _isPolling;
    private bool _disposed;

    public string Id => "notifications";
    public string Name => "Windows 通知";
    public string Description => "监听 Windows toast 通知，在灵动岛上显示应用来源、标题和正文。需 WinRT SDK 支持以获取完整通知内容。";
    public string Icon => "\uE7F4";
    public bool Enabled { get; set; } = true;
    public IPluginConfig? Config => null;
    public event Action<IslandEvent>? EventTriggered;

    // Known notification-related window class names
    private static readonly HashSet<string> NotificationClasses = new(StringComparer.Ordinal)
    {
        "Windows.UI.Core.CoreWindow",
        "ToastNotification",
        "NotificationWindow",
    };

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Store found windows to avoid repeated callbacks
    [ThreadStatic]
    private static List<WindowInfo>? _foundWindows;

    private sealed record WindowInfo(string Title, string ClassName);

    public NotificationsPlugin()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        _timer.Tick += (_, _) => SafePoll();
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

    public Task<string> RequestAccessAsync()
    {
        // Non-WinRT path: always report as "Unavailable" since we can't use UserNotificationListener
        return Task.FromResult("WinRT Unavailable");
    }

    private void SafePoll()
    {
        if (_disposed)
            return;

        try
        {
            Poll();
        }
        catch
        {
        }
    }

    private void Poll()
    {
        if (_isPolling)
            return;

        _isPolling = true;
        try
        {
            // Try to read system toast text via Win32 window enumeration.
            // This is a best-effort fallback; full toast access requires WinRT.
            var windows = EnumerateNotificationWindows();
            foreach (var window in windows)
            {
                var key = $"{window.ClassName}|{window.Title}";
                if (!_seenKeys.Add(key))
                    continue;

                var snapshot = CreateSnapshot(window);
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

    private static List<WindowInfo> EnumerateNotificationWindows()
    {
        _foundWindows = new List<WindowInfo>();
        EnumWindows(EnumProc, IntPtr.Zero);
        var result = _foundWindows;
        _foundWindows = null;
        return result;
    }

    private static bool EnumProc(IntPtr hWnd, IntPtr lParam)
    {
        if (!IsWindowVisible(hWnd))
            return true;

        var className = new StringBuilder(256);
        GetClassName(hWnd, className, 256);

        if (!NotificationClasses.Contains(className.ToString()))
            return true;

        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
            return true;

        var title = new StringBuilder(length + 1);
        GetWindowText(hWnd, title, length + 1);

        var titleStr = title.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(titleStr))
            _foundWindows?.Add(new WindowInfo(titleStr, className.ToString()));

        return true;
    }

    private static NotificationSnapshot? CreateSnapshot(WindowInfo window)
    {
        if (string.IsNullOrWhiteSpace(window.Title))
            return null;

        // Use the window title as notification title
        // Try to extract app name from process associated with this class
        return new NotificationSnapshot(
            Id: (uint)window.Title.GetHashCode(),
            AppName: "系统通知",
            Title: window.Title,
            Body: "",
            Timestamp: DateTimeOffset.Now);
    }
}
