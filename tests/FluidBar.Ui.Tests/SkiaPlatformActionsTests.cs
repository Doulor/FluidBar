using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Core.Diagnostics;
using FluidBar.Platform.Windows.Focus;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Radios;
using FluidBar.Platform.Windows.Tasks;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Headless tests for the production seam adapter. No platform service is started: the tests pin the
/// unsupported/deferred contract (exact reasons, no fake success), the settings-only theme toggle and
/// the injected quick-toggle services (Wi-Fi/Bluetooth/Focus) with in-memory fakes.
/// </summary>
public sealed class SkiaPlatformActionsTests
{
    [Fact]
    public async Task UnstartedServices_ReportUnsupportedWithTheMissingService()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);

        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.VolumeSet, Value: 0.5)), "VolumeMonitor");
        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.VolumeMute)), "VolumeMonitor");
        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.BrightnessSet, Value: 0.5)), "BrightnessMonitor");
        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.MediaTogglePlay)), "GsmtcMediaSessionService");
        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.MediaSeek, Value: 0.5)), "GsmtcMediaSessionService");

        settings.Dispose();
    }

    [Fact]
    public async Task UnstartedQuickToggles_ReportThatTheServiceIsNotRunning()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);

        AssertUnsupported(
            actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Wifi)),
            "Wi-Fi radio service is not running");
        AssertUnsupported(
            actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Bluetooth)),
            "Bluetooth radio service is not running");
        AssertUnsupported(
            actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Focus)),
            "FocusAssistService is not running");

        settings.Dispose();
    }

    [Fact]
    public async Task ThemeQuickToggle_FlipsTheAppThemeThroughSettings()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);
        Assert.Equal(AppTheme.System, settings.Current.Theme);

        var first = actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Theme));
        Assert.Equal(IslandActionOutcome.Handled, first.Outcome);
        Assert.Equal(AppTheme.Light, settings.Current.Theme);

        var second = actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Theme));
        Assert.Equal(IslandActionOutcome.Handled, second.Outcome);
        Assert.Equal(AppTheme.Dark, settings.Current.Theme);

        settings.Dispose();
    }

    [Fact]
    public async Task WifiQuickToggle_FlipsTheInjectedRadioService()
    {
        var settings = CreateSettings();
        var wifi = new FakeRadioToggleService(new RadioSnapshot(
            PlatformRadioKind.Wifi, RadioAvailability.Available, true, "WLAN"));
        var bluetooth = new FakeRadioToggleService(new RadioSnapshot(
            PlatformRadioKind.Bluetooth, RadioAvailability.Available, true, "Bluetooth"));
        var focus = new FakeFocusAssistService(new FocusAssistSnapshot(false, FocusAssistAvailability.Available));
        await using var actions = CreateActions(settings, wifi, bluetooth, focus);

        var result = actions.Invoke(new IslandPlatformAction(
            IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Wifi));

        Assert.Equal(IslandActionOutcome.Handled, result.Outcome);
        await WaitUntilAsync(() => wifi.Requests.Count == 1);
        Assert.False(wifi.Requests[0]);
        Assert.False(actions.IsWifiEnabled);
        Assert.True(actions.IsBluetoothEnabled);
        settings.Dispose();
    }

    [Fact]
    public async Task BluetoothQuickToggle_FlipsTheInjectedRadioService()
    {
        var settings = CreateSettings();
        var wifi = new FakeRadioToggleService(new RadioSnapshot(
            PlatformRadioKind.Wifi, RadioAvailability.Available, true, "WLAN"));
        var bluetooth = new FakeRadioToggleService(new RadioSnapshot(
            PlatformRadioKind.Bluetooth, RadioAvailability.Available, false, "Bluetooth"));
        var focus = new FakeFocusAssistService(new FocusAssistSnapshot(false, FocusAssistAvailability.Available));
        await using var actions = CreateActions(settings, wifi, bluetooth, focus);

        var result = actions.Invoke(new IslandPlatformAction(
            IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Bluetooth));

        Assert.Equal(IslandActionOutcome.Handled, result.Outcome);
        await WaitUntilAsync(() => bluetooth.Requests.Count == 1);
        Assert.True(bluetooth.Requests[0]);
        Assert.True(actions.IsBluetoothEnabled);
        settings.Dispose();
    }

    [Fact]
    public async Task QuickToggleWithoutAdapter_ReportsUnsupportedWithTheExactReason()
    {
        var settings = CreateSettings();
        var wifi = new FakeRadioToggleService(RadioSnapshot.NoAdapter(PlatformRadioKind.Wifi));
        var bluetooth = new FakeRadioToggleService(RadioSnapshot.NoAdapter(PlatformRadioKind.Bluetooth));
        var focus = new FakeFocusAssistService(new FocusAssistSnapshot(false, FocusAssistAvailability.Available));
        await using var actions = CreateActions(settings, wifi, bluetooth, focus);

        AssertUnsupported(
            actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Wifi)),
            "no Wi-Fi radio");
        AssertUnsupported(
            actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Bluetooth)),
            "no Bluetooth radio");
        Assert.Empty(wifi.Requests);
        Assert.Empty(bluetooth.Requests);
        settings.Dispose();
    }

    [Fact]
    public async Task FocusQuickToggle_LimitedAccess_ReportsUnsupportedWithoutDispatching()
    {
        var settings = CreateSettings();
        var wifi = new FakeRadioToggleService(new RadioSnapshot(
            PlatformRadioKind.Wifi, RadioAvailability.Available, true, "WLAN"));
        var bluetooth = new FakeRadioToggleService(new RadioSnapshot(
            PlatformRadioKind.Bluetooth, RadioAvailability.Available, true, "Bluetooth"));
        var focus = new FakeFocusAssistService(new FocusAssistSnapshot(false,
            FocusAssistAvailability.LimitedAccessDenied,
            "Windows focus sessions are a Limited Access Feature " +
            "(feature id com.microsoft.windows.focussessionmanager.1)"));
        await using var actions = CreateActions(settings, wifi, bluetooth, focus);

        var result = actions.Invoke(new IslandPlatformAction(
            IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Focus));

        AssertUnsupported(result, "Limited Access");
        Assert.Empty(focus.Requests);
        settings.Dispose();
    }

    [Fact]
    public async Task FocusQuickToggle_Available_DispatchesTheInjectedService()
    {
        var settings = CreateSettings();
        var wifi = new FakeRadioToggleService(new RadioSnapshot(
            PlatformRadioKind.Wifi, RadioAvailability.Available, true, "WLAN"));
        var bluetooth = new FakeRadioToggleService(new RadioSnapshot(
            PlatformRadioKind.Bluetooth, RadioAvailability.Available, true, "Bluetooth"));
        var focus = new FakeFocusAssistService(new FocusAssistSnapshot(false, FocusAssistAvailability.Available));
        await using var actions = CreateActions(settings, wifi, bluetooth, focus);

        var result = actions.Invoke(new IslandPlatformAction(
            IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Focus));

        Assert.Equal(IslandActionOutcome.Handled, result.Outcome);
        await WaitUntilAsync(() => focus.Requests.Count == 1);
        Assert.True(focus.Requests[0]);
        Assert.True(actions.IsFocusActive);
        settings.Dispose();
    }

    [Fact]
    public async Task SetWifiEnabledAsync_ExposesTheServiceResult()
    {
        var settings = CreateSettings();
        var wifi = new FakeRadioToggleService(new RadioSnapshot(
            PlatformRadioKind.Wifi, RadioAvailability.Available, true, "WLAN"));
        var bluetooth = new FakeRadioToggleService(new RadioSnapshot(
            PlatformRadioKind.Bluetooth, RadioAvailability.Available, true, "Bluetooth"));
        var focus = new FakeFocusAssistService(new FocusAssistSnapshot(false, FocusAssistAvailability.Available));
        await using var actions = CreateActions(settings, wifi, bluetooth, focus);

        var result = await actions.SetWifiEnabledAsync(false);

        Assert.True(result.Succeeded);
        Assert.False(actions.IsWifiEnabled);
        settings.Dispose();
    }

    [Fact]
    public async Task NotificationActivate_WithoutAListenerRow_IsUnsupported()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);

        // No listener row exists in this fixture; activation must report unsupported instead of
        // pretending to open an app.
        var result = actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.NotificationActivate, Index: 2));

        Assert.Equal(IslandActionOutcome.Unsupported, result.Outcome);

        settings.Dispose();
    }

    [Fact]
    public async Task NotificationDismiss_WithoutAListenerRow_IsUnsupported()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);

        // The listener is not started here, so there is no row to dismiss: report it, never fake success.
        AssertUnsupported(
            actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.NotificationDismiss, Index: 0)),
            "no longer exists");

        settings.Dispose();
    }

    [Fact]
    public async Task TaskToggle_WithoutTaskData_IsUnsupported()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);

        AssertUnsupported(
            actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.TaskToggle, Index: 0)),
            "tasks.json");

        settings.Dispose();
    }

    [Fact]
    public async Task WidgetsContent_WithoutRealData_IsExplicitlyEmpty()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);

        var content = actions.Snapshot();

        Assert.Null(content.WeatherTemp);
        Assert.Null(content.WeatherHighLow);
        Assert.Empty(content.Tasks);
        Assert.NotNull(content.TasksEmptyHint);
        Assert.Contains("tasks.json", content.TasksEmptyHint!, StringComparison.Ordinal);
        settings.Dispose();
    }

    [Fact]
    public async Task TaskToggle_WritesTheConfiguredTaskFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluidbar-ui-tasks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "tasks.json");
        await File.WriteAllTextAsync(path,
            """[ { "title": "Alpha", "done": false }, { "title": "Beta", "done": true } ]""");
        try
        {
            var settings = CreateSettings();
            await using var actions = new SkiaPlatformActions(settings, logger: null, timeProvider: null,
                quickToggles: null, localizer: null, weatherOptions: null,
                taskOptions: new TaskListOptions
                {
                    FilePath = path,
                    ReloadInterval = TimeSpan.FromSeconds(1),
                });

            await actions.TaskSource.StartAsync();
            await WaitUntilAsync(() => actions.TaskSource.Current is not null);
            Assert.Equal(2, actions.Snapshot().Tasks.Length);

            var result = actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.TaskToggle, Index: 0));

            Assert.Equal(IslandActionOutcome.Handled, result.Outcome);
            await WaitUntilAsync(() => actions.TaskSource.Current is { } current && current.Items[0].Done);
            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"title\": \"Alpha\"", json, StringComparison.Ordinal);
            Assert.Contains("\"done\": true", json, StringComparison.Ordinal);
            await actions.TaskSource.StopAsync();
            settings.Dispose();
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    [Fact]
    public async Task ClipboardCopyWithoutText_IsUnsupported()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);

        AssertUnsupported(
            actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.ClipboardCopy, Text: "")),
            "no text to copy");

        settings.Dispose();
    }

    [Fact]
    public async Task NotificationListener_IsOneSharedInstanceForPageAndPlugin()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);

        var view = Assert.IsType<NewArrivalsNotificationSource>(actions.PluginNotificationSource);

        // The plugin path wraps the very listener the notification page maps: one read loop.
        Assert.Same(actions.NotificationSource, view.Inner);
        // The view is created once (a plugin-runtime restart must not re-baseline and swallow arrivals).
        Assert.Same(view, actions.PluginNotificationSource);
        settings.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_ReleasesTheSharedListenerOnce()
    {
        var settings = CreateSettings();
        var actions = new SkiaPlatformActions(settings);

        await actions.DisposeAsync();
        await actions.DisposeAsync();

        settings.Dispose();
    }

    private static SkiaPlatformActions CreateActions(SettingsController settings,
        FakeRadioToggleService wifi, FakeRadioToggleService bluetooth, FakeFocusAssistService focus)
        => new(settings, logger: null, timeProvider: null,
            new IslandQuickToggleServices(wifi, bluetooth, focus));

    private static SettingsController CreateSettings()
    {
        var store = new InMemoryUiSettingsStore(AppSettings.Default);
        return new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
    }

    private static void AssertUnsupported(IslandActionResult result, string reasonFragment)
    {
        Assert.Equal(IslandActionOutcome.Unsupported, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Contains(reasonFragment, result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The quick-toggle service was not called within the timeout.");
            }

            await Task.Delay(10);
        }
    }
}

