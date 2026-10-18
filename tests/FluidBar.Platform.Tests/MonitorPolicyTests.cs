using System.Net.NetworkInformation;
using FluidBar.Platform.Windows.Monitors;

namespace FluidBar.Platform.Tests;

public sealed class MonitorPolicyTests
{
    [Theory]
    [InlineData(0, 0, 1, 2, 8, 20)]
    [InlineData(0, 1, 50, 2, 8, 20)]
    [InlineData(1, 8, 80, 0xFFFFFFFF, 0xFFFFFFFF, 20)]
    [InlineData(1, 0, 100, 7000, 0xFFFFFFFF, 20)]
    [InlineData(0, 128, 255, 0xFFFFFFFF, 0xFFFFFFFF, 20)]
    [InlineData(1, 0, 200, 60, 60, 20)]
    public void BatteryStatusParser_ParsesNativeFields(byte acLine, byte flags, byte percent, uint lifeTime,
        uint fullLifeTime, int threshold)
    {
        var snapshot = BatteryStatusParser.Parse(acLine, flags, percent, lifeTime, fullLifeTime, threshold);
        if ((flags & 128) != 0)
        {
            Assert.False(snapshot.HasBattery);
        }

        if (percent == 255)
        {
            Assert.Equal(-1, snapshot.Percent);
        }
        else if (percent > 100)
        {
            Assert.Equal(100, snapshot.Percent);
        }
        else
        {
            Assert.Equal(percent, snapshot.Percent);
        }
    }

    [Fact]
    public void BatteryStatusParser_ChargingBeatsAcSource()
    {
        var snapshot = BatteryStatusParser.Parse(1, 8, 55, 1800, 3600);
        Assert.True(snapshot.IsCharging);
        Assert.Equal(BatteryPowerSource.Ac, snapshot.Source);
        Assert.Null(snapshot.MinutesRemaining); // remaining time is only meaningful while discharging
        Assert.Equal(60, snapshot.MinutesToFull);
        Assert.False(snapshot.IsLow);
        Assert.Equal(30, BatteryStatusParser.Parse(0, 0, 55, 1800, 0xFFFFFFFF).MinutesRemaining);
    }

    [Fact]
    public void BatteryStatusParser_LowBatteryOnlyWhenDischarging()
    {
        Assert.True(BatteryStatusParser.Parse(0, 2, 10, 600, 0).IsLow);
        Assert.False(BatteryStatusParser.Parse(1, 2, 10, 600, 0).IsLow);
        Assert.False(BatteryStatusParser.Parse(0, 0, 50, 600, 0).IsLow);
    }

    [Fact]
    public void BatteryStatusParser_CriticalFlagIsSurfaced()
    {
        Assert.True(BatteryStatusParser.Parse(0, 4, 3, 120, 0).IsCritical);
        Assert.True(BatteryStatusParser.IsLowOrCritical(4));
    }

    [Theory]
    [InlineData(96u, 0u, 0u, null)]
    [InlineData(0u, 50u, 100u, 50)]
    [InlineData(0u, 0u, 100u, 0)]
    [InlineData(0u, 100u, 100u, 100)]
    [InlineData(0u, 150u, 100u, 100)]
    [InlineData(10u, 5u, 20u, 0)]
    [InlineData(10u, 15u, 20u, 50)]
    public void BrightnessMath_FromDdc(uint minimum, uint current, uint maximum, int? expected)
    {
        Assert.Equal(expected, BrightnessMath.FromDdc(minimum, current, maximum));
    }

    [Theory]
    [InlineData(50, 0u, 100u, 50u)]
    [InlineData(0, 10u, 20u, 10u)]
    [InlineData(100, 10u, 20u, 20u)]
    [InlineData(150, 0u, 100u, 100u)]
    public void BrightnessMath_ToDdc(int percent, uint minimum, uint maximum, uint expected)
    {
        Assert.Equal(expected, BrightnessMath.ToDdc(percent, minimum, maximum));
    }

    [Theory]
    [InlineData((byte)75, 75)]
    [InlineData((sbyte)40, 40)]
    [InlineData((short)30, 30)]
    [InlineData(60, 60)]
    [InlineData(120, 100)]
    [InlineData(-5, null)]
    [InlineData(70L, 70)]
    [InlineData(42.4, 42)]
    [InlineData(42.5, 43)]
    [InlineData("55", 55)]
    [InlineData("abc", null)]
    [InlineData(null, null)]
    public void BrightnessWmiParser_ParsesVariants(object? value, int? expected)
    {
        Assert.Equal(expected, BrightnessWmiParser.Parse(value));
    }

    [Fact]
    public void BrightnessWmiParser_ByteArrayUsesFirstElement()
    {
        Assert.Equal(33, BrightnessWmiParser.Parse(new byte[] { 33, 44 }));
        Assert.Equal(44, BrightnessWmiParser.Parse(new object[] { 44 }));
        Assert.Null(BrightnessWmiParser.Parse(Array.Empty<byte>()));
    }

