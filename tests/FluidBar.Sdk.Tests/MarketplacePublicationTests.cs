using FluidBar.Marketplace.Index;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Sdk.Tests;

public sealed class MarketplacePublicationTests
{
    [Fact]
    public void Discovery_includes_nested_samples_and_optional_plugins_but_skips_build_outputs()
    {
        using var temporary = TempDirectory.Create();
        foreach (var relative in new[] { "timer", "samples/minimal", "timer/bin/Release/net10.0", "obj/staging", ".cache/hidden" })
        {
            var directory = temporary.Combine(relative);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{}");
        }
        var found = MarketplacePluginDiscovery.Discover(temporary.Path)
            .Select(directory => Path.GetRelativePath(temporary.Path, directory).Replace('\\', '/')).ToArray();
        Assert.Equal(["samples/minimal", "timer"], found);
    }

    [Fact]
    public void Missing_registered_plugin_fails_before_publishing_a_partial_catalog()
    {
        Assert.Throws<MarketplaceIndexException>(() =>
            MarketplacePluginDiscovery.RequirePlugins(["sample-minimal"], ["sample-minimal", "fluidbar-timer"]));
    }

    [Fact]
    public void Existing_release_is_verified_and_reused_without_repacking_or_resigning()
    {
        using var temporary = TempDirectory.Create();
        using var keys = new TestKeys();
        var (package, signature) = TestPackageFactory.CreateSignedPackage(temporary.Path, keys);
        File.WriteAllText(package + ".sig.json", signature.ToJson());
        var before = File.ReadAllBytes(package);
        var released = MarketplaceReleasedArtifact.Load(package, "sample-minimal", "1.0.0", keys.Trust);
        Assert.Equal(signature.Package.Sha256, released.Sha256);
        Assert.Equal(before, File.ReadAllBytes(package));
        Assert.Equal(signature.Signatures[0].SignedAt, released.PublishedAt);
        Assert.Throws<MarketplaceIndexException>(() => MarketplaceReleasedArtifact.Load(package, "other-plugin", "1.0.0", keys.Trust));
        File.AppendAllText(package, "tampered");
        Assert.Throws<MarketplaceIndexException>(() => MarketplaceReleasedArtifact.Load(package, "sample-minimal", "1.0.0", keys.Trust));
    }

    [Theory]
    [InlineData("fluidbar-timer")]
    [InlineData("fluidbar-file-shelf")]
    public void New_plugins_require_the_sdk_that_provides_their_host_contract(string id)
    {
        var manifest = PluginManifestParser.Parse(File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "plugins", id, "manifest.json")));
        Assert.False(PluginApiVersion.CheckSdkDependency(manifest.Dependencies?.Sdk, new SemanticVersion(2, 0, 0)).IsCompatible);
        Assert.True(PluginApiVersion.CheckSdkDependency(manifest.Dependencies?.Sdk, PluginApiVersion.Current).IsCompatible);
    }
}
