using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Threading;
using FluidBar.Sdk;
using FluidBar.Sdk.Contributions;

namespace FluidBar.Plugins.Builtin;

/// <summary>
/// First-party notifications plugin: toast snapshots (including the verification-code flag) become SDK
/// notification events; permission state is surfaced declaratively and can be requested from settings.
/// </summary>
public sealed class NotificationsPlugin : BuiltinPluginBase
{
    private readonly INotificationSource _notifications;
    private readonly Func<string, Task<bool>>? _copyText;
    private readonly object _stateGate = new();
    private uint _lastNotificationId;
    private NotificationAccessState _lastAccess = NotificationAccessState.Unavailable;

    public NotificationsPlugin(INotificationSource notifications, Func<string, Task<bool>>? copyText = null)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _copyText = copyText;
    }

    public override string PluginId => BuiltinIds.NotificationsPlugin;

    public static PluginContributions Contributions => BuiltinContributions.ForNotifications();

    protected override void OnStarted()
    {
        TrackSubscription(() => _notifications.SnapshotChanged += OnSnapshotChanged,
            () => _notifications.SnapshotChanged -= OnSnapshotChanged);

        RegisterCommand(BuiltinIds.NotificationsCopyCode, async (_, cancellationToken) =>
        {
            var code = _notifications.Current?.Latest?.VerificationCode;
            if (code is not null && _copyText is not null)
            {
                await _copyText(code).ConfigureAwait(false);
            }
        });
        RegisterCommand(BuiltinIds.NotificationsRequestAccess, async (_, cancellationToken) =>
        {
            var access = await _notifications.RequestAccessAsync(cancellationToken).ConfigureAwait(false);
            PublishAccessState(access);
        });
        RegisterCommand(BuiltinIds.NotificationsRetract, (_, _) =>
        {
            Retract();
            return ValueTask.CompletedTask;
        });

        _lastAccess = _notifications.AccessState;
        PublishAccessState(_lastAccess);
        PublishSnapshot(_notifications.Current);
    }

    private void OnSnapshotChanged(object? sender, SnapshotEventArgs<NotificationsSnapshot> args)
    {
        if (!IsStarted)
        {
            return;
        }

        if (args.Snapshot.Access != _lastAccess)
        {
            PublishAccessState(args.Snapshot.Access);
        }

        PublishSnapshot(args.Snapshot);
    }

    private void PublishAccessState(NotificationAccessState access)
    {
        if (!IsStarted)
        {
            return;
        }

        var changed = access != _lastAccess;
        _lastAccess = access;
        if (access == NotificationAccessState.Allowed || !changed)
        {
            return;
        }

        Publish(new IslandEvent
        {
            Source = PluginId,
            Title = "notification-access",
            IconKind = "notification",
            Kind = IslandEventKind.Status,
            LayoutId = BuiltinIds.NotificationsCompactLayout,
            Payload = new IslandEventPayload
            {
                Subtitle = access.ToString(),
                Badge = access.ToString(),
                Fields = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["icon"] = "notification",
                    ["title"] = "notification-access",
                    ["body"] = access.ToString(),
                    ["code"] = null,
                    ["actions"] = BuiltinIds.NotificationsRequestAccess,
                },
            },
        });
    }

    internal void PublishSnapshot(NotificationsSnapshot? snapshot)
    {
        var latest = snapshot?.Latest;
        if (latest is null)
        {
            lock (_stateGate)
            {
                if (_lastNotificationId == 0)
                {
                    return;
                }

                _lastNotificationId = 0;
            }

            Retract();
            return;
        }

        lock (_stateGate)
        {
            if (latest.Id == _lastNotificationId)
            {
                return;
            }

            _lastNotificationId = latest.Id;
        }

        var code = latest.VerificationCode;
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["icon"] = "notification",
            ["title"] = latest.Title,
            ["body"] = latest.Body,
            ["code"] = code,
            ["actions"] = code is null ? BuiltinIds.NotificationsRetract : BuiltinIds.NotificationsCopyCode,
        };

        Publish(new IslandEvent
        {
            Source = PluginId,
            Title = string.IsNullOrWhiteSpace(latest.Title) ? latest.AppName : latest.Title,
            Content = latest.Body,
            IconKind = "notification",
            Kind = IslandEventKind.Notification,
            LayoutId = BuiltinIds.NotificationsCompactLayout,
            Payload = new IslandEventPayload
            {
                Subtitle = latest.AppName,
                Badge = code,
                SourceName = latest.AppName,
                VerificationCode = code,
                Fields = fields,
            },
        });
    }

    protected override void OnStopped()
    {
        lock (_stateGate)
        {
            _lastNotificationId = 0;
        }
    }
}
