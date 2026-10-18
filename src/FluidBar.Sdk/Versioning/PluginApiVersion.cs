namespace FluidBar.Sdk.Versioning;

/// <summary>
/// The compatibility policy between the FluidBar host, the SDK ABI (<c>apiVersion</c>) and the plugin
/// manifest (<c>minAppVersion</c> / <c>maxAppVersion</c>). The host supports one SDK major version at a
/// time; breaking SDK changes bump the major.
/// </summary>
public static class PluginApiVersion
{
    /// <summary>The SDK ABI version implemented by this build of <c>FluidBar.Sdk</c>.</summary>
    public static SemanticVersion Current { get; } = new(2, 0, 0);

    /// <summary>The SDK major version accepted by this host major.</summary>
    public const int SupportedMajor = 2;

    /// <summary>Manifest schema version understood by this SDK build.</summary>
    public const int SupportedManifestSchemaVersion = 2;

    /// <summary>Returns <see langword="true"/> when a plugin built against <paramref name="apiVersion"/> can load.</summary>
    public static bool IsApiVersionSupported(SemanticVersion apiVersion) => apiVersion.Major == SupportedMajor;

    /// <summary>
    /// Full load/install gate: SDK ABI major, minimum app version and optional maximum app version.
    /// </summary>
    public static VersionCompatibility CheckCompatibility(
        SemanticVersion apiVersion,
        SemanticVersion minAppVersion,
        SemanticVersion? maxAppVersion,
        SemanticVersion hostApiVersion,
        SemanticVersion appVersion)
    {
        if (!IsApiVersionSupported(apiVersion) || hostApiVersion.Major != SupportedMajor)
        {
            return new VersionCompatibility(
                false,
                VersionCompatibilityFailure.ApiMajorUnsupported,
                $"Plugin apiVersion {apiVersion} is not supported; this FluidBar build implements apiVersion {hostApiVersion.Major}.x.");
        }

        if (appVersion < minAppVersion)
        {
            return new VersionCompatibility(
                false,
                VersionCompatibilityFailure.AppVersionTooOld,
                $"Plugin requires FluidBar {minAppVersion} or later; running {appVersion}.");
        }

        if (maxAppVersion is { } maximum && appVersion > maximum)
        {
            return new VersionCompatibility(
                false,
                VersionCompatibilityFailure.AppVersionTooNew,
                $"Plugin supports FluidBar up to {maximum}; running {appVersion}.");
        }

        return new VersionCompatibility(true, VersionCompatibilityFailure.None, string.Empty);
    }

    /// <summary>Checks the manifest <c>dependencies.sdk</c> range against the current SDK ABI.</summary>
    public static VersionCompatibility CheckSdkDependency(string? sdkRange, SemanticVersion sdkVersion)
    {
        if (string.IsNullOrWhiteSpace(sdkRange))
        {
            return new VersionCompatibility(true, VersionCompatibilityFailure.None, string.Empty);
        }

        if (!SemanticVersionRange.TryParse(sdkRange, out var range))
        {
            return new VersionCompatibility(
                false,
                VersionCompatibilityFailure.InvalidVersion,
                $"Dependency range '{sdkRange}' is not supported syntax.");
        }

        return range!.IsSatisfiedBy(sdkVersion)
            ? new VersionCompatibility(true, VersionCompatibilityFailure.None, string.Empty)
            : new VersionCompatibility(
                false,
                VersionCompatibilityFailure.SdkDependencyUnsatisfied,
                $"Plugin requires FluidBar.Sdk '{sdkRange}' but the host provides {sdkVersion}.");
    }
}

/// <summary>The reason a plugin is incompatible with the running host.</summary>
public enum VersionCompatibilityFailure
{
    None = 0,
    ApiMajorUnsupported,
    AppVersionTooOld,
    AppVersionTooNew,
    SdkDependencyUnsatisfied,
    InvalidVersion,
}

/// <summary>Outcome of an API/app compatibility check.</summary>
public readonly record struct VersionCompatibility(bool IsCompatible, VersionCompatibilityFailure Failure, string Reason)
{
    /// <summary>A successful compatibility result.</summary>
    public static VersionCompatibility Compatible { get; } = new(true, VersionCompatibilityFailure.None, string.Empty);
}
