using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Core.Diagnostics;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Headless tests for the production seam adapter. No platform service is started: the tests pin the
/// unsupported/deferred contract (exact reasons, no fake success) and the settings-only theme toggle.
/// </summary>
public sealed class SkiaPlatformActionsTests
{
    [Fact]
    public async Task UnstartedServices_ReportUnsupportedWithTheMissingService()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);

        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.VolumeSet, Value: 0.5)), "VolumeMonitor");
        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.BrightnessSet, Value: 0.5)), "BrightnessMonitor");
        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.MediaTogglePlay)), "GsmtcMediaSessionService");
        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.MediaSeek, Value: 0.5)), "GsmtcMediaSessionService");

        settings.Dispose();
    }

    [Fact]
    public async Task QuickToggles_ReportTheExactMissingService()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);

        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Wifi)), "NetworkMonitor");
        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Bluetooth)), "BluetoothDeviceMonitor");
        AssertUnsupported(actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Focus)), "Focus Assist");

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
    public async Task AppOwnedActions_AreDeferredAndRaisedAsEvents()
    {
        var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);
        IslandPlatformAction? raised = null;
        actions.DeferredActionRequested += action => raised = action;

        var result = actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.NotificationDismiss, Index: 2));

        Assert.Equal(IslandActionOutcome.Deferred, result.Outcome);
        Assert.NotNull(raised);
        Assert.Equal(IslandPlatformActionKind.NotificationDismiss, raised!.Value.Kind);
        Assert.Equal(2, raised.Value.Index);

        settings.Dispose();
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
}


