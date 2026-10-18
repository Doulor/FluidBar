namespace FluidBar.Core.Diagnostics;

/// <summary>
/// Severity of a structured log entry. Values are ordered so that
/// <c>level &gt;= threshold</c> can be used for filtering.
/// </summary>
public enum LogLevel
{
    /// <summary>Fine-grained diagnostic detail, disabled in shipping builds.</summary>
    Trace = 0,

    /// <summary>Diagnostic detail useful while developing.</summary>
    Debug = 1,

    /// <summary>Normal lifecycle information.</summary>
    Information = 2,

    /// <summary>A recoverable problem with a visible degradation.</summary>
    Warning = 3,

    /// <summary>An operation failed; the application keeps running in a degraded state.</summary>
    Error = 4,

    /// <summary>The process or a subsystem cannot continue safely.</summary>
    Critical = 5,
}

/// <summary>
/// Canonical log categories. Every log entry must carry one of these values so
/// log slices stay stable across releases (spec 9.3: <c>cat</c> is a fixed enum).
/// </summary>
public static class LogCategories
{
    /// <summary>Application and window lifecycle.</summary>
    public const string Lifecycle = "lifecycle";

    /// <summary>Activity scheduling and island states.</summary>
    public const string Activity = "activity";

    /// <summary>Spring/frame loop diagnostics.</summary>
    public const string Motion = "motion";

    /// <summary>Render surface and composition.</summary>
    public const string Render = "render";

    /// <summary>Plugin loading, activation and isolation.</summary>
    public const string Plugin = "plugin";

    /// <summary>Marketplace index, download and signature checks.</summary>
    public const string Market = "market";

    /// <summary>Settings load/save/migration.</summary>
    public const string Settings = "settings";

    /// <summary>Platform services (monitors, media, clipboard, notifications...).</summary>
    public const string Platform = "platform";

    /// <summary>All categories, for validation/tests.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Lifecycle, Activity, Motion, Render, Plugin, Market, Settings, Platform,
    ];
}

/// <summary>
/// Structured logging abstraction owned by the core. Implementations live in the host
/// (JSON-lines file sink, debug output, tests); the core never writes files or the console.
/// </summary>
public interface IAppLogger
{
    /// <summary>Returns whether entries of <paramref name="level"/> are recorded.</summary>
    bool IsEnabled(LogLevel level);

    /// <summary>
    /// Records one structured entry.
    /// </summary>
    /// <param name="level">Severity of the entry.</param>
    /// <param name="category">One of <see cref="LogCategories"/>.</param>
    /// <param name="eventId">Stable identifier used for dedupe/rate limiting (e.g. <c>settings.save.failed</c>).</param>
    /// <param name="message">Human-readable message; never localized and never shown directly to end users.</param>
    /// <param name="exception">Optional exception; implementations must log type/message/stack.</param>
    /// <param name="data">Optional structured fields.</param>
    void Log(
        LogLevel level,
        string category,
        string eventId,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? data = null);
}

/// <summary>Convenience helpers for <see cref="IAppLogger"/>.</summary>
public static class AppLoggerExtensions
{
    /// <inheritdoc cref="IAppLogger.Log"/>
    public static void Trace(this IAppLogger logger, string category, string eventId, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
        => logger.Log(LogLevel.Trace, category, eventId, message, exception, data);

    /// <inheritdoc cref="IAppLogger.Log"/>
    public static void Debug(this IAppLogger logger, string category, string eventId, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
        => logger.Log(LogLevel.Debug, category, eventId, message, exception, data);

    /// <inheritdoc cref="IAppLogger.Log"/>
    public static void Information(this IAppLogger logger, string category, string eventId, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
        => logger.Log(LogLevel.Information, category, eventId, message, exception, data);

    /// <inheritdoc cref="IAppLogger.Log"/>
    public static void Warning(this IAppLogger logger, string category, string eventId, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
        => logger.Log(LogLevel.Warning, category, eventId, message, exception, data);

    /// <inheritdoc cref="IAppLogger.Log"/>
    public static void Error(this IAppLogger logger, string category, string eventId, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
        => logger.Log(LogLevel.Error, category, eventId, message, exception, data);

    /// <inheritdoc cref="IAppLogger.Log"/>
    public static void Critical(this IAppLogger logger, string category, string eventId, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
        => logger.Log(LogLevel.Critical, category, eventId, message, exception, data);
}

/// <summary>
/// Logger that discards everything. Used as the default in pure unit tests and in
/// object graphs where logging is optional.
/// </summary>
public sealed class NullAppLogger : IAppLogger
{
    /// <summary>Shared instance.</summary>
    public static NullAppLogger Instance { get; } = new();

    private NullAppLogger()
    {
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level) => false;

    /// <inheritdoc />
    public void Log(
        LogLevel level,
        string category,
        string eventId,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
    }
}

/// <summary>One entry captured by <see cref="RecordingAppLogger"/>.</summary>
/// <param name="Level">Severity.</param>
/// <param name="Category">Log category.</param>
/// <param name="EventId">Stable event identifier.</param>
/// <param name="Message">Message text.</param>
/// <param name="Exception">Optional exception.</param>
/// <param name="Timestamp">Timestamp supplied by the injected <see cref="TimeProvider"/>.</param>
/// <param name="Data">Optional structured fields.</param>
public sealed record LogEntry(
    LogLevel Level,
    string Category,
    string EventId,
    string Message,
    Exception? Exception,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, object?>? Data);

/// <summary>
/// Bounded, thread-safe in-memory logger for tests and the diagnostics bundle.
/// The ring buffer is bounded so a failing subsystem can never grow memory without limit.
/// </summary>
public sealed class RecordingAppLogger : IAppLogger
{
    private readonly object _gate = new();
    private readonly Queue<LogEntry> _entries;
    private readonly int _capacity;
    private readonly TimeProvider _timeProvider;
    private readonly LogLevel _minimumLevel;
    private long _dropped;

    /// <summary>Creates a recording logger.</summary>
    /// <param name="capacity">Maximum retained entries; older entries are dropped first.</param>
    /// <param name="minimumLevel">Minimum retained level.</param>
    /// <param name="timeProvider">Clock used for entry timestamps; defaults to <see cref="TimeProvider.System"/>.</param>
    public RecordingAppLogger(int capacity = 256, LogLevel minimumLevel = LogLevel.Trace, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _entries = new Queue<LogEntry>(capacity);
        _minimumLevel = minimumLevel;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Number of entries currently retained.</summary>
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

    /// <summary>Number of entries dropped because the ring buffer was full.</summary>
    public long DroppedCount
    {
        get
        {
            lock (_gate)
            {
                return _dropped;
            }
        }
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level) => level >= _minimumLevel;

    /// <inheritdoc />
    public void Log(
        LogLevel level,
        string category,
        string eventId,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var entry = new LogEntry(level, category, eventId, message, exception, _timeProvider.GetUtcNow(), data);
        lock (_gate)
        {
            if (_entries.Count == _capacity)
            {
                _entries.Dequeue();
                _dropped++;
            }

            _entries.Enqueue(entry);
        }
    }

    /// <summary>Returns a stable snapshot of the retained entries (oldest first).</summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    /// <summary>Returns whether any retained entry has the given event id.</summary>
    public bool HasEvent(string eventId)
    {
        lock (_gate)
        {
            return _entries.Any(e => string.Equals(e.EventId, eventId, StringComparison.Ordinal));
        }
    }

    /// <summary>Removes all retained entries.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}
