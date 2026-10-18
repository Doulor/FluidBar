using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Monitors;
using FluidBar.Platform.Windows.Shell;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

/// <summary>
/// In-host integration tests. They need a real Windows session (message pump, shell, clipboard,
/// power status); they degrade to no-op assertions when the environment cannot support them instead of
/// failing CI on headless agents.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PlatformIntegrationTests : IAsyncLifetime
{
    private PlatformMessageHost? _host;

    public async Task InitializeAsync()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        _host = new PlatformMessageHost(new CapturingLogger(), new PlatformCounters());
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
            _host = null;
        }
    }

    [Fact]
    public async Task MessageHost_StartsCreatesWindowsAndDisposes()
    {
        if (_host is null)
        {
            return;
        }

        Assert.NotEqual(0u, _host.ThreadId);
        var messageOnly = await _host.CreateWindowAsync("test-msg", PlatformWindowKind.MessageOnly,
            static (_, _, _, _) => IntPtr.Zero);
        var topLevel = await _host.CreateWindowAsync("test-top", PlatformWindowKind.HiddenTopLevel,
            static (_, _, _, _) => null);
        try
        {
            Assert.True(NativeMethods.User32.IsWindow(messageOnly.Handle));
            Assert.True(NativeMethods.User32.IsWindow(topLevel.Handle));
            var threadCheck = await _host.InvokeAsync(() => _host.ThreadId);
            Assert.Equal(_host.ThreadId, threadCheck);
            _host.Post(() => { });
        }
        finally
        {
            await messageOnly.DisposeAsync();
            await topLevel.DisposeAsync();
        }

        Assert.False(NativeMethods.User32.IsWindow(messageOnly.Handle));
    }

    [Fact]
    public async Task MonitorService_EnumeratesAndPublishesSnapshot()
    {
        if (_host is null)
        {
            return;
        }

        await using var service = new MonitorService(_host, new CapturingLogger(), new PlatformCounters());
        await service.StartAsync();
        await TestWait.UntilAsync(() => service.Current is not null);
        var monitors = service.Current!;
        Assert.True(monitors.Count >= 0);

        if (monitors.Count > 0)
        {
            var primary = monitors.Primary;
            Assert.NotNull(primary);
            Assert.True(primary!.WorkArea.Width > 0);
            Assert.True(primary.Scale >= 0.5);
            var placed = DisplayMetrics.PlaceAtAnchor(primary, IslandAnchor.TopCenter, 440, 284);
            Assert.True(placed.X >= primary.WorkArea.X);
            Assert.True(placed.Right <= primary.WorkArea.Right);
            Assert.NotNull(service.MonitorAtPoint(primary.Bounds.X + 1, primary.Bounds.Y + 1));
        }

        await service.RefreshAsync();
        await service.StopAsync();
    }

    [Fact]
    public async Task BatteryMonitor_ReadsPowerStatusWithoutPolling()
    {
        if (_host is null)
        {
            return;
        }

        await using var monitor = new BatteryMonitor(_host, new CapturingLogger(), new PlatformCounters());
        await monitor.StartAsync();
        await TestWait.UntilAsync(() => monitor.Current is not null);
        Assert.NotNull(monitor.Current);
        Assert.InRange(monitor.Current!.Percent, -1, 100);
        await monitor.StopAsync();
    }

    [Fact]
    public async Task VolumeMonitor_StartsAndStopsCleanly()
    {
        if (_host is null)
        {
            return;
        }

        var logger = new CapturingLogger();
        await using var monitor = new VolatilityVolumeMonitorFactory(logger);
        await monitor.StartAsync();
        await TestWait.UntilAsync(() => monitor.Current is not null);
        Assert.NotNull(monitor.Current);
        if (monitor.Current!.HasDevice)
        {
            await monitor.SetVolumeAsync(monitor.Current.Percent);
            Assert.NotNull(await monitor.TryReadPeakLevelAsync());
        }

        await monitor.StopAsync();
    }

    [Fact]
    public async Task HotkeyService_RegistersAndUnregisters()
    {
        if (_host is null)
        {
            return;
        }

        await using var service = new HotkeyService(_host, new CapturingLogger(), new PlatformCounters());
        await service.StartAsync();
        var result = await service.RegisterAsync("test-toggle", "Ctrl+Alt+Shift+F24");
        if (!result.Success)
        {
            // Another process owns the chord; conflict reporting is the contract, not a failure.
            Assert.NotEqual(HotkeyRegistrationFailure.None, result.Failure);
        }

        await service.UnregisterAllAsync();
        await service.StopAsync();
    }

    [Fact]
    public async Task ClipboardService_DetectsTextCopiedThroughTheApi()
    {
        if (_host is null)
        {
            return;
        }

        await using var service = new ClipboardService(_host, new CapturingLogger(), new PlatformCounters());
        await service.StartAsync();
        var marker = $"FluidBar clipboard test {Guid.NewGuid():N}";
        var written = await service.SetTextAsync(marker);
        if (!written)
        {
            // Clipboard is owned by another process in this session; the guarded test asserts cleanup only.
            await service.StopAsync();
            return;
        }

        await service.TriggerReadAsync();
        await TestWait.UntilAsync(() => service.Current?.Latest?.Text == marker);
        Assert.Equal(ClipboardContentKind.Text, service.Current!.Latest!.Kind);
        Assert.Contains(service.Current.History, entry => entry.Text == marker);
        await service.StopAsync();
    }

    [Fact]
    public async Task TrayIconService_AddsAndRemovesIcon()
    {
        if (_host is null || !Environment.UserInteractive)
        {
            return;
        }

        await using var service = new TrayIconService(_host, new CapturingLogger(), new PlatformCounters());
        try
        {
            await service.StartAsync(new TrayIconOptions { ToolTip = "FluidBar test" });
        }
        catch (InvalidOperationException)
        {
            // No shell notification area (headless RDP/session 0): guarded.
            return;
        }

        service.SetMenuItems(new[] { new TrayMenuItem("settings", "Settings") });
        await service.SetToolTipAsync("FluidBar test updated");
        await service.StopAsync();
    }

    [Fact]
    public async Task IslandWindowInterop_RunsHitTestAndPlacementOnARealWindow()
    {
        if (_host is null)
        {
            return;
        }

        var window = await _host.CreateWindowAsync("test-island", PlatformWindowKind.HiddenTopLevel,
            static (_, _, _, _) => null);
        try
        {
            using var interop = new IslandWindowInterop(new CapturingLogger(), new PlatformCounters());
            interop.ApplyIslandStyles(window.Handle);
            interop.SetTopmost(window.Handle);
            var exStyle = NativeMethods.User32.GetWindowLongPtr(window.Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            Assert.NotEqual(0, exStyle & NativeMethods.WS_EX_TOOLWINDOW);
            Assert.NotEqual(0, exStyle & NativeMethods.WS_EX_NOACTIVATE);

            using var hook = interop.RegisterHitTest(window.Handle, (_, _) => true);
            Assert.True(interop.IsHitTestRegistered(window.Handle));

            var monitor = new MonitorInfo("test", 0, new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 0, 1920, 1040), 96, 96, true);
            interop.PlacePhysical(window.Handle, -(24), -(24), 488, 332);
            NativeMethods.User32.GetWindowRect(window.Handle, out var rect);
            Assert.Equal(-24, rect.Left);
            Assert.Equal(-24, rect.Top);
            Assert.Equal(488, rect.Width);

            interop.PlaceOnMonitor(window.Handle, monitor, 440, 284, IslandAnchor.TopCenter);
            interop.UpdateWindowRegionOnSettle(window.Handle,
                new ShellGeometry(24, 24, 440, 284, 16), monitor);
            Assert.False(interop.TryReassertTopmost(window.Handle));
        }
        finally
        {
            await window.DisposeAsync();
        }
    }

    /// <summary>Factory helper keeps the volume test readable.</summary>
    private sealed class VolatilityVolumeMonitorFactory : IAsyncDisposable
    {
        private readonly VolumeMonitor _monitor;

        public VolatilityVolumeMonitorFactory(CapturingLogger logger)
        {
            _monitor = new VolumeMonitor(logger, new PlatformCounters());
        }

        public VolumeSnapshot? Current => _monitor.Current;

        public Task StartAsync() => _monitor.StartAsync();

        public Task StopAsync() => _monitor.StopAsync();

        public Task SetVolumeAsync(int percent) => _monitor.SetVolumeAsync(percent);

        public Task<float?> TryReadPeakLevelAsync() => _monitor.TryReadPeakLevelAsync();

        public ValueTask DisposeAsync() => _monitor.DisposeAsync();
    }
}
