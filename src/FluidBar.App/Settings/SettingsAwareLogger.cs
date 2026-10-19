using System.Diagnostics;
using FluidBar.Core.Diagnostics;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.App.Settings;

/// <summary>
/// The App logger with a level that follows the advanced "log level" setting, plus the platform
/// JSON-lines file sink (spec §8.1/§9.3: <c>%LOCALAPPDATA%\FluidBar\logs</c>, 5 MB × 3 rotations).
/// </summary>
/// <remarks>
/// The minimum level is read from the live settings snapshot on every call, so changing it in the
/// settings window takes effect immediately. Trace output is kept (developers/debugger), and the file
/// sink is optional so headless tests and the smoke harness need no disk.
/// </remarks>
public sealed class SettingsAwareLogger : IAppLogger, IAsyncDisposable
{
    private readonly Func<string?> _levelToken;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private RollingFileLogger? _fileSink;
    private int _disposed;

    /// <summary>Creates the logger.</summary>
    /// <param name="levelToken">Provider for the configured level token (<c>trace/debug/info/warn/error</c>).</param>
    public SettingsAwareLogger(Func<string?> levelToken)
        => _levelToken = levelToken ?? throw new ArgumentNullException(nameof(levelToken));

    /// <summary>Number of entries written (diagnostics/tests).</summary>
    public long Written { get; private set; }

    /// <summary>Path of the active JSONL file, or null when the file sink is not running.</summary>
    public string? LogFilePath => _fileSink?.ActiveFilePath;

    /// <summary>The current minimum level.</summary>
    public LogLevel MinimumLevel => ParseLevel(_levelToken());

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level) => level >= MinimumLevel;

    /// <summary>Starts the platform JSON-lines file sink (background writer, no UI-thread I/O).</summary>
    /// <param name="directory">Log directory, e.g. <c>%LOCALAPPDATA%\FluidBar\logs</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartFileSinkAsync(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_fileSink is not null)
        {
            return;
        }

        var sink = new RollingFileLogger(new RollingLogOptions
        {
            DirectoryPath = directory,
            MinimumLevel = PlatformLogLevel.Trace,
            MaxFiles = 3,
            MaxFileBytes = 5L * 1024 * 1024,
        });
        await sink.StartAsync(cancellationToken).ConfigureAwait(false);
        _fileSink = sink;
    }

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

        var sink = _fileSink;
        if (sink is null)
        {
            return;
        }

        try
        {
            sink.Log(new PlatformLogEntry(
                DateTimeOffset.UtcNow,
                ToPlatformLevel(level),
                ToPlatformCategory(category),
                eventId,
                message,
                data,
                exception?.ToString()));
        }
        catch (Exception)
        {
            // The logging path must never take a producer down.
        }
    }

    /// <summary>Maps a level token onto the Core level enum; unknown tokens fall back to <c>warn</c>.</summary>
    /// <param name="token">Configured token.</param>
    public static LogLevel ParseLevel(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "info" => LogLevel.Information,
        "error" => LogLevel.Error,
        _ => LogLevel.Warning,
    };

    private static PlatformLogLevel ToPlatformLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => PlatformLogLevel.Trace,
        LogLevel.Debug => PlatformLogLevel.Debug,
        LogLevel.Information => PlatformLogLevel.Info,
        LogLevel.Warning => PlatformLogLevel.Warning,
        LogLevel.Error => PlatformLogLevel.Error,
        _ => PlatformLogLevel.Critical,
    };

    private static PlatformLogCategory ToPlatformCategory(string category) => category switch
    {
        LogCategories.Lifecycle => PlatformLogCategory.Lifecycle,
        LogCategories.Activity => PlatformLogCategory.Activity,
        LogCategories.Motion => PlatformLogCategory.Motion,
        LogCategories.Render => PlatformLogCategory.Render,
        LogCategories.Plugin => PlatformLogCategory.Plugin,
        LogCategories.Market => PlatformLogCategory.Market,
        LogCategories.Settings => PlatformLogCategory.Settings,
        _ => PlatformLogCategory.Platform,
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_fileSink is not null)
        {
            await _fileSink.DisposeAsync().ConfigureAwait(false);
            _fileSink = null;
        }
    }
}
