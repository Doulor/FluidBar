using System.Collections.Immutable;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Agent;

/// <summary>
/// Merges several agent event sources into the single <see cref="IAgentInboxSource"/> the island and the
/// agent-status plugin consume. The hook inbox watcher and the built-in
/// <see cref="DshSessionProvider"/> stay independent; rows are merged by id (last writer wins), ordered
/// newest first and bounded to 100 entries.
/// </summary>
public sealed class AgentEventSourceAggregator : IAgentInboxSource, IDisposable, IAsyncDisposable
{
    private const int MaxMergedEntries = 100;

    private readonly IReadOnlyList<IAgentInboxSource> _sources;
    private readonly object _gate = new();
    private AgentEventSnapshot? _current;
    private bool _disposed;

    public AgentEventSourceAggregator(params IAgentInboxSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.Where(static source => source is not null).ToArray();
        if (_sources.Count == 0)
        {
            throw new ArgumentException("At least one agent event source is required.", nameof(sources));
        }

        foreach (var source in _sources)
        {
            source.SnapshotChanged += OnSourceChanged;
        }

        _current = Merge();
    }

    /// <inheritdoc />
    public AgentEventSnapshot? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<SnapshotEventArgs<AgentEventSnapshot>>? SnapshotChanged;

    /// <inheritdoc />
    public string InboxDirectory => _sources[0].InboxDirectory;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var source in _sources)
        {
            source.SnapshotChanged -= OnSourceChanged;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnSourceChanged(object? sender, SnapshotEventArgs<AgentEventSnapshot> args)
    {
        if (_disposed)
        {
            return;
        }

        var merged = Merge();
        lock (_gate)
        {
            _current = merged;
        }

        SnapshotChanged?.Invoke(this, new SnapshotEventArgs<AgentEventSnapshot>(merged));
    }

    private AgentEventSnapshot? Merge()
    {
        var byId = new Dictionary<string, AgentEventEntry>(StringComparer.Ordinal);
        foreach (var source in _sources)
        {
            var snapshot = source.Current;
            if (snapshot is null)
            {
                continue;
            }

            foreach (var entry in snapshot.Recent)
            {
                if (!byId.TryGetValue(entry.Id, out var existing) || entry.Timestamp >= existing.Timestamp)
                {
                    byId[entry.Id] = entry;
                }
            }
        }

        if (byId.Count == 0)
        {
            return null;
        }

        var merged = byId.Values
            .OrderByDescending(static entry => entry.Timestamp)
            .Take(MaxMergedEntries)
            .ToImmutableArray();
        return new AgentEventSnapshot(merged);
    }
}
