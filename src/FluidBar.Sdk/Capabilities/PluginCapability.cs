using System.Collections.Immutable;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Sdk.Capabilities;

/// <summary>
/// Declared plugin capabilities. For in-process plugins these are <b>disclosure and consent</b>, not a
/// sandbox: an in-process .NET plugin runs with the user's full rights. The same enum is designed to be
/// enforceable by the v2.1 out-of-process broker.
/// </summary>
public enum PluginCapability
{
    Network = 0,
    Clipboard = 1,
    Files = 2,
    Media = 3,
    Notifications = 4,
    Shell = 5,
    Hotkeys = 6,
    Process = 7,
    Registry = 8,
    Telemetry = 9,
    Background = 10,
    Ui = 11,
}

/// <summary>Helpers that map the manifest capability block to the disclosure/consent enum.</summary>
public static class PluginCapabilityExtensions
{
    private static readonly ImmutableHashSet<PluginCapability> HighRiskCapabilities =
    [
        PluginCapability.Network,
        PluginCapability.Files,
        PluginCapability.Shell,
        PluginCapability.Process,
        PluginCapability.Registry,
        PluginCapability.Hotkeys,
    ];

    private static readonly ImmutableDictionary<PluginCapability, string> ManifestNames =
        new Dictionary<PluginCapability, string>
        {
            [PluginCapability.Network] = "network",
            [PluginCapability.Clipboard] = "clipboard",
            [PluginCapability.Files] = "files",
            [PluginCapability.Media] = "media",
            [PluginCapability.Notifications] = "notifications",
            [PluginCapability.Shell] = "shell",
            [PluginCapability.Hotkeys] = "hotkeys",
            [PluginCapability.Process] = "process",
            [PluginCapability.Registry] = "registry",
            [PluginCapability.Telemetry] = "telemetry",
            [PluginCapability.Background] = "background",
            [PluginCapability.Ui] = "ui",
        }.ToImmutableDictionary();

    /// <summary>The manifest property name for a capability.</summary>
    public static string ToManifestName(this PluginCapability capability) =>
        ManifestNames.TryGetValue(capability, out var name)
            ? name
            : throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown capability.");

    /// <summary>Capabilities that require an explicit written reason and a security-reviewed install.</summary>
    public static bool IsHighRisk(this PluginCapability capability) => HighRiskCapabilities.Contains(capability);

    /// <summary>
    /// Returns the capabilities actually declared by a manifest. A capability object with no meaningful
    /// flags still counts as declared: declaring it is a disclosure decision.
    /// </summary>
    public static ImmutableArray<PluginCapability> GetDeclaredCapabilities(this PluginCapabilities? capabilities)
    {
        if (capabilities is null)
        {
            return ImmutableArray<PluginCapability>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<PluginCapability>();
        if (capabilities.Network is not null)
        {
            builder.Add(PluginCapability.Network);
        }

        if (capabilities.Clipboard is not null)
        {
            builder.Add(PluginCapability.Clipboard);
        }

        if (capabilities.Files is not null)
        {
            builder.Add(PluginCapability.Files);
        }

        if (capabilities.Media is not null)
        {
            builder.Add(PluginCapability.Media);
        }

        if (capabilities.Notifications is not null)
        {
            builder.Add(PluginCapability.Notifications);
        }

        if (capabilities.Shell is not null)
        {
            builder.Add(PluginCapability.Shell);
        }

        if (capabilities.Hotkeys is not null)
        {
            builder.Add(PluginCapability.Hotkeys);
        }

        if (capabilities.Process is not null)
        {
            builder.Add(PluginCapability.Process);
        }

        if (capabilities.Registry is not null)
        {
            builder.Add(PluginCapability.Registry);
        }

        if (capabilities.Telemetry is not null)
        {
            builder.Add(PluginCapability.Telemetry);
        }

        if (capabilities.Background is not null)
        {
            builder.Add(PluginCapability.Background);
        }

        if (capabilities.Ui is not null)
        {
            builder.Add(PluginCapability.Ui);
        }

        return builder.ToImmutable();
    }

    /// <summary>Portable capability names (manifest property names) declared by the manifest.</summary>
    public static ImmutableArray<string> GetDeclaredCapabilityNames(this PluginCapabilities? capabilities) =>
        [.. capabilities.GetDeclaredCapabilities().Select(static capability => capability.ToManifestName())];

    /// <summary>
    /// Returns <see langword="true"/> when the manifest declares a wildcard network allowlist, which is a
    /// security-reviewed capability and must carry a reason.
    /// </summary>
    public static bool HasWildcardNetwork(this PluginCapabilities? capabilities) =>
        capabilities?.Network?.Domains?.Any(
            static domain => domain == "*" || domain.StartsWith("*.", StringComparison.Ordinal)) == true;

    /// <summary>
    /// Returns <see langword="true"/> when the manifest requests file write access outside the plugin data /
    /// install directories.
    /// </summary>
    public static bool WritesOutsidePluginData(this PluginCapabilities? capabilities)
    {
        var writeScopes = capabilities?.Files?.Write;
        if (writeScopes is null)
        {
            return false;
        }

        return writeScopes.Any(static scope => scope is not (FileScope.PluginData or FileScope.PluginInstall or FileScope.Temp));
    }
}
