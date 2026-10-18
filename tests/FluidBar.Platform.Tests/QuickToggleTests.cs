using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Focus;
using FluidBar.Platform.Windows.Radios;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Offline tests for the quick-toggle platform services. Every hardware call goes through
/// <see cref="IRadioBackend"/> / <see cref="IFocusAssistBackend"/>, so no radio, adapter or focus
/// session is required (and none is touched).
/// </summary>
public sealed class QuickToggleServiceTests
{
    [Fact]
    public async Task RadioService_StartPublishesTheAdapterState()
    {
        var backend = new FakeRadioBackend(PlatformRadioKind.Wifi,
            new RadioSnapshot(PlatformRadioKind.Wifi, RadioAvailability.Available, true, "WLAN"));
        await using var service = CreateRadio(backend);

        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);

        Assert.Equal(PlatformMonitorState.Running, service.State);
        Assert.NotNull(service.Current);
        Assert.True(service.Current!.IsEnabled);
        Assert.True(service.IsEnabled);
        Assert.True(service.Current.IsSupported);
        Assert.Equal("WLAN", service.Current.RadioName);
    }

    [Fact]
    public async Task RadioService_SetEnabledFlipsTheRadioAndPublishesTheObservedState()
    {
        var backend = new FakeRadioBackend(PlatformRadioKind.Bluetooth,
            new RadioSnapshot(PlatformRadioKind.Bluetooth, RadioAvailability.Available, true, "Bluetooth"));
        await using var service = CreateRadio(backend);
        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);

        var result = await service.SetEnabledAsync(false);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { false }, backend.Requests);
        Assert.False(service.Current!.IsEnabled);
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public async Task RadioService_NoAdapter_ReportsUnsupportedWithTheExactReason()
    {
        var backend = new FakeRadioBackend(PlatformRadioKind.Wifi,
            RadioSnapshot.NoAdapter(PlatformRadioKind.Wifi));
        await using var service = CreateRadio(backend);
        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);

        var result = await service.SetEnabledAsync(true);

        Assert.Equal(RadioToggleOutcome.Unsupported, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Contains("no Wi-Fi radio", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Requests);
        Assert.False(service.Current!.CanToggle);
    }

    [Fact]
    public async Task RadioService_HardwareDisabled_ReportsUnsupportedWithoutRequestingAChange()
    {
        var backend = new FakeRadioBackend(PlatformRadioKind.Wifi,
            new RadioSnapshot(PlatformRadioKind.Wifi, RadioAvailability.HardwareDisabled, false, "WLAN",
                "the radio is disabled by a hardware switch"));
        await using var service = CreateRadio(backend);
        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);

        var result = await service.SetEnabledAsync(true);

        Assert.Equal(RadioToggleOutcome.Unsupported, result.Outcome);
        Assert.Contains("hardware switch", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task RadioService_AccessDenied_IsReportedAsAccessDeniedNotSuccess()
    {
        var backend = new FakeRadioBackend(PlatformRadioKind.Wifi,
            new RadioSnapshot(PlatformRadioKind.Wifi, RadioAvailability.Available, true, "WLAN"));
        backend.NextResult = RadioToggleResult.AccessDenied(
            "Windows denied radio access (RadioAccessStatus.DeniedBySystem)", backend.Snapshot);
        await using var service = CreateRadio(backend);
        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);

        var result = await service.SetEnabledAsync(false);

        Assert.Equal(RadioToggleOutcome.AccessDenied, result.Outcome);
        Assert.Contains("denied", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RadioService_StateChangedRefreshesTheSnapshotWithoutPolling()
    {
        var backend = new FakeRadioBackend(PlatformRadioKind.Wifi,
            new RadioSnapshot(PlatformRadioKind.Wifi, RadioAvailability.Available, true, "WLAN"));
        await using var service = CreateRadio(backend);
        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);
        Assert.True(service.Current!.IsEnabled);

        backend.Snapshot = backend.Snapshot with { IsEnabled = false };
        backend.RaiseStateChanged();

        await TestWait.UntilAsync(() => service.Current?.IsEnabled == false);
        Assert.False(service.Current!.IsEnabled);
    }

    [Fact]
    public async Task RadioService_DisposeReleasesTheBackend()
    {
        var backend = new FakeRadioBackend(PlatformRadioKind.Wifi,
            new RadioSnapshot(PlatformRadioKind.Wifi, RadioAvailability.Available, false, "WLAN"));
        var service = CreateRadio(backend);
        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);

        await service.DisposeAsync();

        Assert.True(backend.Disposed);
        Assert.Equal(PlatformMonitorState.Disposed, service.State);
    }

    [Fact]
    public async Task FocusService_LimitedAccess_ReportsUnsupportedAndNeverFakesSuccess()
    {
        var backend = new FakeFocusBackend(new FocusAssistSnapshot(false,
            FocusAssistAvailability.LimitedAccessDenied,
            "Windows focus sessions are a Limited Access Feature " +
            "(feature id com.microsoft.windows.focussessionmanager.1)"));
        await using var service = CreateFocus(backend);
        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);

        Assert.False(service.Current!.CanToggle);
        var result = await service.SetActiveAsync(true);

        Assert.Equal(FocusToggleOutcome.Unsupported, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Contains("Limited Access", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task FocusService_Available_SetActiveDispatchesAndPublishesTheObservedState()
    {
        var backend = new FakeFocusBackend(new FocusAssistSnapshot(false, FocusAssistAvailability.Available));
        await using var service = CreateFocus(backend);
        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);

        var result = await service.SetActiveAsync(true);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { true }, backend.Requests);
        Assert.True(service.Current!.IsActive);
        Assert.True(service.IsActive);
    }

    [Fact]
    public async Task FocusService_StateChangedRefreshesTheSnapshot()
    {
        var backend = new FakeFocusBackend(new FocusAssistSnapshot(false, FocusAssistAvailability.Available));
        await using var service = CreateFocus(backend);
        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);

        backend.Snapshot = backend.Snapshot with { IsActive = true };
        backend.RaiseStateChanged();

        await TestWait.UntilAsync(() => service.Current?.IsActive == true);
        Assert.True(service.Current!.IsActive);
    }

    [Fact]
    public async Task FocusService_UnsupportedOs_ReportsTheExactReason()
    {
        var backend = new FakeFocusBackend(new FocusAssistSnapshot(false,
            FocusAssistAvailability.Unsupported,
            "focus sessions require Windows 11 22H2 (build 22621) or later"));
        await using var service = CreateFocus(backend);
        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);

        var result = await service.SetActiveAsync(true);

        Assert.Equal(FocusToggleOutcome.Unsupported, result.Outcome);
        Assert.Contains("Windows 11 22H2", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(backend.Requests);
    }

    private static RadioToggleService CreateRadio(IRadioBackend backend)
        => new(backend, NullPlatformLogger.Instance, new PlatformCounters());

    private static FocusAssistService CreateFocus(IFocusAssistBackend backend)
        => new(backend, NullPlatformLogger.Instance, new PlatformCounters());
}

