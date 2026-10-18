using System.IO.Compression;
using System.Security.Cryptography;
using FluidBar.Sdk.Json;
using FluidBar.Sdk.Localization;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Marketplace.Packaging;

/// <summary>
/// Deterministic <c>.fbp</c> packager: sorted entries, fixed timestamps, no extra ZIP fields, generated
/// <c>checksums.json</c>. Two builds of the same input must produce the same SHA-256 (verified by tests/CI).
/// </summary>
public static class FbpPackager
{
    /// <summary>Packs a directory that already has the <c>.fbp</c> layout (manifest.json, plugin/, locales/, ...).</summary>
    public static FbpPackageBuildResult PackDirectory(string contentDirectory, string outputPath, FbpPackagerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        options ??= FbpPackagerOptions.Default;

        var root = Path.GetFullPath(contentDirectory);
        if (!Directory.Exists(root))
        {
            throw new PackageFormatException($"Package content directory '{root}' does not exist.");
        }

        var files = EnumeratePackageFiles(root);
        return PackFiles(root, files, outputPath, options);
    }

    /// <summary>Packs an explicit file list; source paths are absolute and entry paths are package-relative.</summary>
    public static FbpPackageBuildResult PackFiles(
        string rootDirectory,
        IReadOnlyList<FbpPackageFile> files,
        string outputPath,
        FbpPackagerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        options ??= FbpPackagerOptions.Default;
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (files.Count == 0)
        {
            throw new PackageFormatException("A package must contain at least manifest.json and the entry assembly.");
        }

        if (files.Count > options.MaxEntries)
        {
            throw new PackageFormatException($"Package has {files.Count} entries; the limit is {options.MaxEntries}.");
        }

        var manifestFile = files.FirstOrDefault(static file =>
            string.Equals(file.EntryPath, FbpPackageLayout.ManifestFileName, StringComparison.Ordinal));
        if (manifestFile is null)
        {
            throw new PackageFormatException("manifest.json is required at the package root.");
        }

        var manifestJson = File.ReadAllText(manifestFile.SourcePath);
        if (!PluginManifestParser.TryParse(manifestJson, out var manifest, out var parseErrors) || manifest is null)
        {
            throw new PackageFormatException($"Manifest cannot be parsed: {string.Join("; ", parseErrors)}");
        }

        if (options.ValidateMarketplaceLayout)
        {
            ValidateMarketplaceLayout(rootDirectory, files, manifest);
        }

        var checksums = options.GenerateChecksums
            ? FbpChecksums.Compute(rootDirectory, files.Select(static file => file.EntryPath))
            : null;

        // Validate entry paths and duplicate detection before writing anything.
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long uncompressed = 0;
        foreach (var file in files)
        {
            var entryPath = NormalizeEntryPath(file.EntryPath);
            if (!normalized.TryAdd(entryPath, file.SourcePath))
            {
                throw new PackageFormatException($"Duplicate package entry '{entryPath}'.");
            }

            var length = new FileInfo(file.SourcePath).Length;
            uncompressed += length;
            if (uncompressed > options.MaxUncompressedBytes)
            {
                throw new PackageFormatException($"Package exceeds the {options.MaxUncompressedBytes} byte uncompressed limit.");
            }
        }

        if (checksums is not null && !normalized.ContainsKey(FbpPackageLayout.ChecksumsFileName))
        {
            normalized[FbpPackageLayout.ChecksumsFileName] = string.Empty; // generated in-memory below
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = fullOutputPath + ".tmp";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: null))
            {
                foreach (var pair in normalized.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    var zipEntry = archive.CreateEntry(pair.Key, CompressionLevel.Optimal);
                    zipEntry.LastWriteTime = options.FixedTimestamp;
                    using var entryStream = zipEntry.Open();
                    if (string.Equals(pair.Key, FbpPackageLayout.ChecksumsFileName, StringComparison.Ordinal) && checksums is not null)
                    {
                        var checksumBytes = System.Text.Encoding.UTF8.GetBytes(checksums.ToCanonicalJson());
                        entryStream.Write(checksumBytes);
                    }
                    else
                    {
                        using var source = new FileStream(pair.Value, FileMode.Open, FileAccess.Read, FileShare.Read);
                        source.CopyTo(entryStream);
                    }
                }
            }

            var temporaryInfo = new FileInfo(temporaryPath);
            if (temporaryInfo.Length > options.MaxCompressedBytes)
            {
                throw new PackageFormatException($"Package is {temporaryInfo.Length} bytes; the limit is {options.MaxCompressedBytes}.");
            }

            File.Move(temporaryPath, fullOutputPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }

