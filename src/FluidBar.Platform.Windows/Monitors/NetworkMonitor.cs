using System.Net.NetworkInformation;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Monitors;

/// <summary>Adapter category used by the UI to pick an icon.</summary>
public enum NetworkAdapterKind
{
    Unknown,
    Ethernet,
    Wifi,
    Virtual,
    Other,
}

/// <summary>Immutable network snapshot.</summary>
/// <param name="IsConnected">Whether Windows reports network connectivity.</param>
/// <param name="AdapterName">Friendly name of the selected adapter.</param>
/// <param name="Kind">Adapter category.</param>
/// <param name="Ssid">Wireless SSID (real value, not the adapter name — v1 bug F-7), or null.</param>
/// <param name="SignalQuality">WLAN signal quality 0–100, when connected over Wi-Fi.</param>
public sealed record NetworkSnapshot(
    bool IsConnected,
    string? AdapterName,
    NetworkAdapterKind Kind,
    string? Ssid,
    uint? SignalQuality);

/// <summary>One adapter reduced to the fields the selection policy needs (pure input for tests).</summary>
public sealed record NetworkAdapterDescriptor(
    string Name,
    NetworkAdapterKind Kind,
    bool IsUp,
    bool HasGateway,
    uint? SignalQuality = null);

/// <summary>Real WLAN connection details from <c>wlanapi</c>.</summary>
public sealed record WlanConnectionInfo(
    string Ssid,
    uint SignalQuality,
    string InterfaceDescription,
    string? ProfileName = null);

/// <summary>Pure adapter selection and snapshot construction.</summary>
public static class NetworkAdapterSelector
{
    /// <summary>
    /// Selects the adapter to display: an up adapter with a default gateway wins; wireless is preferred
    /// over wired when both are usable (the SSID is the informative part); virtual adapters are last.
    /// </summary>
    public static NetworkAdapterDescriptor? SelectPrimary(IReadOnlyList<NetworkAdapterDescriptor> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        NetworkAdapterDescriptor? best = null;
        var bestScore = int.MinValue;
        foreach (var adapter in adapters)
        {
            if (!adapter.IsUp)
            {
                continue;
            }

            var score = adapter.HasGateway ? 100 : 0;
            score += adapter.Kind switch
            {
                NetworkAdapterKind.Wifi => 30,
                NetworkAdapterKind.Ethernet => 20,
                NetworkAdapterKind.Unknown => 10,
                NetworkAdapterKind.Virtual => 0,
                _ => 5,
            };
            if (score > bestScore)
            {
                bestScore = score;
                best = adapter;
            }
        }

        return best;
    }

    /// <summary>Builds the UI-facing snapshot from the selected adapter and optional WLAN data.</summary>
    public static NetworkSnapshot CreateSnapshot(IReadOnlyList<NetworkAdapterDescriptor> adapters,
        bool isNetworkAvailable, WlanConnectionInfo? wlan)
    {
        var primary = SelectPrimary(adapters);
        if (primary is null)
        {
            return new NetworkSnapshot(isNetworkAvailable, null, NetworkAdapterKind.Unknown, null, null);
        }

        var ssid = primary.Kind == NetworkAdapterKind.Wifi
            ? (string.IsNullOrWhiteSpace(wlan?.Ssid) ? wlan?.ProfileName : wlan?.Ssid)
            : null;
        var signal = primary.Kind == NetworkAdapterKind.Wifi ? wlan?.SignalQuality : primary.SignalQuality;
        return new NetworkSnapshot(true, primary.Name, primary.Kind, ssid, signal);
    }