/// <summary>In-memory radio toggle service for the App adapter tests (no hardware).</summary>
internal sealed class FakeRadioToggleService : IRadioToggleService
{
    public FakeRadioToggleService(RadioSnapshot snapshot) => Current = snapshot;

    public PlatformRadioKind Kind => Current!.Kind;

    public PlatformMonitorState State { get; set; } = PlatformMonitorState.Running;

    public RadioSnapshot? Current { get; set; }

    public bool? IsEnabled => Current?.IsEnabled;

    public List<bool> Requests { get; } = [];

    public event EventHandler<SnapshotEventArgs<RadioSnapshot>>? SnapshotChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        State = PlatformMonitorState.Running;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        State = PlatformMonitorState.Stopped;
        return Task.CompletedTask;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<RadioToggleResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        Requests.Add(enabled);
        if (!Current!.CanToggle)
        {
            return Task.FromResult(RadioToggleResult.Unsupported(
                Current.Reason ?? $"{Kind} radio is unavailable", Current));
        }

        Current = Current with { IsEnabled = enabled };
        SnapshotChanged?.Invoke(this, new SnapshotEventArgs<RadioSnapshot>(Current));
        return Task.FromResult(RadioToggleResult.Success(Current));
    }

    public ValueTask DisposeAsync()
    {
        State = PlatformMonitorState.Disposed;
        return ValueTask.CompletedTask;
    }
}

