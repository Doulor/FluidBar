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
    private string? _lastContentKey;
    private DateTimeOffset _lastShownAt;
    private NotificationAccessState _lastAccess = NotificationAccessState.Unavailable;

    /// <summary>
    /// How long content that was already shown must stay away before it may be shown again.
    /// <para>
    /// Messengers re-post the same toast with a fresh id (Phone Link does it for every QQ message: same app, same
    /// "QQ" title, same body, new id), so id-only de-duplication let the island pop the same notification up again
    /// every few minutes (report 2026-10-28). Content is what the user sees, so content is what we compare.
    /// </para>
    /// </summary>
    private static readonly TimeSpan DuplicateContentWindow = TimeSpan.FromMinutes(5);

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

        MediaAndState("plugin.notify.started");
        _lastAccess = _notifications.AccessState;
        PublishAccessState(_lastAccess);

        // The listener is primed before the plugin starts, so whatever Current holds is backlog. Remembering it
        // silently is what keeps a plugin (re)start from replaying the last toast on the island — the other half of
        // the "the same notification shows up again and again" report.
        RememberWithoutPublishing(_notifications.Current?.Latest);
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

            MediaAndState("plugin.notify.retract");
            Retract();
            return;
        }

        var now = Context.Time.GetUtcNow();
        var contentKey = ContentKey(latest);
        var suppressed = false;
        var olderThanShown = false;
        lock (_stateGate)
        {
            // Notification ids only ever grow within a session. A *lower* id therefore means the snapshot went
            // backwards — which happens when the newer toasts are cleared from Action Center and the listener's list
            // falls back to an older one. Republishing it popped an already-shown notification up again (seen with
            // PowerToys' "AlwaysOnTop" right after three QQ toasts, 2026-10-28).
            if (latest.Id <= _lastNotificationId)
            {
                olderThanShown = latest.Id < _lastNotificationId;
                if (!olderThanShown)
                {
                    return;
                }
            }

            _lastNotificationId = latest.Id;
            if (string.Equals(contentKey, _lastContentKey, StringComparison.Ordinal)
                && now - _lastShownAt < DuplicateContentWindow)
            {
                suppressed = true;
            }
            else
            {
                _lastContentKey = contentKey;
                _lastShownAt = now;
            }
        }

        if (olderThanShown)
        {
            MediaAndState($"plugin.notify.skipped-older id={latest.Id} title='{ShortText(latest.Title)}'");
            return;
        }

        if (suppressed)
        {
            MediaAndState($"plugin.notify.suppressed id={latest.Id} title='{ShortText(latest.Title)}'");
            return;
        }

        MediaAndState($"plugin.notify.publish id={latest.Id} title='{ShortText(latest.Title)}'");

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

    /// <summary>Remembers a notification without showing it (startup backlog / restart replay guard).</summary>
    /// <param name="latest">Newest entry of the current snapshot, or null.</param>
    private void RememberWithoutPublishing(NotificationEntry? latest)
    {
        if (latest is null)
        {
            return;
        }

        lock (_stateGate)
        {
            _lastNotificationId = latest.Id;
            _lastContentKey = ContentKey(latest);
            _lastShownAt = Context.Time.GetUtcNow();
        }
    }

    /// <summary>Identity of what the user actually sees (app + title + body).</summary>
    /// <param name="entry">Notification entry.</param>
    private static string ContentKey(NotificationEntry entry)
        => entry.AppName + "|" + entry.Title + "|" + entry.Body;

    private static string ShortText(string? value)
        => value is { Length: > 40 } ? value[..40] : value ?? string.Empty;

    protected override void OnStopped()
    {
        lock (_stateGate)
        {
            _lastNotificationId = 0;
        }

        MediaAndState("plugin.notify.stopped");
    }

    /// <summary>Diagnostic line into the shared media-state log (rare: one per notification transition).</summary>
    private static void MediaAndState(string message)
        => FluidBar.Platform.Windows.Media.MediaDiagnostics.LogAlways(message);
}
