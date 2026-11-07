using System.Globalization;
using System.Text.Json;
using FluidBar.Core.Configuration;
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

    /// <summary>Karaoke lead/lag in milliseconds, owned by the media plugin (no island-side knob).</summary>
    public const string LyricOffsetKey = "media.lyricOffsetMs";

    /// <summary>A new isolated reader with compact lyrics on; no file I/O (tests/headless hosts).</summary>
    public static MediaPillPreferences Default => new(showCompactLyrics: true);

    /// <summary>A new isolated reader with compact lyrics off (tests that pin the title form).</summary>
    public static MediaPillPreferences Off => new(showCompactLyrics: false);

    private readonly string _path;
    private readonly bool _followFile;
    private readonly object _gate = new();
    private bool _showCompactLyrics = true;
    private double _lyricOffsetMs;
    private float _lyricGlowStrength = LyricGlowSettings.DefaultStrength;
    private DateTime _stamp = DateTime.MinValue;

    /// <summary>Cached halo strength. The host pushes changes; this getter performs no file I/O.</summary>
    public float LyricGlowStrength
    {
        get { lock (_gate) { return _lyricGlowStrength; } }
    }

    /// <summary>Applies the host-owned percentage; returns true only when the effective value changes.</summary>
    public bool SetLyricGlowStrength(JsonElement? value)
    {
        var strength = ParseLyricGlowStrength(value);
        lock (_gate)
        {
            if (_lyricGlowStrength == strength) return false;
            _lyricGlowStrength = strength;
            return true;
        }
    }

    /// <summary>Normalizes a plugin value without touching storage; malformed or missing values use 70%.</summary>
    public static float ParseLyricGlowStrength(JsonElement? value)
    {
        var percent = LyricGlowSettings.DefaultPercent;
        if (value is { } element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
                percent = number;
            else if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                percent = parsed;
        }
        return LyricGlowSettings.ToStrength(percent);
    }

    /// <summary>Reads the startup value on a worker even when the media plugin cannot start.</summary>
    public Task<JsonElement?> ReadStoredLyricGlowSettingAsync(CancellationToken cancellationToken = default)
        => Task.Run(async () =>
        {
            if (!_followFile) return (JsonElement?)null;
            try
            {
                var info = new FileInfo(_path);
                if (!info.Exists || info.Length > 1024 * 1024) return null;
                var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("values", out var values)
                    && values.ValueKind == JsonValueKind.Object
                    && values.TryGetProperty(LyricGlowSettings.Key, out var value))
                    return value.Clone();
                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Same fallback as the plugin store: an unavailable/corrupt value uses the default.
                return null;
            }
        }, cancellationToken);

    /// <summary>Creates a reader for the media plugin's settings file.</summary>
    /// <param name="settingsPath">Overrides the settings file (tests).</param>
    /// <param name="followFile">When false the reader never touches the file system.</param>
    public MediaPillPreferences(string? settingsPath = null, bool followFile = true)
    {
        _path = settingsPath ?? PluginRuntimePaths.SettingsFile(
            PluginRuntimePaths.ResolveRoot(), MediaPluginId);
        _followFile = followFile;
    }

    /// <summary>Karaoke offset in milliseconds (-800..+800); positive shows the words sooner.</summary>
    public double LyricOffsetMs
    {
        get
        {
            if (!_followFile)
            {
                return _lyricOffsetMs;
            }

            lock (_gate)
            {
                Refresh();
                return _lyricOffsetMs;
            }
        }
    }

    private MediaPillPreferences(bool showCompactLyrics)
    {
        _path = string.Empty;
        _followFile = false;
        _showCompactLyrics = showCompactLyrics;
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
            var json = File.ReadAllText(_path);
            _showCompactLyrics = ParseShowCompactLyrics(json);
            _lyricOffsetMs = ParseLyricOffsetMs(json);
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
    /// <summary>Parses <c>media.lyricOffsetMs</c>; missing/invalid means 0.</summary>
    /// <param name="json">Host-owned settings payload.</param>
    public static double ParseLyricOffsetMs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("values", out var values)
                || !values.TryGetProperty(LyricOffsetKey, out var raw))
            {
                return 0;
            }

            var value = raw.ValueKind switch
            {
                JsonValueKind.Number => raw.GetDouble(),
                JsonValueKind.String when double.TryParse(raw.GetString(), out var parsed) => parsed,
                _ => 0,
            };
            return double.IsFinite(value) ? Math.Clamp(value, -800, 800) : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

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
