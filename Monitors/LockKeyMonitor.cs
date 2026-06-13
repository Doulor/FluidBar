using System.Runtime.InteropServices;

namespace FluidBar.Monitors;

/// <summary>
/// 锁键监控 - Caps Lock / Num Lock 状态变化
/// </summary>
public sealed class LockKeyMonitor : ISystemMonitor
{
    public string Id => "lockkey";
    public string Name => "锁键指示";
    public string Description => "Caps Lock / Num Lock 切换时提示";
    public string Icon => "\uE72E"; // Segoe MDL2 Lock
    public bool Enabled { get; set; } = true;
    public event Action<IslandEvent>? EventTriggered;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private const int VK_CAPITAL = 0x14;
    private const int VK_NUMLOCK = 0x90;

    private bool _lastCaps;
    private bool _lastNum;
    private System.Windows.Threading.DispatcherTimer? _timer;
    private bool _isRunning;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _lastCaps = (GetKeyState(VK_CAPITAL) & 1) != 0;
        _lastNum = (GetKeyState(VK_NUMLOCK) & 1) != 0;

        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _timer.Tick += (_, _) => CheckKeys();
        _timer.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        _timer?.Stop();
        _timer = null;
    }

    private void CheckKeys()
    {
        var caps = (GetKeyState(VK_CAPITAL) & 1) != 0;
        var num = (GetKeyState(VK_NUMLOCK) & 1) != 0;

        if (caps != _lastCaps)
        {
            _lastCaps = caps;
            var status = caps ? "Caps Lock ON" : "Caps Lock OFF";
            EventTriggered?.Invoke(new IslandEvent(Id, "Caps Lock", status, "lockkey"));
        }

        if (num != _lastNum)
        {
            _lastNum = num;
            var status = num ? "Num Lock ON" : "Num Lock OFF";
            EventTriggered?.Invoke(new IslandEvent(Id, "Num Lock", status, "lockkey"));
        }
    }

    public void Dispose() => Stop();
}
