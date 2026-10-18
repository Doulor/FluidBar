using System.Text.Json;
using FluidBar.PluginHost.Loader;
using FluidBar.PluginHost.Logging;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.PluginHost.Runtime;

/// <summary>
/// Discovers plugin packages under the plugin root. Deterministic layout rules (from the ALC spike report):
/// <list type="bullet">
///   <item><description><c>&lt;root&gt;/&lt;id&gt;/manifest.json</c> — flat/dev install.</description></item>
///   <item><description><c>&lt;root&gt;/&lt;id&gt;/&lt;version&gt;/manifest.json</c> — marketplace install; selected through
///   the atomic <c>&lt;id&gt;/active.json</c> pointer, falling back to the highest installed version. Versioned
///   directories are mandatory for updates because Windows locks loaded DLLs (never overwrite a loaded DLL).</description></item>
/// </list>
/// Symlinks/reparse points and hidden folders (for example the installer's <c>.staging</c>) are ignored.
/// </summary>
internal sealed class PluginDiscoverer
{
    private const int MaxActiveJsonBytes = 4 * 1024;

    private readonly PluginHostOptions _options;

    internal PluginDiscoverer(PluginHostOptions options) => _options = options;

    internal Task<IReadOnlyList<PluginDiscoveryInfo>> DiscoverAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Discover(cancellationToken), cancellationToken);

    private IReadOnlyList<PluginDiscoveryInfo> Discover(CancellationToken cancellationToken)
    {
        var results = new List<PluginDiscoveryInfo>();
        var root = Path.GetFullPath(_options.PluginRootDirectory);
        if (!Directory.Exists(root))
        {
            return results;
        }

        var pluginDirectories = Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrEmpty(name) && !name.StartsWith('.') && !name.StartsWith('_'))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (pluginDirectories.Length > _options.MaxDiscoveredPlugins)
        {
            _options.Logger.Warn(
                "<host>",
                $"Plugins folder contains {pluginDirectories.Length} entries; only the first {_options.MaxDiscoveredPlugins} are considered.");
        }

        foreach (var directoryName in pluginDirectories.Take(_options.MaxDiscoveredPlugins))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pluginDirectory = Path.Combine(root, directoryName!);
            var candidate = SelectCandidateDirectory(pluginDirectory, out var versionDirectory, out var selectionError);
            if (candidate is null)
            {
                results.Add(new PluginDiscoveryInfo(
                    directoryName!,
                    pluginDirectory,
                    Path.Combine(pluginDirectory, "manifest.json"),
                    manifest: null,
                    isValid: false,
                    errors: [selectionError ?? "No manifest.json found."]));
                continue;
            }

            var manifestPath = Path.Combine(candidate, "manifest.json");
            try
            {
                var json = ReadManifest(manifestPath);
                if (!PluginManifestParser.TryParse(json, out var manifest, out var parseErrors) || manifest is null)
                {
                    results.Add(new PluginDiscoveryInfo(directoryName!, candidate, manifestPath, null, false, parseErrors));
                    continue;
                }

                var errors = new List<string>();
                if (!string.Equals(manifest.Id, directoryName, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Manifest id '{manifest.Id}' does not match the plugin directory name '{directoryName}'.");
                }

                if (versionDirectory is not null &&
                    SemanticVersion.TryParse(versionDirectory, out var folderVersion) &&
                    manifest.TryGetVersion(out var manifestVersion) &&
                    folderVersion != manifestVersion)
                {
                    errors.Add($"Version directory '{versionDirectory}' does not match manifest version '{manifest.Version}'.");
                }

                var validation = PluginManifestValidator.Validate(manifest, _options.ValidationOptions);
                errors.AddRange(validation.Errors.Select(static issue => issue.ToString()));
                results.Add(new PluginDiscoveryInfo(
                    manifest.Id,
                    candidate,
                    manifestPath,
                    manifest,
                    isValid: errors.Count == 0,
                    errors));
            }
            catch (PluginManifestException exception)
            {
                results.Add(new PluginDiscoveryInfo(
                    directoryName!,
                    candidate,
                    manifestPath,
                    null,
                    false,
                    exception.Errors.Count > 0 ? exception.Errors : [exception.Message]));
            }
            catch (IOException exception)
            {
                results.Add(new PluginDiscoveryInfo(directoryName!, candidate, manifestPath, null, false, [exception.Message]));
            }
            catch (UnauthorizedAccessException exception)
            {
                results.Add(new PluginDiscoveryInfo(directoryName!, candidate, manifestPath, null, false, [exception.Message]));
            }
        }

        return results;
    }

    private string? SelectCandidateDirectory(
        string pluginDirectory,
        out string? versionDirectory,
        out string? error)
    {
        versionDirectory = null;
        error = null;
        if (!Directory.Exists(pluginDirectory))
        {
            error = "Plugin directory does not exist.";
            return null;
        }

        if (IsReparsePoint(pluginDirectory))
        {
            error = "Plugin directory must not be a symlink/reparse point.";
            return null;
        }

        // 1. Flat package: <id>/manifest.json (developer/local install).
        var flatManifest = Path.Combine(pluginDirectory, "manifest.json");
        if (File.Exists(flatManifest))
        {
            return pluginDirectory;
        }

        // 2. Versioned package: <id>/<version>/manifest.json, selected by active.json then highest version.
        var versioned = EnumerateVersionDirectories(pluginDirectory);
        if (versioned.Count == 0)
        {
            error = "No manifest.json found in the plugin directory or a version subdirectory.";
            return null;
        }

        var activeVersion = ReadActiveVersion(pluginDirectory);
        if (activeVersion is { } active)
        {
            var match = versioned.FirstOrDefault(entry => entry.Version == active);
            if (match.Directory is not null)
            {
                versionDirectory = match.Name;
                return match.Directory;
            }

            // Fail-safe for a corrupted pointer: log and fall back to the highest installed version.
            _options.Logger.Warn(
                Path.GetFileName(pluginDirectory),
                $"layout.active_version_missing: active.json points at {active}, which is not installed; falling back to the highest version.");
        }

        var highest = versioned[0];
        versionDirectory = highest.Name;
        return highest.Directory;
    }

    private static List<(string Directory, string Name, SemanticVersion Version)> EnumerateVersionDirectories(string pluginDirectory)
    {
        var results = new List<(string Directory, string Name, SemanticVersion Version)>();
        foreach (var candidate in Directory.EnumerateDirectories(pluginDirectory))
        {
            var name = Path.GetFileName(candidate);
            if (string.IsNullOrEmpty(name) || name.StartsWith('.') || name.StartsWith('_') || IsReparsePoint(candidate))
            {
                continue;
            }

            if (!File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                continue;
            }

            if (SemanticVersion.TryParse(name, out var version))
            {
                results.Add((candidate, name, version));
            }
        }

        results.Sort(static (left, right) => right.Version.CompareTo(left.Version));
        return results;
    }

    private SemanticVersion? ReadActiveVersion(string pluginDirectory)
    {
        var activePath = Path.Combine(pluginDirectory, "active.json");
        if (!File.Exists(activePath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(activePath);
            if (info.Length > MaxActiveJsonBytes)
            {
                _options.Logger.Warn(Path.GetFileName(pluginDirectory), "active.json is oversized; ignoring the active pointer.");
                return null;
            }

            using var stream = File.OpenRead(activePath);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("activeVersion", out var activeVersion) &&
                activeVersion.ValueKind == JsonValueKind.String &&
                SemanticVersion.TryParse(activeVersion.GetString(), out var parsed))
            {
                return parsed;
            }

            _options.Logger.Warn(Path.GetFileName(pluginDirectory), "active.json is malformed; falling back to the highest installed version.");
            return null;
        }
        catch (JsonException exception)
        {
            _options.Logger.Warn(Path.GetFileName(pluginDirectory), $"active.json is not valid JSON: {exception.Message}");
            return null;
        }
        catch (IOException exception)
        {
            _options.Logger.Warn(Path.GetFileName(pluginDirectory), $"active.json could not be read: {exception.Message}");
            return null;
        }
    }

    private string ReadManifest(string manifestPath)
    {
        var info = new FileInfo(manifestPath);
        if (info.Length > _options.MaxManifestBytes)
        {
            throw new PluginManifestException($"Manifest file is {info.Length} bytes; the limit is {_options.MaxManifestBytes}.");
        }

        return File.ReadAllText(manifestPath);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
