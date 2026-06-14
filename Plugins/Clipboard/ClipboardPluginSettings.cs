using System.IO;
using System.Text.Json;

namespace FluidBar;

public sealed class ClipboardPluginSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "clipboard.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public int MinFullDisplayChars { get; set; } = 20;
    public int DisplayDurationMs { get; set; } = 3000;
    public double ScrollSpeed { get; set; } = 2.0;

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
        catch
        {
        }

        return new ClipboardPluginSettings();
    }

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
        }
    }

    public void ResetToDefaults()
    {
        MinFullDisplayChars = 20;
        DisplayDurationMs = 3000;
        ScrollSpeed = 2.0;
    }
}

