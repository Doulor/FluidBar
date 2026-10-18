using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Json;

namespace FluidBar.Sdk.Tests;

internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path) => Path = path;

    public string Path { get; }

    public static TempDirectory Create(string prefix = "fluidbar-sdk-tests")
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public string Combine(params string[] segments) => System.IO.Path.Combine([Path, .. segments]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test temp cleanup is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Test temp cleanup is best effort.
        }
    }
}

/// <summary>Generated-at-test-time P-256 keys; production keys are injected from an HSM/KMS.</summary>
internal sealed class TestKeys : IDisposable
{
    public TestKeys()
    {
        MarketplaceKey = EcdsaKeyMaterial.GenerateP256Key();
        AuthorKey = EcdsaKeyMaterial.GenerateP256Key();
        Marketplace = TrustedKey.FromPublicKey(MarketplaceKey, "test-marketplace-2026", SignatureRole.Marketplace);
        Author = TrustedKey.FromPublicKey(AuthorKey, "test-publisher-2026", SignatureRole.Author);
        Trust = new MarketplaceTrustOptions
        {
            MarketplaceKeys = [Marketplace],
            PublisherKeys = [Author],
        };
    }

    public ECDsa MarketplaceKey { get; }

    public ECDsa AuthorKey { get; }

    public TrustedKey Marketplace { get; }

    public TrustedKey Author { get; }

    public MarketplaceTrustOptions Trust { get; }

    public void Dispose()
    {
        MarketplaceKey.Dispose();
        AuthorKey.Dispose();
    }
}

internal static class TestPaths
{
    /// <summary>Locates the repository root from the compiled test source path.</summary>
    public static string RepoRoot([CallerFilePath] string callerFilePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(callerFilePath)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluidBar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (FluidBar.sln) was not found.");
    }

    public static string SampleProjectOutput(string projectName)
    {
        var projectDirectory = Path.Combine(RepoRoot(), "plugins", "samples", projectName);
        var bin = Path.Combine(projectDirectory, "bin");
        if (!Directory.Exists(bin))
        {
            throw new DirectoryNotFoundException($"Build '{projectName}' first: '{bin}' does not exist.");
        }

        var candidates = Directory.EnumerateDirectories(bin, "net10.0", SearchOption.AllDirectories)
            .OrderByDescending(static directory => directory, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return candidates.FirstOrDefault()
            ?? throw new DirectoryNotFoundException($"No net10.0 output under '{bin}'. Build the sample project first.");
    }
}

/// <summary>Builds deterministic fake packages/manifests/indexes for marketplace and SDK tests.</summary>
internal static class TestPackageFactory
{
    public static string MinimalManifestJson(
        string id = "sample-minimal",
        string version = "1.0.0",
        string apiVersion = "2.0.0",
        string minAppVersion = "2.0.0",
        string entryAssembly = "plugin/Fake.Plugin.dll",
        string entryType = "Fake.Plugin.Plugin",
        string publisherKeyId = "test-publisher-2026",
        Action<JsonObject>? customize = null)
    {
        var manifest = new JsonObject
        {
            ["$schema"] = "https://fluidbar.app/schemas/plugin.schema.json",
            ["schemaVersion"] = 2,
            ["id"] = id,
            ["version"] = version,
            ["apiVersion"] = apiVersion,
            ["minAppVersion"] = minAppVersion,
            ["publisher"] = new JsonObject
            {
                ["id"] = "test-publisher",
                ["displayName"] = "Test Publisher",
                ["url"] = "https://example.com",
                ["keyId"] = publisherKeyId,
            },
            ["name"] = new JsonObject { ["en-US"] = "Test Plugin" },
            ["summary"] = new JsonObject { ["en-US"] = "A test plugin." },
            ["license"] = "MIT",
            ["categories"] = new JsonArray("developer"),
            ["entry"] = new JsonObject
            {
                ["runtime"] = "inproc",
                ["assembly"] = entryAssembly,
                ["type"] = entryType,
                ["targetFramework"] = "net10.0",
            },
            ["platforms"] = new JsonArray("win-x64"),
            ["locales"] = new JsonObject
            {
                ["default"] = "en-US",
                ["available"] = new JsonArray("en-US"),
                ["path"] = "locales",
                ["fallback"] = "en-US",
            },
            ["capabilities"] = new JsonObject
            {
                ["ui"] = new JsonObject { ["island"] = true, ["xaml"] = false },
            },
            ["activation"] = new JsonArray("startup"),
            ["contributes"] = new JsonObject
            {
                ["layouts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = id + ".island",
                        ["surface"] = "island.compact",
                        ["template"] = "text.v1",
                        ["slots"] = new JsonObject
                        {
                            ["title"] = new JsonObject { ["field"] = "title", ["style"] = "title" },
                        },
                        ["constraints"] = new JsonObject
                        {
                            ["minWidth"] = 132,
                            ["maxWidth"] = 300,
                            ["minHeight"] = 36,
                            ["maxHeight"] = 44,
                        },
                    },
                },
            },
        };

