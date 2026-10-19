using System.Collections.Immutable;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Notifications;

/// <summary>
/// Read-only <see cref="INotificationSource"/> view that hides the toasts that already existed when the
/// shared listener became ready.
/// </summary>
/// <remarks>
/// <para>The app runs exactly one <see cref="NotificationService"/>: the notification page seeds it with
/// the existing Action Center backlog (<see cref="NotificationServiceOptions.SeedRecentFromExisting"/>)
/// while the notifications plugin turns <em>arrivals</em> into activities. Sharing the raw service with
/// the plugin would replay the backlog as a stale "notification" pill on every launch, so the plugin is
/// handed this view instead: it keeps the single listener (and therefore the single read loop) while
/// preserving the old "no stale activity" semantics.</para>
/// <para>When the inner source is a <see cref="NotificationService"/> the baseline is the service's
/// <see cref="NotificationService.SeededIds"/> once the first read finished
/// (<see cref="NotificationService.IsPrimed"/>), which is exact even when the listener is still starting
/// while the view is created. Any other source is treated generically: the first snapshot the view
/// observes is its backlog.</para>
/// </remarks>
public sealed class NewArrivalsNotificationSource : INotificationSource
{
    private readonly INotificationSource _inner;
    private readonly IPlatformLogger _logger;
    private IReadOnlySet<uint>? _baselineIds;

    /// <summary>Creates an arrivals-only view over <paramref name="inner"/>.</summary>
    public NewArrivalsNotificationSource(INotificationSource inner, IPlatformLogger? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? NullPlatformLogger.Instance;
        _inner.SnapshotChanged += OnInnerSnapshotChanged;
        TryCaptureBaseline(_inner.Current);
    }

    /// <summary>The underlying source this view wraps (same instance the notification page reads).</summary>
    public INotificationSource Inner => _inner;

    /// <summary>Number of toasts treated as the already-seen backlog (diagnostics/tests).</summary>
    public int BaselineCount => Volatile.Read(ref _baselineIds)?.Count ?? 0;

    /// <summary>True once the backlog baseline is known (the view then forwards arrivals).</summary>
    public bool IsBaselineReady => Volatile.Read(ref _baselineIds) is not null;

    /// <inheritdoc />
    public NotificationsSnapshot? Current
    {
        get
        {
            var snapshot = _inner.Current;
            if (snapshot is null)
            {
                return null;
            }

            var baseline = TryCaptureBaseline(snapshot);
            return baseline is null ? null : Project(snapshot, baseline);
        }
    }

    /// <inheritdoc />
    public NotificationAccessState AccessState => _inner.AccessState;

    /// <inheritdoc />
    public event EventHandler<SnapshotEventArgs<NotificationsSnapshot>>? SnapshotChanged;

    /// <inheritdoc />
    public Task<NotificationAccessState> RequestAccessAsync(CancellationToken cancellationToken = default)
        => _inner.RequestAccessAsync(cancellationToken);

    /// <summary>
    /// Removes the baseline entries from a snapshot. Returns the same instance when nothing is filtered,
    /// so consumers see identical snapshots and never need to compare lists.
    /// </summary>
    public static NotificationsSnapshot Project(NotificationsSnapshot snapshot, IReadOnlySet<uint> baselineIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(baselineIds);
        if (snapshot.Recent.IsDefaultOrEmpty || baselineIds.Count == 0)
        {
            return snapshot;
        }

        var builder = ImmutableArray.CreateBuilder<NotificationEntry>(snapshot.Recent.Length);
        foreach (var entry in snapshot.Recent)
        {
            if (!baselineIds.Contains(entry.Id))
            {
                builder.Add(entry);
            }
        }

        return builder.Count == snapshot.Recent.Length
            ? snapshot
            : new NotificationsSnapshot(snapshot.Access, builder.ToImmutable());
    }

    private void OnInnerSnapshotChanged(object? sender, SnapshotEventArgs<NotificationsSnapshot> args)
    {
        try
        {
            var hadBaseline = Volatile.Read(ref _baselineIds) is not null;
            var baseline = TryCaptureBaseline(args.Snapshot);
            if (baseline is null)
            {
                // The listener is still priming; nothing is "new" yet.
                return;
            }

            if (!hadBaseline && _inner is not NotificationService)
            {
                // A generic source's establishing snapshot *is* the backlog; it is not an arrival. The
                // notification service knows its exact seed ids, so its first post-prime snapshot may
                // already contain a real arrival and is forwarded instead.
                return;
            }

            SnapshotChanged?.Invoke(this, new SnapshotEventArgs<NotificationsSnapshot>(Project(args.Snapshot, baseline)));
        }
        catch (Exception ex)
        {
            // A throwing plugin/consumer must not escape into the shared listener: the notification page
            // keeps working even when the plugin path fails.
            _logger.LogError(PlatformLogCategory.Notifications, "notifications.view-dispatch-failed",
                "A consumer of the arrivals-only notification view threw; the shared listener keeps running.", ex);
        }
    }

    private IReadOnlySet<uint>? TryCaptureBaseline(NotificationsSnapshot? snapshot)
    {
        if (Volatile.Read(ref _baselineIds) is { } existing)
        {
            return existing;
        }

        if (snapshot is null)
        {
            return null;
        }

        IReadOnlySet<uint> ids;
        if (_inner is NotificationService service)
        {
            if (!service.IsPrimed)
            {
                return null;
            }

            ids = service.SeededIds;
        }
        else
        {
            ids = IdsOf(snapshot);
        }

        var captured = ids.Count == 0 ? EmptySet : new HashSet<uint>(ids);
        return Interlocked.CompareExchange(ref _baselineIds, captured, null) ?? captured;
    }

    private static HashSet<uint> IdsOf(NotificationsSnapshot snapshot)
    {
        var ids = new HashSet<uint>(snapshot.Recent.IsDefaultOrEmpty ? 0 : snapshot.Recent.Length);
        if (!snapshot.Recent.IsDefaultOrEmpty)
        {
            foreach (var entry in snapshot.Recent)
            {
                ids.Add(entry.Id);
            }
        }

        return ids;
    }

    private static readonly IReadOnlySet<uint> EmptySet = new HashSet<uint>();
}
