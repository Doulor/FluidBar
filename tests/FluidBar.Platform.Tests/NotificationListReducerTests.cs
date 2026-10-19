using System.Collections.Immutable;
using FluidBar.Platform.Windows.Notifications;

namespace FluidBar.Platform.Tests;

/// <summary>Pure "recent notifications" list semantics: arrival, cap, close sync and local dismiss.</summary>
public sealed class NotificationListReducerTests
{
    private static NotificationEntry Entry(uint id, int ageSeconds = 0, string app = "App", string title = "Title")
        => new(id, app, title, "body", DateTimeOffset.UnixEpoch.AddSeconds(ageSeconds), null);

    [Fact]
    public void Merge_PrependsTheNewBatchNewestFirst()
    {
        var existing = ImmutableArray.Create(Entry(10), Entry(9));
        var added = new[] { Entry(11), Entry(12) };

        var merged = NotificationListReducer.Merge(existing, added, maxEntries: 10);

        Assert.Equal([12u, 11u, 10u, 9u], merged.Select(entry => entry.Id).ToArray());
    }

    [Fact]
    public void Merge_RemovesDuplicateIdsAcrossBatches()
    {
        var existing = ImmutableArray.Create(Entry(3), Entry(2));
        var added = new[] { Entry(2), Entry(4) };

        var merged = NotificationListReducer.Merge(existing, added, maxEntries: 10);

        Assert.Equal([4u, 2u, 3u], merged.Select(entry => entry.Id).ToArray());
    }

    [Fact]
    public void Merge_BoundsToMaxEntriesKeepingTheNewest()
    {
        var existing = ImmutableArray.Create(Entry(3), Entry(2), Entry(1));
        // "added" arrives oldest -> newest (NotificationHistory.Process sorts by creation).
        var added = new[] { Entry(4), Entry(5) };

        var merged = NotificationListReducer.Merge(existing, added, maxEntries: 3);

        Assert.Equal([5u, 4u, 3u], merged.Select(entry => entry.Id).ToArray());
    }

    [Fact]
    public void Prune_DropsToastsTheUserClosed_PreservingOrder()
    {
        var entries = ImmutableArray.Create(Entry(5), Entry(4), Entry(3));
        IReadOnlySet<uint> live = new HashSet<uint> { 5, 3 };

        var pruned = NotificationListReducer.Prune(entries, live);

        Assert.Equal([5u, 3u], pruned.Select(entry => entry.Id).ToArray());
    }

    [Fact]
    public void Prune_ReturnsTheSameListWhenNothingWasClosed()
    {
        var entries = ImmutableArray.Create(Entry(2), Entry(1));
        IReadOnlySet<uint> live = new HashSet<uint> { 1, 2, 3 };

        // ImmutableArray is a struct: value-equal means the very same backing array was returned.
        Assert.True(entries.Equals(NotificationListReducer.Prune(entries, live)));
    }

    [Fact]
    public void Prune_EmptyListStaysEmpty()
    {
        Assert.Empty(NotificationListReducer.Prune(ImmutableArray<NotificationEntry>.Empty, new HashSet<uint>()));
        Assert.Empty(NotificationListReducer.Prune(default, new HashSet<uint>()));
    }

    [Fact]
    public void Bounded_DedupesAndCapsButKeepsTheGivenOrder()
    {
        var entries = new[] { Entry(9), Entry(8), Entry(9), Entry(7) };

        var bounded = NotificationListReducer.Bounded(entries, maxEntries: 2);

        Assert.Equal([9u, 8u], bounded.Select(entry => entry.Id).ToArray());
    }

    [Fact]
    public void Bounded_NonPositiveCapYieldsAnEmptyList()
    {
        Assert.Empty(NotificationListReducer.Bounded([Entry(1)], maxEntries: 0));
    }

    [Fact]
    public void Remove_DropsExactlyOneEntry()
    {
        var entries = ImmutableArray.Create(Entry(3), Entry(2), Entry(1));

        var removed = NotificationListReducer.Remove(entries, 2);
        Assert.Equal([3u, 1u], removed.Select(entry => entry.Id).ToArray());

        var missing = NotificationListReducer.Remove(entries, 99);
        Assert.Equal(entries, missing);
    }

    [Fact]
    public void SequenceEqual_ComparesEntriesInOrder()
    {
        var left = ImmutableArray.Create(Entry(2), Entry(1));

        Assert.True(NotificationListReducer.SequenceEqual(left, ImmutableArray.Create(Entry(2), Entry(1))));
        Assert.False(NotificationListReducer.SequenceEqual(left, ImmutableArray.Create(Entry(1), Entry(2))));
        Assert.False(NotificationListReducer.SequenceEqual(left, ImmutableArray.Create(Entry(2), Entry(1, 1))));
    }

    [Fact]
    public void SequenceEqual_TreatsDefaultAndEmptyAsEqual()
    {
        Assert.True(NotificationListReducer.SequenceEqual(default, ImmutableArray<NotificationEntry>.Empty));
        Assert.False(NotificationListReducer.SequenceEqual(default, ImmutableArray.Create(Entry(1))));
    }
}
