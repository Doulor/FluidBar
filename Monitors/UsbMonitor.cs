using System.Management;

namespace FluidBar.Monitors;

/// <summary>
/// USB 设备监控 - 插拔 USB 设备
/// </summary>
public sealed class UsbMonitor : ISystemMonitor
{
    public string Id => "usb";
    public string Name => "USB 设备";
    public string Description => "插拔 USB 设备时提示";
    public string Icon => "\uE88E"; // Segoe MDL2 USB
    public bool Enabled { get; set; } = true;
    public event Action<IslandEvent>? EventTriggered;

    private ManagementEventWatcher? _insertWatcher;
    private ManagementEventWatcher? _removeWatcher;
    private bool _isRunning;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        try
        {
            _insertWatcher = new ManagementEventWatcher(
                new WqlEventQuery(
                    "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'"));
            _insertWatcher.EventArrived += (_, e) => OnDeviceChanged(e, true);
            _insertWatcher.Start();

            _removeWatcher = new ManagementEventWatcher(
                new WqlEventQuery(
                    "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'"));
            _removeWatcher.EventArrived += (_, e) => OnDeviceChanged(e, false);
            _removeWatcher.Start();
        }
        catch { }
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        if (_insertWatcher != null)
        {
            _insertWatcher.Stop();
            _insertWatcher.Dispose();
            _insertWatcher = null;
        }
        if (_removeWatcher != null)
        {
            _removeWatcher.Stop();
            _removeWatcher.Dispose();
            _removeWatcher = null;
        }
    }

    private void OnDeviceChanged(EventArrivedEventArgs e, bool inserted)
    {
        try
        {
            var target = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var name = target["Description"]?.ToString() ?? "USB 设备";

            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var title = inserted ? "设备已连接" : "设备已断开";
                EventTriggered?.Invoke(new IslandEvent(Id, title, name, "usb"));
            });
        }
        catch { }
    }

    public void Dispose() => Stop();
}
