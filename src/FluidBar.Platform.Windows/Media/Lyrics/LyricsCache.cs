using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>
/// Bounded lyrics cache: entry count + byte budget + TTL, LRU eviction. One of the mandatory
/// "every cache has a budget and an eviction test" components (spec §10.1 #13, guardrail 6).
/// </summary>
public sealed class LyricsCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<CacheItem>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheItem> _lru = new();
    private readonly TimeProvider _timeProvider;
    private readonly PlatformCounters? _counters;
    private long _totalBytes;
    private long _hits;
    private long _misses;
    private long _evictions;
    private int _disposed;

    public LyricsCache(long maxBytes = 4L * 1024 * 1024, int maxEntries = 32, TimeSpan? ttl = null,
        TimeProvider? timeProvider = null, PlatformCounters? counters = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        MaxBytes = maxBytes;
        MaxEntries = maxEntries;
        Ttl = ttl ?? TimeSpan.FromMinutes(30);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _counters = counters;
    }

    public long MaxBytes { get; }

    public int MaxEntries { get; }

    public TimeSpan Ttl { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                return _totalBytes;
            }
        }
    }

    public long Hits => Interlocked.Read(ref _hits);

    public long Misses => Interlocked.Read(ref _misses);

    public long Evictions => Interlocked.Read(ref _evictions);

    /// <summary>Approximate memory footprint of a parsed document.</summary>
    public static long EstimateBytes(LyricsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        long bytes = 96 + (document.Title.Length * 2) + (document.Artist.Length * 2) + (document.Source.Length * 2);
        foreach (var line in document.Lines)
        {
            bytes += 48 + (line.Text.Length * 2) + ((line.Translation?.Length ?? 0) * 2);
        }

        return bytes;
    }

    /// <summary>Gets a non-expired entry and marks it most recently used.</summary>
    public bool TryGet(string key, out LyricsDocument? document)
    {
        document = null;
        if (string.IsNullOrEmpty(key) || Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Increment(ref _misses);
            return false;
        }

        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                Interlocked.Increment(ref _misses);
                _counters?.IncrementLyricsCacheMisses();
                return false;
            }

            if (_timeProvider.GetUtcNow() - node.Value.CreatedAt > Ttl)
            {
                RemoveNode(node);
                Interlocked.Increment(ref _misses);
                Interlocked.Increment(ref _evictions);
                _counters?.IncrementLyricsCacheEvictions();
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            document = node.Value.Document;
            Interlocked.Increment(ref _hits);
            _counters?.IncrementLyricsCacheHits();
            return true;
        }
    }

    /// <summary>Adds or replaces an entry, evicting expired/LRU entries until within budget.</summary>
    public void Add(string key, LyricsDocument document)
    {
        if (string.IsNullOrEmpty(key) || document is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var bytes = EstimateBytes(document);
        if (bytes > MaxBytes)
        {
            // A single oversized document is never cached (bounded-budget invariant).
            Interlocked.Increment(ref _evictions);
            _counters?.IncrementLyricsCacheEvictions();
            return;
        }

        lock (_gate)
        {
            RemoveExpiredLocked();
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _totalBytes -= existing.Value.Bytes;
                _map.Remove(key);
            }

            var node = _lru.AddFirst(new CacheItem(key, document, bytes, _timeProvider.GetUtcNow()));
            _map[key] = node;
            _totalBytes += bytes;

            while ((_totalBytes > MaxBytes || _map.Count > MaxEntries) && _lru.Last is { } last)
            {
                RemoveNode(last);
                Interlocked.Increment(ref _evictions);
                _counters?.IncrementLyricsCacheEvictions();
            }
        }
    }

    /// <summary>Removes one entry.</summary>
    public bool Remove(string key)
    {
        lock (_gate)
        {
            return _map.TryGetValue(key, out var node) && RemoveNode(node);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
            _totalBytes = 0;
        }
    }

    private void RemoveExpiredLocked()
    {
        var now = _timeProvider.GetUtcNow();
        var node = _lru.Last;
        while (node is not null)
        {
            var previous = node.Previous;
            if (now - node.Value.CreatedAt > Ttl)
            {
                RemoveNode(node);
                Interlocked.Increment(ref _evictions);
                _counters?.IncrementLyricsCacheEvictions();
            }

            node = previous;
        }
    }

    private bool RemoveNode(LinkedListNode<CacheItem> node)
    {
        _lru.Remove(node);
        _map.Remove(node.Value.Key);
        _totalBytes -= node.Value.Bytes;
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Clear();
    }

    private sealed record CacheItem(string Key, LyricsDocument Document, long Bytes, DateTimeOffset CreatedAt);
}