    /// <summary>
    /// Display label for a snapshot: real SSID on Wi-Fi, adapter name on wired, and an explicit
    /// disconnected label instead of pretending a network exists. Null only before the first snapshot.
    /// </summary>
    public static string? DisplayName(NetworkSnapshot? snapshot, string disconnectedLabel)
    {
        if (snapshot is null)
        {
            return null;
        }

        if (!snapshot.IsConnected)
        {
            return disconnectedLabel;
        }

        if (snapshot.Kind == NetworkAdapterKind.Wifi)
        {
            return string.IsNullOrWhiteSpace(snapshot.Ssid) ? snapshot.AdapterName : snapshot.Ssid;
        }

        return snapshot.AdapterName;
    }

    /// <summary>Maps a <see cref="NetworkInterfaceType"/> to the small UI-facing enum.</summary>
    public static NetworkAdapterKind Classify(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => NetworkAdapterKind.Wifi,
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.FastEthernetFx
            => NetworkAdapterKind.Ethernet,
        NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel => NetworkAdapterKind.Virtual,
        _ => NetworkAdapterKind.Other,
    };
}

/// <summary>Pure parsing of the WLAN SSID byte array returned by <c>WlanQueryInterface</c>.</summary>
public static class WlanSsidParser
{
    /// <summary>Decodes the raw SSID bytes (UTF-8, no terminator). Returns null when empty.</summary>
    public static string? ParseSsid(byte[]? ssidBytes, uint length)
    {
        if (ssidBytes is null || length == 0)
        {
            return null;
        }

        var count = (int)Math.Min(length, (uint)ssidBytes.Length);
        if (count <= 0)
        {
            return null;
        }

        var text = System.Text.Encoding.UTF8.GetString(ssidBytes, 0, count).TrimEnd('\0');
        return string.IsNullOrEmpty(text) ? null : text;
    }
}

/// <summary>Reads the real connected SSID via the native WLAN API, off the caller's thread.</summary>
internal static class WlanSsidReader
{
    public static WlanConnectionInfo? TryGetConnectedNetwork(IPlatformLogger logger, PlatformCounters counters)
    {
        try
        {
            using var client = SafeWlanHandle.Open();
            if (NativeMethods.WlanApi.WlanEnumInterfaces(client.DangerousGetHandle(), IntPtr.Zero, out var listPtr) != 0)
            {
                return null;
            }

            try
            {
                var header = System.Runtime.InteropServices.Marshal.PtrToStructure<
                    NativeMethods.WLAN_INTERFACE_INFO_LIST_HEADER>(listPtr);
                if (header.dwNumberOfItems == 0)
                {
                    return null;
                }

                var itemSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WLAN_INTERFACE_INFO>();
                var firstItem = IntPtr.Add(listPtr, 8);
                for (var i = 0; i < header.dwNumberOfItems; i++)
                {
                    var info = System.Runtime.InteropServices.Marshal.PtrToStructure<
                        NativeMethods.WLAN_INTERFACE_INFO>(IntPtr.Add(firstItem, i * itemSize));
                    var result = QueryInterface(client.DangerousGetHandle(), info.InterfaceGuid, logger);
                    if (result is not null)
                    {
                        return result with { InterfaceDescription = info.strInterfaceDescription ?? string.Empty };
                    }
                }
            }
            finally
            {
                NativeMethods.WlanApi.WlanFreeMemory(listPtr);
            }
        }
        catch (Exception ex)
        {
            counters.IncrementWin32Failures();
            logger.LogWarning(PlatformLogCategory.Monitor, "network.wlan-query-failed",
                "WLAN SSID query failed; continuing without SSID.", ex);
        }

        return null;
    }