        customize?.Invoke(manifest);
        return manifest.ToJsonString(PluginJson.CreateStrictOptions());
    }

    public static string LocaleJson(string locale, params (string Key, string Value)[] messages)
    {
        var messagesObject = new JsonObject();
        foreach (var (key, value) in messages)
        {
            messagesObject[key] = value;
        }

        return new JsonObject
        {
            ["$schema"] = "https://fluidbar.app/schemas/locale.schema.json",
            ["locale"] = locale,
            ["version"] = 1,
            ["messages"] = messagesObject,
        }.ToJsonString(PluginJson.CreateStrictOptions());
    }

    /// <summary>Creates a valid signed `.fbp` on disk and returns the package path + detached signature.</summary>
    public static (string PackagePath, PackageSignatureDocument Signature) CreateSignedPackage(
        string directory,
        TestKeys keys,
        string id = "sample-minimal",
        string version = "1.0.0",
        string? manifestJson = null,
        bool includeAuthorSignature = false,
        string fileNamePrefix = "test")
    {
        Directory.CreateDirectory(directory);
        using var staging = FbpStagingBuilder.Create(directory);
        staging.AddManifestJson(manifestJson ?? MinimalManifestJson(id: id, version: version));
        var fakeDllPath = Path.Combine(directory, "Fake.Plugin.dll");
        File.WriteAllBytes(fakeDllPath, Encoding.UTF8.GetBytes("not a real assembly: packaging tests never load it"));
        staging.AddFile("plugin/Fake.Plugin.dll", fakeDllPath);
        staging.AddFile("locales/en-US.json", WriteTemp(directory, "en-US.json", LocaleJson("en-US", ("fixture.body", "body"))));
        staging.AddFile("LICENSE", WriteTemp(directory, "LICENSE", "MIT"));
        staging.AddFile("README.md", WriteTemp(directory, "README.md", "# Test"));
        var packagePath = Path.Combine(directory, fileNamePrefix + "-" + id + "-" + version + ".fbp");
        var build = staging.Build(packagePath);

        var digest = SHA256.HashData(File.ReadAllBytes(packagePath));
        var signatures = new List<SignatureEntry>
        {
            PackageSigner.SignDigest(keys.MarketplaceKey, digest, keys.Marketplace.KeyId, SignatureRole.Marketplace),
        };
        if (includeAuthorSignature)
        {
            signatures.Add(PackageSigner.SignDigest(keys.AuthorKey, digest, keys.Author.KeyId, SignatureRole.Author));
        }

        var document = PackageSigner.CreateDocument(packagePath, digest, build.Size, signatures);
        return (packagePath, document);
    }

