using System.Collections.Immutable;
using FluidBar.Core.Activities;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.CoreIntegration;
using FluidBar.Platform.Windows.Devices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;
using FluidBar.Platform.Windows.Update;

namespace FluidBar.Platform.Tests;

public sealed class DeviceParserTests
{
    [Fact]
    public void UsbPath_ParsesVidPidSerial()
    {
        const string path = @"\\?\USB#VID_046D&PID_C52B#5&2A3B4C5D&0&1#{a5dcbf10-6530-11d2-901f-00c04fb951ed}";
        var (vendor, product, serial) = UsbDevicePathParser.Parse(path);
        Assert.Equal("046D", vendor);
        Assert.Equal("C52B", product);
        Assert.Equal("5&2A3B4C5D&0&1", serial);
        Assert.Equal("USB 046D:C52B", UsbDevicePathParser.FormatFriendlyName(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HID#VID_1234")]
    [InlineData(@"\\.\DISPLAY1")]
    public void UsbPath_FallsBackForNonUsbPaths(string? path)
    {
        var (vendor, product, _) = UsbDevicePathParser.Parse(path);
        Assert.Null(vendor);
        Assert.Null(product);
        var name = UsbDevicePathParser.FormatFriendlyName(path);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void UsbPath_TryCreateEntryUsesChangeDirection()
    {
        const string path = @"\\?\USB#VID_1234&PID_5678#SER#{}";
        var arrival = UsbDevicePathParser.TryCreateEntry(path, connected: true, DateTimeOffset.UnixEpoch);
        Assert.NotNull(arrival);
        Assert.Equal(DeviceKind.Usb, arrival!.Kind);
        Assert.Equal(DeviceChangeKind.Connected, arrival.Change);
        Assert.Equal("USB 1234:5678", arrival.Name);

        var removal = UsbDevicePathParser.TryCreateEntry(path, connected: false, DateTimeOffset.UnixEpoch);
        Assert.Equal(DeviceChangeKind.Disconnected, removal!.Change);
        Assert.Null(UsbDevicePathParser.TryCreateEntry(null, true, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void DeviceEventHistory_IsBoundedAndDedupesConsecutive()
    {
        var history = new DeviceEventHistory(maxEntries: 2);
        var entry = new DeviceEventEntry("id", DeviceKind.Usb, DeviceChangeKind.Connected, "USB", DateTimeOffset.UnixEpoch);
        history.Add(entry);
        history.Add(entry);
        Assert.Equal(1, history.Count);

        history.Add(entry with { Change = DeviceChangeKind.Disconnected });
        history.Add(entry with { DeviceId = "other" });
        Assert.Equal(2, history.Count);
        Assert.Equal(1, history.Evictions);
        Assert.Equal("other", history.Snapshot().Latest!.DeviceId);
    }
}

public sealed class DeviceIntegrationTests : IAsyncLifetime
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
    public async Task UsbMonitor_StartsAndStopsCleanly()
    {
        if (_host is null)
        {
            return;
        }

        await using var monitor = new UsbDeviceMonitor(_host, new CapturingLogger(), new PlatformCounters());
        await monitor.StartAsync();
        Assert.NotNull(monitor.Current);
        await monitor.StopAsync();
        Assert.Equal(PlatformMonitorState.Stopped, monitor.State);
    }

    [Fact]
    public async Task BluetoothMonitor_StartsAndStopsCleanly()
    {
        await using var monitor = new BluetoothDeviceMonitor(new CapturingLogger(), new PlatformCounters());
        await monitor.StartAsync(); // degrades to an empty snapshot when no radio is present
        Assert.NotNull(monitor.Current);
        await monitor.StopAsync();
    }
}

public sealed class P3CoreMappingTests
{
    [Fact]
    public void AgentSnapshot_MapsToStablePayload()
    {
        var entry = new AgentEventEntry("id", AgentKind.Claude, AgentEventKind.Completed, "Done", "ok",
            DateTimeOffset.UnixEpoch, "a.json");
        var payload = CoreSnapshotMapper.ToPayload(new AgentEventSnapshot(ImmutableArray.Create(entry)));
        Assert.Equal("Done", payload.Title);
        Assert.Equal("Claude", payload.SourceName);
        Assert.Equal("agent_completed", payload.IconToken);
        Assert.Contains("agent:completed", payload.DetailLines);
        Assert.Equal("agent:claude", CoreSnapshotMapper.SourceAgentClaude);
    }

    [Fact]
    public void DeviceSnapshot_MapsKindAndDirectionTokens()
    {
        var entry = new DeviceEventEntry("usb-id", DeviceKind.Usb, DeviceChangeKind.Disconnected, "USB 1234:5678",
            DateTimeOffset.UnixEpoch);
        var payload = CoreSnapshotMapper.ToPayload(new DeviceEventSnapshot(ImmutableArray.Create(entry)));
        Assert.Equal("USB 1234:5678", payload.Title);
        Assert.False(payload.IsActive);
        Assert.Contains("usb:removal", payload.DetailLines);

        var bt = new DeviceEventEntry("bt-id", DeviceKind.Bluetooth, DeviceChangeKind.Connected, "Headphones",
            DateTimeOffset.UnixEpoch);
        var btPayload = CoreSnapshotMapper.ToPayload(new DeviceEventSnapshot(ImmutableArray.Create(bt)));
        Assert.Contains("bluetooth:connected", btPayload.DetailLines);
        Assert.Equal("system:bluetooth", CoreSnapshotMapper.SourceBluetooth);
    }

    [Fact]
    public void UpdateSnapshot_MapsStateTokensAndNeverForIdle()
    {
        var idle = CoreSnapshotMapper.ToPayload(UpdateStatusSnapshot.Idle(new Version(2, 0, 0)));
        Assert.Equal(ActivityPayload.Empty, idle);

        var release = new UpdateRelease("v2.1.0", new SemanticVersion(2, 1, 0, null), "2.1.0", null, null, false,
            null, ImmutableArray<UpdateAsset>.Empty, null, null);
        var downloading = CoreSnapshotMapper.ToPayload(new UpdateStatusSnapshot(UpdateState.Downloading,
            new Version(2, 0, 0), release, null, UpdateVerificationStatus.NotVerified, null, null));
        Assert.True(downloading.ProgressIndeterminate);
        Assert.Contains("update:downloading", downloading.DetailLines);

        var ready = CoreSnapshotMapper.ToPayload(new UpdateStatusSnapshot(UpdateState.ReadyToInstall,
            new Version(2, 0, 0), release, "C:\\temp\\setup.exe", UpdateVerificationStatus.Verified, null, null));
        Assert.Contains("update:ready", ready.DetailLines);
        Assert.Equal("update:check", CoreSnapshotMapper.SourceUpdate);
    }
}
