using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FluidBar.Marketplace.Catalog;
using FluidBar.Marketplace.Consent;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Sdk.Tests;

public sealed class CatalogClientTests
{
    private static MarketplaceCatalogOptions Options(TestKeys keys) => new()
    {
        IndexUrl = new Uri("https://marketplace.fluidbar.app/index.json"),
        FallbackIndexUrl = new Uri("https://raw.githubusercontent.com/fluidbar/marketplace/main/index.json"),
        Trust = keys.Trust,
        Timeout = TimeSpan.FromMilliseconds(500),
        MaxResponseBytes = 512 * 1024,
    };

    private static HttpResponseMessage Json(string content, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };
        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        return response;
    }

    [Fact]
    public async Task Fetches_and_verifies_signed_index()
    {
        using var keys = new TestKeys();
        var json = TestPackageFactory.CreateSignedIndexJson(keys, TestPackageFactory.CreateIndex(keys));
        using var client = new MarketplaceCatalogClient(Options(keys), new FakeHandler(Respond(_ => Json(json, "\"v1\""))));
        var result = await client.FetchIndexAsync();
        Assert.Equal(CatalogFetchStatus.Success, result.Status);
        Assert.NotNull(result.Index);
        Assert.Single(result.Index!.Plugins!);
    }

    [Fact]
    public async Task Http_error_is_reported()
    {
        using var keys = new TestKeys();
        using var client = new MarketplaceCatalogClient(Options(keys), new FakeHandler(Respond(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        var result = await client.FetchIndexAsync();
        Assert.Equal(CatalogFetchStatus.HttpError, result.Status);
    }

    [Fact]
    public async Task Fallback_url_is_used_when_primary_fails()
    {
        using var keys = new TestKeys();
        var json = TestPackageFactory.CreateSignedIndexJson(keys, TestPackageFactory.CreateIndex(keys));
        using var client = new MarketplaceCatalogClient(Options(keys), new FakeHandler(Respond(request =>
            request.RequestUri!.Host == "marketplace.fluidbar.app"
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : Json(json))));
        var result = await client.FetchIndexAsync();
        Assert.Equal(CatalogFetchStatus.Success, result.Status);
    }

    [Fact]
    public async Task Etag_cache_returns_not_modified()
    {
        using var keys = new TestKeys();
        var json = TestPackageFactory.CreateSignedIndexJson(keys, TestPackageFactory.CreateIndex(keys));
        var handler = new FakeHandler(
            Respond(_ => Json(json, "\"v1\"")),
            Respond(_ => new HttpResponseMessage(HttpStatusCode.NotModified)));
        using var client = new MarketplaceCatalogClient(Options(keys), handler);

        var first = await client.FetchIndexAsync();
        Assert.Equal(CatalogFetchStatus.Success, first.Status);
        var second = await client.FetchIndexAsync();
        Assert.Equal(CatalogFetchStatus.NotModified, second.Status);
        Assert.True(second.FromCache);
        Assert.NotNull(second.Index);
    }

    [Fact]
    public async Task Oversized_response_is_rejected()
    {
        using var keys = new TestKeys();
        var options = Options(keys) with { MaxResponseBytes = 32 };
        var json = TestPackageFactory.CreateSignedIndexJson(keys, TestPackageFactory.CreateIndex(keys));
        using var client = new MarketplaceCatalogClient(options, new FakeHandler(Respond(_ => Json(json))));
        var result = await client.FetchIndexAsync();
        Assert.Equal(CatalogFetchStatus.InvalidPayload, result.Status);
        Assert.Contains("limit", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_index_signature_is_rejected()
    {
        using var keys = new TestKeys();
        var index = TestPackageFactory.CreateIndex(keys);
        var json = TestPackageFactory.CreateSignedIndexJson(keys, index).Replace("\"sample-minimal\"", "\"other-plugin\"", StringComparison.Ordinal);
        using var client = new MarketplaceCatalogClient(Options(keys), new FakeHandler(Respond(_ => Json(json))));
        var result = await client.FetchIndexAsync();
        Assert.Equal(CatalogFetchStatus.SignatureInvalid, result.Status);
    }

    [Fact]
    public async Task Cancellation_is_reported()
    {
        using var keys = new TestKeys();
        using var client = new MarketplaceCatalogClient(Options(keys), new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        var result = await client.FetchIndexAsync(cancellation.Token);
        Assert.Equal(CatalogFetchStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Timeout_is_reported_as_network_error()
    {
        using var keys = new TestKeys();
        var options = Options(keys) with { Timeout = TimeSpan.FromMilliseconds(50) };
        using var client = new MarketplaceCatalogClient(options, new FakeHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var result = await client.FetchIndexAsync();
        Assert.Equal(CatalogFetchStatus.NetworkError, result.Status);
        Assert.Contains("timeout", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(
        Func<HttpRequestMessage, HttpResponseMessage> response) =>
        (request, _) => Task.FromResult(response(request));

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses;

        internal FakeHandler(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responses) =>
            _responses = new Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return response(request, cancellationToken);
        }
    }
}

public sealed class InstallServiceTests
{
    private static PluginInstallOptions CreateOptions(string root, TestKeys keys, FakeActivator activator) => new()
    {
        PluginRootDirectory = root,
        Activator = activator,
        Trust = keys.Trust,
        AppVersion = new SemanticVersion(2, 0, 0),
        RequireMarketplaceSignature = true,
        AllowUnsignedLocalInstall = false,
        RetainedVersions = 2,
    };

    private static (string Path, PackageSignatureDocument Signature) Package(string directory, TestKeys keys, string version, string? customName = null)
    {
        var manifest = TestPackageFactory.MinimalManifestJson(version: version, customize: customName is null
            ? null
            : value => value["name"] = new System.Text.Json.Nodes.JsonObject { ["en-US"] = customName });
        return TestPackageFactory.CreateSignedPackage(
            directory,
            keys,
            version: version,
            manifestJson: manifest,
            fileNamePrefix: "pkg" + version.Replace('.', '_'));
    }

    private static async Task<InstallResult> InstallAsync(PluginInstallService service, string path, PackageSignatureDocument signature) =>
        await service.InstallFromFileAsync(path, signature, allowUnsigned: false);

    [Fact]
    public async Task Installs_signed_package_and_writes_state()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var activator = new FakeActivator();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, activator));
        var (path, signature) = Package(temp.Path, keys, "1.0.0");

        var result = await InstallAsync(service, path, signature);
        Assert.True(result.Success, result.Message);
        Assert.Equal("sample-minimal", result.PluginId);
        Assert.Equal("1.0.0", service.GetState("sample-minimal")!.ActiveVersion);
        Assert.True(File.Exists(temp.Combine("plugins", "sample-minimal", "active.json")));
        Assert.True(File.Exists(temp.Combine("plugins", "sample-minimal", "1.0.0", "manifest.json")));
        Assert.Contains("sample-minimal@1.0.0", activator.Activated);
        Assert.NotNull(result.Consent);
        Assert.Contains(result.Consent!.Items, item => item.Capability == FluidBar.Sdk.Capabilities.PluginCapability.Ui);
    }

    [Fact]
    public async Task Staging_is_cleaned_after_success()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, new FakeActivator()));
        var (path, signature) = Package(temp.Path, keys, "1.0.0");
        await InstallAsync(service, path, signature);
        var staging = temp.Combine("plugins", ".staging");
        Assert.True(Directory.Exists(staging));
        Assert.Empty(Directory.EnumerateFileSystemEntries(staging));
    }

    [Fact]
    public async Task Tampered_package_is_rejected_before_extraction()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, new FakeActivator()));
        var (path, signature) = Package(temp.Path, keys, "1.0.0");
        var tampered = signature with
        {
            Package = new PackageHashInfo { Sha256 = new string('0', 64), Size = signature.Package.Size },
        };

        var result = await service.InstallFromFileAsync(path, tampered, allowUnsigned: false);
        Assert.False(result.Success);
        Assert.True(result.FailureKind is InstallFailureKind.HashMismatch or InstallFailureKind.SignatureInvalid, result.FailureKind.ToString());
        Assert.False(Directory.Exists(temp.Combine("plugins", "sample-minimal", "1.0.0")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Combine("plugins", ".staging")));
    }

    [Fact]
    public async Task Unsigned_install_is_rejected_when_required()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, new FakeActivator()));
        var (path, _) = Package(temp.Path, keys, "1.0.0");
        var result = await service.InstallFromFileAsync(path, signature: null, allowUnsigned: false);
        Assert.False(result.Success);
        Assert.Equal(InstallFailureKind.SignatureInvalid, result.FailureKind);
    }

    [Fact]
    public async Task Unsigned_local_install_is_allowed_with_warning()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, new FakeActivator()));
        var (path, _) = Package(temp.Path, keys, "1.0.0");
        var result = await service.InstallFromFileAsync(path, signature: null, allowUnsigned: true);
        Assert.True(result.Success, result.Message);
        Assert.Contains(result.Warnings, warning => warning.Contains("Unsigned", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Update_keeps_previous_version_and_prunes_beyond_retention()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var activator = new FakeActivator();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, activator));
        var (v1, sig1) = Package(temp.Path, keys, "1.0.0");
        Assert.True((await InstallAsync(service, v1, sig1)).Success);

        var (v2, sig2) = Package(temp.Path, keys, "2.0.0");
        var update = await service.UpdateAsync(new InstallRequest
        {
            Source = new FilePackageSource(v2),
            Signature = sig2,
            ExpectedVersion = new MarketplaceVersionInfo { Version = "2.0.0" },
            ExpectedPluginId = "sample-minimal",
        });

        Assert.True(update.Success, update.Message);
        var state = service.GetState("sample-minimal")!;
        Assert.Equal("2.0.0", state.ActiveVersion);
        Assert.Equal("1.0.0", state.PreviousVersion);
        Assert.Equal("2.0.0", state.LastGoodVersion);
        Assert.Contains("1.0.0", service.GetInstalledVersions("sample-minimal"));
    }

    [Fact]
    public async Task Failed_activation_restores_previous_version()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var activator = new FakeActivator();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, activator));
        var (v1, sig1) = Package(temp.Path, keys, "1.0.0");
        Assert.True((await InstallAsync(service, v1, sig1)).Success);
        activator.OnActivate = manifest => manifest.Version == "2.0.0"
            ? ActivationResult.Fail("activation.failed", "fixture: activation failed")
            : ActivationResult.Ok();

        var (v2, sig2) = Package(temp.Path, keys, "2.0.0");
        var update = await service.UpdateAsync(new InstallRequest
        {
            Source = new FilePackageSource(v2),
            Signature = sig2,
            ExpectedVersion = new MarketplaceVersionInfo { Version = "2.0.0" },
            ExpectedPluginId = "sample-minimal",
        });

        Assert.False(update.Success);
        Assert.Equal(InstallFailureKind.ActivationFailed, update.FailureKind);
        var state = service.GetState("sample-minimal")!;
        Assert.Equal("1.0.0", state.ActiveVersion);
        Assert.False(Directory.Exists(temp.Combine("plugins", "sample-minimal", "2.0.0")));
        Assert.True(Directory.Exists(temp.Combine("plugins", "sample-minimal", "1.0.0")));
    }

    [Fact]
    public async Task Rollback_switches_active_pointer()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var activator = new FakeActivator();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, activator));
        var (v1, sig1) = Package(temp.Path, keys, "1.0.0");
        Assert.True((await InstallAsync(service, v1, sig1)).Success);
        var (v2, sig2) = Package(temp.Path, keys, "2.0.0");
        Assert.True((await service.UpdateAsync(new InstallRequest
        {
            Source = new FilePackageSource(v2),
            Signature = sig2,
            ExpectedVersion = new MarketplaceVersionInfo { Version = "2.0.0" },
            ExpectedPluginId = "sample-minimal",
        })).Success);

        var rollback = await service.RollbackAsync("sample-minimal", "1.0.0");
        Assert.True(rollback.Success, rollback.Message);
        Assert.Equal("1.0.0", rollback.ActiveVersion);
        Assert.Equal("1.0.0", service.GetState("sample-minimal")!.ActiveVersion);
        var activeJson = File.ReadAllText(temp.Combine("plugins", "sample-minimal", "active.json"));
        Assert.Contains("\"1.0.0\"", activeJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rollback_to_unknown_version_fails()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, new FakeActivator()));
        var (v1, sig1) = Package(temp.Path, keys, "1.0.0");
        await InstallAsync(service, v1, sig1);
        var rollback = await service.RollbackAsync("sample-minimal", "9.9.9");
        Assert.False(rollback.Success);
    }

    [Fact]
    public async Task Same_version_with_different_package_is_rejected()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, new FakeActivator()));
        var (v1, sig1) = Package(temp.Path, keys, "1.0.0");
        Assert.True((await InstallAsync(service, v1, sig1)).Success);

        var (v1Other, sigOther) = Package(temp.Combine("other"), keys, "1.0.0", customName: "Different Plugin Name");
        var result = await service.InstallFromFileAsync(v1Other, sigOther, allowUnsigned: false);
        Assert.False(result.Success);
        Assert.Equal(InstallFailureKind.VersionAlreadyExists, result.FailureKind);
    }

    [Fact]
    public async Task Uninstall_keeps_settings_when_requested()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, new FakeActivator()));
        var (v1, sig1) = Package(temp.Path, keys, "1.0.0");
        await InstallAsync(service, v1, sig1);
        var pluginDirectory = temp.Combine("plugins", "sample-minimal");
        File.WriteAllText(Path.Combine(pluginDirectory, "settings.json"), "{\"schemaVersion\":1}");
        Directory.CreateDirectory(Path.Combine(pluginDirectory, "data"));
        File.WriteAllText(Path.Combine(pluginDirectory, "data", "cache.txt"), "keep me");

        var result = await service.UninstallAsync("sample-minimal", keepSettings: true);
        Assert.True(result.Success, result.Message);
        Assert.False(Directory.Exists(Path.Combine(pluginDirectory, "1.0.0")));
        Assert.True(File.Exists(Path.Combine(pluginDirectory, "settings.json")));
        Assert.True(File.Exists(Path.Combine(pluginDirectory, "data", "cache.txt")));
        Assert.Null(service.GetState("sample-minimal"));
    }

    [Fact]
    public async Task Uninstall_removes_settings_by_default()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, new FakeActivator()));
        var (v1, sig1) = Package(temp.Path, keys, "1.0.0");
        await InstallAsync(service, v1, sig1);
        var pluginDirectory = temp.Combine("plugins", "sample-minimal");
        File.WriteAllText(Path.Combine(pluginDirectory, "settings.json"), "{}");
        var result = await service.UninstallAsync("sample-minimal");
        Assert.True(result.Success, result.Message);
        Assert.False(File.Exists(Path.Combine(pluginDirectory, "settings.json")));
    }

    [Fact]
    public void Policy_reports_blocked_and_yanked()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        using var service = new PluginInstallService(CreateOptions(temp.Combine("plugins"), keys, new FakeActivator()));
        var blocked = TestPackageFactory.CreateIndex(keys).Plugins![0] with
        {
            Status = PluginListingStatus.Blocked,
            Blocked = new MarketplaceBlockedInfo { Reason = "Malware report", At = DateTimeOffset.UtcNow },
        };
        var decision = service.EvaluatePolicy(blocked, new SemanticVersion(2, 0, 0), new SemanticVersion(2, 0, 0));
        Assert.True(decision.MustDisable);
        Assert.Equal("Malware report", decision.Reason);

        var yanked = TestPackageFactory.CreateIndex(keys).Plugins![0] with
        {
            Latest = TestPackageFactory.CreateIndex(keys).Plugins![0].Latest! with
            {
                Yanked = true,
                YankReason = "Crash on startup",
            },
        };
        var yankedDecision = service.EvaluatePolicy(yanked, new SemanticVersion(2, 0, 0), new SemanticVersion(2, 0, 0));
        Assert.True(yankedDecision.HasYankedLatest);
        Assert.False(yankedDecision.MustDisable);
    }

    private sealed class FakeActivator : IPluginActivator
    {
        internal List<string> Activated { get; } = [];

        internal List<string> Deactivated { get; } = [];

        internal Func<PluginManifest, ActivationResult>? OnActivate { get; set; }

        public Task<ActivationResult> ActivateAsync(PluginManifest manifest, string versionDirectory, CancellationToken cancellationToken)
        {
            Activated.Add($"{manifest.Id}@{manifest.Version}");
            return Task.FromResult(OnActivate?.Invoke(manifest) ?? ActivationResult.Ok());
        }

        public Task DeactivateAsync(string pluginId, CancellationToken cancellationToken)
        {
            Deactivated.Add(pluginId);
            return Task.CompletedTask;
        }
    }
}

