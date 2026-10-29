namespace FluidBar.Core.Telemetry;

/// <summary>Why a FluidBar session ended.</summary>
public enum TelemetryStopReason
{
    /// <summary>The user quit FluidBar.</summary>
    User,

    /// <summary>Windows shut down or the user logged off.</summary>
    System,

    /// <summary>The process ended unexpectedly (crash guard or unhandled exception).</summary>
    Crash,
}

/// <summary>
/// One completed session of the app.
/// </summary>
/// <remarks>
/// Every member is a number, a flag or a timestamp, so the anonymous payload cannot carry a song title, a
/// notification body, a file path, a window title or any other user content — see docs/privacy/telemetry.md.
/// </remarks>
public sealed record TelemetryRun
{
    /// <summary>How long a session may report at most (a week; longer uptimes are clamped).</summary>
    public const int MaxUptimeSeconds = 7 * 24 * 60 * 60;

    /// <summary>Anonymous per-install identifier (random GUID, stored locally, resettable).</summary>
    public required string InstallId { get; init; }

    /// <summary>Session start (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Session end (UTC).</summary>
    public required DateTimeOffset StoppedAt { get; init; }

    /// <summary>Milliseconds from process start to the first rendered island frame.</summary>
    public int StartupMs { get; init; }

    /// <summary>True for the very first session of this install.</summary>
    public bool FirstRun { get; init; }

    /// <summary>Why the session ended.</summary>
    public TelemetryStopReason Reason { get; init; }

    /// <summary>Session length in whole seconds, clamped to <see cref="MaxUptimeSeconds"/>.</summary>
    public int UptimeSeconds =>
        (int)Math.Clamp((StoppedAt - StartedAt).TotalSeconds, 0, MaxUptimeSeconds);
}
