using FluidBar.Sdk.Versioning;

namespace FluidBar.Marketplace.Community;

/// <summary>
/// Owns the on-disk layout of the local marketplace:
/// <code>
/// %APPDATA%\FluidBar\marketplace\index.json                          listings + submissions (single review queue document)
/// %APPDATA%\FluidBar\marketplace\submissions\&lt;id&gt;-&lt;version&gt;.json     per-submission audit copy
/// %APPDATA%\FluidBar\marketplace\packages\&lt;id&gt;-&lt;version&gt;.fbp         reviewed package bytes
/// %APPDATA%\FluidBar\marketplace\packages\&lt;id&gt;-&lt;version&gt;.sig.json    optional detached signature copy
/// </code>
/// Every write is atomic (temp file + replace) so a crash never leaves a half-written index. The root is
/// overridable through <c>FLUIDBAR_MARKETPLACE_DIR</c> (tests, portable installs); it is never hard-coded.
/// </summary>
public sealed class CommunityMarketplaceStore
{
    /// <summary>Environment variable that overrides the marketplace root (tests, portable installs).</summary>
    public const string RootOverrideVariable = "FLUIDBAR_MARKETPLACE_DIR";

    /// <summary>Index document file name.</summary>
    public const string IndexFileName = "index.json";

    /// <summary>Per-submission audit directory name.</summary>
    public const string SubmissionsDirectoryName = "submissions";

    /// <summary>Reviewed package directory name.</summary>
    public const string PackagesDirectoryName = "packages";

    private readonly object _gate = new();

