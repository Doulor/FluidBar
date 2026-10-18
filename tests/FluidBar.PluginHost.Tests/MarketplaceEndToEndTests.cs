using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluidBar.Marketplace.Catalog;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.PluginHost.Loader;
using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.PluginHost.Tests;

/// <summary>
/// Offline, deterministic end-to-end publish/install verification (no GitHub, no network):
/// sample plugin -> deterministic .fbp -> detached signature -> signed index + shards -> marketplace client
/// fetch (local HTTP handler) -> install v1 -> host load/unload -> update to v2 -> load/unload -> rollback to v1
/// -> load/unload. This is the CI proof that the publish loop works without any hosted service.
/// </summary>
public sealed class MarketplaceEndToEndTests
{
    [Fact]
    public async Task Publish_install_update_rollback_loop_loads_and_unloads_offline()
    {
        using var workspace = TempPluginRoot.Create();
        using var keys = new HostTestKeys();
        var artifactsDirectory = workspace.Combine("artifacts");
        var marketplaceDirectory = workspace.Combine("marketplace");
        Directory.CreateDirectory(artifactsDirectory);

        var versionOne = BuildArtifact("FluidBar.Plugins.SampleMinimal", "1.0.0", keys, artifactsDirectory);
        var versionTwo = BuildArtifact("FluidBar.Plugins.SampleMinimal", "2.0.0", keys, artifactsDirectory);
        Assert.NotEqual(versionOne.Artifact.Sha256, versionTwo.Artifact.Sha256);
        Assert.True(FbpPackageReader.Inspect(versionOne.PackageBytes).Manifest.Id == "sample-minimal");

        // Regenerate the signed index + per-plugin shard + keys.json exactly like CI would.
        var generatedAt = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
        var build = MarketplaceIndexGenerator.Build(
            [versionOne.Artifact, versionTwo.Artifact],
            new MarketplaceIndexGeneratorOptions
            {
                OutputDirectory = marketplaceDirectory,
                ArtifactBaseUrl = "https://github.com/fluidbar/plugins/releases/download",
                MarketplaceBaseUrl = "https://marketplace.fluidbar.app",
                KeyId = keys.MarketplaceTrusted.KeyId,
                SigningKey = keys.MarketplaceKey,
                GeneratedAt = generatedAt,
                SourceCommit = new string('a', 40),
            });

        Assert.Single(build.Index.Plugins!);
        Assert.Equal("2.0.0", build.Index.Plugins![0].Latest!.Version);
        Assert.Single(build.ShardPaths);
        var shard = MarketplaceIndexParser.ParseShard(File.ReadAllText(Assert.Single(build.ShardPaths)));
        Assert.Equal(2, shard.Versions!.Count);

        var indexBytes = File.ReadAllBytes(build.IndexPath);
        var indexUrl = new Uri("https://marketplace.fluidbar.app/index.json");
        var routes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [indexUrl.ToString()] = indexBytes,
        };
        foreach (var (version, built) in new[] { ("1.0.0", versionOne), ("2.0.0", versionTwo) })
        {
            var versionEntry = shard.Versions!.Single(entry => entry.Version == version);
            var artifact = versionEntry.Artifact!;
            routes[artifact.Url] = built.PackageBytes;
            routes[artifact.SignatureUrl] = built.SignatureBytes;
        }

        routes[build.Index.Plugins![0].HistoryUrl] = File.ReadAllBytes(build.ShardPaths[0]);

        var trust = new MarketplaceTrustOptions { MarketplaceKeys = build.MarketplaceKeys };
        using var httpClient = new HttpClient(new LocalHttpHandler(routes), disposeHandler: false);
        using var catalog = new MarketplaceCatalogClient(
            new MarketplaceCatalogOptions
            {
                IndexUrl = indexUrl,
                Trust = trust,
                Timeout = TimeSpan.FromSeconds(5),
            },
            new LocalHttpHandler(routes));