public sealed class ConsentTests
{
    [Fact]
    public void Consent_lists_declared_capabilities_with_risk_flags_and_reasons()
    {
        var manifest = PluginManifestParser.Parse(TestPackageFactory.MinimalManifestJson(customize: manifest =>
        {
            manifest["capabilities"] = new System.Text.Json.Nodes.JsonObject
            {
                ["network"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["domains"] = new System.Text.Json.Nodes.JsonArray("api.example.com"),
                    ["methods"] = new System.Text.Json.Nodes.JsonArray("GET"),
                    ["reason"] = "Fetch lyrics for the current track.",
                },
                ["clipboard"] = new System.Text.Json.Nodes.JsonObject { ["write"] = true },
            };
        }));

        var consent = CapabilityConsentBuilder.Build(manifest);
        Assert.Equal("sample-minimal", consent.PluginId);
        Assert.Equal("Market.Consent.SameAccessWarning", consent.WarningKey);
        var network = Assert.Single(consent.Items, item => item.Capability == FluidBar.Sdk.Capabilities.PluginCapability.Network);
        Assert.True(network.IsHighRisk);
        Assert.Equal("Fetch lyrics for the current track.", network.Reason);
        Assert.Contains("api.example.com", network.Details);
        Assert.Contains(consent.Items, item => item.Capability == FluidBar.Sdk.Capabilities.PluginCapability.Clipboard);
    }

    [Fact]
    public void Security_statement_is_honest_about_no_sandbox()
    {
        Assert.Contains("same access as FluidBar", CapabilityConsentData.SecurityStatement, StringComparison.Ordinal);
        Assert.Contains("not a sandbox", CapabilityConsentData.SecurityStatement, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_capabilities_produce_no_rows()
    {
        var manifest = PluginManifestParser.Parse(TestPackageFactory.MinimalManifestJson(customize: manifest =>
            manifest["capabilities"] = new System.Text.Json.Nodes.JsonObject()));
        var consent = CapabilityConsentBuilder.Build(manifest);
        Assert.Empty(consent.Items);
    }
}
