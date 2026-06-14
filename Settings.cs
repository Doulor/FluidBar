using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluidBar;

public enum IslandDisplayStrategy
{
    LatestOnly,
    Multiple
}

/// <summary>
/// FluidBar 配置模型 + JSON 持久化
/// </summary>
public sealed class FluidBarSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    // === 位置 ===
    public string Position { get; set; } = "Top";
    public double OffsetX { get; set; } = 0;
    public double OffsetY { get; set; } = 0;

    // === 尺寸 ===
    public double CollapsedWidth { get; set; } = 126;
    public double CollapsedHeight { get; set; } = 38;
    public double ExpandedMaxWidth { get; set; } = 430;
    public double ExpandedHeight { get; set; } = 76;

    // === 外观 ===
    public double CornerRadius { get; set; } = 24;
    public double Opacity { get; set; } = 0.96;
    public string BackgroundColor { get; set; } = "#F4000000";
    public string AccentColor { get; set; } = "#0A84FF";

    // === 行为 ===
    public bool AlwaysOnTop { get; set; } = true;
    public bool AlwaysVisible { get; set; } = false;
    public int AutoHideDelayMs { get; set; } = 3000;
    public IslandDisplayStrategy DisplayStrategy { get; set; } = IslandDisplayStrategy.LatestOnly;
    public int MaxVisibleIslands { get; set; } = 4;
    public double MultiIslandGap { get; set; } = 10;

    // === 环绕微光 ===
    // "Always" = 始终旋转, "Event" = 新状态时旋转(除时钟), "Plugin" = 仅插件状态旋转
    public string RimMode { get; set; } = "Event";

    // === 事件开关 ===
    public bool ClipboardEnabled { get; set; } = true;

    // === 功能详情配置 ===
    public Dictionary<string, MonitorFeatureSettings> MonitorFeatureSettings { get; set; } = new();

    // === 杂项 ===
    public bool HideTrayIcon { get; set; } = false;

    public MonitorFeatureSettings GetMonitorFeatureSettings(string id)
    {
        if (!MonitorFeatureSettings.TryGetValue(id, out var settings))
        {
            settings = new MonitorFeatureSettings();
            MonitorFeatureSettings[id] = settings;
        }
        return settings;
    }

    /// <summary>
    /// 保存配置到 JSON 文件
    /// </summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // 保存失败时静默忽略
        }
    }

    /// <summary>
    /// 从 JSON 文件加载配置，不存在则返回默认值
    /// </summary>
    public static FluidBarSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<FluidBarSettings>(json, JsonOptions) ?? new FluidBarSettings();
            }
        }
        catch
        {
            // 加载失败时返回默认值
        }
        return new FluidBarSettings();
    }

    /// <summary>
    /// 重置为默认值
    /// </summary>
    public void ResetToDefaults()
    {
        var defaults = new FluidBarSettings();
        Position = defaults.Position;
        OffsetX = defaults.OffsetX;
        OffsetY = defaults.OffsetY;
        CollapsedWidth = defaults.CollapsedWidth;
        CollapsedHeight = defaults.CollapsedHeight;
        ExpandedMaxWidth = defaults.ExpandedMaxWidth;
        ExpandedHeight = defaults.ExpandedHeight;
        CornerRadius = defaults.CornerRadius;
        Opacity = defaults.Opacity;
        BackgroundColor = defaults.BackgroundColor;
        AccentColor = defaults.AccentColor;
        AlwaysOnTop = defaults.AlwaysOnTop;
        AlwaysVisible = defaults.AlwaysVisible;
        AutoHideDelayMs = defaults.AutoHideDelayMs;
        DisplayStrategy = defaults.DisplayStrategy;
        MaxVisibleIslands = defaults.MaxVisibleIslands;
        MultiIslandGap = defaults.MultiIslandGap;
        RimMode = defaults.RimMode;
        ClipboardEnabled = defaults.ClipboardEnabled;
        MonitorFeatureSettings = new Dictionary<string, MonitorFeatureSettings>();
        HideTrayIcon = defaults.HideTrayIcon;
    }
}

public sealed class MonitorFeatureSettings
{
    public bool HoverCardEnabled { get; set; } = true;
    public int DisplayDurationMs { get; set; } = 3000;
    public bool EmphasizeTransitions { get; set; } = true;
}
