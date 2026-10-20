using System.Text.Json;
using FluidBar.App.Plugins;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Reads the media plugin's "lyrics in the pill" setting for the island.
/// </summary>
/// <remarks>
/// The toggle lives on the media plugin's settings page, so its value is owned by the plugin's
/// host-side settings store (<c>%APPDATA%\FluidBar\plugins\fluidbar-media\settings.json</c>). The island
/// is App-side code and cannot reach the plugin's <c>IPluginSettings</c> instance, so it reads the same
/// file the host writes, cached by file timestamp: the change is picked up on the next frame after the
/// user flips the switch, and a missing file/key means "on" (the documented default).
/// </remarks>
public sealed class MediaPillPreferences
{
    /// <summary>Builtin media plugin id.</summary>
    public const string MediaPluginId = "fluidbar-media";

    /// <summary>Setting key rendered by the media plugin's settings page.</summary>
    public const string CompactLyricsKey = "media.compactLyrics";

    /// <summary>A reader that always reports the default (used by tests and headless hosts).</summary>
    public static MediaPillPreferences Default { get; } = new(null, followFile: false);

    private readonly string _path;
    private readonly bool _followFile;
    private readonly object _gate = new();
    private bool _showCompactLyrics = true;
    private DateTime _stamp = DateTime.MinValue;

    /// <summary>Creates a reader for the media plugin's settings file.</summary>
    /// <param name="settingsPath">Overrides the settings file (tests).</param>
    /// <param name="followFile">When false the reader never touches the file system.</param>
    public MediaPillPreferences(string? settingsPath = null, bool followFile = true)
    {
        _path = settingsPath ?? PluginRuntimePaths.SettingsFile(
            PluginRuntimePaths.ResolveRoot(), MediaPluginId);
        _followFile = followFile;
    }

    /// <summary>Whether the compact pill should show the live lyric instead of the title/artist.</summary>
    public bool ShowCompactLyrics
    {
        get
        {
            if (!_followFile)
            {
                return _showCompactLyrics;
            }

            lock (_gate)
            {
                Refresh();
                return _showCompactLyrics;
            }
        }
    }

    private void Refresh()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.LastWriteTimeUtc == _stamp)
            {
                return;
            }

            _stamp = info.LastWriteTimeUtc;
            _showCompactLyrics = ParseShowCompactLyrics(File.ReadAllText(_path));
        }
        catch (IOException)
        {
            // A half-written store keeps the previous value; the default stays "on".
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Parses the host-owned settings payload (<c>{"values":{"media.compactLyrics":false}}</c>).</summary>
    /// <param name="json">Raw settings file content (may be null/blank).</param>
    /// <returns>True unless the store explicitly says false.</returns>
    public static bool ParseShowCompactLyrics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("values", out var values)
                || values.ValueKind != JsonValueKind.Object
                || !values.TryGetProperty(CompactLyricsKey, out var flag))
            {
                return true;
            }

            return flag.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => true,
            };
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