        var packageInfo = new FileInfo(fullOutputPath);
        var sha256 = ComputeSha256(fullOutputPath);
        var manifestSha256 = Sha256File(manifestFile.SourcePath);
        var entryAssembly = manifest.Entry is { Assembly.Length: > 0 } entry
            ? files.FirstOrDefault(file => string.Equals(NormalizeEntryPath(file.EntryPath), entry.Assembly, StringComparison.OrdinalIgnoreCase))
            : null;
        var entrySha256 = entryAssembly is null ? string.Empty : Sha256File(entryAssembly.SourcePath);

        return new FbpPackageBuildResult(
            fullOutputPath,
            sha256,
            packageInfo.Length,
            [.. normalized.Keys.OrderBy(static path => path, StringComparer.Ordinal)],
            manifestSha256,
            entrySha256);
    }

    /// <summary>Computes the package SHA-256 (the value the detached signature covers).</summary>
    public static string ComputeSha256(string packagePath) => Sha256File(packagePath);

    /// <summary>Streams the package SHA-256 without loading it into memory.</summary>
    public static async Task<string> ComputeSha256Async(string packagePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static List<FbpPackageFile> EnumeratePackageFiles(string root)
    {
        var files = new List<FbpPackageFile>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (string.Equals(relative, FbpPackageLayout.ChecksumsFileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relative, FbpPackageLayout.SignatureFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new PackageFormatException(
                    $"'{relative}' must not be committed inside the package content; checksums are generated and signatures are detached.");
            }

            files.Add(new FbpPackageFile(relative, path));
        }

        return files;
    }

    private static void ValidateMarketplaceLayout(string root, IReadOnlyList<FbpPackageFile> files, PluginManifest manifest)
    {
        var validation = PluginManifestValidator.Validate(
            manifest,
            new ManifestValidationOptions { RequirePublisherKeyId = true });
        if (!validation.IsValid)
        {
            throw new PackageFormatException(
                $"Manifest is not valid for a marketplace package: {string.Join("; ", validation.Errors)}");
        }

        var byPath = files.ToDictionary(static file => NormalizeEntryPath(file.EntryPath), static file => file.SourcePath, StringComparer.OrdinalIgnoreCase);
        if (manifest.Entry is not { } entry || !byPath.ContainsKey(entry.Assembly))
        {
            throw new PackageFormatException($"Entry assembly '{manifest.Entry?.Assembly}' is not part of the package.");
        }

        if (!byPath.ContainsKey(FbpPackageLayout.LicenseFileName))
        {
            throw new PackageFormatException("LICENSE is required for marketplace packages.");
        }

        if (!byPath.ContainsKey(FbpPackageLayout.ReadmeFileName))
        {
            throw new PackageFormatException("README.md is required for marketplace packages.");
        }

        var locales = manifest.Locales ?? throw new PackageFormatException("Manifest locales block is required.");
        var localeDirectory = string.IsNullOrWhiteSpace(locales.Path) ? "locales" : locales.Path.TrimEnd('/');
        var bundles = new List<LocaleBundle>();
        foreach (var locale in locales.Available ?? [])
        {
            var localePath = $"{localeDirectory}/{locale}.json";
            if (!byPath.TryGetValue(localePath, out var sourcePath))
            {
                if (string.Equals(locale, locales.Default, StringComparison.OrdinalIgnoreCase))
                {
                    throw new PackageFormatException($"Required default locale bundle '{localePath}' is missing.");
                }

                continue;
            }

            if (!LocaleBundleLoader.TryParse(File.ReadAllText(sourcePath), out var bundle, out var errors) || bundle is null)
            {
                throw new PackageFormatException($"Locale bundle '{localePath}' is invalid: {string.Join("; ", errors)}");
            }

            bundles.Add(bundle);
        }

        var defaultBundle = bundles.FirstOrDefault(bundle =>
            string.Equals(bundle.Locale, locales.Default, StringComparison.OrdinalIgnoreCase));
        if (defaultBundle is null)
        {
            throw new PackageFormatException($"Default locale '{locales.Default}' has no valid bundle.");
        }

        foreach (var bundle in bundles)
        {
            var mismatches = LocaleBundleConsistency.FindPlaceholderMismatches(defaultBundle, bundle);
            if (mismatches.Count > 0)
            {
                throw new PackageFormatException($"Locale bundle consistency failed: {string.Join("; ", mismatches)}");
            }
        }
    }

    private static string NormalizeEntryPath(string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath) || entryPath.Length > FbpPackageLimits.MaxPathLength)
        {
            throw new PackageFormatException($"Package entry path '{entryPath}' is empty or too long.");
        }

        var normalized = entryPath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Split('/').Any(static segment => segment is ".." or "." or ""))
        {
            throw new PackageFormatException($"Package entry path '{entryPath}' is not a safe relative path.");
        }

        return normalized;
    }

    private static string Sha256File(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temporary artifact; the original error is what matters.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup; the original error is what matters.
        }
    }
}

