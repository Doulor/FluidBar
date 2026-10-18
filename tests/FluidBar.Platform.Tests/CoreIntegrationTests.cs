using System.Collections.Immutable;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Events;
using FluidBar.Core.Layout;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.CoreIntegration;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Monitors;
using FluidBar.Platform.Windows.Notifications;

namespace FluidBar.Platform.Tests;

public sealed class CoreIntegrationTests
{
    private sealed class CapturingAppLogger : IAppLogger
    {
        public List<(LogLevel Level, string Category, string EventId)> Entries { get; } = new();

        public bool IsEnabled(LogLevel level) => true;

        public void Log(LogLevel level, string category, string eventId, string message, Exception? exception = null,
            IReadOnlyDictionary<string, object?>? data = null)
            => Entries.Add((level, category, eventId));
    }

    private static FluidBar.Platform.Windows.Display.MonitorInfo PlatformMonitor()
        => new("\\\\.\\DISPLAY1", 0, new FluidBar.Platform.Windows.Display.PixelRect(1920, 0, 1920, 1080), new FluidBar.Platform.Windows.Display.PixelRect(1920, 0, 1920, 1040),
            144, 144, false);

    [Fact]
    public void MonitorConversion_MapsFieldsBothWays()
    {
        var platform = PlatformMonitor();
        var core = platform.ToCore("display:edid-hash");
        Assert.Equal("display:edid-hash", core.Id);
        Assert.Equal(144u, core.Dpi);
        Assert.Equal(1.5, core.Scale);
        Assert.Equal(new FluidBar.Core.Layout.PixelRect(1920, 0, 1920, 1040), core.WorkArea);
        Assert.False(core.IsPrimary);

        var roundTripped = core.ToPlatform(3);
        Assert.Equal(3, roundTripped.Index);
        Assert.Equal(platform.WorkArea, roundTripped.WorkArea);
        Assert.Equal(144u, roundTripped.DpiX);
    }

    [Fact]
    public void MonitorConversion_KeepsDeviceIdWhenNoStableId()
    {
        Assert.Equal("\\\\.\\DISPLAY1", PlatformMonitor().ToCore().Id);
    }

    [Fact]
    public void AnchorConversion_RoundTrips()
    {
        var pairs = new (FluidBar.Platform.Windows.Display.IslandAnchor Platform, FluidBar.Core.Layout.IslandAnchor Core)[]
        {
            (FluidBar.Platform.Windows.Display.IslandAnchor.TopLeft, FluidBar.Core.Layout.IslandAnchor.TopLeft),
            (FluidBar.Platform.Windows.Display.IslandAnchor.TopCenter, FluidBar.Core.Layout.IslandAnchor.TopCenter),
            (FluidBar.Platform.Windows.Display.IslandAnchor.TopRight, FluidBar.Core.Layout.IslandAnchor.TopRight),
            (FluidBar.Platform.Windows.Display.IslandAnchor.BottomLeft, FluidBar.Core.Layout.IslandAnchor.BottomLeft),
            (FluidBar.Platform.Windows.Display.IslandAnchor.BottomCenter, FluidBar.Core.Layout.IslandAnchor.BottomCenter),
            (FluidBar.Platform.Windows.Display.IslandAnchor.BottomRight, FluidBar.Core.Layout.IslandAnchor.BottomRight),
        };
        Assert.Equal(6, pairs.Length);
        foreach (var pair in pairs)
        {
            Assert.Equal(pair.Core, pair.Platform.ToCore());
            Assert.Equal(pair.Platform, pair.Core.ToPlatform());
        }
    }
    [Fact]
    public void ShellGeometryConversion_UsesPaddingOffsetAndLargestRadius()
    {
        var core = FluidBar.Core.Layout.ShellGeometry.Create(
            new VisualRect(100, 20, 440, 284), new CornerRadii(16, 20, 16, 12));
        var hosting = core.ToHostingShellGeometry();
        Assert.Equal(24, hosting.XDip);
        Assert.Equal(24, hosting.YDip);
        Assert.Equal(440, hosting.WidthDip);
        Assert.Equal(284, hosting.HeightDip);
        Assert.Equal(20, hosting.CornerRadiusDip);
    }

    [Fact]
    public void SnapshotMapper_MediaCarriesContentAndPlayback()
    {
        var snapshot = new MediaSnapshot("Spotify.exe", "Spotify", "Song", "Artist", "Album",
            MediaPlaybackState.Playing, true, TimeSpan.FromSeconds(30), DateTimeOffset.UnixEpoch,
            TimeSpan.FromSeconds(200), true, true, true, "key");
        var payload = CoreSnapshotMapper.ToPayload(snapshot);
        Assert.Equal("Song", payload.Title);
        Assert.Equal("Artist", payload.Subtitle);
        Assert.True(payload.IsActive);
        Assert.True(payload.ShowsAudioWave);
        Assert.Equal(TimeSpan.FromSeconds(30).Ticks, payload.PositionTicks);
        Assert.Equal(TimeSpan.FromSeconds(200).Ticks, payload.EndTicks);
    }

    [Fact]
    public void SnapshotMapper_VolumeAndBrightnessExposeLevelsOnly()
    {
        var volume = CoreSnapshotMapper.ToPayload(new VolumeSnapshot(42, true, true, 2));
        Assert.Equal(42, volume.Level);
        Assert.False(volume.IsActive);
        Assert.Equal("volume_mute", volume.IconToken);

        var brightness = CoreSnapshotMapper.ToPayload(new BrightnessSnapshot(73, BrightnessSource.DdcCi, true));
        Assert.Equal(73, brightness.Level);
        Assert.True(brightness.IsActive);
    }

