using System.Diagnostics;
using FluidBar.Core.Diagnostics;

namespace FluidBar.App.Diagnostics;

/// <summary>
/// Trace-backed implementation of the Core logger for the App process. No file or console I/O on the
/// caller thread: entries go to <see cref="Trace"/>, and Platform's rolling file sink is wired through
/// <c>AppLoggerPlatformAdapter</c> when the platform host is composed.
/// </summary>
public sealed class TraceAppLogger : IAppLogger
{
    private readonly LogLevel _minimumLevel;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>Creates a trace logger.</summary>
    public TraceAppLogger(LogLevel minimumLevel = LogLevel.Information) => _minimumLevel = minimumLevel;

    /// <summary>Number of entries written (diagnostics/tests).</summary>
    public long Written { get; private set; }

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

        Written++;
        var line = $"[fluidbar {_clock.Elapsed.TotalSeconds:0.000}s {level} {category}] {eventId}: {message}";
        if (exception is not null)
        {
            line += $" | {exception.GetType().Name}: {exception.Message}";
        }

        if (data is { Count: > 0 })
        {
            line += " | " + string.Join(", ", data.Select(static pair => $"{pair.Key}={pair.Value}"));
        }

        Trace.WriteLine(line);
    }
}