    public static string WriteTemp(string directory, string fileName, string content)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Writes a ZIP with arbitrary (including malicious) entries for reader/safety tests.</summary>
    public static string WriteZip(string path, params (string Name, byte[] Content, int ExternalAttributes)[] entries)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content, attributes) in entries)
        {
            var entry = archive.CreateEntry(name);
            entry.ExternalAttributes = attributes;
            if (content.Length > 0)
            {
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        return path;
    }

    public static MarketplaceIndexDocument CreateIndex(
        TestKeys keys,
        string pluginId = "sample-minimal",
        string artifactSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        string publisherKeyId = "test-publisher-2026",
        Action<MarketplaceIndexDocument>? customize = null)
    {
        var index = new MarketplaceIndexDocument
        {
            Schema = "https://fluidbar.app/schemas/marketplace-index.schema.json",
            SchemaVersion = 1,
            GeneratedAt = DateTimeOffset.UtcNow,
            SourceCommit = new string('a', 40),
            AppCompatibility = new AppCompatibilityInfo("2.0.0", ["2.0"]),
            DefaultLocale = "en-US",
            AvailableLocales = ["en-US"],
            Plugins =
            [
                new MarketplacePluginEntry
                {
                    Id = pluginId,
                    Name = new FluidBar.Sdk.Manifest.LocalizedString("Test Plugin"),
                    Summary = new FluidBar.Sdk.Manifest.LocalizedString("A test plugin."),
                    Publisher = new MarketplacePublisherInfo
                    {
                        Id = "test-publisher",
                        DisplayName = "Test Publisher",
                        Verified = true,
                        KeyId = publisherKeyId,
                        Url = "https://example.com",
                    },
                    Latest = new MarketplaceVersionInfo
                    {
                        Version = "1.0.0",
                        ApiVersion = "2.0.0",
                        MinAppVersion = "2.0.0",
                        PublishedAt = DateTimeOffset.UtcNow,
                        Channel = PluginChannel.Stable,
                        Artifact = new MarketplaceArtifactInfo
                        {
                            Url = $"https://github.com/fluidbar/plugins/releases/download/{pluginId}/plugin.fbp",
                            Sha256 = artifactSha256,
                            Size = 1024,
                            SignatureUrl = $"https://github.com/fluidbar/plugins/releases/download/{pluginId}/signature.json",
                            KeyId = keys.Marketplace.KeyId,
                            Algorithm = SignatureAlgorithm.EcdsaP256Sha256,
                        },
                        Capabilities = ["ui"],
                        Locales = ["en-US"],
                    },
                    Categories = ["developer"],
                    HistoryUrl = $"https://marketplace.fluidbar.app/plugins/{pluginId}.json",
                    Listing = new MarketplaceListingInfo
                    {
                        DefaultLocale = "en-US",
                        AvailableLocales = ["en-US"],
                        BaseUrl = $"https://marketplace.fluidbar.app/listings/{pluginId}/",
                    },
                    Stats = new MarketplaceStats { Downloads = 10, Rating = null },
                    Status = PluginListingStatus.Active,
                },
            ],
            Advisories = [],
            Keys =
            [
                new MarketplaceKeyInfo
                {
                    KeyId = keys.Marketplace.KeyId,
                    Role = "marketplace",
                    Algorithm = SignatureAlgorithm.EcdsaP256Sha256,
                    PublicKey = keys.Marketplace.PublicKeySpkiBase64,
                    NotBefore = DateTimeOffset.UtcNow.AddYears(-1),
                    NotAfter = DateTimeOffset.UtcNow.AddYears(1),
                },
            ],
            Signatures = [],
        };

        customize?.Invoke(index);
        return index;
    }

    public static string CreateSignedIndexJson(TestKeys keys, MarketplaceIndexDocument index, DateTimeOffset? signedAt = null) =>
        IndexSigner.SignToJson(index, keys.MarketplaceKey, keys.Marketplace.KeyId, signedAt);
}