/// <summary>In-memory focus toggle service for the App adapter tests (no focus session).</summary>
internal sealed class FakeFocusAssistService : IFocusAssistService
{
    public FakeFocusAssistService(FocusAssistSnapshot snapshot) => Current = snapshot;

    public PlatformMonitorState State { get; set; } = PlatformMonitorState.Running;

    public FocusAssistSnapshot? Current { get; set; }

    public bool? IsActive => Current?.IsActive;

    public List<bool> Requests { get; } = [];

    public event EventHandler<SnapshotEventArgs<FocusAssistSnapshot>>? SnapshotChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        State = PlatformMonitorState.Running;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        State = PlatformMonitorState.Stopped;
        return Task.CompletedTask;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<FocusToggleResult> SetActiveAsync(bool active, CancellationToken cancellationToken = default)
    {
        Requests.Add(active);
        if (!Current!.CanToggle)
        {
            return Task.FromResult(FocusToggleResult.Unsupported(
                Current.Reason ?? "focus sessions are unavailable", Current));
        }

        Current = Current with { IsActive = active };
        SnapshotChanged?.Invoke(this, new SnapshotEventArgs<FocusAssistSnapshot>(Current));
        return Task.FromResult(FocusToggleResult.Success(Current));
    }

    public ValueTask DisposeAsync()
    {
        State = PlatformMonitorState.Disposed;
        return ValueTask.CompletedTask;
    }
}
