using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace FluidBar.Platform.Windows.Notifications;

/// <summary>Permission state of the Windows notification listener.</summary>
public enum NotificationAccessState
{
    Unavailable,
    Denied,
    Unspecified,
    Allowed,
}

/// <summary>One captured toast notification.</summary>
public sealed record NotificationEntry(
    uint Id,
    string AppName,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    string? VerificationCode);

/// <summary>A raw toast candidate before filtering/deduplication (pure input for the history).</summary>
public sealed record NotificationCandidate(
    uint Id,
    string AppName,
    string Title,
    string Body,
    DateTimeOffset CreatedAt);

/// <summary>Immutable notifications snapshot.</summary>
/// <param name="Access">Current listener permission state.</param>
/// <param name="Recent">Most recent notifications, newest first (bounded).</param>
public sealed record NotificationsSnapshot(
    NotificationAccessState Access,
    ImmutableArray<NotificationEntry> Recent)
{
    public static readonly NotificationsSnapshot Empty =
        new(NotificationAccessState.Unavailable, ImmutableArray<NotificationEntry>.Empty);

    public NotificationEntry? Latest => Recent.IsEmpty ? null : Recent[0];
}

/// <summary>
/// Pure verification-code detector (v1 behaviour: 6-digit code in the toast body).
/// Prefers a code near an explicit keyword; ignores digits embedded in longer numbers.
/// </summary>
public static partial class VerificationCodeDetector
{
    private static readonly string[] Keywords =
    [
        "验证码", "校验码", "动态码", "确认码", "verification code", "verify code", "otp", "code", "pin",
        "安全码", "认证码",
    ];

    /// <summary>Returns the most plausible 6-digit verification code, or null.</summary>
    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string? fallback = null;
        foreach (Match match in SixDigitPattern().Matches(text))
        {
            fallback ??= match.Value;
            if (HasKeywordNearby(text, match.Index))
            {
                return match.Value;
            }
        }

        return fallback;
    }

    /// <summary>True when the text contains any verification-code keyword.</summary>
    public static bool ContainsVerificationKeyword(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lowered = text.ToLowerInvariant();
        return Keywords.Any(keyword => lowered.Contains(keyword, StringComparison.Ordinal));
    }

    private static bool HasKeywordNearby(string text, int matchIndex)
    {
        var start = Math.Max(0, matchIndex - 24);
        var window = text.AsSpan(start, Math.Min(matchIndex - start, 24));
        var lowered = window.ToString().ToLowerInvariant();
        return Keywords.Any(keyword => lowered.Contains(keyword, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"(?<!\d)\d{6}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex SixDigitPattern();
}

/// <summary>
/// Pure bounded notification history: ring buffer of seen ids plus a monotonic watermark so a fresh
/// listener start never replays the whole notification history (v1 unbounded HashSet + 1.6 s full poll).
/// </summary>
public sealed class NotificationHistory
{
    private readonly object _gate = new();
    private readonly HashSet<uint> _seen = new();
    private readonly Queue<uint> _seenOrder = new();
    private uint _watermark;
    private bool _primed;

    public NotificationHistory(int maxSeenIds = 256, int maxBatch = 20)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSeenIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBatch);
        MaxSeenIds = maxSeenIds;
        MaxBatch = maxBatch;
    }

    public int MaxSeenIds { get; }

    public int MaxBatch { get; }

    public bool IsPrimed
    {
        get
        {
            lock (_gate)
            {
                return _primed;
            }
        }
    }

    public uint Watermark
    {
        get
        {
            lock (_gate)
            {
                return _watermark;
            }
        }
    }

    public int SeenCount
    {
        get
        {
            lock (_gate)
            {
                return _seen.Count;
            }
        }
    }

    /// <summary>
    /// Marks the current notification ids as already known without producing entries. Called once on
    /// start so existing toasts do not replay.
    /// </summary>
    public void Prime(IEnumerable<uint> existingIds)
    {
        lock (_gate)
        {
            foreach (var id in existingIds)
            {
                RememberLocked(id);
                if (id > _watermark)
                {
                    _watermark = id;
                }
            }

            _primed = true;
        }
    }

    /// <summary>Filters and converts candidates into new entries (oldest first, bounded batch).</summary>
    public IReadOnlyList<NotificationEntry> Process(IEnumerable<NotificationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        lock (_gate)
        {
            if (!_primed)
            {
                // Defensive: never replay before the caller primes (event raced start).
                foreach (var candidate in candidates)
                {
                    RememberLocked(candidate.Id);
                }

                _primed = true;
                return Array.Empty<NotificationEntry>();
            }

            var result = new List<NotificationEntry>();
            foreach (var candidate in candidates.OrderBy(candidate => candidate.CreatedAt))
            {
                if (candidate.Id <= _watermark || _seen.Contains(candidate.Id))
                {
                    continue;
                }

                RememberLocked(candidate.Id);
                if (candidate.Id > _watermark)
                {
                    _watermark = candidate.Id;
                }

                result.Add(new NotificationEntry(candidate.Id, candidate.AppName, candidate.Title, candidate.Body,
                    candidate.CreatedAt, VerificationCodeDetector.Detect($"{candidate.Title}\n{candidate.Body}")));
                if (result.Count >= MaxBatch)
                {
                    break;
                }
            }

            return result;
        }
    }

    /// <summary>Clears history (used when permission is revoked or on restart).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _seen.Clear();
            _seenOrder.Clear();
            _watermark = 0;
            _primed = false;
        }
    }

    private void RememberLocked(uint id)
    {
        if (!_seen.Add(id))
        {
            return;
        }

        _seenOrder.Enqueue(id);
        while (_seenOrder.Count > MaxSeenIds)
        {
            _seen.Remove(_seenOrder.Dequeue());
        }
    }
}
