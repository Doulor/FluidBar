using System.Collections.Immutable;

namespace FluidBar.App.State;

/// <summary>
/// Honest empty snapshot used when the platform seam could not be composed. It deliberately carries no
/// demo track, notifications or clipboard rows: a broken service must look unavailable, never like a
/// real Spotify/Teams session that the user cannot control.
/// </summary>
public sealed class EmptyIslandDataSource : IIslandDataSource, IDisposable
{
    private readonly IslandContent _content = new()
    {
        Now = DateTimeOffset.Now,
        Notifications = ImmutableArray<NotificationItemData>.Empty,
        Clipboard = ImmutableArray<ClipboardItemData>.Empty,
        Agents = ImmutableArray<AgentItemData>.Empty,
        Tasks = ImmutableArray<WidgetTaskData>.Empty,
        UnreadCount = 0,
    };

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IslandContent Snapshot() => _content;

    /// <inheritdoc />
    public void Start()
    {
    }

    /// <inheritdoc />
    public void Stop()
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Changed = null;
    }
}