        // 1) Catalog fetch verifies the signed index.
        var catalogResult = await catalog.FetchIndexAsync();
        Assert.Equal(CatalogFetchStatus.Success, catalogResult.Status);
        Assert.NotNull(catalogResult.Index);
        var latest = catalogResult.Index!.Plugins![0].Latest!;
        Assert.Equal("2.0.0", latest.Version);

        var history = await catalog.FetchPluginHistoryAsync(new Uri(catalogResult.Index.Plugins[0].HistoryUrl));
        Assert.Equal(CatalogFetchStatus.Success, history.Status);
        Assert.Equal(2, history.Shard!.Versions!.Count);

        // 2) Install v1 offline from the generated package, then load it in the real host.
        var pluginRoot = workspace.Combine("plugins");
        var activator = new RecordingActivator();
        using var installService = new PluginInstallService(new PluginInstallOptions
        {
            PluginRootDirectory = pluginRoot,
            Activator = activator,
            Trust = trust,
            AppVersion = new SemanticVersion(2, 0, 0),
            RequireMarketplaceSignature = true,
        });

        var installProgress = new InlineProgress<InstallPhase>();
        var installOne = await installService.InstallFromFileAsync(
            versionOne.Artifact.PackagePath,
            versionOne.Signature,
            allowUnsigned: false,
            progress: installProgress);
        Assert.True(installOne.Success, installOne.Message);
        Assert.Equal("1.0.0", installService.GetState("sample-minimal")!.ActiveVersion);
        Assert.Equal(
            [InstallPhase.Downloading, InstallPhase.Verifying, InstallPhase.Extracting, InstallPhase.Staged, InstallPhase.Activating, InstallPhase.Active],
            installProgress.Values);

        var (host, _, publisher) = HostFactory.Create(pluginRoot);
        await using var hostScope = host;
        var loadOne = await host.LoadAsync("sample-minimal");
        Assert.True(loadOne.Success, loadOne.Message);
        Assert.Equal("1.0.0", loadOne.Handle!.Manifest.Version);
        Assert.NotEmpty(publisher.Events);
        var unloadOne = await host.UnloadAsync("sample-minimal");
        Assert.True(unloadOne.Collected, unloadOne.FailureReason);

        // 3) Update to v2 using the signed index + client-provided signature (no network: local handler).
        var v2VersionEntry = shard.Versions!.Single(entry => entry.Version == "2.0.0");
        var v2Signature = PackageSignatureDocument.Parse(Encoding.UTF8.GetString(routes[v2VersionEntry.Artifact!.SignatureUrl]));
        var updateProgress = new InlineProgress<InstallPhase>();
        var update = await installService.UpdateAsync(new InstallRequest
        {
            Source = new HttpPackageSource(httpClient, new Uri(v2VersionEntry.Artifact.Url)),
            Signature = v2Signature,
            ExpectedVersion = v2VersionEntry,
            ExpectedPluginId = "sample-minimal",
            Progress = updateProgress,
        });
        Assert.True(update.Success, update.Message);
        Assert.Equal(InstallPhase.Active, updateProgress.Values[^1]);
        Assert.Contains(InstallPhase.Downloading, updateProgress.Values);
        Assert.Contains(InstallPhase.Activating, updateProgress.Values);

        var changedFile = Path.Combine(pluginRoot, "sample-minimal", "active.json");
        Assert.Contains("2.0.0", File.ReadAllText(changedFile), StringComparison.Ordinal);

        var loadTwo = await host.LoadAsync("sample-minimal");
        Assert.True(loadTwo.Success, loadTwo.Message);
        Assert.Equal("2.0.0", loadTwo.Handle!.Manifest.Version);
        var unloadTwo = await host.UnloadAsync("sample-minimal");
        Assert.True(unloadTwo.Collected, unloadTwo.FailureReason);

