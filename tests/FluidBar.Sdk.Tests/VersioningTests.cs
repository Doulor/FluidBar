using FluidBar.Sdk.Versioning;

namespace FluidBar.Sdk.Tests;

public sealed class VersioningTests
{
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("0.0.1")]
    [InlineData("2.0.0-alpha.1")]
    [InlineData("2.0.0+build.5")]
    [InlineData("2.0.0-rc.1+build.5")]
    public void Parses_valid_semver(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out var version));
        Assert.Equal(value, version.ToString());
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3.4")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-version")]
    public void Rejects_invalid_semver(string? value)
    {
        Assert.False(SemanticVersion.TryParse(value, out _));
    }

    [Fact]
    public void Precedence_follows_semver()
    {
        Assert.True(SemanticVersion.Parse("1.0.0-alpha") < SemanticVersion.Parse("1.0.0"));
        Assert.True(SemanticVersion.Parse("1.0.0-alpha.1") < SemanticVersion.Parse("1.0.0-alpha.2"));
        Assert.True(SemanticVersion.Parse("1.0.0-alpha.2") < SemanticVersion.Parse("1.0.0-beta.1"));
        Assert.True(SemanticVersion.Parse("1.0.0") < SemanticVersion.Parse("1.0.1"));
        // Build metadata is ignored for precedence.
        Assert.Equal(0, SemanticVersion.Parse("1.0.0+build.1").CompareTo(SemanticVersion.Parse("1.0.0+build.2")));
    }

    [Theory]
    [InlineData("^2.0.0", "2.0.0", true)]
    [InlineData("^2.0.0", "2.9.9", true)]
    [InlineData("^2.0.0", "3.0.0", false)]
    [InlineData("~2.1.0", "2.1.5", true)]
    [InlineData("~2.1.0", "2.2.0", false)]
    [InlineData(">=2.0.0 <2.5.0", "2.4.9", true)]
    [InlineData(">=2.0.0 <2.5.0", "2.5.0", false)]
    [InlineData("2.0.0", "2.0.0", true)]
    [InlineData("2.0.0", "2.0.1", false)]
    [InlineData("*", "9.9.9", true)]
    [InlineData("^1.0.0 || ^2.0.0", "2.3.4", true)]
    public void Range_parsing_and_matching(string range, string version, bool expected)
    {
        Assert.True(SemanticVersionRange.TryParse(range, out var parsed), $"Range '{range}' should parse.");
        Assert.Equal(expected, parsed!.IsSatisfiedBy(SemanticVersion.Parse(version)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("^x.y.z")]
    public void Invalid_ranges_are_rejected(string value)
    {
        Assert.False(SemanticVersionRange.TryParse(value, out _));
    }

    [Fact]
    public void Api_compatibility_requires_same_major()
    {
        var host = new SemanticVersion(2, 0, 0);
        Assert.True(PluginApiVersion.IsApiVersionSupported(new SemanticVersion(2, 1, 0)));
        Assert.False(PluginApiVersion.IsApiVersionSupported(new SemanticVersion(3, 0, 0)));
        Assert.False(PluginApiVersion.IsApiVersionSupported(new SemanticVersion(1, 9, 0)));

        var compatible = PluginApiVersion.CheckCompatibility(
            new SemanticVersion(2, 0, 0),
            new SemanticVersion(2, 0, 0),
            null,
            host,
            new SemanticVersion(2, 0, 0));
        Assert.True(compatible.IsCompatible);

        var tooOld = PluginApiVersion.CheckCompatibility(
            new SemanticVersion(2, 0, 0),
            new SemanticVersion(2, 5, 0),
            null,
            host,
            new SemanticVersion(2, 0, 0));
        Assert.False(tooOld.IsCompatible);
        Assert.Equal(VersionCompatibilityFailure.AppVersionTooOld, tooOld.Failure);

        var tooNew = PluginApiVersion.CheckCompatibility(
            new SemanticVersion(2, 0, 0),
            new SemanticVersion(2, 0, 0),
            new SemanticVersion(2, 2, 0),
            host,
            new SemanticVersion(2, 3, 0));
        Assert.False(tooNew.IsCompatible);
        Assert.Equal(VersionCompatibilityFailure.AppVersionTooNew, tooNew.Failure);
    }

    [Fact]
    public void Sdk_dependency_range_is_checked_against_sdk_version()
    {
        Assert.True(PluginApiVersion.CheckSdkDependency("^2.0.0", new SemanticVersion(2, 3, 0)).IsCompatible);
        Assert.False(PluginApiVersion.CheckSdkDependency("^3.0.0", new SemanticVersion(2, 3, 0)).IsCompatible);
        Assert.False(PluginApiVersion.CheckSdkDependency("not a range", new SemanticVersion(2, 3, 0)).IsCompatible);
    }
}
