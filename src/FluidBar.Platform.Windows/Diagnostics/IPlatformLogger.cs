namespace FluidBar.Platform.Windows.Diagnostics;

/// <summary>
/// Logging abstraction for the Windows platform layer. Deliberately tiny so the App project can
/// adapt its own (Core) logger to it, or plug the rolling file sink in directly.
/// </summary>
/// <remarks>
/// Implementations must never throw: a failing log sink may not take a monitor down.
/// Implementations must not perform blocking I/O on the calling thread (see <see cref="RollingFileLogger"/>).
/// </remarks>
public interface IPlatformLogger
{
    /// <summary>Returns whether an entry of <paramref name="level"/> would be recorded.</summary>
    bool IsEnabled(PlatformLogLevel level);

    /// <summary>Records one entry. Must be cheap and non-blocking.</summary>
    void Log(in PlatformLogEntry entry);
}

/// <summary>Convenience factory methods over <see cref="IPlatformLogger"/>.</summary>
public static class PlatformLoggerExtensions
{
    public static void LogTrace(this IPlatformLogger logger, PlatformLogCategory category, string eventId, string message,
        IReadOnlyDictionary<string, object?>? data = null)
        => Write(logger, PlatformLogLevel.Trace, category, eventId, message, null, data);

    public static void LogDebug(this IPlatformLogger logger, PlatformLogCategory category, string eventId, string message,
        IReadOnlyDictionary<string, object?>? data = null)
        => Write(logger, PlatformLogLevel.Debug, category, eventId, message, null, data);

    public static void LogInfo(this IPlatformLogger logger, PlatformLogCategory category, string eventId, string message,
        IReadOnlyDictionary<string, object?>? data = null)
        => Write(logger, PlatformLogLevel.Info, category, eventId, message, null, data);

    public static void LogWarning(this IPlatformLogger logger, PlatformLogCategory category, string eventId, string message,
        Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
        => Write(logger, PlatformLogLevel.Warning, category, eventId, message, exception, data);

    public static void LogError(this IPlatformLogger logger, PlatformLogCategory category, string eventId, string message,
        Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
        => Write(logger, PlatformLogLevel.Error, category, eventId, message, exception, data);

    public static void LogCritical(this IPlatformLogger logger, PlatformLogCategory category, string eventId, string message,
        Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
        => Write(logger, PlatformLogLevel.Critical, category, eventId, message, exception, data);

    private static void Write(IPlatformLogger logger, PlatformLogLevel level, PlatformLogCategory category,
        string eventId, string message, Exception? exception, IReadOnlyDictionary<string, object?>? data)
    {
        if (logger is null || !logger.IsEnabled(level))
        {
            return;
        }

        var entry = new PlatformLogEntry(
            DateTimeOffset.UtcNow,
            level,
            category,
            eventId,
            message,
            data,
            exception is null ? null : $"{exception.GetType().FullName}: {exception.Message}");
        logger.Log(in entry);
    }

    /// <summary>
    /// Writes an entry with an explicit timestamp. Used by sinks/services that already have a
    /// <see cref="TimeProvider"/> and by tests.
    /// </summary>
    public static void LogAt(this IPlatformLogger logger, PlatformLogLevel level, PlatformLogCategory category,
        string eventId, string message, DateTimeOffset timestamp,
        Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
    {
        if (logger is null || !logger.IsEnabled(level))
        {
            return;
        }

        var entry = new PlatformLogEntry(timestamp, level, category, eventId, message,
            data, exception is null ? null : $"{exception.GetType().FullName}: {exception.Message}");
        logger.Log(in entry);
    }
}

/// <summary>Logger that discards everything. Useful in tests and for disabled subsystems.</summary>
public sealed class NullPlatformLogger : IPlatformLogger
{
    public static readonly NullPlatformLogger Instance = new();

    private NullPlatformLogger()
    {
    }

    public bool IsEnabled(PlatformLogLevel level) => false;

    public void Log(in PlatformLogEntry entry)
    {
    }
}

/// <summary>
/// Adapter that forwards to a callback (for wiring the Core logger / a diagnostics view into
/// the platform layer without introducing a Core dependency here).
/// </summary>
public sealed class DelegatingPlatformLogger : IPlatformLogger
{
    private readonly Action<PlatformLogEntry> _sink;
    private readonly PlatformLogLevel _minimumLevel;

    public DelegatingPlatformLogger(Action<PlatformLogEntry> sink, PlatformLogLevel minimumLevel = PlatformLogLevel.Debug)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _minimumLevel = minimumLevel;
    }

    public bool IsEnabled(PlatformLogLevel level) => level >= _minimumLevel;

    public void Log(in PlatformLogEntry entry)
    {
        try
        {
            _sink(entry);
        }
        catch (Exception)
        {
            // A log sink must never take the producer down. Swallowing is intentional and the
            // only place in this layer where an exception is not recorded (recursion risk).
        }
    }
}
