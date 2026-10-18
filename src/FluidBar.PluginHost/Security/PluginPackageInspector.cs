using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using FluidBar.Sdk.Manifest;

namespace FluidBar.PluginHost.Loader;

/// <summary>Severity of a package inspection finding.</summary>
public enum PluginPackageIssueSeverity
{
    Warning = 0,
    Error = 1,
}

/// <summary>A deterministic package inspection finding.</summary>
public sealed record PluginPackageIssue(PluginPackageIssueSeverity Severity, string Code, string Path, string Message);

/// <summary>The aggregate result of scanning a plugin package directory before loading it.</summary>
public sealed record PluginPackageInspection(bool IsValid, IReadOnlyList<PluginPackageIssue> Issues)
{
    public IReadOnlyList<PluginPackageIssue> Errors =>
        Issues.Where(static issue => issue.Severity == PluginPackageIssueSeverity.Error).ToArray();
}

/// <summary>Bounded scanning limits; a hostile package must not be able to stall load.</summary>
public sealed class PluginPackageInspectorOptions
{
    public static PluginPackageInspectorOptions Default { get; } = new();

    public int MaxFiles { get; init; } = 5_000;

    public long MaxDepsJsonBytes { get; init; } = 1 * 1024 * 1024;

    public long MaxAssemblyBytes { get; init; } = 64 * 1024 * 1024;
}