        // 4) Rollback is a pointer switch; the old version directory is still installed.
        var rollbackProgress = new InlineProgress<InstallPhase>();
        var rollback = await installService.RollbackAsync("sample-minimal", "1.0.0", progress: rollbackProgress);
        Assert.True(rollback.Success, rollback.Message);
        Assert.Equal([InstallPhase.Activating, InstallPhase.Active], rollbackProgress.Values);
        Assert.Equal("1.0.0", rollback.ActiveVersion);
        Assert.Contains("1.0.0", File.ReadAllText(changedFile), StringComparison.Ordinal);

        var loadRolledBack = await host.LoadAsync("sample-minimal");
        Assert.True(loadRolledBack.Success, loadRolledBack.Message);
        Assert.Equal("1.0.0", loadRolledBack.Handle!.Manifest.Version);
        var unloadRolledBack = await host.UnloadAsync("sample-minimal");
        Assert.True(unloadRolledBack.Collected, unloadRolledBack.FailureReason);

        var state = installService.GetState("sample-minimal")!;
        Assert.Equal("1.0.0", state.ActiveVersion);
        Assert.Equal("1.0.0", state.LastGoodVersion);
        Assert.Contains("2.0.0", installService.GetInstalledVersions("sample-minimal"));
    }

    private static BuiltArtifact BuildArtifact(string sampleProjectName, string version, HostTestKeys keys, string artifactsDirectory)
    {
        var sampleOutput = TestPaths.SampleOutput(sampleProjectName);
        var manifestNode = JsonNode.Parse(File.ReadAllText(Path.Combine(sampleOutput, "manifest.json")))
            ?? throw new InvalidOperationException("Sample manifest is not valid JSON.");
        manifestNode["version"] = version;
        var manifestJson = manifestNode.ToJsonString();
        var manifest = PluginManifestParser.Parse(manifestJson);

        using var staging = FbpStagingBuilder.Create();
        staging.AddManifestJson(manifestJson);
        foreach (var dll in Directory.EnumerateFiles(sampleOutput, "*.dll", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(dll), "FluidBar.Sdk", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            staging.AddFile("plugin/" + Path.GetFileName(dll), dll);
        }

        foreach (var deps in Directory.EnumerateFiles(sampleOutput, "*.deps.json", SearchOption.TopDirectoryOnly))
        {
            staging.AddFile("plugin/" + Path.GetFileName(deps), deps);
        }

        foreach (var locale in Directory.EnumerateFiles(Path.Combine(sampleOutput, "locales"), "*.json", SearchOption.TopDirectoryOnly))
        {
            staging.AddFile("locales/" + Path.GetFileName(locale), locale);
        }

        foreach (var file in new[] { "LICENSE", "README.md" })
        {
            var path = Path.Combine(sampleOutput, file);
            if (File.Exists(path))
            {
                staging.AddFile(file, path);
            }
        }

        var packagePath = Path.Combine(artifactsDirectory, $"{manifest.Id}-{version}.fbp");
        staging.Build(packagePath);
        var signaturePath = packagePath + ".sig.json";
        var signatureDocument = keys.Sign(packagePath);
        File.WriteAllText(signaturePath, signatureDocument.ToJson());

        var artifact = new MarketplacePluginArtifact
        {
            Manifest = manifest,
            PackagePath = packagePath,
            SignaturePath = signaturePath,
            Sha256 = FbpPackager.ComputeSha256(packagePath),
            Size = new FileInfo(packagePath).Length,
        };

        return new BuiltArtifact(
            artifact,
            File.ReadAllBytes(packagePath),
            File.ReadAllBytes(signaturePath),
            signatureDocument);
    }

    private sealed record BuiltArtifact(
        MarketplacePluginArtifact Artifact,
        byte[] PackageBytes,
        byte[] SignatureBytes,
        PackageSignatureDocument Signature);

    /// <summary>In-memory HTTP handler: maps exact URLs to file bytes; 404 otherwise. Keeps the E2E fully offline.</summary>
    private sealed class LocalHttpHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, byte[]> _routes;

        internal LocalHttpHandler(IReadOnlyDictionary<string, byte[]> routes) => _routes = routes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri && _routes.TryGetValue(uri.ToString(), out var content))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content),
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
