using System.Collections.Immutable;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace FluidBar.Platform.Windows.Notifications;

/// <summary>Options for the notification listener.</summary>
public sealed class NotificationServiceOptions
{
    /// <summary>Maximum text characters stored per notification field.</summary>
    public int MaxTextCharacters { get; init; } = 512;

    /// <summary>Maximum recent notifications kept in the published snapshot.</summary>
    public int MaxRecentNotifications { get; init; } = 20;

    /// <summary>Maximum seen notification ids kept (bounded ring).</summary>
    public int MaxSeenIds { get; init; } = 256;

    /// <summary>Maximum notifications surfaced per change batch.</summary>
    public int MaxBatch { get; init; } = 20;
}

/// <summary>
/// Event-driven Windows toast notification listener (<c>UserNotificationListener.NotificationChanged</c>
/// + watermark + bounded ring). Zero polling: v1 polled the full history every 1.6 s with an unbounded
/// <c>HashSet</c> (audit F-13).
/// </summary>
public sealed class NotificationService : PlatformMonitor<NotificationsSnapshot>, INotificationSource
{
    private readonly NotificationServiceOptions _options;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly NotificationHistory _history;
    private readonly object _stateGate = new();
    private UserNotificationListener? _listener;
    private ImmutableArray<NotificationEntry> _recent = ImmutableArray<NotificationEntry>.Empty;
    private NotificationAccessState _access = NotificationAccessState.Unavailable;
    private int _processing;
    private int _subscribed;

    public NotificationService(IPlatformLogger logger, PlatformCounters counters,
        NotificationServiceOptions? options = null, TimeProvider? timeProvider = null)
        : base("notifications", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _options = options ?? new NotificationServiceOptions();
        _history = new NotificationHistory(_options.MaxSeenIds, _options.MaxBatch);
    }

    /// <summary>Current permission state.</summary>
    public NotificationAccessState AccessState
    {
        get
        {
            lock (_stateGate)
            {
                return _access;
            }
        }
    }

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        _history.Reset();
        _recent = ImmutableArray<NotificationEntry>.Empty;

        UserNotificationListener listener;
        try
        {
            listener = UserNotificationListener.Current;
        }
        catch (Exception ex)
        {
            _access = NotificationAccessState.Unavailable;
            Publish(new NotificationsSnapshot(_access, _recent));
            _logger.LogWarning(PlatformLogCategory.Notifications, "notifications.unavailable",
                "UserNotificationListener is not available on this system.", ex);
            return;
        }

        _listener = listener;
        var access = MapAccess(listener.GetAccessStatus());
        SetAccess(access, publish: true);

        if (access == NotificationAccessState.Allowed)
        {
            await AttachAndPrimeAsync(listener, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInfo(PlatformLogCategory.Notifications, "notifications.permission-required",
                $"Notification access is '{access}'; the listener is idle until the user grants permission.");
        }
    }

    /// <summary>Requests notification-listener permission; call from an explicit user action.</summary>
    public async Task<NotificationAccessState> RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        var listener = _listener;
        if (listener is null)
        {
            return NotificationAccessState.Unavailable;
        }