/// <summary>
/// Helper that stages a plugin build output into the <c>.fbp</c> layout and packs it. Used by the contributor
/// CLI, CI and tests so local and CI packaging are byte-identical.
/// </summary>
public sealed class FbpStagingBuilder : IDisposable
{
    private readonly string _stagingDirectory;
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    private FbpStagingBuilder(string stagingDirectory) => _stagingDirectory = stagingDirectory;

    public string StagingDirectory => _stagingDirectory;

    /// <summary>Creates an empty staging directory under the temp path.</summary>
    public static FbpStagingBuilder Create(string? baseDirectory = null)
    {
        var root = baseDirectory ?? Path.Combine(Path.GetTempPath(), "fluidbar-fbp-staging");
        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        return new FbpStagingBuilder(staging);
    }

    /// <summary>
    /// Stages a plugin project output directory (manifest.json, locales/, LICENSE/README copied to output by the
    /// plugin csproj) into package layout: entry dll/deps/deps of private deps go to <c>plugin/</c>.
    /// </summary>
    public static FbpStagingBuilder FromPluginOutput(string pluginOutputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginOutputDirectory);
        var output = Path.GetFullPath(pluginOutputDirectory);
        var manifestPath = Path.Combine(output, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new PackageFormatException(
                $"'{manifestPath}' is missing. Plugin projects must copy manifest.json (CopyToOutputDirectory) for packaging.");
        }

        var manifest = PluginManifestParser.Parse(File.ReadAllText(manifestPath));
        var builder = Create();
        try
        {
            builder.AddFile(FbpPackageLayout.ManifestFileName, manifestPath);

            var entryAssemblyName = manifest.Entry?.Assembly;
            if (string.IsNullOrWhiteSpace(entryAssemblyName))
            {
                throw new PackageFormatException("Manifest has no entry assembly.");
            }

            var entryFileName = Path.GetFileName(entryAssemblyName);
            var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dll in Directory.EnumerateFiles(output, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(dll);
                if (string.Equals(Path.GetFileNameWithoutExtension(dll), "FluidBar.Sdk", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // shared from the host: never shipped
                }

                builder.AddFile(FbpPackageLayout.PluginDirectory + fileName, dll);
                copied.Add(fileName);
            }

            if (!copied.Contains(entryFileName))
            {
                throw new PackageFormatException($"Entry assembly '{entryFileName}' was not found in '{output}'.");
            }

            foreach (var depsJson in Directory.EnumerateFiles(output, "*.deps.json", SearchOption.TopDirectoryOnly))
            {
                builder.AddFile(FbpPackageLayout.PluginDirectory + Path.GetFileName(depsJson), depsJson);
            }

            var localesDirectory = Path.Combine(output, "locales");
            if (Directory.Exists(localesDirectory))
            {
                foreach (var localeFile in Directory.EnumerateFiles(localesDirectory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    builder.AddFile(FbpPackageLayout.LocalesDirectory + Path.GetFileName(localeFile), localeFile);
                }
            }

            var assetsDirectory = Path.Combine(output, "assets");
            if (Directory.Exists(assetsDirectory))
            {
                foreach (var asset in Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(output, asset).Replace('\\', '/');
                    builder.AddFile(relative, asset);
                }
            }

            foreach (var name in new[] { FbpPackageLayout.LicenseFileName, FbpPackageLayout.ReadmeFileName })
            {
                var path = Path.Combine(output, name);
                if (File.Exists(path))
                {
                    builder.AddFile(name, path);
                }
            }

            return builder;
        }
        catch
        {
            builder.Dispose();
            throw;
        }
    }

    /// <summary>Adds a file at a package-relative entry path.</summary>
    public FbpStagingBuilder AddFile(string entryPath, string sourcePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!File.Exists(sourcePath))
        {
            throw new PackageFormatException($"Source file '{sourcePath}' for '{entryPath}' does not exist.");
        }

        var relative = entryPath.Replace('\\', '/');
        var destination = Path.Combine(_stagingDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(sourcePath, destination, overwrite: true);
        _files[relative] = destination;
        return this;
    }

    /// <summary>Adds the manifest JSON directly.</summary>
    public FbpStagingBuilder AddManifestJson(string json)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var destination = Path.Combine(_stagingDirectory, FbpPackageLayout.ManifestFileName);
        File.WriteAllText(destination, json);
        _files[FbpPackageLayout.ManifestFileName] = destination;
        return this;
    }

    /// <summary>Builds the deterministic package.</summary>
    public FbpPackageBuildResult Build(string outputPath, FbpPackagerOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var files = _files.Select(static pair => new FbpPackageFile(pair.Key, pair.Value)).ToArray();
        return FbpPackager.PackFiles(_stagingDirectory, files, outputPath, options);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (Directory.Exists(_stagingDirectory))
            {
                Directory.Delete(_stagingDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp staging directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of a temp staging directory.
        }
    }
}
