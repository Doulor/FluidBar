using System.IO;
using System.Text.Json;

namespace FluidBar;

public sealed class MediaPluginSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "media.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public bool ShowLyrics { get; set; } = true;
    public int PollIntervalMs { get; set; } = 1200;
    public bool ShowWhenPaused { get; set; } = true;
    public string LyricsDirectory { get; set; } = Path.Combine(SettingsDir, "lyrics");

    public static MediaPluginSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<MediaPluginSettings>(json, JsonOptions)
                       ?? new MediaPluginSettings();
            }
        }
        catch
        {
        }

        return new MediaPluginSettings();
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
}

public sealed class MediaPluginConfig : IPluginConfig
{
    private readonly MediaPluginSettings _settings;

    public MediaPluginConfig(MediaPluginSettings settings)
    {
        _settings = settings;
    }

    public string Title => "媒体插件设置";

    public bool ShowLyrics
    {
        get => _settings.ShowLyrics;
        set => _settings.ShowLyrics = value;
    }

    public int PollIntervalMs
    {
        get => _settings.PollIntervalMs;
        set => _settings.PollIntervalMs = Math.Clamp(value, 400, 5000);
    }

    public bool ShowWhenPaused
    {
        get => _settings.ShowWhenPaused;
        set => _settings.ShowWhenPaused = value;
    }

    public string LyricsDirectory
    {
        get => _settings.LyricsDirectory;
        set => _settings.LyricsDirectory = value;
    }

    public object CreateSettingsPanel() => _settings;

    public void Save() => _settings.Save();

    public void Load() { }
}