        try
        {
            var status = await listener.RequestAccessAsync().AsTask(cancellationToken).ConfigureAwait(false);
            var access = MapAccess(status);
            SetAccess(access, publish: true);
            if (access == NotificationAccessState.Allowed)
            {
                await AttachAndPrimeAsync(listener, cancellationToken).ConfigureAwait(false);
            }

            return access;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _counters.IncrementNotificationAccessDenied();
            _logger.LogWarning(PlatformLogCategory.Notifications, "notifications.request-access-failed",
                "Requesting notification-listener access failed.", ex);
            return AccessState;
        }
    }

    private async Task AttachAndPrimeAsync(UserNotificationListener listener, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
        {
            listener.NotificationChanged += OnNotificationChanged;
        }

        try
        {
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(cancellationToken)
                .ConfigureAwait(false);
            _history.Prime(notifications.Select(notification => notification.Id));
            _logger.LogInfo(PlatformLogCategory.Notifications, "notifications.primed",
                "Notification listener primed.", new Dictionary<string, object?>
                {
                    ["existing"] = notifications.Count,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Notifications, "notifications.prime-failed",
                "Could not prime the notification listener; existing toasts may be replayed once.", ex);
            _history.Prime(Array.Empty<uint>());
        }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        _counters.IncrementNotificationEvents();
        if (State != PlatformMonitorState.Running)
        {
            return;
        }

        // Any change (add/remove) triggers a coalesced read; removals produce no new entries because
        // the history watermark/ring already knows the ids.
        _ = ProcessChangesSafeAsync();
    }

    private async Task ProcessChangesSafeAsync()
    {
        if (Interlocked.Exchange(ref _processing, 1) != 0)
        {
            return;
        }

        try
        {
            var listener = _listener;
            if (listener is null)
            {
                return;
            }

            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask()
                .ConfigureAwait(false);
            var candidates = notifications.Select(CreateCandidate).Where(candidate => candidate is not null)
                .Select(candidate => candidate!).ToList();
            var added = _history.Process(candidates);
            if (added.Count == 0)
            {
                return;
            }

            lock (_stateGate)
            {
                var builder = ImmutableArray.CreateBuilder<NotificationEntry>(
                    Math.Min(_options.MaxRecentNotifications, _recent.Length + added.Count));
                for (var index = added.Count - 1; index >= 0; index--)
                {
                    builder.Add(added[index]);
                }

                foreach (var entry in _recent)
                {
                    if (builder.Count >= _options.MaxRecentNotifications)
                    {
                        break;
                    }

                    builder.Add(entry);
                }

                _recent = builder.MoveToImmutable();
            }

            Publish(new NotificationsSnapshot(NotificationAccessState.Allowed, _recent));
            _logger.LogInfo(PlatformLogCategory.Notifications, "notifications.received",
                "New notification(s) captured.", new Dictionary<string, object?>
                {
                    ["count"] = added.Count,
                    ["latestApp"] = added[^1].AppName,
                    ["verificationCode"] = added[^1].VerificationCode is not null,
                });
        }
        catch (Exception ex)
        {
            _counters.IncrementMediaSessionErrors();
            _logger.LogError(PlatformLogCategory.Notifications, "notifications.process-failed",
                "Processing notification changes failed.", ex);
        }
        finally
        {
            Volatile.Write(ref _processing, 0);
        }
    }

    private NotificationCandidate? CreateCandidate(UserNotification notification)
    {
        try
        {
            var appName = notification.AppInfo?.DisplayInfo.DisplayName ?? "Windows";
            var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements()
                .Select(element => element.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray() ?? Array.Empty<string>();
            var title = texts.ElementAtOrDefault(0) ?? appName;
            var body = texts.Length > 1 ? string.Join(Environment.NewLine, texts.Skip(1)) : string.Empty;
            var max = Math.Max(1, _options.MaxTextCharacters);
            return new NotificationCandidate(notification.Id, Truncate(appName, max), Truncate(title, max),
                Truncate(body, max), notification.CreationTime);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Notifications, "notifications.parse-failed",
                "Could not parse a toast notification.", ex);
            return null;
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private void SetAccess(NotificationAccessState access, bool publish)
    {
        lock (_stateGate)
        {
            _access = access;
        }

        if (publish)
        {
            Publish(new NotificationsSnapshot(access, _recent));
        }
    }

    private static NotificationAccessState MapAccess(UserNotificationListenerAccessStatus status) => status switch
    {
        UserNotificationListenerAccessStatus.Allowed => NotificationAccessState.Allowed,
        UserNotificationListenerAccessStatus.Denied => NotificationAccessState.Denied,
        UserNotificationListenerAccessStatus.Unspecified => NotificationAccessState.Unspecified,
        _ => NotificationAccessState.Unavailable,
    };

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        var listener = _listener;
        _listener = null;
        if (listener is not null && Interlocked.Exchange(ref _subscribed, 0) != 0)
        {
            try
            {
                listener.NotificationChanged -= OnNotificationChanged;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(PlatformLogCategory.Notifications, "notifications.detach-failed",
                    "Failed to detach the notification listener handler.", ex);
            }
        }

        _history.Reset();
        await ValueTask.CompletedTask;
    }
}
