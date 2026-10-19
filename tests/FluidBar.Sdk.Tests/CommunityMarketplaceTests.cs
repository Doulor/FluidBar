using FluidBar.Marketplace.Community;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Sdk.Tests;

/// <summary>
/// Local community marketplace: submit validation, review state machine, rating bounds and the
/// persisted download counter. Every assertion reads back what the service wrote to disk.
/// </summary>
public sealed class CommunityMarketplaceTests
{
    private const string PluginId = "community-sample";

    [Fact]
    public async Task Submit_rejects_a_non_package_file()
    {
        using var temp = TempDirectory.Create("fluidbar-community-submit");
        var bogus = Path.Combine(temp.Path, "not-a-package.fbp");
        File.WriteAllText(bogus, "this is not a zip archive");
        var (service, store, _) = CreateService(temp);

        var result = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = bogus });

        Assert.False(result.Success);
        Assert.Null(result.Submission);
        Assert.Contains(result.Errors, error => error.Contains("not a valid ZIP", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(store.IndexPath));
        Assert.Empty(await service.GetPendingAsync());
    }

    [Fact]
    public async Task Submit_rejects_a_package_whose_manifest_is_invalid()
    {
        using var temp = TempDirectory.Create("fluidbar-community-submit");
        var invalidManifest = TestPackageFactory.MinimalManifestJson(id: PluginId, customize: manifest =>
            manifest["categories"] = new System.Text.Json.Nodes.JsonArray("not-a-category"));
        using var staging = FbpStagingBuilder.Create(temp.Path);
        staging.AddManifestJson(invalidManifest);
        var packagePath = Path.Combine(temp.Path, "invalid-manifest.fbp");
        Assert.ThrowsAny<PackageFormatException>(() => staging.Build(packagePath));

        // A hand-rolled ZIP that has a manifest but no valid marketplace layout is still rejected by the reader.
        var zipPath = TestPackageFactory.WriteZip(
            Path.Combine(temp.Path, "reader-invalid.fbp"),
            ("manifest.json", System.Text.Encoding.UTF8.GetBytes(invalidManifest), 0));
        var (service, _, _) = CreateService(temp);

        var result = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = zipPath });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("Package", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Submit_accepts_an_unsigned_package_and_queues_it_as_pending()
    {
        using var temp = TempDirectory.Create("fluidbar-community-submit");
        var (service, store, _) = CreateService(temp);
        var packagePath = CreatePackage(temp.Path, version: "1.0.0");

        var result = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = packagePath });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var submission = Assert.IsType<CommunitySubmission>(result.Submission);
        Assert.Equal(PluginId, submission.PluginId);
        Assert.Equal("1.0.0", submission.Version);
        Assert.Equal(CommunitySubmissionState.Pending, submission.State);
        Assert.Equal(CommunitySignatureState.Unsigned, submission.SignatureState);
        Assert.Contains(result.Warnings, warning => warning.Contains("unsigned", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(64, submission.PackageSha256.Length);
        Assert.True(File.Exists(store.ResolvePackagePath(submission.PackageFileName)));
        Assert.True(File.Exists(store.IndexPath));

        var pending = await service.GetPendingAsync();
        var queued = Assert.Single(pending);
        Assert.Equal(PluginId, queued.PluginId);
        Assert.Empty(await service.BrowseAsync());
    }

    [Fact]
    public async Task Submit_verifies_a_detached_signature_when_trust_keys_are_injected()
    {
        using var temp = TempDirectory.Create("fluidbar-community-submit");
        using var keys = new TestKeys();
        var (packagePath, signature) = TestPackageFactory.CreateSignedPackage(temp.Path, keys, id: PluginId);
        var signaturePath = Path.Combine(temp.Path, "package.sig.json");
        File.WriteAllText(signaturePath, signature.ToJson());
        var (service, _, _) = CreateService(temp, keys);

        var signed = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = packagePath, SignaturePath = signaturePath });
        Assert.True(signed.Success, string.Join("; ", signed.Errors));
        Assert.Equal(CommunitySignatureState.Verified, signed.Submission!.SignatureState);
        Assert.Equal(keys.Marketplace.KeyId, signed.Submission.SignatureKeyId);
        Assert.Empty(signed.Warnings);

        // A signature document that does not match the package bytes must fail closed.
        var tampered = signature with
        {
            Package = new PackageHashInfo { Sha256 = new string('0', 64), Size = signature.Package.Size },
        };
        var tamperedPath = Path.Combine(temp.Path, "tampered.sig.json");
        File.WriteAllText(tamperedPath, tampered.ToJson());
        var rejected = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = packagePath, SignaturePath = tamperedPath });
        Assert.False(rejected.Success);
        Assert.Contains(rejected.Errors, error => error.Contains("signature", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Submit_rejects_a_duplicate_version_until_it_was_rejected()
    {
        using var temp = TempDirectory.Create("fluidbar-community-submit");
        var (service, _, _) = CreateService(temp);
        var packagePath = CreatePackage(temp.Path, version: "1.0.0");

        Assert.True((await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = packagePath })).Success);
        var duplicate = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = packagePath });
        Assert.False(duplicate.Success);
        Assert.Contains(duplicate.Errors, error => error.Contains("already submitted", StringComparison.OrdinalIgnoreCase));

        var review = await service.RejectAsync(PluginId, "1.0.0", "not useful");
        Assert.True(review.Success);
        var resubmitted = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = packagePath });
        Assert.True(resubmitted.Success, string.Join("; ", resubmitted.Errors));
        Assert.Equal(CommunitySubmissionState.Pending, resubmitted.Submission!.State);
    }

    [Fact]
    public async Task Review_moves_a_submission_into_the_listed_index_and_delists_it_again()
    {
        using var temp = TempDirectory.Create("fluidbar-community-review");
        var (service, store, _) = CreateService(temp);
        var packagePath = CreatePackage(temp.Path, version: "1.2.0");
        Assert.True((await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = packagePath })).Success);

        var review = await service.ApproveAsync(PluginId, "1.2.0", "looks good");
        Assert.True(review.Success, string.Join("; ", review.Errors));
        var listing = Assert.IsType<CommunityListing>(review.Listing);
        Assert.Equal(PluginId, listing.Id);
        Assert.Equal("1.2.0", listing.Version);
        Assert.Equal(0, listing.Stats.Downloads);
        Assert.Null(listing.Stats.Rating);
        Assert.Equal(CommunitySubmissionState.Approved, review.Submission!.State);
        Assert.Equal("looks good", review.Submission.ReviewNote);

        var listed = Assert.Single(await service.BrowseAsync());
        Assert.Equal(PluginId, listed.Id);
        Assert.Contains("community-sample", File.ReadAllText(store.IndexPath), StringComparison.Ordinal);
        Assert.Empty(await service.GetPendingAsync());

        var approval = await service.ApproveAsync(PluginId, "1.2.0");
        Assert.True(approval.Success);

        var delist = await service.RejectAsync(PluginId, "1.2.0", "pulled by the reviewer");
        Assert.True(delist.Success);
        Assert.Null(delist.Listing);
        Assert.Empty(await service.BrowseAsync());
        var rejected = await service.GetSubmissionsAsync(CommunitySubmissionState.Rejected);
        Assert.Single(rejected);
    }

    [Fact]
    public async Task Review_rejects_unknown_submissions_and_keeps_counts_across_re_review()
    {
        using var temp = TempDirectory.Create("fluidbar-community-review");
        var (service, _, _) = CreateService(temp);

        var missing = await service.ApproveAsync(PluginId, "9.9.9");
        Assert.False(missing.Success);
        Assert.Contains(missing.Errors, error => error.Contains("No submission", StringComparison.Ordinal));

        Assert.True((await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = CreatePackage(temp.Path, version: "1.0.0") })).Success);
        Assert.True((await service.ApproveAsync(PluginId, "1.0.0")).Success);
        var rated = await service.RateAsync(PluginId, 4);
        Assert.Equal(4.0, rated.Stats.Rating);
        var installed = await service.RecordDownloadAsync(PluginId);
        Assert.Equal(1, installed.Stats.Downloads);

        // Re-review of a newer version keeps the existing counters (they belong to the plugin, not the version).
        var newerPackage = CreatePackage(temp.Path, version: "2.0.0");
        Assert.True((await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = newerPackage })).Success);
        var secondReview = await service.ApproveAsync(PluginId, "2.0.0");
        Assert.True(secondReview.Success, string.Join("; ", secondReview.Errors));
        var listing = secondReview.Listing!;
        Assert.Equal("2.0.0", listing.Version);
        Assert.Equal(4.0, listing.Stats.Rating);
        Assert.Equal(1, listing.Stats.Downloads);
        Assert.Equal(["2.0.0", "1.0.0"], listing.Versions.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Rating_rejects_values_outside_one_to_five(int stars)
    {
        using var temp = TempDirectory.Create("fluidbar-community-rating");
        var (service, _, _) = CreateService(temp);
        await ApproveAsync(service, temp.Path);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RateAsync(PluginId, stars));
        var listing = await service.GetListingAsync(PluginId);
        Assert.NotNull(listing);
        Assert.Null(listing!.Stats.Rating);
        Assert.Null(listing.Stats.UserRating);
    }

    [Fact]
    public async Task Rating_persists_and_updates_the_average_after_a_restart()
    {
        using var temp = TempDirectory.Create("fluidbar-community-rating");
        var (service, store, _) = CreateService(temp);
        await ApproveAsync(service, temp.Path);

        var five = await service.RateAsync(PluginId, 5);
        Assert.Equal(5.0, five.Stats.Rating);
        Assert.Equal(1, five.Stats.RatingCount);
        Assert.Equal(5, five.Stats.UserRating);

        // Re-rating replaces this device's vote (no invented extra reviewers).
        var one = await service.RateAsync(PluginId, 1);
        Assert.Equal(1.0, one.Stats.Rating);
        Assert.Equal(1, one.Stats.RatingCount);

        var reread = new CommunityMarketplaceService(new CommunityMarketplaceOptions { Store = store });
        var persisted = await reread.GetListingAsync(PluginId);
        Assert.NotNull(persisted);
        Assert.Equal(1.0, persisted!.Stats.Rating);
        Assert.Equal(1, persisted.Stats.UserRating);
    }

    [Fact]
    public async Task Download_counter_increments_only_after_a_successful_install_and_persists()
    {
        using var temp = TempDirectory.Create("fluidbar-community-install");
        using var keys = new TestKeys();
        var activator = new RecordingActivator();
        var installer = new PluginInstallServiceCommunityInstaller(new PluginInstallOptions
        {
            PluginRootDirectory = temp.Combine("plugins"),
            Activator = activator,
            Trust = MarketplaceTrustOptions.Empty,
            RequireMarketplaceSignature = false,
            AllowUnsignedLocalInstall = true,
        });
        var (service, store, _) = CreateService(temp, installer: installer);
        await ApproveAsync(service, temp.Path);

        var outcome = await service.InstallAsync(PluginId);
        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(1, outcome.Listing!.Stats.Downloads);
        Assert.Single(activator.Activated);
        Assert.Equal("community-sample@1.0.0", activator.Activated[0]);
        Assert.True(File.Exists(temp.Combine("plugins", PluginId, "active.json")));

        var reread = new CommunityMarketplaceService(new CommunityMarketplaceOptions { Store = store });
        var persisted = await reread.GetListingAsync(PluginId);
        Assert.Equal(1, persisted!.Stats.Downloads);
    }

    [Fact]
    public async Task Install_failure_does_not_increment_the_download_counter()
    {
        using var temp = TempDirectory.Create("fluidbar-community-install");
        var (service, _, _) = CreateService(temp);
        await ApproveAsync(service, temp.Path);

        // No installer is configured: the install fails, so the counter must stay at zero.
        var outcome = await service.InstallAsync(PluginId);
        Assert.False(outcome.Success);
        Assert.Equal(0, (await service.GetListingAsync(PluginId))!.Stats.Downloads);

        // A missing package copy is also a failure.
        var packageFile = Directory.EnumerateFiles(temp.Combine("marketplace", "packages"), "*.fbp").Single();
        File.Delete(packageFile);
        var missingCopy = await service.InstallAsync(PluginId);
        Assert.False(missingCopy.Success);
        Assert.Equal(0, (await service.GetListingAsync(PluginId))!.Stats.Downloads);
    }

    [Fact]
    public async Task Store_round_trips_the_index_and_recovers_from_a_corrupt_document()
    {
        using var temp = TempDirectory.Create("fluidbar-community-store");
        var (service, store, _) = CreateService(temp);
        Assert.True((await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = CreatePackage(temp.Path) })).Success);
        Assert.True((await service.ApproveAsync(PluginId, "1.0.0")).Success);

        var reloaded = store.Load();
        Assert.Single(reloaded.Submissions);
        Assert.Single(reloaded.Plugins);
        Assert.Equal(LocalMarketplaceIndex.KindValue, reloaded.Kind);
        Assert.Equal(1, reloaded.SchemaVersion);

        File.WriteAllText(store.IndexPath, "{ not json");
        await Assert.ThrowsAsync<CommunityMarketplaceException>(() => service.BrowseAsync());
    }

    private static (CommunityMarketplaceService Service, CommunityMarketplaceStore Store, TempDirectory Temp) CreateService(
        TempDirectory temp,
        TestKeys? keys = null,
        ICommunityPackageInstaller? installer = null)
    {
        var store = new CommunityMarketplaceStore(temp.Combine("marketplace"));
        var service = new CommunityMarketplaceService(new CommunityMarketplaceOptions
        {
            Store = store,
            Trust = keys?.Trust ?? MarketplaceTrustOptions.Empty,
            Installer = installer,
        });
        return (service, store, temp);
    }

    private static async Task ApproveAsync(CommunityMarketplaceService service, string directory)
    {
        var packagePath = CreatePackage(directory, version: "1.0.0");
        var submitted = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = packagePath });
        Assert.True(submitted.Success, string.Join("; ", submitted.Errors));
        var review = await service.ApproveAsync(PluginId, "1.0.0");
        Assert.True(review.Success, string.Join("; ", review.Errors));
    }

    private static string CreatePackage(string directory, string id = PluginId, string version = "1.0.0")
    {
        var stagingDirectory = Path.Combine(directory, "staging-" + id + "-" + version.Replace('.', '_'));
        Directory.CreateDirectory(stagingDirectory);
        using var staging = FbpStagingBuilder.Create(stagingDirectory);
        staging.AddManifestJson(TestPackageFactory.MinimalManifestJson(id: id, version: version));
        var entryDll = Path.Combine(stagingDirectory, "Fake.Plugin.dll");
        if (!File.Exists(entryDll))
        {
            File.WriteAllBytes(entryDll, System.Text.Encoding.UTF8.GetBytes("community test entry assembly"));
        }

        staging.AddFile("plugin/Fake.Plugin.dll", entryDll);
        staging.AddFile("locales/en-US.json", TestPackageFactory.WriteTemp(
            stagingDirectory,
            "en-US.json",
            TestPackageFactory.LocaleJson("en-US", ("community.body", "body"))));
        staging.AddFile("LICENSE", TestPackageFactory.WriteTemp(stagingDirectory, "LICENSE", "MIT"));
        staging.AddFile("README.md", TestPackageFactory.WriteTemp(stagingDirectory, "README.md", "# Community sample"));
        var packagePath = Path.Combine(directory, "pkg-" + id + "-" + version + ".fbp");
        staging.Build(packagePath);
        return packagePath;
    }

    private sealed class RecordingActivator : IPluginActivator
    {
        internal List<string> Activated { get; } = [];

        public Task<ActivationResult> ActivateAsync(PluginManifest manifest, string versionDirectory, CancellationToken cancellationToken)
        {
            Activated.Add(manifest.Id + "@" + manifest.Version);
            return Task.FromResult(ActivationResult.Ok());
        }

        public Task DeactivateAsync(string pluginId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