    [Fact]
    public void ClockAlignment_TruncatesToMinute()
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, 34, 56, 789, TimeSpan.Zero);
        var truncated = ClockAlignment.TruncateToMinute(now);
        Assert.Equal(0, truncated.Second);
        Assert.Equal(0, truncated.Millisecond);
        Assert.Equal(34, truncated.Minute);
    }

    [Theory]
    [InlineData(0, 0, 0, 60000)]
    [InlineData(0, 0, 500, 59500)]
    [InlineData(0, 30, 0, 30000)]
    [InlineData(0, 59, 999, 1)]
    [InlineData(0, 59, 0, 1000)]
    public void ClockAlignment_DelayUntilNextMinute(int minutes, int seconds, int millis, int expectedMs)
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, minutes, seconds, millis, TimeSpan.Zero);
        var delay = ClockAlignment.DelayUntilNextMinute(now);
        Assert.Equal(expectedMs, (int)Math.Ceiling(delay.TotalMilliseconds));
    }

    [Fact]
    public void ClockAlignment_DelayIsNeverZero()
    {
        var boundary = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        Assert.True(ClockAlignment.DelayUntilNextMinute(boundary) > TimeSpan.Zero);
    }

    [Fact]
    public void LockKeyTracker_TogglesOnKeyDown()
    {
        var tracker = new LockKeyTracker();
        var afterDown = tracker.HandleKeyEvent(0x14, true);
        Assert.NotNull(afterDown);
        Assert.True(afterDown!.CapsLockOn);

        tracker.HandleKeyEvent(0x14, false);
        var afterSecondDown = tracker.HandleKeyEvent(0x14, true);
        Assert.False(afterSecondDown!.CapsLockOn);
    }

    [Fact]
    public void LockKeyTracker_AutoRepeatDoesNotToggleTwice()
    {
        var tracker = new LockKeyTracker();
        Assert.True(tracker.HandleKeyEvent(0x14, true)!.CapsLockOn);
        Assert.Null(tracker.HandleKeyEvent(0x14, true));
        Assert.Null(tracker.HandleKeyEvent(0x14, true));
    }

    [Fact]
    public void LockKeyTracker_IgnoresNonLockKeysAndKeyUp()
    {
        var tracker = new LockKeyTracker();
        Assert.Null(tracker.HandleKeyEvent(0x41, true));
        Assert.Null(tracker.HandleKeyEvent(0x14, false));
    }

    [Fact]
    public void LockKeyTracker_NumAndScrollToggleIndependently()
    {
        var tracker = new LockKeyTracker();
        tracker.HandleKeyEvent(0x90, true);
        tracker.HandleKeyEvent(0x90, false);
        var snapshot = tracker.HandleKeyEvent(0x91, true);
        Assert.True(snapshot!.NumLockOn);
        Assert.True(snapshot.ScrollLockOn);
        Assert.False(snapshot.CapsLockOn);
    }

    [Fact]
    public void NetworkAdapterSelector_PrefersUsableWifiOverWired()
    {
        var adapters = new[]
        {
            new NetworkAdapterDescriptor("Ethernet", NetworkAdapterKind.Ethernet, true, true),
            new NetworkAdapterDescriptor("Wi-Fi", NetworkAdapterKind.Wifi, true, true),
            new NetworkAdapterDescriptor("VPN", NetworkAdapterKind.Virtual, true, true),
        };
        var selected = NetworkAdapterSelector.SelectPrimary(adapters);
        Assert.Equal("Wi-Fi", selected!.Name);
    }

    [Fact]
    public void NetworkAdapterSelector_IgnoresDownAdaptersAndPrefersGateway()
    {
        var adapters = new[]
        {
            new NetworkAdapterDescriptor("Wi-Fi", NetworkAdapterKind.Wifi, false, true),
            new NetworkAdapterDescriptor("Ethernet", NetworkAdapterKind.Ethernet, true, false),
            new NetworkAdapterDescriptor("Ethernet2", NetworkAdapterKind.Ethernet, true, true),
        };
        Assert.Equal("Ethernet2", NetworkAdapterSelector.SelectPrimary(adapters)!.Name);
    }

    [Fact]
    public void NetworkAdapterSelector_CreateSnapshotUsesRealSsidForWifi()
    {
        var adapters = new[]
        {
            new NetworkAdapterDescriptor("Wi-Fi", NetworkAdapterKind.Wifi, true, true),
        };
        var snapshot = NetworkAdapterSelector.CreateSnapshot(adapters, true,
            new WlanConnectionInfo("MyNetwork", 80, "Intel"));
        Assert.True(snapshot.IsConnected);
        Assert.Equal("MyNetwork", snapshot.Ssid);
        Assert.Equal(80u, snapshot.SignalQuality);
        Assert.Equal(NetworkAdapterKind.Wifi, snapshot.Kind);
    }

    [Fact]
    public void NetworkAdapterSelector_NoAdaptersIsDisconnected()
    {
        var snapshot = NetworkAdapterSelector.CreateSnapshot(Array.Empty<NetworkAdapterDescriptor>(), false, null);
        Assert.False(snapshot.IsConnected);
        Assert.Null(snapshot.AdapterName);
        Assert.Null(snapshot.Ssid);
    }

    [Theory]
    [InlineData(NetworkInterfaceType.Wireless80211, NetworkAdapterKind.Wifi)]
    [InlineData(NetworkInterfaceType.Ethernet, NetworkAdapterKind.Ethernet)]
    [InlineData(NetworkInterfaceType.GigabitEthernet, NetworkAdapterKind.Ethernet)]
    [InlineData(NetworkInterfaceType.Loopback, NetworkAdapterKind.Virtual)]
    [InlineData(NetworkInterfaceType.Unknown, NetworkAdapterKind.Other)]
    public void NetworkAdapterSelector_ClassifiesInterfaceTypes(NetworkInterfaceType type, NetworkAdapterKind expected)
    {
        Assert.Equal(expected, NetworkAdapterSelector.Classify(type));
    }

    [Fact]
    public void WlanSsidParser_DecodesUtf8Ssid()
    {
        var bytes = new byte[32];
        var ssid = System.Text.Encoding.UTF8.GetBytes("FluidBar-5G");
        ssid.CopyTo(bytes, 0);
        Assert.Equal("FluidBar-5G", WlanSsidParser.ParseSsid(bytes, (uint)ssid.Length));
        Assert.Null(WlanSsidParser.ParseSsid(bytes, 0));
        Assert.Null(WlanSsidParser.ParseSsid(null, 5));
    }

    [Theory]
    [InlineData(0x0804, true)]
    [InlineData(0x0404, true)]
    [InlineData(0x0C04, true)]
    [InlineData(0x1004, true)]
    [InlineData(0x1404, true)]
    [InlineData(0x0409, false)]
    public void ImeLayoutMapper_DetectsChineseLayouts(ushort languageId, bool expected)
    {
        Assert.Equal(expected, ImeLayoutMapper.IsChineseLayout(languageId));
    }

    [Fact]
    public void ImeConversionMode_ReadsNativeFlag()
    {
        Assert.True(ImeConversionMode.HasNativeMode(1));
        Assert.False(ImeConversionMode.HasNativeMode(0));
        Assert.True(ImeConversionMode.HasNativeMode(0x0001 | 0x0008));
    }

    [Fact]
    public void ImeChangePolicy_OnlyPublishesSameWindowModeChanges()
    {
        Assert.False(ImeChangePolicy.ShouldPublish(new ImeTransition(false, true, true)));
        Assert.False(ImeChangePolicy.ShouldPublish(new ImeTransition(true, false, true)));
        Assert.False(ImeChangePolicy.ShouldPublish(new ImeTransition(true, true, false)));
        Assert.True(ImeChangePolicy.ShouldPublish(new ImeTransition(true, true, true)));
    }

    [Fact]
    public void ThemeValueMapper_MapsLightFlagsAndHighContrast()
    {
        Assert.Equal(SystemThemeKind.Light, ThemeValueMapper.FromLightFlag(1));
        Assert.Equal(SystemThemeKind.Dark, ThemeValueMapper.FromLightFlag(0));
        Assert.Equal(SystemThemeKind.Unknown, ThemeValueMapper.FromLightFlag(null));

        var snapshot = ThemeValueMapper.Create(0, 1, false, 0xFF0000FF); // AABBGGRR: A=FF, B=00, G=00, R=FF
        Assert.Equal(SystemThemeKind.Dark, snapshot.AppTheme);
        Assert.Equal(SystemThemeKind.Light, snapshot.SystemTheme);
        Assert.Equal(0xFFFF0000u, snapshot.AccentColorArgb);

        var highContrast = ThemeValueMapper.Create(1, 1, true, null);
        Assert.Equal(SystemThemeKind.HighContrast, highContrast.AppTheme);
        Assert.True(highContrast.IsHighContrast);
        Assert.Equal(0u, highContrast.AccentColorArgb);
    }

    [Fact]
    public void ThemeValueMapper_UnpacksAbgr()
    {
        var (a, r, g, b) = ThemeValueMapper.UnpackAbgr(0xCC112233);
        Assert.Equal(0xCC, a);
        Assert.Equal(0x33, r);
        Assert.Equal(0x22, g);
        Assert.Equal(0x11, b);
    }

    [Fact]
    public void VolumeLevelPolicy_ClampsAndRounds()
    {
        Assert.Equal(0, VolumeLevelPolicy.ToPercent(0f));
        Assert.Equal(100, VolumeLevelPolicy.ToPercent(1f));
        Assert.Equal(50, VolumeLevelPolicy.ToPercent(0.5f));
        Assert.Equal(0, VolumeLevelPolicy.ToPercent(-1f));
        Assert.Equal(100, VolumeLevelPolicy.ToPercent(2f));
        Assert.Equal(0, VolumeLevelPolicy.ToPercent(float.NaN));
        Assert.Equal(0.5f, VolumeLevelPolicy.ToScalar(50));
        Assert.Equal(1f, VolumeLevelPolicy.ToScalar(250));
        Assert.Equal(0, VolumeLevelPolicy.Step(2, -10));
        Assert.Equal(100, VolumeLevelPolicy.Step(98, 10));
        Assert.Equal(60, VolumeLevelPolicy.Step(58, 2));
    }
}
