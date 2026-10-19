using System.Globalization;
using System.Text.Json;
using FluidBar.Core.Diagnostics;

namespace FluidBar.App.Settings;

/// <summary>Persisted crash-guard state (<c>%LOCALAPPDATA%\FluidBar\state\crash-guard.json</c>).</summary>
/// <param name="ConsecutiveCrashes">Unclean starts observed since the last clean exit.</param>
/// <param name="LastCrashAtUtc">Timestamp of the most recent recorded crash.</param>
/// <param name="SafeModeEnteredAtUtc">Timestamp of the most recent automatic safe-mode entry.</param>
public sealed record CrashGuardState(
    int ConsecutiveCrashes,
    DateTimeOffset? LastCrashAtUtc,
    DateTimeOffset? SafeModeEnteredAtUtc)
{
    /// <summary>Empty state (clean machine).</summary>
    public static CrashGuardState Empty { get; } = new(0, null, null);
}

/// <summary>
/// Implements the advanced "crash guard" setting (spec §8.4/§9.3): unhandled exceptions write a crash
/// marker, and two consecutive crashes make the next start enter safe mode (community plugins off,
/// island hidden, Tier 0 material). A clean exit clears the counter.
/// </summary>
/// <remarks>
/// The crash file is small and rewritten atomically (temp + replace). Recording happens on the crash
/// path, where blocking file I/O is acceptable and preferable to losing the marker.
/// </remarks>
public sealed class CrashGuard
{
    /// <summary>Consecutive crashes that trigger safe mode (spec §8.4: 2).</summary>
    public const int DefaultSafeModeThreshold = 2;

    private readonly string _path;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly int _safeModeThreshold;

    /// <summary>Default state file path.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FluidBar",
        "state",
        "crash-guard.json");

    /// <summary>Creates a crash guard.</summary>
    /// <param name="path">State file path (tests use a temp directory); defaults to <see cref="DefaultPath"/>.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="timeProvider">Clock (tests).</param>
    /// <param name="safeModeThreshold">Consecutive crashes required for safe mode.</param>
    public CrashGuard(string? path = null, IAppLogger? logger = null, TimeProvider? timeProvider = null,
        int safeModeThreshold = DefaultSafeModeThreshold)
    {
        _path = path ?? DefaultPath;
        _logger = logger ?? NullAppLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;
        _safeModeThreshold = Math.Max(1, safeModeThreshold);
    }

    /// <summary>State file path (diagnostics/tests).</summary>
    public string StatePath => _path;

    /// <summary>Reads the persisted state; a missing/corrupt file yields <see cref="CrashGuardState.Empty"/>.</summary>
    public CrashGuardState Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return CrashGuardState.Empty;
            }

            using var stream = File.OpenRead(_path);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var crashes = root.TryGetProperty("consecutiveCrashes", out var count) && count.TryGetInt32(out var value)
                ? Math.Max(0, value)
                : 0;
            var lastCrash = ReadTimestamp(root, "lastCrashAtUtc");
            var safeModeAt = ReadTimestamp(root, "safeModeEnteredAtUtc");
            return new CrashGuardState(crashes, lastCrash, safeModeAt);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.Warning(LogCategories.Lifecycle, "crash-guard.read-failed",
                "The crash-guard state could not be read; treating it as a clean start.", ex);
            return CrashGuardState.Empty;
        }
    }

    /// <summary>
    /// Decides whether this start must enter safe mode. Returns true once per crash streak and resets
    /// the counter so the user is not trapped in safe mode after a single bad run.
    /// </summary>
    public bool TryEnterSafeMode()
    {
        var state = Read();
        if (state.ConsecutiveCrashes < _safeModeThreshold)
        {
            return false;
        }

        var now = _time.GetUtcNow();
        Write(new CrashGuardState(0, state.LastCrashAtUtc, now));
        _logger.Critical(LogCategories.Lifecycle, "crash-guard.safe-mode",
            $"Entering safe mode after {state.ConsecutiveCrashes} consecutive crashes.",
            data: new Dictionary<string, object?>
            {
                ["crashes"] = state.ConsecutiveCrashes,
                ["threshold"] = _safeModeThreshold,
                ["state"] = _path,
            });
        return true;
    }

    /// <summary>Records an unhandled exception (writes the crash marker/counter).</summary>
    /// <param name="exception">Exception that escaped a handler.</param>
    /// <param name="origin">Where it was caught (<c>appdomain</c>/<c>task</c>).</param>
    public void RecordUnhandledException(Exception exception, string origin)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var state = Read();
        var now = _time.GetUtcNow();
        Write(new CrashGuardState(state.ConsecutiveCrashes + 1, now, state.SafeModeEnteredAtUtc));
        _logger.Error(LogCategories.Lifecycle, "crash-guard.crash-recorded",
            "An unhandled exception was recorded by the crash guard.",
            exception,
            new Dictionary<string, object?>
            {
                ["origin"] = origin,
                ["crashes"] = state.ConsecutiveCrashes + 1,
            });
    }

    /// <summary>Clears the crash streak (clean exit).</summary>
    public void MarkCleanExit()
    {
        var state = Read();
        if (state.ConsecutiveCrashes == 0)
        {
            return;
        }

        Write(new CrashGuardState(0, state.LastCrashAtUtc, state.SafeModeEnteredAtUtc));
    }

    /// <summary>Resets the whole state (user reset / recovery).</summary>
    public void Reset() => Write(CrashGuardState.Empty);

    private static DateTimeOffset? ReadTimestamp(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
               DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private void Write(CrashGuardState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("consecutiveCrashes", state.ConsecutiveCrashes);
                if (state.LastCrashAtUtc is { } lastCrash)
                {
                    writer.WriteString("lastCrashAtUtc", lastCrash.ToString("O", CultureInfo.InvariantCulture));
                }

                if (state.SafeModeEnteredAtUtc is { } safeModeAt)
                {
                    writer.WriteString("safeModeEnteredAtUtc", safeModeAt.ToString("O", CultureInfo.InvariantCulture));
                }

                writer.WriteEndObject();
            }

            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, stream.ToArray());
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The crash path must not throw; a lost marker only weakens the guard.
            _logger.Warning(LogCategories.Lifecycle, "crash-guard.write-failed",
                "The crash-guard state could not be written.", ex);
        }
    }
}