/// <summary>
/// Pre-load package security scan: zip-slip-safe path resolution for the manifest entry, forbidden assembly
/// detection (WPF/Avalonia/WebView2/App/host), bundled shared-SDK rejection, and metadata scanning of every
/// shipped DLL (assembly definition name and <c>AssemblyReferences</c>). This is defence in depth, not a
/// sandbox: in-process plugins are fully trusted code.
/// </summary>
public sealed class PluginPackageInspector
{
    /// <summary>Scans a package directory against its manifest.</summary>
    public static PluginPackageInspection Inspect(
        string packageDirectory,
        PluginManifest manifest,
        HostAssemblyCatalog hostAssemblies,
        PluginPackageInspectorOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(hostAssemblies);
        options ??= PluginPackageInspectorOptions.Default;

        var issues = new List<PluginPackageIssue>();

        if (manifest.Entry is not { } entry)
        {
            issues.Add(new PluginPackageIssue(PluginPackageIssueSeverity.Error, "package.entry.missing", "manifest.entry", "Manifest has no entry block."));
            return new PluginPackageInspection(false, issues);
        }

        if (!TryResolvePackagePath(packageDirectory, entry.Assembly, out var entryPath, out var pathError))
        {
            issues.Add(new PluginPackageIssue(PluginPackageIssueSeverity.Error, "package.entry.outside", "manifest.entry.assembly", pathError!));
            return new PluginPackageInspection(false, issues);
        }

        if (!File.Exists(entryPath))
        {
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Error,
                "package.entry.notFound",
                "manifest.entry.assembly",
                $"Entry assembly '{entry.Assembly}' is missing from the package."));
            return new PluginPackageInspection(false, issues);
        }

        if (IsReparsePoint(packageDirectory))
        {
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Error,
                "package.reparsePoint",
                "$",
                "Plugin package directories must not be symlinks/reparse points."));
        }

        ScanFiles(packageDirectory, options, issues);
        ScanEntryIsManaged(entryPath, options, issues);
        ScanDepsJson(entryPath, options, issues);

        return new PluginPackageInspection(issues.All(static issue => issue.Severity != PluginPackageIssueSeverity.Error), issues);
    }

    /// <summary>
    /// Resolves a package-relative path and rejects absolute paths, rooted paths and <c>..</c> escapes.
    /// Used by the loader and mirrors the marketplace extraction rules (zip-slip defence).
    /// </summary>
    public static bool TryResolvePackagePath(string packageDirectory, string relativePath, out string fullPath, out string? error)
    {
        fullPath = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(packageDirectory))
        {
            error = "Package directory is empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            error = "Package-relative path is empty.";
            return false;
        }

        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':', StringComparison.Ordinal) || relativePath.Contains('\\', StringComparison.Ordinal))
        {
            error = $"Path '{relativePath}' must be a relative, forward-slash path.";
            return false;
        }

        if (relativePath.Split('/').Any(static segment => segment is ".." or "."))
        {
            error = $"Path '{relativePath}' contains a traversal segment.";
            return false;
        }

        var root = Path.GetFullPath(packageDirectory);
        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Path '{relativePath}' escapes the package directory.";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void ScanFiles(string packageDirectory, PluginPackageInspectorOptions options, List<PluginPackageIssue> issues)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories))
        {
            count++;
            if (count > options.MaxFiles)
            {
                issues.Add(new PluginPackageIssue(
                    PluginPackageIssueSeverity.Error,
                    "package.files.tooMany",
                    "$",
                    $"Package contains more than {options.MaxFiles} files."));
                return;
            }

            if (IsReparsePoint(file))
            {
                var reparseRelative = Path.GetRelativePath(packageDirectory, file).Replace('\\', '/');
                issues.Add(new PluginPackageIssue(
                    PluginPackageIssueSeverity.Error,
                    "package.reparsePoint",
                    reparseRelative,
                    "Files inside a plugin package must not be symlinks/reparse points."));
                continue;
            }

            if (!string.Equals(Path.GetExtension(file), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(packageDirectory, file).Replace('\\', '/');
            var stem = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(stem, "FluidBar.Sdk", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new PluginPackageIssue(
                    PluginPackageIssueSeverity.Error,
                    "package.bundlesSharedSdk",
                    relative,
                    "Packages must not bundle FluidBar.Sdk.dll; the SDK is provided by the host (single type identity)."));
                continue;
            }

            if (HostAssemblyCatalog.IsForbidden(stem))
            {
                issues.Add(new PluginPackageIssue(
                    PluginPackageIssueSeverity.Error,
                    "package.forbiddenAssembly",
                    relative,
                    $"Assembly file '{stem}' is forbidden in plugin packages (UI framework or FluidBar host assembly)."));
                continue;
            }

            InspectManagedAssembly(file, relative, options, issues);
        }
    }

    private static void InspectManagedAssembly(string path, string relative, PluginPackageInspectorOptions options, List<PluginPackageIssue> issues)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > options.MaxAssemblyBytes)
            {
                issues.Add(new PluginPackageIssue(
                    PluginPackageIssueSeverity.Error,
                    "package.assembly.tooLarge",
                    relative,
                    $"Assembly is {info.Length} bytes; the inspection limit is {options.MaxAssemblyBytes}."));
                return;
            }

            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                // Native library: metadata scanning cannot prove anything; filename policy above is all we have.
                return;
            }

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
            {
                return;
            }

            var definitionName = reader.GetString(reader.GetAssemblyDefinition().Name);
            if (string.Equals(definitionName, "FluidBar.Sdk", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new PluginPackageIssue(
                    PluginPackageIssueSeverity.Error,
                    "package.bundlesSharedSdk",
                    relative,
                    "Managed assembly definition 'FluidBar.Sdk' must not be shipped in a package; the host owns the SDK identity."));
            }
            else if (HostAssemblyCatalog.IsForbidden(definitionName))
            {
                issues.Add(new PluginPackageIssue(
                    PluginPackageIssueSeverity.Error,
                    "package.assembly.forbiddenDefinition",
                    relative,
                    $"Assembly definition name '{definitionName}' is forbidden."));
            }

            // FluidBar.Sdk references are expected (it is the plugin contract); forbidden names are not.
            foreach (var referenceHandle in reader.AssemblyReferences)
            {
                var reference = reader.GetAssemblyReference(referenceHandle);
                var name = reader.GetString(reference.Name);
                if (HostAssemblyCatalog.IsForbidden(name))
                {
                    issues.Add(new PluginPackageIssue(
                        PluginPackageIssueSeverity.Error,
                        "package.assembly.forbiddenReference",
                        relative,
                        $"Assembly references forbidden assembly '{name}'."));
                }
            }
        }
        catch (BadImageFormatException exception)
        {
            // Fail closed: a managed-looking DLL that cannot be parsed must not load.
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Error,
                "package.scan.failed",
                relative,
                $"Assembly could not be parsed: {exception.Message}"));
        }
        catch (IOException exception)
        {
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Error,
                "package.scan.failed",
                relative,
                $"Assembly could not be read: {exception.Message}"));
        }
        catch (UnauthorizedAccessException exception)
        {
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Error,
                "package.scan.failed",
                relative,
                $"Assembly could not be read: {exception.Message}"));
        }
    }

    private static void ScanEntryIsManaged(string entryPath, PluginPackageInspectorOptions options, List<PluginPackageIssue> issues)
    {
        try
        {
            var info = new FileInfo(entryPath);
            if (info.Length > options.MaxAssemblyBytes)
            {
                issues.Add(new PluginPackageIssue(
                    PluginPackageIssueSeverity.Error,
                    "package.entry.tooLarge",
                    "manifest.entry.assembly",
                    $"Entry assembly is {info.Length} bytes; the inspection limit is {options.MaxAssemblyBytes}."));
                return;
            }

            using var stream = File.OpenRead(entryPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata || !peReader.GetMetadataReader().IsAssembly)
            {
                issues.Add(new PluginPackageIssue(
                    PluginPackageIssueSeverity.Error,
                    "package.entry.notManaged",
                    "manifest.entry.assembly",
                    "The entry assembly is not a managed .NET assembly."));
            }
        }
        catch (BadImageFormatException exception)
        {
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Error,
                "package.entry.badImage",
                "manifest.entry.assembly",
                $"Entry assembly is not a valid managed assembly: {exception.Message}"));
        }
        catch (IOException exception)
        {
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Error,
                "package.entry.unreadable",
                "manifest.entry.assembly",
                $"Entry assembly could not be read: {exception.Message}"));
        }
    }

    private static void ScanDepsJson(string entryPath, PluginPackageInspectorOptions options, List<PluginPackageIssue> issues)
    {
        var depsPath = Path.ChangeExtension(entryPath, ".deps.json");
        if (!File.Exists(depsPath))
        {
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Warning,
                "package.depsJson.missing",
                Path.GetFileName(depsPath),
                "No .deps.json next to the entry assembly; NuGet/RID runtime assets will not resolve. Set EnableDynamicLoading=true."));
            return;
        }

        var info = new FileInfo(depsPath);
        if (info.Length > options.MaxDepsJsonBytes)
        {
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Error,
                "package.depsJson.tooLarge",
                Path.GetFileName(depsPath),
                $"{Path.GetFileName(depsPath)} exceeds {options.MaxDepsJsonBytes} bytes."));
            return;
        }

        try
        {
            using var stream = File.OpenRead(depsPath);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = document.RootElement;
            var relative = Path.GetRelativePath(Path.GetDirectoryName(entryPath)!, depsPath).Replace('\\', '/');
            foreach (var sectionName in new[] { "targets", "libraries" })
            {
                if (!root.TryGetProperty(sectionName, out var section) || section.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (sectionName == "targets")
                {
                    foreach (var target in section.EnumerateObject())
                    {
                        if (target.Value.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        foreach (var library in target.Value.EnumerateObject())
                        {
                            InspectLibraryKey(library.Name, relative, issues);
                        }
                    }
                }
                else
                {
                    foreach (var library in section.EnumerateObject())
                    {
                        InspectLibraryKey(library.Name, relative, issues);
                    }
                }
            }
        }
        catch (JsonException exception)
        {
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Error,
                "package.depsJson.invalid",
                Path.GetFileName(depsPath),
                $"Deps file is not valid JSON: {exception.Message}"));
        }
        catch (IOException exception)
        {
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Error,
                "package.depsJson.unreadable",
                Path.GetFileName(depsPath),
                $"Deps file could not be read: {exception.Message}"));
        }
    }

    private static void InspectLibraryKey(string libraryKey, string relative, List<PluginPackageIssue> issues)
    {
        var separator = libraryKey.IndexOf('/');
        var name = separator < 0 ? libraryKey : libraryKey[..separator];
        if (string.Equals(name, "FluidBar.Sdk", StringComparison.OrdinalIgnoreCase))
        {
            // Listing the shared SDK as a dependency is expected (Private=false still writes it to deps.json);
            // only shipping FluidBar.Sdk.dll is forbidden. Surface it for the diagnostics log.
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Warning,
                "package.depsJson.sharedSdk",
                relative,
                "The deps file lists FluidBar.Sdk; it is shared from the host and must not be shipped in the package."));
            return;
        }

        if (HostAssemblyCatalog.IsForbidden(name))
        {
            issues.Add(new PluginPackageIssue(
                PluginPackageIssueSeverity.Error,
                "package.depsJson.forbiddenAssembly",
                relative,
                $"The deps file references forbidden assembly '{name}'."));
        }
    }
}
