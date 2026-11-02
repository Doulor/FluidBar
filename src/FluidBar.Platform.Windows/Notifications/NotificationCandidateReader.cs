using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Notifications;

/// <summary>Isolates transient WinRT projection failures with bounded retry state and one diagnostic per incident.</summary>
internal sealed class NotificationCandidateReader(IPlatformLogger logger, TimeProvider time, int capacity)
{
    private sealed record Failure(int Attempts, DateTimeOffset RetryAt);
    private readonly object _gate = new();
    private readonly Dictionary<uint, Failure> _failures = [];
    private readonly int _capacity = Math.Clamp(capacity, 1, 4096);
    private int _generation;

    internal int FailureCount { get { lock (_gate) return _failures.Count; } }

    internal NotificationCandidate? Read(uint id, Func<NotificationCandidate> read)
    {
        int generation;
        lock (_gate)
        {
            generation = _generation;
            if (_failures.TryGetValue(id, out var failure) && time.GetUtcNow() < failure.RetryAt) return null;
        }
        try
        {
            var candidate = read();
            lock (_gate)
            {
                if (_generation != generation) return null;
                _failures.Remove(id);
            }
            return candidate;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            bool first;
            double seconds;
            lock (_gate)
            {
                if (_generation != generation) return null;
                first = !_failures.TryGetValue(id, out var previous);
                var attempts = Math.Min(16, (previous?.Attempts ?? 0) + 1);
                seconds = Math.Min(300, 30 * Math.Pow(2, attempts - 1));
                if (first && _failures.Count >= _capacity)
                    _failures.Remove(_failures.MinBy(pair => pair.Value.RetryAt).Key);
                _failures[id] = new Failure(attempts, time.GetUtcNow().AddSeconds(seconds));
            }
            if (first)
                logger.LogWarning(PlatformLogCategory.Notifications, "notifications.parse-failed",
                    "Could not parse a toast notification; this item will retry with backoff.", ex,
                    new Dictionary<string, object?> { ["notificationId"] = id, ["retryAfterSeconds"] = seconds, ["hresult"] = $"0x{ex.HResult:X8}" });
            return null;
        }
    }

    internal void Prune(IReadOnlySet<uint> liveIds)
    {
        lock (_gate)
            foreach (var id in _failures.Keys.Where(id => !liveIds.Contains(id)).ToArray()) _failures.Remove(id);
    }

    internal void Reset()
    {
        lock (_gate) { _generation++; _failures.Clear(); }
    }

    internal static NotificationCandidate Parse(uint id, Func<string?> readAppName,
        Func<IReadOnlyList<string>> readTexts, Func<DateTimeOffset> readCreatedAt, int maxCharacters)
    {
        var appName = ReadAppName(readAppName);
        var texts = readTexts().Where(text => !string.IsNullOrWhiteSpace(text)).Take(64).ToArray();
        var max = Math.Max(1, maxCharacters);
        return new NotificationCandidate(id, Truncate(appName, max), Truncate(texts.ElementAtOrDefault(0) ?? appName, max),
            Truncate(texts.Length > 1 ? string.Join(Environment.NewLine, texts.Skip(1)) : string.Empty, max), readCreatedAt());
    }

    internal static string ReadAppName(Func<string?> read)
    {
        try { return read() is { Length: > 0 } name ? name : "Windows"; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { return "Windows"; } // An expired app registration must not discard text or stop icon processing.
    }

    private static string Truncate(string value, int length)
    {
        if (value.Length <= length) return value;
        if (char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length];
    }
}
