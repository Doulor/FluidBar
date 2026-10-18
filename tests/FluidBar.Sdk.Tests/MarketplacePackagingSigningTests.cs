using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;

namespace FluidBar.Sdk.Tests;

public sealed class PackagingTests
{
    [Fact]
    public void Packing_twice_is_byte_identical()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (first, _) = TestPackageFactory.CreateSignedPackage(temp.Combine("a"), keys, fileNamePrefix: "first");
        var (second, _) = TestPackageFactory.CreateSignedPackage(temp.Combine("b"), keys, fileNamePrefix: "second");

        Assert.Equal(FbpPackager.ComputeSha256(first), FbpPackager.ComputeSha256(second));
        Assert.Equal(new FileInfo(first).Length, new FileInfo(second).Length);
    }

    [Fact]
    public void Package_has_the_normative_layout()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, _) = TestPackageFactory.CreateSignedPackage(temp.Path, keys);

        var inspection = FbpPackageReader.Inspect(File.ReadAllBytes(package));
        Assert.Equal("sample-minimal", inspection.Manifest.Id);
        Assert.Contains("manifest.json", inspection.EntryPaths);
        Assert.Contains("plugin/Fake.Plugin.dll", inspection.EntryPaths);
        Assert.Contains("locales/en-US.json", inspection.EntryPaths);
        Assert.Contains("LICENSE", inspection.EntryPaths);
        Assert.Contains("README.md", inspection.EntryPaths);
        Assert.Contains("checksums.json", inspection.EntryPaths);
        Assert.NotNull(inspection.ChecksumsJson);
    }

    [Fact]
    public async Task Extraction_verifies_checksums_and_reports_files()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, _) = TestPackageFactory.CreateSignedPackage(temp.Path, keys);
        var destination = temp.Combine("extracted", "1.0.0");

        var result = await FbpPackageReader.ExtractFileAsync(package, destination);
        Assert.Equal("sample-minimal", result.Manifest.Id);
        Assert.True(File.Exists(Path.Combine(destination, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(destination, "plugin", "Fake.Plugin.dll")));
        Assert.Equal(64, result.Sha256.Length);
        Assert.DoesNotContain(".staging", result.DestinationDirectory);
    }

    [Fact]
    public async Task Extraction_refuses_to_overwrite_an_installed_version()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, _) = TestPackageFactory.CreateSignedPackage(temp.Path, keys);
        var destination = temp.Combine("1.0.0");
        await FbpPackageReader.ExtractFileAsync(package, destination);
        await Assert.ThrowsAsync<PackageFormatException>(() => FbpPackageReader.ExtractFileAsync(package, destination));
    }

    [Fact]
    public void Reader_rejects_zip_slip_entry()
    {
        using var temp = TempDirectory.Create();
        var path = TestPackageFactory.WriteZip(
            temp.Combine("slip.fbp"),
            ("manifest.json", Encoding.UTF8.GetBytes(TestPackageFactory.MinimalManifestJson()), 0),
            ("../evil.txt", Encoding.UTF8.GetBytes("evil"), 0));
        Assert.Throws<PackageFormatException>(() => FbpPackageReader.Inspect(File.ReadAllBytes(path)));
    }

    [Fact]
    public void Reader_rejects_absolute_path_entry()
    {
        using var temp = TempDirectory.Create();
        var path = TestPackageFactory.WriteZip(
            temp.Combine("abs.fbp"),
            ("manifest.json", Encoding.UTF8.GetBytes(TestPackageFactory.MinimalManifestJson()), 0),
            ("/etc/passwd", Encoding.UTF8.GetBytes("evil"), 0));
        Assert.Throws<PackageFormatException>(() => FbpPackageReader.Inspect(File.ReadAllBytes(path)));
    }

    [Fact]
    public void Reader_rejects_duplicate_entries()
    {
        using var temp = TempDirectory.Create();
        var path = TestPackageFactory.WriteZip(
            temp.Combine("dupe.fbp"),
            ("manifest.json", Encoding.UTF8.GetBytes(TestPackageFactory.MinimalManifestJson()), 0),
            ("plugin/a.dll", Encoding.UTF8.GetBytes("a"), 0),
            ("PLUGIN/A.DLL", Encoding.UTF8.GetBytes("b"), 0));
        Assert.Throws<PackageFormatException>(() => FbpPackageReader.Inspect(File.ReadAllBytes(path)));
    }

    [Fact]
    public void Reader_rejects_symlink_entries()
    {
        using var temp = TempDirectory.Create();
        // Unix symlink mode bits in the external attributes (0xA000 << 16).
        var path = TestPackageFactory.WriteZip(
            temp.Combine("symlink.fbp"),
            ("manifest.json", Encoding.UTF8.GetBytes(TestPackageFactory.MinimalManifestJson()), 0),
            ("plugin/link.dll", Encoding.UTF8.GetBytes("target"), 0xA1FF << 16));
        Assert.Throws<PackageFormatException>(() => FbpPackageReader.Inspect(File.ReadAllBytes(path)));
    }

    [Fact]
    public void Reader_rejects_missing_manifest()
    {
        using var temp = TempDirectory.Create();
        var path = TestPackageFactory.WriteZip(temp.Combine("nomanifest.fbp"), ("plugin/a.dll", Encoding.UTF8.GetBytes("a"), 0));
        Assert.Throws<PackageFormatException>(() => FbpPackageReader.Inspect(File.ReadAllBytes(path)));
    }

    [Fact]
    public void Reader_enforces_uncompressed_size_limit()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, _) = TestPackageFactory.CreateSignedPackage(temp.Path, keys);
        var bytes = File.ReadAllBytes(package);
        var options = new FbpPackageReaderOptions { MaxUncompressedBytes = 10 };
        Assert.Throws<PackageFormatException>(() => FbpPackageReader.Inspect(bytes, options));
    }

    [Fact]
    public void Reader_enforces_entry_count_limit()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, _) = TestPackageFactory.CreateSignedPackage(temp.Path, keys);
        var bytes = File.ReadAllBytes(package);
        var options = new FbpPackageReaderOptions { MaxEntries = 2 };
        Assert.Throws<PackageFormatException>(() => FbpPackageReader.Inspect(bytes, options));
    }

    [Fact]
    public void Packager_requires_marketplace_files()
    {
        using var temp = TempDirectory.Create();
        using var staging = FbpStagingBuilder.Create(temp.Path);
        staging.AddManifestJson(TestPackageFactory.MinimalManifestJson());
        var fake = TestPackageFactory.WriteTemp(temp.Path, "fake.dll", "x");
        staging.AddFile("plugin/Fake.Plugin.dll", fake);
        staging.AddFile("locales/en-US.json", TestPackageFactory.WriteTemp(temp.Path, "en-US.json", TestPackageFactory.LocaleJson("en-US", ("fixture.body", "b"))));
        Assert.Throws<PackageFormatException>(() => staging.Build(temp.Combine("missing-license.fbp")));
    }

    [Fact]
    public void Packager_rejects_locale_placeholder_mismatch()
    {
        using var temp = TempDirectory.Create();
        using var staging = FbpStagingBuilder.Create(temp.Path);
        staging.AddManifestJson(TestPackageFactory.MinimalManifestJson(customize: manifest =>
        {
            var locales = (System.Text.Json.Nodes.JsonObject)manifest["locales"]!;
            locales["available"] = new System.Text.Json.Nodes.JsonArray("en-US", "zh-Hans");
        }));
        var fake = TestPackageFactory.WriteTemp(temp.Path, "fake.dll", "x");
        staging.AddFile("plugin/Fake.Plugin.dll", fake);
        staging.AddFile("locales/en-US.json", TestPackageFactory.WriteTemp(temp.Path, "en.json", TestPackageFactory.LocaleJson("en-US", ("greet", "Hi {name}"))));
        staging.AddFile("locales/zh-Hans.json", TestPackageFactory.WriteTemp(temp.Path, "zh.json", TestPackageFactory.LocaleJson("zh-Hans", ("greet", "你好"))));
        staging.AddFile("LICENSE", TestPackageFactory.WriteTemp(temp.Path, "LICENSE", "MIT"));
        staging.AddFile("README.md", TestPackageFactory.WriteTemp(temp.Path, "README.md", "# x"));
        var exception = Assert.Throws<PackageFormatException>(() => staging.Build(temp.Combine("bad-locale.fbp")));
        Assert.Contains("Locale bundle consistency", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Packager_rejects_checksums_inside_content()
    {
        using var temp = TempDirectory.Create();
        Directory.CreateDirectory(temp.Combine("content"));
        TestPackageFactory.WriteTemp(temp.Combine("content"), "manifest.json", TestPackageFactory.MinimalManifestJson());
        Assert.Throws<PackageFormatException>(() => FbpPackager.PackDirectory(temp.Combine("content"), temp.Combine("x.fbp")));
        File.WriteAllText(temp.Combine("content", "checksums.json"), "{}");
        Assert.Throws<PackageFormatException>(() => FbpPackager.PackDirectory(temp.Combine("content"), temp.Combine("y.fbp")));
    }

    [Fact]
    public async Task Extraction_fails_when_checksums_are_tampered()
    {
        using var temp = TempDirectory.Create();
        var staging = temp.Combine("content");
        Directory.CreateDirectory(staging);
        TestPackageFactory.WriteTemp(staging, "manifest.json", TestPackageFactory.MinimalManifestJson());
        Directory.CreateDirectory(Path.Combine(staging, "plugin"));
        File.WriteAllText(Path.Combine(staging, "plugin", "Fake.Plugin.dll"), "payload");
        Directory.CreateDirectory(Path.Combine(staging, "locales"));
        TestPackageFactory.WriteTemp(Path.Combine(staging, "locales"), "en-US.json", TestPackageFactory.LocaleJson("en-US", ("fixture.body", "b")));
        TestPackageFactory.WriteTemp(staging, "LICENSE", "MIT");
        TestPackageFactory.WriteTemp(staging, "README.md", "# x");
        var package = temp.Combine("valid.fbp");
        FbpPackager.PackDirectory(staging, package);

        // Rebuild the ZIP with a modified entry (checksums no longer match the files).
        var tampered = temp.Combine("tampered.fbp");
        using (var input = File.OpenRead(package))
        using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
        using (var output = new FileStream(tampered, FileMode.Create))
        using (var rewritten = new ZipArchive(output, ZipArchiveMode.Create))
        {
            foreach (var entry in archive.Entries)
            {
                var newEntry = rewritten.CreateEntry(entry.FullName);
                using var reader = new StreamReader(entry.Open());
                using var writer = new StreamWriter(newEntry.Open());
                var content = reader.ReadToEnd();
                if (entry.FullName == "plugin/Fake.Plugin.dll")
                {
                    content = "tampered payload";
                }

                writer.Write(content);
            }
        }

        await Assert.ThrowsAsync<PackageFormatException>(() =>
            FbpPackageReader.ExtractFileAsync(tampered, temp.Combine("extract")));
    }
}

public sealed class SignatureTests
{
    [Fact]
    public async Task Valid_signature_verifies()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, signature) = TestPackageFactory.CreateSignedPackage(temp.Path, keys, includeAuthorSignature: true);
        var verifier = new PackageSignatureVerifier(keys.Trust);
        var result = await verifier.VerifyFileAsync(package, signature, new PackageVerificationRequest
        {
            RequireMarketplaceSignature = true,
            ExpectedPublisherKeyId = keys.Author.KeyId,
        });

        Assert.True(result.Verified, string.Join("; ", result.Errors));
        Assert.Contains(SignatureRole.Marketplace, result.VerifiedRoles);
        Assert.Equal(keys.Author.KeyId, result.VerifiedPublisherKeyId);
    }

    [Fact]
    public async Task Tampered_package_bytes_are_rejected()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, signature) = TestPackageFactory.CreateSignedPackage(temp.Path, keys);
        var original = File.ReadAllBytes(package);
        var tamperedBytes = (byte[])original.Clone();
        tamperedBytes[tamperedBytes.Length - 30] ^= 0xFF;
        File.WriteAllBytes(package, tamperedBytes);

        var verifier = new PackageSignatureVerifier(keys.Trust);
        var result = await verifier.VerifyFileAsync(package, signature);
        Assert.False(result.Verified);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Tampered_signature_value_is_rejected()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, signature) = TestPackageFactory.CreateSignedPackage(temp.Path, keys);
        var tampered = signature with
        {
            Signatures =
            [
                signature.Signatures[0] with { Value = Convert.ToBase64String(new byte[64]) },
            ],
        };

        var verifier = new PackageSignatureVerifier(keys.Trust);
        var hash = FbpPackager.ComputeSha256(package);
        var result = verifier.Verify(hash, new FileInfo(package).Length, tampered);
        Assert.False(result.Verified);
        Assert.Contains(result.Errors, error => error.Contains("invalid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unknown_signing_key_is_rejected()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, signature) = TestPackageFactory.CreateSignedPackage(temp.Path, keys);
        var untrusted = new MarketplaceTrustOptions(); // no trusted keys at all
        var verifier = new PackageSignatureVerifier(untrusted);
        var result = verifier.Verify(FbpPackager.ComputeSha256(package), new FileInfo(package).Length, signature);
        Assert.False(result.Verified);
        Assert.Contains(result.Errors, error => error.Contains("not trusted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Revoked_key_is_rejected()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, signature) = TestPackageFactory.CreateSignedPackage(temp.Path, keys);
        var revoked = new MarketplaceTrustOptions
        {
            MarketplaceKeys = [keys.Marketplace with { RevokedAt = DateTimeOffset.UtcNow.AddDays(-1) }],
        };
        var verifier = new PackageSignatureVerifier(revoked);
        var result = verifier.Verify(FbpPackager.ComputeSha256(package), new FileInfo(package).Length, signature);
        Assert.False(result.Verified);
        Assert.Contains(result.Errors, error => error.Contains("revoked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_marketplace_signature_fails_closed()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, _) = TestPackageFactory.CreateSignedPackage(temp.Path, keys);
        var verifier = new PackageSignatureVerifier(keys.Trust);
        var result = verifier.Verify(FbpPackager.ComputeSha256(package), new FileInfo(package).Length, signatureDocument: null);
        Assert.False(result.Verified);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Pinned_publisher_requires_author_signature()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, signature) = TestPackageFactory.CreateSignedPackage(temp.Path, keys, includeAuthorSignature: false);
        var verifier = new PackageSignatureVerifier(keys.Trust);
        var result = verifier.Verify(FbpPackager.ComputeSha256(package), new FileInfo(package).Length, signature, new PackageVerificationRequest
        {
            ExpectedPublisherKeyId = keys.Author.KeyId,
        });
        Assert.False(result.Verified);
        Assert.Contains(result.Errors, error => error.Contains("pinned publisher", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Node_sign_package_file_writes_detached_document()
    {
        using var temp = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, _) = TestPackageFactory.CreateSignedPackage(temp.Path, keys);
        var signaturePath = temp.Combine("signature.json");
        using var author = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = await PackageSigner.SignPackageFileAsync(package, signaturePath, author, "author-key", SignatureRole.Author);
        Assert.True(File.Exists(signaturePath));
        Assert.Equal(document.Package.Sha256, FbpPackager.ComputeSha256(package));
        Assert.Equal(SignatureRole.Author, Assert.Single(document.Signatures).Role);
    }
}

public sealed class IndexTests
{
    [Fact]
    public void Signed_index_verifies()
    {
        using var keys = new TestKeys();
        var index = TestPackageFactory.CreateIndex(keys);
        var json = TestPackageFactory.CreateSignedIndexJson(keys, index);
        var verifier = new IndexSignatureVerifier(keys.Trust);
        var result = verifier.Verify(json);
        Assert.True(result.Verified, string.Join("; ", result.Errors));
        Assert.Contains(keys.Marketplace.KeyId, result.VerifiedKeyIds);
    }

    [Fact]
    public void Canonical_json_is_order_independent_and_excludes_signatures()
    {
        var first = """{"b":2,"a":{"z":"x","y":[3,2,1]},"signatures":[{"keyId":"k"}]}""";
        var second = """{"signatures":[],"a":{"y":[3,2,1],"z":"x"},"b":2}""";
        var canonicalFirst = CanonicalJson.CanonicalizeToString(first, "signatures");
        var canonicalSecond = CanonicalJson.CanonicalizeToString(second, "signatures");
        Assert.Equal(canonicalFirst, canonicalSecond);
        Assert.DoesNotContain("signatures", canonicalFirst, StringComparison.Ordinal);
        Assert.StartsWith("{\"a\":", canonicalFirst, StringComparison.Ordinal);
    }

    [Fact]
    public void Tampered_index_payload_is_rejected()
    {
        using var keys = new TestKeys();
        var index = TestPackageFactory.CreateIndex(keys);
        var json = TestPackageFactory.CreateSignedIndexJson(keys, index);
        var tampered = json.Replace("A test plugin.", "A tampered plugin.", StringComparison.Ordinal);
        var verifier = new IndexSignatureVerifier(keys.Trust);
        var result = verifier.Verify(tampered);
        Assert.False(result.Verified);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Index_rollback_is_rejected()
    {
        using var keys = new TestKeys();
        var index = TestPackageFactory.CreateIndex(keys);
        var json = TestPackageFactory.CreateSignedIndexJson(keys, index);
        var verifier = new IndexSignatureVerifier(keys.Trust);
        var result = verifier.Verify(json, minimumGeneratedAt: index.GeneratedAt.AddMinutes(1));
        Assert.False(result.Verified);
        Assert.Contains(result.Errors, error => error.Contains("rollback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Future_index_timestamp_is_rejected()
    {
        using var keys = new TestKeys();
        var future = TestPackageFactory.CreateIndex(keys) with { GeneratedAt = DateTimeOffset.UtcNow.AddDays(3) };
        var json = TestPackageFactory.CreateSignedIndexJson(keys, future);
        var verifier = new IndexSignatureVerifier(keys.Trust);
        var result = verifier.Verify(json);
        Assert.False(result.Verified);
        Assert.Contains(result.Errors, error => error.Contains("future", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unknown_signing_key_is_rejected()
    {
        using var keys = new TestKeys();
        var index = TestPackageFactory.CreateIndex(keys);
        var json = TestPackageFactory.CreateSignedIndexJson(keys, index);
        var verifier = new IndexSignatureVerifier(MarketplaceTrustOptions.Empty);
        var result = verifier.Verify(json);
        Assert.False(result.Verified);
    }

    [Fact]
    public void Invalid_index_structure_is_rejected()
    {
        Assert.Throws<MarketplaceIndexException>(() => MarketplaceIndexParser.Parse("""{"schemaVersion":1}"""));
    }

    [Fact]
    public void Valid_index_parses()
    {
        using var keys = new TestKeys();
        var index = TestPackageFactory.CreateIndex(keys);
        var json = TestPackageFactory.CreateSignedIndexJson(keys, index);
        var parsed = MarketplaceIndexParser.Parse(json);
        Assert.Single(parsed.Plugins!);
        Assert.Equal("sample-minimal", parsed.Plugins![0].Id);
        Assert.NotNull(parsed.Plugins[0].Latest!.Artifact);
        Assert.Equal(PluginChannel.Stable, parsed.Plugins[0].Latest!.Channel);
    }

    [Fact]
    public void Compatibility_filter_works()
    {
        using var keys = new TestKeys();
        var entry = TestPackageFactory.CreateIndex(keys).Plugins![0];
        Assert.True(MarketplaceIndexValidator.IsCompatible(entry, new FluidBar.Sdk.Versioning.SemanticVersion(2, 0, 0), new FluidBar.Sdk.Versioning.SemanticVersion(2, 0, 0)));
        Assert.False(MarketplaceIndexValidator.IsCompatible(entry, new FluidBar.Sdk.Versioning.SemanticVersion(1, 0, 0), new FluidBar.Sdk.Versioning.SemanticVersion(2, 0, 0)));
    }

    [Fact]
    public void History_shard_parses()
    {
        var json = """
            {
              "schemaVersion": 1,
              "id": "sample-minimal",
              "versions": [
                {
                  "version": "1.0.0",
                  "apiVersion": "2.0.0",
                  "minAppVersion": "2.0.0",
                  "publishedAt": "2026-10-18T00:00:00Z",
                  "channel": "stable",
                  "yanked": false,
                  "artifact": {
                    "url": "https://github.com/x/y/releases/download/v1/sample.fbp",
                    "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    "size": 10,
                    "signatureUrl": "https://github.com/x/y/releases/download/v1/signature.json",
                    "keyId": "k",
                    "algorithm": "ECDSA-P256-SHA256"
                  },
                  "capabilities": ["ui"],
                  "locales": ["en-US"]
                }
              ]
            }
            """;
        var shard = MarketplaceIndexParser.ParseShard(json);
        Assert.Equal("sample-minimal", shard.Id);
        Assert.Single(shard.Versions!);
    }
}