    /// <summary>Creates a store over <paramref name="rootDirectory"/>.</summary>
    public CommunityMarketplaceStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
        IndexPath = Path.Combine(RootDirectory, IndexFileName);
        SubmissionsDirectory = Path.Combine(RootDirectory, SubmissionsDirectoryName);
        PackagesDirectory = Path.Combine(RootDirectory, PackagesDirectoryName);
    }

    /// <summary>Default root: <c>%APPDATA%\FluidBar\marketplace</c>.</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar", "marketplace");

    /// <summary>Resolves the marketplace root, honoring <see cref="RootOverrideVariable"/> when fully qualified.</summary>
    public static string ResolveRoot()
    {
        var configured = Environment.GetEnvironmentVariable(RootOverrideVariable);
        return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? Path.GetFullPath(configured)
            : DefaultRoot;
    }

    /// <summary>Marketplace root directory.</summary>
    public string RootDirectory { get; }

    /// <summary><c>index.json</c> path.</summary>
    public string IndexPath { get; }

    /// <summary><c>submissions/</c> path.</summary>
    public string SubmissionsDirectory { get; }

    /// <summary><c>packages/</c> path.</summary>
    public string PackagesDirectory { get; }

    /// <summary>Creates the root and subdirectories (idempotent).</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(SubmissionsDirectory);
        Directory.CreateDirectory(PackagesDirectory);
    }

    /// <summary>Reads <c>index.json</c>; a missing file yields an empty document. Corrupt JSON throws.</summary>
    public LocalMarketplaceIndex Load()
    {
        lock (_gate)
        {
            if (!File.Exists(IndexPath))
            {
                return LocalMarketplaceIndex.Empty;
            }

            string json;
            try
            {
                json = File.ReadAllText(IndexPath);
            }
            catch (IOException exception)
            {
                throw new CommunityMarketplaceException($"The local marketplace index '{IndexPath}' could not be read: {exception.Message}", exception);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new CommunityMarketplaceException($"The local marketplace index '{IndexPath}' is empty.");
            }

            var document = CommunityJson.TryDeserialize<LocalMarketplaceIndex>(json);
            if (document is null)
            {
                throw new CommunityMarketplaceException($"The local marketplace index '{IndexPath}' is not valid JSON for schema version 1.");
            }

            if (document.SchemaVersion != 1 || !string.Equals(document.Kind, LocalMarketplaceIndex.KindValue, StringComparison.Ordinal))
            {
                throw new CommunityMarketplaceException(
                    $"The local marketplace index '{IndexPath}' has an unsupported schemaVersion/kind ({document.SchemaVersion}/{document.Kind}).");
            }

            return document;
        }
    }

    /// <summary>Atomically writes <c>index.json</c> plus the per-submission audit copies.</summary>
    public void Save(LocalMarketplaceIndex document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate)
        {
            EnsureCreated();
            var persisted = document with { GeneratedAt = document.GeneratedAt == default ? DateTimeOffset.UtcNow : document.GeneratedAt };
            WriteAtomic(IndexPath, CommunityJson.Serialize(persisted));

            foreach (var submission in persisted.Submissions)
            {
                WriteAtomic(Path.Combine(SubmissionsDirectory, SubmissionFileName(submission.PluginId, submission.Version)), CommunityJson.Serialize(submission));
            }
        }
    }

    /// <summary>Stores a reviewed package copy and returns the stored file name.</summary>
    public string SavePackage(string pluginId, string version, ReadOnlySpan<byte> packageBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        lock (_gate)
        {
            EnsureCreated();
            var fileName = PackageFileName(pluginId, version);
            WriteAtomicBytes(Path.Combine(PackagesDirectory, fileName), packageBytes);
            return fileName;
        }
    }

    /// <summary>Stores the detached signature next to the package copy and returns its file name.</summary>
    public string SaveSignature(string pluginId, string version, string signatureJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(signatureJson);
        lock (_gate)
        {
            EnsureCreated();
            var fileName = SignatureFileName(pluginId, version);
            WriteAtomic(Path.Combine(PackagesDirectory, fileName), signatureJson);
            return fileName;
        }
    }

    /// <summary>Absolute path of a stored package copy.</summary>
    public string ResolvePackagePath(string packageFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFileName);
        return Path.Combine(PackagesDirectory, packageFileName);
    }

    /// <summary>Absolute path of a stored signature copy, or <see langword="null"/> when there is none.</summary>
    public string? ResolveSignaturePath(string? signatureFileName) =>
        string.IsNullOrWhiteSpace(signatureFileName) ? null : Path.Combine(PackagesDirectory, signatureFileName);

    /// <summary>Stable package file name for a plugin version.</summary>
    public static string PackageFileName(string pluginId, string version) => $"{SafeSegment(pluginId)}-{SafeSegment(version)}.fbp";

    /// <summary>Stable signature file name for a plugin version.</summary>
    public static string SignatureFileName(string pluginId, string version) => $"{SafeSegment(pluginId)}-{SafeSegment(version)}.sig.json";

    /// <summary>Stable per-submission audit file name.</summary>
    public static string SubmissionFileName(string pluginId, string version) => $"{SafeSegment(pluginId)}-{SafeSegment(version)}.json";

    private static string SafeSegment(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            buffer[index] = Path.GetInvalidFileNameChars().Contains(character) || character == '@'
                ? '_'
                : character;
        }

        return new string(buffer);
    }

    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void WriteAtomicBytes(string path, ReadOnlySpan<byte> content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

/// <summary>Locations of the community plugin lane (mirrors the app's plugin root contract).</summary>
public static class CommunityPluginPaths
{
    /// <summary>Environment variable that overrides the plugins root (tests, portable installs).</summary>
    public const string PluginsRootOverrideVariable = "FLUIDBAR_PLUGINS_DIR";

    /// <summary>Default plugins root: <c>%APPDATA%\FluidBar\plugins</c>.</summary>
    public static string DefaultPluginsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar", "plugins");

    /// <summary>Resolves the plugins root, honoring <see cref="PluginsRootOverrideVariable"/> when fully qualified.</summary>
    public static string ResolvePluginsRoot()
    {
        var configured = Environment.GetEnvironmentVariable(PluginsRootOverrideVariable);
        return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? Path.GetFullPath(configured)
            : DefaultPluginsRoot;
    }
}

/// <summary>Semantic-version ordering helper shared by review and browse paths.</summary>
internal static class CommunityVersionOrder
{
    internal static int Compare(string left, string right)
    {
        var leftParsed = SemanticVersion.TryParse(left, out var leftVersion);
        var rightParsed = SemanticVersion.TryParse(right, out var rightVersion);
        if (leftParsed && rightParsed)
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return leftParsed ? 1 : rightParsed ? -1 : string.CompareOrdinal(left, right);
    }
}
