using System.Windows.Threading;

namespace FluidBar.Monitors;

/// <summary>
/// 时钟监控 - 空闲时显示时间和日期
/// </summary>
public sealed class ClockMonitor : ISystemMonitor
{
    public string Id => "clock";
    public string Name => "时钟";
    public string Description => "空闲时显示当前时间和日期";
    public string Icon => "\uE121"; // Segoe MDL2 Clock
    public bool Enabled { get; set; } = true;
    public event Action<IslandEvent>? EventTriggered;

    private DispatcherTimer? _timer;
    private bool _isRunning;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => ShowTime();
        _timer.Start();
        ShowTime();
    }

    public void Stop()
    {
        _isRunning = false;
        _timer?.Stop();
        _timer = null;
    }

    private void ShowTime()
    {
        var now = DateTime.Now;
        EventTriggered?.Invoke(new IslandEvent(
            Id,
            now.ToString("HH:mm"),
            now.ToString("M月d日 dddd"),
            "clock"));
    }

    public void Dispose() => Stop();
}
