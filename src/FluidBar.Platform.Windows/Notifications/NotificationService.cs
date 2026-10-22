using System.Collections.Immutable;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;
using Windows.Foundation;
using Windows.Storage.Streams;
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

    /// <summary>
    /// Seeds <see cref="NotificationsSnapshot.Recent"/> with the toasts already in the system list when
    /// the service starts. Off by default: consumers that surface *new* arrivals as activities must not
    /// replay the back catalogue (the App's notification page opts in).
    /// </summary>
    public bool SeedRecentFromExisting { get; init; }

    /// <summary>
    /// Poll interval used when <c>UserNotificationListener.NotificationChanged</c> cannot be subscribed
    /// on this machine (0x80070490); the same read path still returns the current toast list.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether to fall back to polling when the change event is unavailable.</summary>
    public bool EnablePollingFallback { get; init; } = true;
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
    private ImmutableHashSet<uint> _readIds = ImmutableHashSet<uint>.Empty;
    private NotificationAccessState _access = NotificationAccessState.Unavailable;
    private static int s_activeInstances;
    private ITimer? _pollTimer;
    private bool _pollingFallback;
    private IReadOnlySet<uint> _seededIds = ImmutableHashSet<uint>.Empty;
    private int _primed;
    private int _instanceCounted;
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

    /// <summary>
    /// Started listener instances in this process. The app composes exactly one
    /// <see cref="NotificationService"/> (page + plugin share it); a value above one means a second read
    /// loop exists and the startup log records it.
    /// </summary>
    public static int ActiveInstanceCount => Volatile.Read(ref s_activeInstances);

    /// <summary>True once the first listener read finished (existing toasts primed/seeded).</summary>
    public bool IsPrimed => Volatile.Read(ref _primed) != 0;

    /// <summary>Ids of the toasts seeded as backlog (<see cref="NotificationServiceOptions.SeedRecentFromExisting"/>).</summary>
    public IReadOnlySet<uint> SeededIds => Volatile.Read(ref _seededIds);

    /// <summary>
    /// Toast ids the user has already seen (opened or clicked). Immutable so the snapshot mapper can
    /// read it without taking the service lock.
    /// </summary>
    public IReadOnlySet<uint> ReadIds => Volatile.Read(ref _readIds);

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
        lock (_stateGate)
        {
            _recent = ImmutableArray<NotificationEntry>.Empty;
        }

        StopPolling();
        _pollingFallback = false;
        Volatile.Write(ref _primed, 0);
        Volatile.Write(ref _seededIds, ImmutableHashSet<uint>.Empty);

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
        CountInstance();
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

        _logger.LogInfo(PlatformLogCategory.Notifications, "notifications.listener-started",
            "Notification listener instance started.", new Dictionary<string, object?>
            {
                ["activeInstances"] = ActiveInstanceCount,
                ["seeded"] = _options.SeedRecentFromExisting,
                ["mode"] = _pollingFallback ? "polling" : "events",
                ["pollIntervalMs"] = (int)_options.PollInterval.TotalMilliseconds,
                ["primed"] = IsPrimed,
            });
    }

    /// <summary>Creates an arrivals-only view over this listener (the plugin path keeps the same instance).</summary>
    public INotificationSource CreateNewArrivalsView() => new NewArrivalsNotificationSource(this, _logger);

    private void CountInstance()
    {
        if (Interlocked.Exchange(ref _instanceCounted, 1) == 0)
        {
            Interlocked.Increment(ref s_activeInstances);
        }
    }

    private void ReleaseInstance()
    {
        if (Interlocked.Exchange(ref _instanceCounted, 0) != 0)
        {
            Interlocked.Decrement(ref s_activeInstances);
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
        SubscribeOrEnablePolling(listener);

        IReadOnlyList<UserNotification> notifications = Array.Empty<UserNotification>();
        try
        {
            notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(cancellationToken)
                .ConfigureAwait(false);
            _history.Prime(notifications.Select(notification => notification.Id));
            _logger.LogInfo(PlatformLogCategory.Notifications, "notifications.primed",
                "Notification listener primed.", new Dictionary<string, object?>
                {
                    ["existing"] = notifications.Count,
                    ["mode"] = _pollingFallback ? "polling" : "events",
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Notifications, "notifications.prime-failed",
                "Could not prime the notification listener; existing toasts may be replayed once.", ex);
            _history.Prime(Array.Empty<uint>());
        }

        if (_options.SeedRecentFromExisting && notifications.Count > 0)
        {
            await SeedRecentAsync(notifications).ConfigureAwait(false);
        }

        if (_pollingFallback)
        {
            StartPolling();
        }

        // The first read finished: the backlog is known (seeded ids, possibly none), so the arrivals-only
        // view can hide it without racing the seed.
        Volatile.Write(ref _primed, 1);
    }

    /// <summary>
    /// Subscribes to <c>NotificationChanged</c>; on machines where WinRT rejects the subscription
    /// (0x80070490 ERROR_NOT_FOUND) it switches to the polling fallback instead of failing the monitor.
    /// </summary>
    private void SubscribeOrEnablePolling(UserNotificationListener listener)
    {
        try
        {
            if (Interlocked.Exchange(ref _subscribed, 1) == 0)
            {
                listener.NotificationChanged += OnNotificationChanged;
            }
        }
        catch (Exception ex)
        {
            _counters.IncrementNotificationEvents();
            _logger.LogWarning(PlatformLogCategory.Notifications, "notifications.events-unavailable",
                "UserNotificationListener.NotificationChanged is unavailable; polling for notification changes instead.", ex);
            if (_options.EnablePollingFallback)
            {
                _pollingFallback = true;
            }

            Interlocked.Exchange(ref _subscribed, 0);
        }
    }

    private void StartPolling()
    {
        if (_pollTimer is not null)
        {
            return;
        }

        var interval = _options.PollInterval > TimeSpan.Zero ? _options.PollInterval : TimeSpan.FromSeconds(30);
        _pollTimer = TimeProvider.CreateTimer(
            static state => ((NotificationService)state!).OnPollTick(),
            this,
            interval,
            interval);
        _logger.LogInfo(PlatformLogCategory.Notifications, "notifications.polling-started",
            "Notification polling fallback started.", new Dictionary<string, object?>
            {
                ["intervalMs"] = (int)interval.TotalMilliseconds,
            });
    }

    private void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void OnPollTick()
    {
        if (State != PlatformMonitorState.Running)
        {
            return;
        }

        // Debug-only: one line per read, so a log capture shows the single poll loop and its cadence.
        _logger.LogDebug(PlatformLogCategory.Notifications, "notifications.poll-tick",
            "Polling the toast list.", new Dictionary<string, object?>
            {
                ["intervalMs"] = (int)_options.PollInterval.TotalMilliseconds,
            });
        _ = ProcessChangesSafeAsync();
    }

    /// <summary>Fills the recent list from the toasts already present when the page opens.</summary>
    private async Task SeedRecentAsync(IReadOnlyList<UserNotification> notifications)
    {
        var max = Math.Max(1, _options.MaxRecentNotifications);
        var candidates = notifications
            .Select(CreateCandidate)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Take(max)
            .ToList();
        for (var i = 0; i < candidates.Count; i++)
        {
            var source = notifications.FirstOrDefault(notification => notification.Id == candidates[i].Id);
            if (source is not null)
            {
                candidates[i] = candidates[i] with
                {
                    AppIconPng = await TryLoadAppIconAsync(source).ConfigureAwait(false),
                };
            }
        }

        var entries = candidates
            .Select(candidate => new NotificationEntry(candidate.Id, candidate.AppName, candidate.Title,
                candidate.Body, candidate.CreatedAt,
                VerificationCodeDetector.Detect($"{candidate.Title}\n{candidate.Body}"),
                candidate.AppIconPng))
            .ToImmutableArray();

        var seeded = NotificationListReducer.Bounded(entries, max);
        lock (_stateGate)
        {
            _recent = seeded;
        }

        if (!seeded.IsDefaultOrEmpty)
        {
            Volatile.Write(ref _seededIds, ImmutableHashSet.CreateRange(seeded.Select(entry => entry.Id)));
        }

        Publish(new NotificationsSnapshot(NotificationAccessState.Allowed, seeded));
        _logger.LogInfo(PlatformLogCategory.Notifications, "notifications.seeded",
            "Notification page seeded with the existing toast list.", new Dictionary<string, object?>
            {
                ["count"] = seeded.Length,
                ["icons"] = seeded.Count(entry => entry.AppIconPng is { Length: > 0 }),
            });
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
            for (var i = 0; i < candidates.Count; i++)
            {
                if (!_history.IsNew(candidates[i].Id))
                {
                    continue;
                }

                var source = notifications.FirstOrDefault(notification => notification.Id == candidates[i].Id);
                if (source is not null)
                {
                    candidates[i] = candidates[i] with
                    {
                        AppIconPng = await TryLoadAppIconAsync(source).ConfigureAwait(false),
                    };
                }
            }

            var added = _history.Process(candidates);

            // Close sync: toasts the user removed from Action Center are no longer returned by the
            // listener, so the recent list must drop them (before this, only additions were handled and
            // a closed toast stayed on the island page forever).
            var liveIds = new HashSet<uint>(notifications.Select(notification => notification.Id));
            var previousCount = 0;
            lock (_stateGate)
            {
                previousCount = _recent.Length;
                var pruned = NotificationListReducer.Prune(_recent, liveIds);
                var merged = NotificationListReducer.Merge(pruned, added, _options.MaxRecentNotifications);
                if (NotificationListReducer.SequenceEqual(merged, _recent))
                {
                    return;
                }

                _recent = merged;
            }

            Publish(new NotificationsSnapshot(NotificationAccessState.Allowed, _recent));
            _logger.LogInfo(PlatformLogCategory.Notifications, "notifications.received",
                "Notification list changed.", new Dictionary<string, object?>
                {
                    ["added"] = added.Count,
                    ["removed"] = Math.Max(0, previousCount + added.Count - _recent.Length),
                    ["latestApp"] = added.Count > 0 ? added[^1].AppName : null,
                    ["verificationCode"] = added.Count > 0 && added[^1].VerificationCode is not null,
                    ["icons"] = added.Count(entry => entry.AppIconPng is { Length: > 0 }),
                });
        }
        catch (Exception ex)
        {
            _counters.IncrementMonitorCallbackFailures();
            _logger.LogError(PlatformLogCategory.Notifications, "notifications.process-failed",
                "Processing notification changes failed.", ex);
        }
        finally
        {
            Volatile.Write(ref _processing, 0);
        }
    }

    /// <summary>
    /// Reads the source app's logo from its <c>AppDisplayInfo</c>. Win32 toasts (QQ included) often
    /// expose no logo or an empty appLogoOverride; fall back to the running executable's real icon.
    /// Windows toast payloads do not expose the sender's personal avatar, so the app icon is the
    /// closest real identity available.
    /// </summary>
    private static async Task<byte[]?> TryLoadAppIconAsync(UserNotification notification)
    {
        // QQ/Win32 toasts put the real sender/group avatar in the toast XML image element; the
        // WinRT UserNotification projection does not expose images, so read it from the toast payload.
        var toastImage = NotificationToastImageReader.TryRead(notification.Id);
        if (toastImage is { Length: > 0 })
        {
            return toastImage;
        }

        var appName = notification.AppInfo?.DisplayInfo?.DisplayName ?? notification.AppInfo?.Id;
        try
        {
            var display = notification.AppInfo?.DisplayInfo;
            if (display is not null)
            {
                var logo = display.GetLogo(new global::Windows.Foundation.Size(64, 64));
                using var stream = await logo.OpenReadAsync().AsTask().ConfigureAwait(false);
                if (stream.Size is > 0 and <= 1024 * 1024)
                {
                    using var managed = stream.AsStreamForRead();
                    using var buffer = new MemoryStream((int)stream.Size);
                    await managed.CopyToAsync(buffer).ConfigureAwait(false);
                    return buffer.ToArray();
                }
            }
        }
        catch (Exception)
        {
            // Fall through to the executable icon below.
        }

        return TryLoadProcessIcon(appName);
    }

    /// <summary>Finds the running app's own executable and reads its icon as PNG bytes.</summary>
    private static byte[]? TryLoadProcessIcon(string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return null;
        }

        var wanted = appName.Trim();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var processName = process.ProcessName;
                if (!processName.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                    && !wanted.Contains(processName, StringComparison.OrdinalIgnoreCase)
                    && !processName.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var path = process.MainModule?.FileName;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    continue;
                }

                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is null)
                {
                    continue;
                }

                using var bitmap = icon.ToBitmap();
                using var png = new MemoryStream();
                bitmap.Save(png, ImageFormat.Png);
                return png.ToArray();
            }
            catch
            {
                // Access denied or process exited between enumeration and inspection; try the next.
            }
        }

        return null;
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

    /// <summary>Marks one toast as read; the next snapshot clears its unread dot.</summary>
    public void MarkRead(uint id)
    {
        NotificationsSnapshot? publish = null;
        lock (_stateGate)
        {
            if (_readIds.Contains(id) || !_recent.Any(entry => entry.Id == id))
            {
                return;
            }

            _readIds = _readIds.Add(id);
            publish = new NotificationsSnapshot(_access, _recent);
        }

        Publish(publish);
        _logger.LogDebug(PlatformLogCategory.Notifications, "notifications.read",
            "Notification marked as read.", new Dictionary<string, object?> { ["id"] = id });
    }

    /// <summary>Marks every currently listed toast as read (the notification page was opened).</summary>
    public void MarkAllRead()
    {
        NotificationsSnapshot? publish = null;
        lock (_stateGate)
        {
            var next = _readIds.Union(_recent.Select(entry => entry.Id));
            if (next.Count == _readIds.Count)
            {
                return;
            }

            _readIds = next;
            publish = new NotificationsSnapshot(_access, _recent);
        }

        Publish(publish);
        _logger.LogDebug(PlatformLogCategory.Notifications, "notifications.read-all",
            "All listed notifications marked as read.");
    }

    /// <summary>
    /// Removes one toast from the island's recent list. Windows exposes no public API to dismiss another
    /// app's toast, so the Action Center entry (if still open) disappears only when the user closes it;
    /// the next poll then keeps it out of the list as well.
    /// </summary>
    /// <param name="id">Toast id from <see cref="NotificationsSnapshot.Recent"/>.</param>
    /// <returns>True when the entry existed and was removed.</returns>
    public bool RemoveLocally(uint id)
    {
        var removed = false;
        lock (_stateGate)
        {
            var next = NotificationListReducer.Remove(_recent, id);
            if (next.Length != _recent.Length)
            {
                _recent = next;
                removed = true;
            }
        }

        if (removed)
        {
            Publish(new NotificationsSnapshot(AccessState, _recent));
            _logger.LogInfo(PlatformLogCategory.Notifications, "notifications.dismissed-locally",
                "Notification removed from the island list.", new Dictionary<string, object?>
                {
                    ["id"] = id,
                    ["remaining"] = _recent.Length,
                });
        }

        return removed;
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
        StopPolling();
        _pollingFallback = false;
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
        Volatile.Write(ref _primed, 0);
        Volatile.Write(ref _seededIds, ImmutableHashSet<uint>.Empty);
        ReleaseInstance();
        await ValueTask.CompletedTask;
    }
}