using System.Collections.Immutable;

namespace FluidBar.Platform.Windows.Clipboard;

/// <summary>Kind of clipboard content detected by the service.</summary>
public enum ClipboardContentKind
{
    Unknown,
    Text,
    Image,
    Files,
}

/// <summary>One immutable clipboard history entry.</summary>
/// <param name="Sequence">Win32 clipboard sequence number at capture time.</param>
/// <param name="CapturedAtUtc">Capture timestamp.</param>
/// <param name="Kind">Content kind.</param>
/// <param name="Text">Text content (truncated to the configured cap), or null.</param>
/// <param name="OriginalTextLength">Untruncated text length (to show "long text").</param>
/// <param name="FilePaths">Dropped file paths when the payload is CF_HDROP.</param>
public sealed record ClipboardEntry(
    uint Sequence,
    DateTimeOffset CapturedAtUtc,
    ClipboardContentKind Kind,
    string? Text,
    int OriginalTextLength,
    ImmutableArray<string> FilePaths,
    byte[]? ImageDib = null)
{
    /// <summary>True when the captured text was truncated for storage.</summary>
    public bool IsTextTruncated => Text is not null && Text.Length < OriginalTextLength;
}

/// <summary>Immutable clipboard snapshot published by <see cref="ClipboardService"/>.</summary>
/// <param name="Latest">Most recently captured entry, or null when the clipboard is empty/unsupported.</param>
/// <param name="History">Bounded newest-first history.</param>
public sealed record ClipboardSnapshot(ClipboardEntry? Latest, ImmutableArray<ClipboardEntry> History)
{
    public static readonly ClipboardSnapshot Empty = new(null, ImmutableArray<ClipboardEntry>.Empty);

    public bool IsEmpty => Latest is null;
}

/// <summary>Options for clipboard capture.</summary>
public sealed class ClipboardServiceOptions
{
    /// <summary>Maximum stored characters per text entry (longer text is truncated for the island).</summary>
    public int MaxTextCharacters { get; init; } = 16_384;

    /// <summary>Maximum entries kept in the bounded history.</summary>
    public int MaxHistoryEntries { get; init; } = 50;

    /// <summary>Maximum number of file paths kept per entry.</summary>
    public int MaxFilePaths { get; init; } = 16;

    /// <summary>Maximum retained image entries; raw DIBs are much larger than text rows.</summary>
    public int MaxImageEntries { get; init; } = 5;

    /// <summary>Maximum attempts to open the clipboard when another process holds it.</summary>
    public int OpenClipboardAttempts { get; init; } = 10;

    /// <summary>Delay between clipboard open attempts.</summary>
    public TimeSpan OpenClipboardRetryDelay { get; init; } = TimeSpan.FromMilliseconds(20);
}

/// <summary>
/// Pure clipboard history: bounded ring with consecutive-duplicate suppression.
/// </summary>
public sealed class ClipboardHistory
{
    private readonly LinkedList<ClipboardEntry> _entries = new();
    private readonly object _gate = new();
    private readonly int _maxImageEntries;

    public ClipboardHistory(int maxEntries = 50, int maxImageEntries = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxImageEntries);
        MaxEntries = maxEntries;
        _maxImageEntries = maxImageEntries;
    }

    public int MaxEntries { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long Evictions { get; private set; }

    /// <summary>
    /// Adds an entry unless it duplicates the most recent one (same kind + same payload). Returns
    /// <c>true</c> when the entry was added.
    /// </summary>
    public bool TryAdd(ClipboardEntry entry, out ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            var latest = _entries.First?.Value;
            if (latest is not null && IsDuplicate(latest, entry))
            {
                snapshot = BuildSnapshotLocked();
                return false;
            }

            _entries.AddFirst(entry);
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveLast();
                Evictions++;
            }

            TrimImagesLocked();

            snapshot = BuildSnapshotLocked();
            return true;
        }
    }

    /// <summary>True when two entries carry the same payload.</summary>
    public static bool IsDuplicate(ClipboardEntry left, ClipboardEntry right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            ClipboardContentKind.Text => string.Equals(left.Text, right.Text, StringComparison.Ordinal),
            ClipboardContentKind.Files => left.FilePaths.SequenceEqual(right.FilePaths, StringComparer.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <summary>Current immutable snapshot (newest first).</summary>
    public ClipboardSnapshot Snapshot()
    {
        lock (_gate)
        {
            return BuildSnapshotLocked();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    /// <summary>Drops the oldest image entries once the image count exceeds the cap.</summary>
    private void TrimImagesLocked()
    {
        var images = _entries.Count(entry => entry.Kind == ClipboardContentKind.Image);
        var node = _entries.Last;
        while (node is not null && images > _maxImageEntries)
        {
            var previous = node.Previous;
            if (node.Value.Kind == ClipboardContentKind.Image)
            {
                _entries.Remove(node);
                images--;
                Evictions++;
            }

            node = previous;
        }
    }

    private ClipboardSnapshot BuildSnapshotLocked()
    {
        if (_entries.Count == 0)
        {
            return ClipboardSnapshot.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<ClipboardEntry>(_entries.Count);
        foreach (var entry in _entries)
        {
            builder.Add(entry);
        }

        return new ClipboardSnapshot(_entries.First!.Value, builder.MoveToImmutable());
    }
}
