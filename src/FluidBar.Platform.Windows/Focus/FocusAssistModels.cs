using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Focus;

/// <summary>Why the Windows focus/quiet-hours toggle is (or is not) usable.</summary>
public enum FocusAssistAvailability
{
    /// <summary>The documented <c>Windows.UI.Shell.FocusSessionManager</c> API is present and permitted.</summary>
    Available,

    /// <summary>The OS predates Windows 11 22H2 or the feature is not supported on this SKU.</summary>
    Unsupported,

    /// <summary>
    /// The API exists but is a Limited Access Feature (LAF) without an unlock token: every call fails
    /// with <c>E_ACCESSDENIED</c> (0x80070005, feature <c>com.microsoft.windows.focussessionmanager.1</c>).
    /// </summary>
    LimitedAccessDenied,

    /// <summary>Reading or changing the focus state failed for another reason.</summary>
    Failed,
}

/// <summary>Immutable focus/quiet-hours state published by <see cref="FocusAssistService"/>.</summary>
/// <param name="IsActive">Whether a Windows focus session is active (Do Not Disturb on).</param>
/// <param name="Availability">Whether the documented toggle API can be used.</param>
/// <param name="Reason">Exact reason when <see cref="Availability"/> is not <see cref="FocusAssistAvailability.Available"/>.</param>
/// <param name="Mechanism">The Windows API/feature backing the value (diagnostics, not localized).</param>
public sealed record FocusAssistSnapshot(
    bool IsActive,
    FocusAssistAvailability Availability,
    string? Reason = null,
    string? Mechanism = null)
{
    /// <summary>True only when the focus state can be read and changed through the documented API.</summary>
    public bool CanToggle => Availability == FocusAssistAvailability.Available;
}

/// <summary>Outcome of one focus/quiet-hours state-change request.</summary>
public enum FocusToggleOutcome
{
    /// <summary>Windows accepted the request (the observed state is in <see cref="FocusToggleResult.Snapshot"/>).</summary>
    Succeeded,

    /// <summary>The API is unavailable or LAF-gated; nothing was changed.</summary>
    Unsupported,

    /// <summary>The call failed; nothing was changed.</summary>
    Failed,
}

/// <summary>Result of <see cref="FocusAssistService.SetActiveAsync"/>.</summary>
/// <param name="Outcome">Succeeded / unsupported / failed.</param>
/// <param name="Snapshot">Observed focus state after the attempt, when readable.</param>
/// <param name="Reason">Exact failure reason for every non-succeeded outcome.</param>
public readonly record struct FocusToggleResult(
    FocusToggleOutcome Outcome,
    FocusAssistSnapshot? Snapshot = null,
    string? Reason = null)
{
    /// <summary>True only when Windows accepted the state change.</summary>
    public bool Succeeded => Outcome == FocusToggleOutcome.Succeeded;

    /// <summary>A successful request; the snapshot carries the observed post-change state.</summary>
    public static FocusToggleResult Success(FocusAssistSnapshot snapshot) =>
        new(FocusToggleOutcome.Succeeded, snapshot);

    /// <summary>The documented API is missing or LAF-gated.</summary>
    public static FocusToggleResult Unsupported(string reason, FocusAssistSnapshot? snapshot = null) =>
        new(FocusToggleOutcome.Unsupported, snapshot, reason);

    /// <summary>The call failed.</summary>
    public static FocusToggleResult Failed(string reason, FocusAssistSnapshot? snapshot = null) =>
        new(FocusToggleOutcome.Failed, snapshot, reason);
}

/// <summary>
/// Focus/quiet-hours quick-toggle service. Windows has no public quiet-hours (Focus Assist) registry or
/// WNF toggle; the only documented mechanism is <c>Windows.UI.Shell.FocusSessionManager</c> (Windows 11
/// 22H2 / UniversalApiContract v15), whose write methods are a Limited Access Feature. This service
/// reports that state explicitly instead of faking a toggle.
/// </summary>
public interface IFocusAssistService : IAsyncDisposable
{
    /// <summary>Explicit lifecycle state (same states as every platform monitor).</summary>
    PlatformMonitorState State { get; }

    /// <summary>Latest immutable snapshot, or null before the first observation.</summary>
    FocusAssistSnapshot? Current { get; }

    /// <summary>Whether a focus session is active, or null before the first observation.</summary>
    bool? IsActive { get; }

    /// <summary>Change-only snapshot notification (thread pool).</summary>
    event EventHandler<SnapshotEventArgs<FocusAssistSnapshot>>? SnapshotChanged;

    /// <summary>Starts the service, probes the documented API and reads the initial state.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the service and releases the WinRT manager reference.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-reads the focus state (event-driven; no polling).</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts (true) or ends (false) a focus session. Returns an explicit outcome.</summary>
    Task<FocusToggleResult> SetActiveAsync(bool active, CancellationToken cancellationToken = default);
}

/// <summary>
/// Test seam over <c>Windows.UI.Shell.FocusSessionManager</c>. The production implementation is
/// <see cref="WindowsFocusAssistBackend"/>; tests inject a fake so no focus session is created.
/// </summary>
public interface IFocusAssistBackend : IAsyncDisposable
{
    /// <summary>Raised when Windows reports a focus-state change outside this service.</summary>
    event EventHandler? StateChanged;

    /// <summary>Reads the current focus state. Must not throw for unsupported/Laf-gated systems.</summary>
    Task<FocusAssistSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests the focus state. Must not throw for expected failures.</summary>
    Task<FocusToggleResult> SetActiveAsync(bool active, CancellationToken cancellationToken = default);
}
