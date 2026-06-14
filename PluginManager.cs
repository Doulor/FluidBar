namespace FluidBar;

/// <summary>
/// 插件配置接口 - 每个插件提供自己的配置 UI
/// </summary>
public interface IPluginConfig
{
    string Title { get; }
    object CreateSettingsPanel();
    void Save();
    void Load();
}

/// <summary>
/// 灵动岛插件接口
/// </summary>
public interface IIslandPlugin : IDisposable
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    string Icon { get; }
    bool Enabled { get; set; }
    IPluginConfig? Config { get; }
    event Action<IslandEvent>? EventTriggered;
    void Initialize();
    void Start();
    void Stop();
}

/// <summary>
/// 插件管理器 - 管理所有插件的生命周期
/// </summary>
public sealed class PluginManager : IDisposable
{
    private readonly List<IIslandPlugin> _plugins = new();
    private readonly EventBus _bus;
    private readonly FluidBarSettings _settings;

    public IReadOnlyList<IIslandPlugin> Plugins => _plugins;

    public PluginManager(EventBus bus, FluidBarSettings settings)
    {
        _bus = bus;
        _settings = settings;
    }

    /// <summary>
    /// 注册插件
    /// </summary>
    public void Register(IIslandPlugin plugin)
    {
        plugin.Enabled = _settings.IsPluginEnabled(plugin.Id, plugin.Enabled);
        plugin.Initialize();
        plugin.EventTriggered += evt => _bus.Publish(evt);
        _plugins.Add(plugin);
    }

    /// <summary>
    /// 启动所有已启用的插件
    /// </summary>
    public void StartAll()
    {
        foreach (var p in _plugins)
        {
            if (p.Enabled) p.Start();
        }
    }

    /// <summary>
    /// 启用/禁用插件
    /// </summary>
    public void SetEnabled(IIslandPlugin plugin, bool enabled)
    {
        plugin.Enabled = enabled;
        _settings.SetPluginEnabled(plugin.Id, enabled);
        _settings.Save();
        if (enabled)
            plugin.Start();
        else
            plugin.Stop();
    }

    public void Dispose()
    {
        foreach (var p in _plugins)
            p.Dispose();
    }
}
