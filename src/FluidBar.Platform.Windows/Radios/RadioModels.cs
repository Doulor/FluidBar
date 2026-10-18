using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Radios;

/// <summary>Radio hardware that the island quick toggles can switch.</summary>
public enum PlatformRadioKind
{
    /// <summary>Wi-Fi (WLAN) radio.</summary>
    Wifi,

    /// <summary>Bluetooth radio.</summary>
    Bluetooth,
}

/// <summary>Why a radio is (or is not) switchable right now.</summary>
public enum RadioAvailability
{
    /// <summary>A radio of the requested kind exists and accepts software state changes.</summary>
    Available,

    /// <summary>No radio of this kind exists (no adapter / no Bluetooth hardware).</summary>
    NoAdapter,

    /// <summary>The user, system policy or an app capability denies radio control.</summary>
    AccessDenied,

    /// <summary>Powered off by a hardware switch or device firmware; software cannot change it.</summary>
    HardwareDisabled,

    /// <summary>The driver reports <c>RadioState.Unknown</c>; the state is not controllable right now.</summary>
    Unavailable,

    /// <summary>Reading the radio state failed (driver/WinRT failure).</summary>
    Failed,
}

/// <summary>Immutable radio state published by <see cref="RadioToggleService"/>.</summary>
/// <param name="Kind">Wi-Fi or Bluetooth.</param>
/// <param name="Availability">Whether the radio exists and can be switched.</param>
/// <param name="IsEnabled">Observed power state; false when the radio is absent or uncontrollable.</param>
/// <param name="RadioName">Friendly radio name reported by Windows (e.g. "WLAN"), when present.</param>
/// <param name="Reason">Exact reason when <see cref="Availability"/> is not <see cref="RadioAvailability.Available"/>.</param>
public sealed record RadioSnapshot(
    PlatformRadioKind Kind,
    RadioAvailability Availability,
    bool IsEnabled,
    string? RadioName = null,
    string? Reason = null)
{
    /// <summary>True only when a radio exists and software can switch it.</summary>
    public bool IsSupported => Availability == RadioAvailability.Available;

    /// <summary>Alias used by the App seam: an action may only be dispatched when this is true.</summary>
    public bool CanToggle => IsSupported;

    /// <summary>Builds the "no adapter of this kind" snapshot with the exact reason.</summary>
    public static RadioSnapshot NoAdapter(PlatformRadioKind kind) => new(
        kind,
        RadioAvailability.NoAdapter,
        false,
        null,
        kind == PlatformRadioKind.Wifi
            ? "no Wi-Fi radio is present (Windows.Devices.Radios returned no RadioKind.WiFi device)"
            : "no Bluetooth radio is present (Windows.Devices.Radios returned no RadioKind.Bluetooth device)");

    /// <summary>Builds a failure snapshot with the exact reason.</summary>
    public static RadioSnapshot Failed(PlatformRadioKind kind, string reason) => new(
        kind, RadioAvailability.Failed, false, null, reason);
}

/// <summary>Outcome of one radio state-change request.</summary>
public enum RadioToggleOutcome
{
    /// <summary>Windows accepted the request (the observed state is in <see cref="RadioToggleResult.Snapshot"/>).</summary>
    Succeeded,

    /// <summary>The radio is absent, hardware-disabled or blocked by policy; nothing was changed.</summary>
    Unsupported,

    /// <summary>The user/system denied radio access; nothing was changed.</summary>
    AccessDenied,

    /// <summary>The WinRT call failed; nothing was changed.</summary>
    Failed,
}

/// <summary>Result of <see cref="RadioToggleService.SetEnabledAsync"/>.</summary>
/// <param name="Outcome">Succeeded / unsupported / access denied / failed.</param>
/// <param name="Snapshot">Observed radio state after the attempt, when a radio was readable.</param>
/// <param name="Reason">Exact failure reason for every non-succeeded outcome.</param>
public readonly record struct RadioToggleResult(
    RadioToggleOutcome Outcome,
    RadioSnapshot? Snapshot = null,
    string? Reason = null)
{
    /// <summary>True only when Windows accepted the state change.</summary>
    public bool Succeeded => Outcome == RadioToggleOutcome.Succeeded;

    /// <summary>A successful request; the snapshot carries the observed post-change state.</summary>
    public static RadioToggleResult Success(RadioSnapshot snapshot) =>
        new(RadioToggleOutcome.Succeeded, snapshot);

    /// <summary>The radio cannot be switched (absent, hardware-disabled, policy).</summary>
    public static RadioToggleResult Unsupported(string reason, RadioSnapshot? snapshot = null) =>
        new(RadioToggleOutcome.Unsupported, snapshot, reason);

    /// <summary>Radio access was denied (user consent, system policy, app capability).</summary>
    public static RadioToggleResult AccessDenied(string reason, RadioSnapshot? snapshot = null) =>
        new(RadioToggleOutcome.AccessDenied, snapshot, reason);

    /// <summary>The WinRT call failed.</summary>
    public static RadioToggleResult Failed(string reason, RadioSnapshot? snapshot = null) =>
        new(RadioToggleOutcome.Failed, snapshot, reason);
}

/// <summary>
/// One radio toggle service (Wi-Fi or Bluetooth): event-driven state snapshot plus an async,
/// explicit-result state change. Implementations never block the caller and never fake success.
/// </summary>
public interface IRadioToggleService : IAsyncDisposable
{
    /// <summary>Wi-Fi or Bluetooth.</summary>
    PlatformRadioKind Kind { get; }

    /// <summary>Explicit lifecycle state (same states as every platform monitor).</summary>
    PlatformMonitorState State { get; }

    /// <summary>Latest immutable snapshot, or null before the first observation.</summary>
    RadioSnapshot? Current { get; }

    /// <summary>Observed power state, or null before the first observation.</summary>
    bool? IsEnabled { get; }

    /// <summary>Change-only snapshot notification (thread pool).</summary>
    event EventHandler<SnapshotEventArgs<RadioSnapshot>>? SnapshotChanged;

    /// <summary>Starts the service and reads the initial state.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the service and releases the radio reference.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-reads the current radio state (event-driven callers use this; no polling).</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests On/Off. Returns an explicit outcome; never throws for expected failures.</summary>
    Task<RadioToggleResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>
/// Test seam over <c>Windows.Devices.Radios</c>. The production implementation is
/// <see cref="WindowsRadioBackend"/>; tests inject an in-memory fake so no radio hardware is required.
/// </summary>
public interface IRadioBackend : IAsyncDisposable
{
    /// <summary>Wi-Fi or Bluetooth.</summary>
    PlatformRadioKind Kind { get; }

    /// <summary>Raised when Windows reports a radio state change outside this service.</summary>
    event EventHandler? StateChanged;

    /// <summary>Reads the current state. Must not throw for "no adapter" (report it in the snapshot).</summary>
    Task<RadioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests On/Off. Must not throw for expected failures (report them in the result).</summary>
    Task<RadioToggleResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