/// <summary>In-memory radio backend: records requests and returns scripted snapshots/results.</summary>
internal sealed class FakeRadioBackend : IRadioBackend
{
    public FakeRadioBackend(PlatformRadioKind kind, RadioSnapshot snapshot)
    {
        Kind = kind;
        Snapshot = snapshot;
    }

    public PlatformRadioKind Kind { get; }

    public RadioSnapshot Snapshot { get; set; }

    public RadioToggleResult? NextResult { get; set; }

    public List<bool> Requests { get; } = [];

    public bool Disposed { get; private set; }

    public event EventHandler? StateChanged;

    public Task<RadioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Snapshot);

    public Task<RadioToggleResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (NextResult is { } scripted)
        {
            Requests.Add(enabled);
            if (scripted.Snapshot is not null)
            {
                Snapshot = scripted.Snapshot;
            }

            return Task.FromResult(scripted);
        }

        // Mirror WindowsRadioBackend: an unavailable radio is rejected before any state request.
        if (!Snapshot.CanToggle)
        {
            return Task.FromResult(RadioToggleResult.Unsupported(
                Snapshot.Reason ?? $"{Kind} radio is unavailable", Snapshot));
        }

        Requests.Add(enabled);
        Snapshot = Snapshot with { IsEnabled = enabled };
        return Task.FromResult(RadioToggleResult.Success(Snapshot));
    }

    public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>In-memory focus backend: records requests and returns scripted snapshots/results.</summary>
internal sealed class FakeFocusBackend : IFocusAssistBackend
{
    public FakeFocusBackend(FocusAssistSnapshot snapshot) => Snapshot = snapshot;

    public FocusAssistSnapshot Snapshot { get; set; }

    public FocusToggleResult? NextResult { get; set; }

    public List<bool> Requests { get; } = [];

    public bool Disposed { get; private set; }

    public event EventHandler? StateChanged;

    public Task<FocusAssistSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Snapshot);

    public Task<FocusToggleResult> SetActiveAsync(bool active, CancellationToken cancellationToken = default)
    {
        if (NextResult is { } scripted)
        {
            Requests.Add(active);
            if (scripted.Snapshot is not null)
            {
                Snapshot = scripted.Snapshot;
            }

            return Task.FromResult(scripted);
        }

        // Mirror WindowsFocusAssistBackend: an unavailable/Laf-gated service is rejected first.
        if (!Snapshot.CanToggle)
        {
            return Task.FromResult(FocusToggleResult.Unsupported(
                Snapshot.Reason ?? "focus sessions are unavailable", Snapshot));
        }

        Requests.Add(active);
        Snapshot = Snapshot with { IsActive = active };
        return Task.FromResult(FocusToggleResult.Success(Snapshot));
    }

    public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Guarded in-host checks for the real WinRT backends. They only read state (no radio is switched and
/// no focus session is created) and degrade to no-op assertions on non-interactive/older sessions.
/// </summary>
[Trait("Category", "Integration")]
public sealed class QuickToggleIntegrationTests
{
    [Fact]
    public async Task WindowsRadioBackend_ReadsStateWithoutThrowing()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        await using var backend = new WindowsRadioBackend(PlatformRadioKind.Wifi,
            NullPlatformLogger.Instance, new PlatformCounters());
        var snapshot = await backend.GetSnapshotAsync();

        Assert.Equal(PlatformRadioKind.Wifi, snapshot.Kind);
        if (!snapshot.IsSupported)
        {
            Assert.False(string.IsNullOrWhiteSpace(snapshot.Reason));
        }
    }

    [Fact]
    public async Task WindowsFocusAssistBackend_ReportsAnExplicitAvailability()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        await using var backend = new WindowsFocusAssistBackend(
            NullPlatformLogger.Instance, new PlatformCounters());
        var snapshot = await backend.GetSnapshotAsync();

        Assert.True(Enum.IsDefined(snapshot.Availability));
        if (snapshot.Availability != FocusAssistAvailability.Available)
        {
            Assert.False(string.IsNullOrWhiteSpace(snapshot.Reason));
        }
    }
}
