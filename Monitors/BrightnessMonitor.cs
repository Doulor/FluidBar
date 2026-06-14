using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace FluidBar.Monitors;

/// <summary>
/// 亮度监控 - 屏幕亮度变化。优先使用 DDC/CI 原生 API，失败时回退到 WMI。
/// </summary>
public sealed class BrightnessMonitor : ISystemMonitor
{
    public string Id => "brightness";
    public string Name => "亮度指示";
    public string Description => "调节屏幕亮度时显示亮度条";
    public string Icon => "\uE706"; // Segoe MDL2 Brightness
    public bool Enabled { get; set; } = true;
    public event Action<IslandEvent>? EventTriggered;

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr hMonitor,
        out uint pdwMinimumBrightness, out uint pdwCurrentBrightness,
        out uint pdwMaximumBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor,
        uint dwPhysicalMonitorArraySize,
        [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize,
        [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
        ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    private DispatcherTimer? _pollTimer;
    private bool _isRunning;
    private int _lastBrightness = -1;
    private bool _useWmiFallback;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _pollTimer.Tick += (_, _) => PollBrightness();
        _pollTimer.Start();
        PollBrightness();
    }

    public void Stop()
    {
        _isRunning = false;
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private void PollBrightness()
    {
        try
        {
            var brightness = _useWmiFallback ? GetBrightnessWmi() : GetCurrentBrightness();
            if (brightness < 0)
            {
                // DDC/CI 失败，切换到 WMI
                brightness = GetBrightnessWmi();
                _useWmiFallback = true;
            }
            if (brightness >= 0 && brightness != _lastBrightness)
            {
                _lastBrightness = brightness;
                EventTriggered?.Invoke(new IslandEvent(
                    Id, $"亮度 {brightness}%", $"{brightness}%", "brightness"));
            }
        }
        catch { }
    }

    /// <summary>通过 WMI 获取笔记本内建屏幕亮度 (Win8+)</summary>
    private static int GetBrightnessWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject obj in searcher.Get())
            {
                var val = obj["CurrentBrightness"];
                if (val != null && byte.TryParse(val.ToString(), out var b))
                    return b;
                break;
            }
        }
        catch { }
        return -1;
    }

    private static int GetCurrentBrightness()
    {
        try
        {
            var min = 0u; var current = 0u; var max = 0u;
            var found = false;

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data) =>
                {
                    if (found) return true;
                    var monitors = new PHYSICAL_MONITOR[1];
                    if (GetPhysicalMonitorsFromHMONITOR(hMon, 1, monitors))
                    {
                        if (GetMonitorBrightness(
                            monitors[0].hPhysicalMonitor, out min, out current, out max))
                        {
                            found = true;
                        }
                        DestroyPhysicalMonitors(1, monitors);
                    }
                    return true;
                }, IntPtr.Zero);

            if (found && max > min)
                return (int)((current - min) * 100.0 / (max - min));
        }
        catch { }
        return -1;
    }

    public void Dispose() => Stop();
}
