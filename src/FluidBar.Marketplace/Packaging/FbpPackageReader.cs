using System.IO.Compression;
using System.Security.Cryptography;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Marketplace.Packaging;

/// <summary>Limits applied when reading/extracting an untrusted package.</summary>
public sealed class FbpPackageReaderOptions
{
    public static FbpPackageReaderOptions Default { get; } = new();

    public long MaxCompressedBytes { get; init; } = FbpPackageLimits.MaxCompressedBytes;

    public long MaxUncompressedBytes { get; init; } = FbpPackageLimits.MaxUncompressedBytes;

    public int MaxEntries { get; init; } = FbpPackageLimits.MaxEntries;

    public int MaxPathLength { get; init; } = FbpPackageLimits.MaxPathLength;

    public bool RequireChecksums { get; init; } = true;

    /// <summary>When false, extraction fails instead of replacing an existing destination directory.</summary>
    public bool OverwriteDestination { get; init; }
}

/// <summary>Structural inspection of a package without extracting it.</summary>
public sealed record FbpPackageInspection(
    PluginManifest Manifest,
    IReadOnlyList<string> EntryPaths,
    long UncompressedSize,
    string? ChecksumsJson);

/// <summary>Result of a verified extraction.</summary>
public sealed record FbpExtractionResult(
    string DestinationDirectory,
    PluginManifest Manifest,
    string Sha256,
    long PackageSize,
    IReadOnlyList<string> Files);

