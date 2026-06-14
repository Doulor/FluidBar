using System.Windows.Threading;

namespace FluidBar.Monitors;

/// <summary>
/// 电池状态监控 - 电量变化和充电状态
/// </summary>
public sealed class BatteryMonitor : ISystemMonitor
{
    public string Id => "battery";
    public string Name => "电池状态";
    public string Description => "电量变化和充电状态提示";
    public string Icon => "\uE850"; // Segoe MDL2 Battery
    public bool Enabled { get; set; } = true;
    public event Action<IslandEvent>? EventTriggered;

    private DispatcherTimer? _timer;
    private int _lastPercent = -1;
    private bool _lastCharging;
    private bool _isRunning;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => CheckBattery();
        _timer.Start();
        CheckBattery();
    }

    public void Stop()
    {
        _isRunning = false;
        _timer?.Stop();
        _timer = null;
    }

    private void CheckBattery()
    {
        try
        {
            var status = System.Windows.Forms.SystemInformation.PowerStatus;

            // 桌面端无电池，直接停止监控
            if (status.BatteryChargeStatus ==
                System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
            {
                Stop();
                return;
            }

            var percent = (int)(status.BatteryLifePercent * 100);
            var charging = status.PowerLineStatus ==
                           System.Windows.Forms.PowerLineStatus.Online;
            var remaining = status.BatteryLifeRemaining;

            // 忽略无效电量（桌面端可能返回 25500%）
            if (percent < 0 || percent > 100) return;

            if (percent != _lastPercent || charging != _lastCharging)
            {
                _lastPercent = percent;
                _lastCharging = charging;

                string iconKind, title, content;

                if (charging)
                {
                    iconKind = "battery_charge";
                    title = $"充电中 {percent}%";
                    content = remaining > 0
                        ? $"约 {remaining / 60} 分钟后充满"
                        : "正在充电...";
                }
                else if (percent <= 15)
                {
                    iconKind = "battery_low";
                    title = $"电量低 {percent}%";
                    content = remaining > 0
                        ? $"剩余约 {remaining / 60} 分钟"
                        : "请尽快充电";
                }
                else
                {
                    iconKind = "battery";
                    title = $"电池 {percent}%";
                    content = remaining > 0
                        ? $"剩余约 {remaining / 60} 分钟"
                        : "电池供电中";
                }

                EventTriggered?.Invoke(new IslandEvent(Id, title, content, iconKind));
            }
        }
        catch { }
    }

    public void Dispose() => Stop();
}
