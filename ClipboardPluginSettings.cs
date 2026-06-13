using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluidBar;

/// <summary>
/// 剪贴板插件配置
/// </summary>
public sealed class ClipboardPluginSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluidBar", "clipboard_plugin.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// 完整显示的最小字符数，超出部分以滚动形式展示
    /// </summary>
    public int MinFullDisplayChars { get; set; } = 20;

    /// <summary>
    /// 停留时间（毫秒）
    /// </summary>
    public int DisplayDurationMs { get; set; } = 3000;

    /// <summary>
    /// 滚动速度（像素/帧，约60fps）
    /// </summary>
    public double ScrollSpeed { get; set; } = 2.0;

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { }
    }

    public static ClipboardPluginSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<ClipboardPluginSettings>(json, JsonOptions)
                       ?? new ClipboardPluginSettings();
            }
        }
        catch { }
        return new ClipboardPluginSettings();
    }

    public void ResetToDefaults()
    {
        MinFullDisplayChars = 20;
        DisplayDurationMs = 3000;
        ScrollSpeed = 2.0;
    }
}
