using System.Net.NetworkInformation;

namespace FluidBar.Monitors;

/// <summary>
/// 网络状态监控 - WiFi 连接/断开
/// </summary>
public sealed class NetworkMonitor : ISystemMonitor
{
    public string Id => "network";
    public string Name => "网络状态";
    public string Description => "网络连接/断开时提示";
    public string Icon => "\uE701"; // Segoe MDL2 WiFi
    public bool Enabled { get; set; } = true;
    public event Action<IslandEvent>? EventTriggered;

    private bool _lastConnected;
    private string _lastSsid = "";
    private bool _isRunning;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _lastConnected = GetConnectionStatus();
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var connected = GetConnectionStatus();
            if (connected == _lastConnected) return;

            _lastConnected = connected;
            var ssid = connected ? GetWifiSsid() : "";

            if (connected)
            {
                _lastSsid = ssid;
                EventTriggered?.Invoke(new IslandEvent(Id, "网络已连接", ssid, "network"));
            }
            else
            {
                _lastSsid = "";
                EventTriggered?.Invoke(new IslandEvent(Id, "网络已断开", "无网络连接", "network_off"));
            }
        });
    }

    private static bool GetConnectionStatus()
    {
        try { return NetworkInterface.GetIsNetworkAvailable(); }
        catch { return false; }
    }

    private static string GetWifiSsid()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                    ni.OperationalStatus == OperationalStatus.Up)
                {
                    return ni.Name;
                }
            }
            return "有线网络";
        }
        catch { return "已连接"; }
    }

    public void Dispose() => Stop();
}