    private static WlanConnectionInfo? QueryInterface(IntPtr client, Guid interfaceGuid, IPlatformLogger logger)
    {
        var result = NativeMethods.WlanApi.WlanQueryInterface(client, in interfaceGuid,
            NativeMethods.WLAN_INTF_OPCODE_CURRENT_CONNECTION, IntPtr.Zero, out var dataSize, out var dataPtr, out _);
        if (result != 0 || dataPtr == IntPtr.Zero || dataSize == 0)
        {
            return null;
        }

        try
        {
            var connection = System.Runtime.InteropServices.Marshal.PtrToStructure<
                NativeMethods.WLAN_CONNECTION_ATTRIBUTES>(dataPtr);
            var ssid = WlanSsidParser.ParseSsid(
                connection.wlanAssociationAttributes.dot11Ssid.ucSSID,
                connection.wlanAssociationAttributes.dot11Ssid.uSSIDLength);
            if (string.IsNullOrEmpty(ssid))
            {
                ssid = connection.strProfileName?.TrimEnd('\0');
            }

            return string.IsNullOrEmpty(ssid)
                ? null
                : new WlanConnectionInfo(
                    ssid,
                    connection.wlanAssociationAttributes.wlanSignalQuality,
                    string.Empty,
                    connection.strProfileName?.TrimEnd('\0'));
        }
        catch (Exception ex)
        {
            logger.LogWarning(PlatformLogCategory.Monitor, "network.wlan-parse-failed",
                "Could not parse WLAN_CONNECTION_ATTRIBUTES; continuing without SSID.", ex);
            return null;
        }
        finally
        {
            NativeMethods.WlanApi.WlanFreeMemory(dataPtr);
        }
    }
}

/// <summary>
/// Event-driven network monitor: <see cref="NetworkChange"/> notifications trigger a coalesced,
/// off-thread refresh that reads adapters + the real WLAN SSID. No polling timer.
/// </summary>
public sealed class NetworkMonitor : PlatformMonitor<NetworkSnapshot>
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly object _debounceGate = new();
    private ITimer? _debounceTimer;
    private int _running;

    public NetworkMonitor(IPlatformLogger logger, PlatformCounters counters, TimeProvider? timeProvider = null)
        : base("network", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _running, 1);
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _running) == 0)
        {
            return;
        }

        lock (_debounceGate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = _timeProvider.CreateTimer(static state =>
            {
                var self = (NetworkMonitor)state!;
                _ = self.RefreshSafeAsync();
            }, this, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RefreshSafeAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Counters.IncrementMonitorCallbackFailures();
            Logger.LogError(PlatformLogCategory.Monitor, "network.refresh-failed", "Network refresh failed.", ex);
        }
    }

    /// <summary>Reads adapters and WLAN state on the thread pool, then publishes change-only.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var snapshot = await Task.Run(BuildSnapshot, cancellationToken).ConfigureAwait(false);
            Publish(snapshot);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private NetworkSnapshot BuildSnapshot()
    {
        var descriptors = new List<NetworkAdapterDescriptor>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var isUp = adapter.OperationalStatus == OperationalStatus.Up;
                var hasGateway = adapter.GetIPProperties().GatewayAddresses
                    .Any(gateway => gateway.Address is not null
                                    && !gateway.Address.Equals(System.Net.IPAddress.Any)
                                    && !gateway.Address.Equals(System.Net.IPAddress.IPv6Any));
                descriptors.Add(new NetworkAdapterDescriptor(adapter.Name,
                    NetworkAdapterSelector.Classify(adapter.NetworkInterfaceType), isUp, hasGateway));
            }
        }
        catch (Exception ex)
        {
            Counters.IncrementWin32Failures();
            Logger.LogWarning(PlatformLogCategory.Monitor, "network.adapter-enum-failed",
                "Could not enumerate network adapters.", ex);
        }

        var primary = NetworkAdapterSelector.SelectPrimary(descriptors);
        WlanConnectionInfo? wlan = null;
        if (primary?.Kind == NetworkAdapterKind.Wifi)
        {
            wlan = WlanSsidReader.TryGetConnectedNetwork(Logger, Counters);
        }

        return NetworkAdapterSelector.CreateSnapshot(descriptors, primary is not null, wlan);
    }

    protected override ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _running, 0);
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        lock (_debounceGate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        return ValueTask.CompletedTask;
    }
}
