namespace FluidBar.Monitors;

/// <summary>
/// 系统监控管理器 - 管理所有内置系统监控
/// </summary>
public sealed class SystemMonitorManager : IDisposable
{
    private readonly List<ISystemMonitor> _monitors = new();
    private readonly EventBus _bus;

    public IReadOnlyList<ISystemMonitor> Monitors => _monitors;

    public SystemMonitorManager(EventBus bus)
    {
        _bus = bus;
    }

    public void Register(ISystemMonitor monitor)
    {
        monitor.EventTriggered += evt => _bus.Publish(evt);
        _monitors.Add(monitor);
    }

    public void StartAll()
    {
        foreach (var m in _monitors)
        {
            if (m.Enabled) m.Start();
        }
    }

    public void SetEnabled(ISystemMonitor monitor, bool enabled)
    {
        monitor.Enabled = enabled;
        if (enabled) monitor.Start();
        else monitor.Stop();
    }

    public void Dispose()
    {
        foreach (var m in _monitors)
            m.Dispose();
    }
}

/// <summary>
/// 系统监控接口
/// </summary>
public interface ISystemMonitor : IDisposable
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    string Icon { get; }
    bool Enabled { get; set; }
    event Action<IslandEvent>? EventTriggered;
    void Start();
    void Stop();
}