/// <summary>
/// Reads + validates <c>.fbp</c> ZIP packages: zip-slip/symlink/duplicate/encrypted entry rejection, size and
/// entry-count limits, manifest + checksums verification, and atomic extraction (temp directory + directory
/// move so an interrupted install never leaves a half-written version directory).
/// </summary>
public static class FbpPackageReader
{
    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
    };

    /// <summary>Inspects package bytes; throws <see cref="PackageFormatException"/> when invalid.</summary>
    public static FbpPackageInspection Inspect(byte[] packageBytes, FbpPackageReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(packageBytes);
        options ??= FbpPackageReaderOptions.Default;
        if (packageBytes.LongLength > options.MaxCompressedBytes)
        {
            throw new PackageFormatException($"Package is {packageBytes.LongLength} bytes; the limit is {options.MaxCompressedBytes}.");
        }

        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = OpenArchive(stream);
        var entries = ValidateEntries(archive, options);
        var manifest = ReadManifest(archive, options);
        var checksumsJson = ReadChecksums(archive, options);
        ValidateChecksumCoverage(checksumsJson, entries, manifest, options);
        return new FbpPackageInspection(manifest, entries, archive.Entries.Sum(static entry => entry.Length), checksumsJson);
    }

    /// <summary>Extracts a package file into <paramref name="destinationDirectory"/> atomically.</summary>
    public static async Task<FbpExtractionResult> ExtractFileAsync(
        string packagePath,
        string destinationDirectory,
        FbpPackageReaderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        options ??= FbpPackageReaderOptions.Default;

        var packageInfo = new FileInfo(packagePath);
        if (!packageInfo.Exists)
        {
            throw new PackageFormatException($"Package '{packagePath}' does not exist.");
        }

        if (packageInfo.Length > options.MaxCompressedBytes)
        {
            throw new PackageFormatException($"Package is {packageInfo.Length} bytes; the limit is {options.MaxCompressedBytes}.");
        }

        var sha256 = await FbpPackager.ComputeSha256Async(packagePath, cancellationToken).ConfigureAwait(false);
        byte[] packageBytes = await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false);
        var inspection = Inspect(packageBytes, options);
        _ = inspection; // structure validated; extraction re-reads the archive

        var destination = Path.GetFullPath(destinationDirectory);
        if (Directory.Exists(destination))
        {
            if (!options.OverwriteDestination)
            {
                throw new PackageFormatException($"Destination '{destination}' already exists; refusing to overwrite an installed version.");
            }

            Directory.Delete(destination, recursive: true);
        }

        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(temporary);
            using (var stream = new MemoryStream(packageBytes, writable: false))
            using (var archive = OpenArchive(stream))
            {
                ExtractEntries(archive, temporary, options, cancellationToken);
            }

            VerifyExtractedChecksums(temporary, options);
            RejectReparsePoints(temporary);

            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            Directory.Move(temporary, destination);
            var files = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(destination, path).Replace('\\', '/'))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();

            return new FbpExtractionResult(destination, inspection.Manifest, sha256, packageInfo.Length, files);
        }
        catch
        {
            TryDeleteDirectory(temporary);
            throw;
        }
    }

    /// <summary>Calculates the package hash without extracting.</summary>
    public static Task<string> ComputeSha256Async(string packagePath, CancellationToken cancellationToken = default) =>
        FbpPackager.ComputeSha256Async(packagePath, cancellationToken);

    private static ZipArchive OpenArchive(Stream stream)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new PackageFormatException($"Package is not a valid ZIP archive: {exception.Message}", exception);
        }
    }

    private static List<string> ValidateEntries(ZipArchive archive, FbpPackageReaderOptions options)
    {
        if (archive.Entries.Count > options.MaxEntries)
        {
            throw new PackageFormatException($"Package has {archive.Entries.Count} entries; the limit is {options.MaxEntries}.");
        }

        var paths = new List<string>(archive.Entries.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue; // directory entry
            }

            var path = NormalizeEntryPath(entry.FullName, options.MaxPathLength);
            if (!seen.Add(path))
            {
                throw new PackageFormatException($"Duplicate package entry '{path}'.");
            }

            if (IsSymlink(entry))
            {
                throw new PackageFormatException($"Package entry '{path}' is a symlink; symlinks are not allowed.");
            }

            total += entry.Length;
            if (total > options.MaxUncompressedBytes)
            {
                throw new PackageFormatException($"Package exceeds the {options.MaxUncompressedBytes} byte uncompressed limit.");
            }

            paths.Add(path);
        }

        if (paths.Count == 0)
        {
            throw new PackageFormatException("Package contains no files.");
        }

        return paths;
    }

    private static PluginManifest ReadManifest(ZipArchive archive, FbpPackageReaderOptions options)
    {
        var entry = archive.GetEntry(FbpPackageLayout.ManifestFileName)
            ?? throw new PackageFormatException("manifest.json is missing from the package root.");
        var json = ReadEntryText(entry, PluginManifestLimits.MaxManifestBytes);
        if (!PluginManifestParser.TryParse(json, out var manifest, out var errors) || manifest is null)
        {
            throw new PackageFormatException($"Manifest cannot be parsed: {string.Join("; ", errors)}");
        }

        var validation = PluginManifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            throw new PackageFormatException($"Manifest validation failed: {string.Join("; ", validation.Errors)}");
        }

        return manifest;
    }

    private static string? ReadChecksums(ZipArchive archive, FbpPackageReaderOptions options)
    {
        var entry = archive.GetEntry(FbpPackageLayout.ChecksumsFileName);
        if (entry is null)
        {
            if (options.RequireChecksums)
            {
                throw new PackageFormatException("checksums.json is missing from the package root.");
            }

            return null;
        }

        var json = ReadEntryText(entry, FbpPackageLimits.MaxChecksumFileBytes);
        if (!FbpChecksums.TryParse(json, out _, out var errors))
        {
            throw new PackageFormatException($"checksums.json is invalid: {string.Join("; ", errors)}");
        }

        return json;
    }

    private static void ValidateChecksumCoverage(
        string? checksumsJson,
        IReadOnlyList<string> entries,
        PluginManifest manifest,
        FbpPackageReaderOptions options)
    {
        if (checksumsJson is null)
        {
            return;
        }

        var checksums = FbpChecksums.Parse(checksumsJson);
        foreach (var entry in entries)
        {
            if (string.Equals(entry, FbpPackageLayout.ChecksumsFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!checksums.Files.ContainsKey(entry))
            {
                throw new PackageFormatException($"checksums.json does not cover package entry '{entry}'.");
            }
        }

        foreach (var path in checksums.Files.Keys)
        {
            if (!entries.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                throw new PackageFormatException($"checksums.json lists '{path}' which is not in the package.");
            }
        }

        if (manifest.Entry is not { } entryPoint)
        {
            throw new PackageFormatException("Manifest has no entry block.");
        }

        if (!entries.Contains(entryPoint.Assembly, StringComparer.OrdinalIgnoreCase))
        {
            throw new PackageFormatException($"Entry assembly '{entryPoint.Assembly}' is not in the package.");
        }
    }

    private static void ExtractEntries(ZipArchive archive, string root, FbpPackageReaderOptions options, CancellationToken cancellationToken)
    {
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }

            var path = NormalizeEntryPath(entry.FullName, options.MaxPathLength);
            var destination = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new PackageFormatException($"Package entry '{path}' escapes the extraction directory.");
            }

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                using var source = entry.Open();
                using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(target);
            }
            catch (InvalidDataException exception)
            {
                throw new PackageFormatException($"Package entry '{path}' could not be extracted (maybe encrypted or corrupt): {exception.Message}", exception);
            }
        }
    }

    private static void VerifyExtractedChecksums(string root, FbpPackageReaderOptions options)
    {
        var checksumsPath = Path.Combine(root, FbpPackageLayout.ChecksumsFileName);
        if (!File.Exists(checksumsPath))
        {
            if (options.RequireChecksums)
            {
                throw new PackageFormatException("checksums.json was not extracted.");
            }

            return;
        }

        var checksums = FbpChecksums.Parse(File.ReadAllText(checksumsPath));
        var actualFiles = Directory.EnumerateFiles(root, "*", SafeEnumeration)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(static path => !string.Equals(path, FbpPackageLayout.ChecksumsFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in actualFiles)
        {
            if (!checksums.Files.TryGetValue(file, out var expected))
            {
                throw new PackageFormatException($"Extracted file '{file}' is not covered by checksums.json.");
            }

            var fullPath = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new PackageFormatException($"Hash mismatch for '{file}': expected {expected}, actual {actual}.");
            }
        }

        foreach (var declared in checksums.Files.Keys)
        {
            if (!actualFiles.Contains(declared, StringComparer.OrdinalIgnoreCase))
            {
                throw new PackageFormatException($"checksums.json declares '{declared}' which is missing after extraction.");
            }
        }
    }

    private static void RejectReparsePoints(string root)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SafeEnumeration))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new PackageFormatException($"Extracted entry '{Path.GetRelativePath(root, path)}' is a reparse point.");
            }
        }
    }

    private static string NormalizeEntryPath(string entryPath, int maxLength)
    {
        var normalized = entryPath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maxLength ||
            normalized.StartsWith('/') ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Split('/').Any(static segment => segment is ".." or "." or ""))
        {
            throw new PackageFormatException($"Package entry path '{entryPath}' is not a safe relative path.");
        }

        return normalized;
    }

    private static bool IsSymlink(ZipArchiveEntry entry) =>
        (entry.ExternalAttributes & 0xF000) == 0xA000 || // Unix symlink mode
        ((FileAttributes)(entry.ExternalAttributes & (int)0xFFFF) & FileAttributes.ReparsePoint) != 0;

    private static string ReadEntryText(ZipArchiveEntry entry, long maxBytes)
    {
        if (entry.Length > maxBytes)
        {
            throw new PackageFormatException($"Package entry '{entry.FullName}' is {entry.Length} bytes; the limit is {maxBytes}.");
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        if (text.Length > maxBytes)
        {
            throw new PackageFormatException($"Package entry '{entry.FullName}' exceeds the {maxBytes} character limit.");
        }

        return text;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; the original extraction error is what matters.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup; the original extraction error is what matters.
        }
    }
}
