using System.Text.Json;
using FluidBar.Marketplace.Install;
using FluidBar.Sdk.Json;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Marketplace.Install;

/// <summary>
/// Owns the on-disk layout of one plugin directory:
/// <code>
/// &lt;root&gt;/&lt;id&gt;/active.json          atomic pointer to the active version
/// &lt;root&gt;/&lt;id&gt;/state.json           install state (last good version, publisher pin, retained versions)
/// &lt;root&gt;/&lt;id&gt;/&lt;version&gt;/manifest.json
/// &lt;root&gt;/&lt;id&gt;/settings.json         host-owned settings (kept on "keep settings" uninstall)
/// &lt;root&gt;/&lt;id&gt;/data/                 $PLUGIN_DATA
/// </code>
/// All writes are atomic (temp file + replace) so a crash never leaves a half-written pointer/state file.
/// </summary>
internal sealed class PluginStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = PluginJson.CreateStrictOptions();

    private readonly string _pluginRoot;
    private readonly string _pluginId;
    private readonly IPluginInstallLogger _logger;

    internal PluginStateStore(string pluginRoot, string pluginId, IPluginInstallLogger logger)
    {
        _pluginRoot = pluginRoot;
        _pluginId = pluginId;
        _logger = logger;
    }

    internal string PluginDirectory => Path.Combine(_pluginRoot, _pluginId);

    internal string StatePath => Path.Combine(PluginDirectory, "state.json");

    internal string ActivePointerPath => Path.Combine(PluginDirectory, "active.json");

    internal PluginInstallState? ReadState()
    {
        if (!File.Exists(StatePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<PluginInstallState>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            _logger.Error($"state.json for plugin '{_pluginId}' is invalid: {exception.Message}");
            return null;
        }
        catch (IOException exception)
        {
            _logger.Error($"state.json for plugin '{_pluginId}' could not be read: {exception.Message}", exception);
            return null;
        }
    }

    internal void WriteState(PluginInstallState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(PluginDirectory);
        WriteAtomic(StatePath, JsonSerializer.Serialize(state, SerializerOptions));
    }

    internal string? ReadActiveVersion()
    {
        if (!File.Exists(ActivePointerPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ActivePointerPath));
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("activeVersion", out var activeVersion) &&
                activeVersion.ValueKind == JsonValueKind.String &&
                SemanticVersion.TryParse(activeVersion.GetString(), out var parsed))
            {
                return parsed.ToString();
            }

            _logger.Warn($"active.json for plugin '{_pluginId}' is malformed; the highest installed version will be used.");
            return null;
        }
        catch (JsonException exception)
        {
            _logger.Warn($"active.json for plugin '{_pluginId}' is invalid JSON: {exception.Message}");
            return null;
        }
        catch (IOException exception)
        {
            _logger.Warn($"active.json for plugin '{_pluginId}' could not be read: {exception.Message}");
            return null;
        }
    }

    internal void WriteActiveVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Directory.CreateDirectory(PluginDirectory);
        var json = JsonSerializer.Serialize(new ActivePointer { ActiveVersion = version }, SerializerOptions);
        WriteAtomic(ActivePointerPath, json);
    }

    internal void DeleteActivePointer()
    {
        if (File.Exists(ActivePointerPath))
        {
            File.Delete(ActivePointerPath);
        }
    }

    internal string GetVersionDirectory(string version) =>
        Path.Combine(PluginDirectory, version);

    internal IReadOnlyList<string> EnumerateInstalledVersions()
    {
        if (!Directory.Exists(PluginDirectory))
        {
            return [];
        }

        var versions = new List<(SemanticVersion Version, string Name)>();
        foreach (var directory in Directory.EnumerateDirectories(PluginDirectory))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.') ||
                string.Equals(name, "data", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(directory, "manifest.json")))
            {
                continue;
            }

            if (SemanticVersion.TryParse(name, out var version))
            {
                versions.Add((version, name));
            }
        }

        return [.. versions.OrderByDescending(static pair => pair.Version).Select(static pair => pair.Name)];
    }

    internal void DeleteVersionDirectory(string version)
    {
        var directory = GetVersionDirectory(version);
        if (!Directory.Exists(directory))
        {
            return;
        }

        Directory.Delete(directory, recursive: true);
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record ActivePointer
    {
        public string ActiveVersion { get; init; } = string.Empty;
    }
}
