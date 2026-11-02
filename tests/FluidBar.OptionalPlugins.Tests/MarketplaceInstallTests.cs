using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluidBar.Marketplace.Catalog;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.OptionalPlugins.Tests;

public sealed class MarketplaceInstallTests
{
    [Fact]
    public async Task Signed_catalog_download_verification_and_install_cover_every_registered_plugin()
    {
        var repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "../.."));
        var temporary = Path.Combine(Path.GetTempPath(), "fluidbar-market-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var artifacts = new List<MarketplacePluginArtifact>();
            foreach (var directory in MarketplacePluginDiscovery.Discover(Path.Combine(repository, "plugins")))
            {
                var project = Path.GetFileNameWithoutExtension(Assert.Single(Directory.GetFiles(directory, "*.csproj")));
                var artifactsBin = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Parent;
                var isolatedOutput = Path.Combine(artifactsBin?.FullName ?? directory, project, "release");
                var output = File.Exists(Path.Combine(isolatedOutput, "manifest.json"))
                    ? isolatedOutput : Path.Combine(directory, "bin/Release/net10.0");
                Assert.Equal(await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json")),
                    await File.ReadAllTextAsync(Path.Combine(output, "manifest.json")));
                using var staging = FbpStagingBuilder.FromPluginOutput(output);
                var manifest = PluginManifestParser.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
                var package = Path.Combine(temporary, manifest.Id + ".fbp");
                var build = staging.Build(package);
                await PackageSigner.SignPackageFileAsync(package, package + ".sig.json", key, "test-market", SignatureRole.Marketplace);
                artifacts.Add(new MarketplacePluginArtifact { Manifest = manifest, PackagePath = package,
                    SignaturePath = package + ".sig.json", Sha256 = build.Sha256, Size = build.Size });
            }
            var generated = MarketplaceIndexGenerator.Build(artifacts, new MarketplaceIndexGeneratorOptions
            {
                OutputDirectory = Path.Combine(temporary, "catalog"), SigningKey = key, KeyId = "test-market",
                ArtifactBaseUrl = "https://github.com/Doulor/FluidBar/releases/download",
                MarketplaceBaseUrl = "https://fluidbar.doulor.cn/market", SourceCommit = new string('a', 40),
            });
            Assert.Equal(4, generated.Index.Plugins!.Count);
            var routes = new Dictionary<string, byte[]> { ["https://fluidbar.doulor.cn/market/index.json"] = await File.ReadAllBytesAsync(generated.IndexPath) };
            foreach (var artifact in generated.Artifacts)
            {
                routes[artifact.ArtifactUrl] = await File.ReadAllBytesAsync(artifact.PackagePath);
                routes[artifact.SignatureUrl] = await File.ReadAllBytesAsync(artifact.SignaturePath);
            }
            var trust = MarketplaceTrustOptions.ForMarketplaceKeys(TrustedKey.FromPublicKey(key, "test-market", SignatureRole.Marketplace));
            using var http = new HttpClient(new Routes(routes));
            using var catalog = new MarketplaceCatalogClient(new MarketplaceCatalogOptions { IndexUrl = new Uri("https://fluidbar.doulor.cn/market/index.json"), Trust = trust }, http);
            var fetched = await catalog.FetchIndexAsync();
            Assert.Equal(CatalogFetchStatus.Success, fetched.Status);
            var activator = new Activator();
            using var installs = new PluginInstallService(new PluginInstallOptions { PluginRootDirectory = Path.Combine(temporary, "installed"),
                AppVersion = new SemanticVersion(4, 1, 0), HostApiVersion = PluginApiVersion.Current, Trust = trust, Activator = activator });
            foreach (var plugin in fetched.Index!.Plugins!)
            {
                var version = plugin.Latest!;
                var signature = PackageSignatureDocument.Parse(Encoding.UTF8.GetString(routes[version.Artifact!.SignatureUrl]));
                var installed = await installs.InstallAsync(new InstallRequest { Source = new HttpPackageSource(http, new Uri(version.Artifact.Url)),
                    Signature = signature, ExpectedVersion = version, ExpectedPluginId = plugin.Id });
                Assert.True(installed.Success, installed.Message);
                Assert.Equal(version.Version, installs.GetState(plugin.Id)!.ActiveVersion);
            }
            Assert.Equal(4, activator.Activations);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static string Source([System.Runtime.CompilerServices.CallerFilePath] string source = "") => source;
    private sealed class Routes(Dictionary<string, byte[]> routes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(routes.TryGetValue(request.RequestUri!.ToString(), out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
    private sealed class Activator : IPluginActivator
    {
        public int Activations { get; private set; }
        public Task<ActivationResult> ActivateAsync(PluginManifest manifest, string versionDirectory, CancellationToken cancellationToken)
        { Activations++; return Task.FromResult(ActivationResult.Ok()); }
        public Task DeactivateAsync(string pluginId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
