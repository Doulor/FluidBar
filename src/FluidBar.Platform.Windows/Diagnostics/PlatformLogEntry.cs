namespace FluidBar.Platform.Windows.Diagnostics;

/// <summary>Severity of a platform log entry. Mirrors the categories used by the Core diagnostics contract.</summary>
public enum PlatformLogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
}

/// <summary>
/// Fixed, closed set of log categories. The categories are the structured-logging contract:
/// dashboards and the diagnostics bundle group by these names, so they must not be invented ad hoc.
/// </summary>
public enum PlatformLogCategory
{
    Lifecycle,
    Activity,
    Motion,
    Render,
    Plugin,
    Market,
    Settings,
    Platform,
    Monitor,
    Media,
    Clipboard,
    Notifications,
    Shell,
    Agent,
    Update,
    Diagnostics,
}

/// <summary>
/// One structured log record. Immutable: producers may hand the same value to several sinks.
/// </summary>
/// <param name="Timestamp">Time the event happened (from the injected <see cref="TimeProvider"/>).</param>
/// <param name="Level">Severity.</param>
/// <param name="Category">Closed-set category.</param>
/// <param name="EventId">Stable, greppable identifier such as <c>monitor.volume.start-failed</c>.</param>
/// <param name="Message">Human-readable, already-localized-independent text (English).</param>
/// <param name="Data">Optional flat key/value payload (JSON-serializable primitives only).</param>
/// <param name="Exception">Optional exception text (already formatted, never a live exception object).</param>
public readonly record struct PlatformLogEntry(
    DateTimeOffset Timestamp,
    PlatformLogLevel Level,
    PlatformLogCategory Category,
    string EventId,
    string Message,
    IReadOnlyDictionary<string, object?>? Data = null,
    string? Exception = null);