    [Fact]
    public void SnapshotMapper_BatteryPassesLevelAndCharging()
    {
        var payload = CoreSnapshotMapper.ToPayload(new BatterySnapshot(true, 15, false, BatteryPowerSource.Battery,
            true, false, 45, null));
        Assert.Equal(15, payload.Level);
        Assert.False(payload.IsActive);
        Assert.Equal("battery_low", payload.IconToken);
        Assert.Contains("remaining_minutes:45", payload.DetailLines);
    }

    [Fact]
    public void SnapshotMapper_LockKeysUsesStableTokens()
    {
        var payload = CoreSnapshotMapper.ToPayload(new LockKeysSnapshot(true, true, false));
        Assert.Equal(new[] { "capslock:on", "numlock:on" }, payload.DetailLines);
    }

    [Fact]
    public void SnapshotMapper_InputMethodUsesStableTokens()
    {
        var native = CoreSnapshotMapper.ToPayload(new InputMethodSnapshot(true, true, 0x0804));
        Assert.True(native.IsActive);
        Assert.Contains("ime:native", native.DetailLines);

        var latin = CoreSnapshotMapper.ToPayload(new InputMethodSnapshot(false, true, 0x0804));
        Assert.False(latin.IsActive);
        Assert.Contains("ime:latin", latin.DetailLines);
    }

    [Fact]
    public void SnapshotMapper_ClipboardMapsTextAndKind()
    {
        var entry = new ClipboardEntry(1, DateTimeOffset.UnixEpoch, ClipboardContentKind.Text, "hello", 5,
            ImmutableArray<string>.Empty);
        var payload = CoreSnapshotMapper.ToPayload(new ClipboardSnapshot(entry, ImmutableArray.Create(entry)));
        Assert.Equal("hello", payload.Body);
        Assert.Equal("clipboard_text", payload.IconToken);

        var empty = CoreSnapshotMapper.ToPayload(ClipboardSnapshot.Empty);
        Assert.Equal(ActivityPayload.Empty, empty);
    }

    [Fact]
    public void SnapshotMapper_NotificationsExposeVerificationCode()
    {
        var entry = new NotificationEntry(7, "App", "Title", "Body", DateTimeOffset.UnixEpoch, "123456");
        var payload = CoreSnapshotMapper.ToPayload(
            new NotificationsSnapshot(NotificationAccessState.Allowed, ImmutableArray.Create(entry)));
        Assert.Equal("Title", payload.Title);
        Assert.Equal("App", payload.Subtitle);
        Assert.Equal("123456", payload.VerificationCode);
        Assert.Equal("123456", payload.Badge);
    }

    [Fact]
    public void SnapshotMapper_NetworkCarriesSsidContent()
    {
        var payload = CoreSnapshotMapper.ToPayload(new NetworkSnapshot(true, "Wi-Fi", NetworkAdapterKind.Wifi,
            "MyNetwork", 80));
        Assert.Equal("MyNetwork", payload.Body);
        Assert.Equal("Wi-Fi", payload.SourceName);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public void EventFactory_BuildsValidCoreEvent()
    {
        var payload = CoreSnapshotMapper.ToPayload(new VolumeSnapshot(50, false, true, 2));
        var islandEvent = CoreSnapshotMapper.ToEvent(ActivityKind.Volume, CoreSnapshotMapper.SourceVolume,
            IslandEventKind.Incremental, payload, 42, TimeSpan.FromSeconds(3));
        Assert.Equal(ActivityKind.Volume, islandEvent.Key.Kind);
        Assert.Equal("system:volume", islandEvent.Key.Source);
        Assert.Equal(IslandEventKind.Incremental, islandEvent.Kind);
        Assert.Equal(42, islandEvent.Sequence);
        Assert.Equal(50, islandEvent.Payload.Level);
    }

    [Fact]
    public void LoggerAdapter_MapsEveryCategoryIntoTheCoreClosedSet()
    {
        var core = new CapturingAppLogger();
        var adapter = new AppLoggerPlatformAdapter(core);
        var index = 0;
        foreach (var category in Enum.GetValues<PlatformLogCategory>())
        {
            adapter.LogWarning(category, $"test.event.{index}", "message");
            index++;
        }

        Assert.Equal(Enum.GetValues<PlatformLogCategory>().Length, core.Entries.Count);
        Assert.All(core.Entries, entry => Assert.Contains(entry.Category, LogCategories.All));
    }

    [Theory]
    [InlineData(PlatformLogLevel.Trace, LogLevel.Trace)]
    [InlineData(PlatformLogLevel.Debug, LogLevel.Debug)]
    [InlineData(PlatformLogLevel.Info, LogLevel.Information)]
    [InlineData(PlatformLogLevel.Warning, LogLevel.Warning)]
    [InlineData(PlatformLogLevel.Error, LogLevel.Error)]
    [InlineData(PlatformLogLevel.Critical, LogLevel.Critical)]
    public void LoggerAdapter_MapsLevels(PlatformLogLevel platform, LogLevel core)
    {
        Assert.Equal(core, AppLoggerPlatformAdapter.MapLevel(platform));
    }

    [Fact]
    public void LoggerAdapter_SwallowsCoreLoggerFailures()
    {
        var adapter = new AppLoggerPlatformAdapter(new ThrowingAppLogger());
        var entry = new PlatformLogEntry(DateTimeOffset.UnixEpoch, PlatformLogLevel.Error,
            PlatformLogCategory.Platform, "x", "y");
        adapter.Log(entry); // must not throw
    }

    private sealed class ThrowingAppLogger : IAppLogger
    {
        public bool IsEnabled(LogLevel level) => true;

        public void Log(LogLevel level, string category, string eventId, string message,
            Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
            => throw new InvalidOperationException("logger exploded");
    }
}
