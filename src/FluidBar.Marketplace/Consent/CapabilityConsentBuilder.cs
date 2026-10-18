using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Marketplace.Consent;

/// <summary>
/// One plain-language capability row for the install consent sheet. The host localizes
/// <see cref="TitleKey"/>/<see cref="DescriptionKey"/> and appends <see cref="Details"/> (domains, hotkeys,
/// file scopes, reason) verbatim from the reviewed manifest.
/// </summary>
public sealed record CapabilityConsentItem(
    PluginCapability Capability,
    string TitleKey,
    string DescriptionKey,
    bool IsHighRisk,
    string? Reason,
    IReadOnlyList<string> Details);

/// <summary>
/// The complete install-consent data model: capability rows plus the mandatory "plugins run with the same
/// access as FluidBar; capabilities are disclosure, not a sandbox" warning key.
/// </summary>
public sealed record CapabilityConsentData(
    string PluginId,
    LocalizedString? DisplayName,
    IReadOnlyList<CapabilityConsentItem> Items,
    string WarningKey)
{
    /// <summary>The un-translated English security statement that must appear on the consent sheet.</summary>
    public const string SecurityStatement =
        "Plugins run with the same access as FluidBar. Only install plugins you trust. " +
        "Capabilities are disclosed for your consent; they are not a sandbox.";
}

/// <summary>Maps a reviewed manifest to the consent UI data model. No enforcement is claimed or implied.</summary>
public static class CapabilityConsentBuilder
{
    private const string WarningKeyName = "Market.Consent.SameAccessWarning";

    /// <summary>Builds consent data for a manifest.</summary>
    public static CapabilityConsentData Build(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var items = new List<CapabilityConsentItem>();
        var capabilities = manifest.Capabilities;
        if (capabilities is not null)
        {
            if (capabilities.Network is { } network)
            {
                items.Add(Item(
                    PluginCapability.Network,
                    "Market.Consent.Network.Title",
                    "Market.Consent.Network.Description",
                    network.Reason,
                    [.. (network.Domains ?? []), .. (network.Methods?.Select(static method => method.ToString()) ?? [])]));
            }

            if (capabilities.Clipboard is { } clipboard)
            {
                var accesses = new List<string>();
                if (clipboard.Read)
                {
                    accesses.Add("read");
                }

                if (clipboard.Write)
                {
                    accesses.Add("write");
                }

                items.Add(Item(PluginCapability.Clipboard, "Market.Consent.Clipboard.Title", "Market.Consent.Clipboard.Description", clipboard.Reason, accesses));
            }

            if (capabilities.Files is { } files)
            {
                items.Add(Item(
                    PluginCapability.Files,
                    "Market.Consent.Files.Title",
                    "Market.Consent.Files.Description",
                    files.Reason,
                    [
                        .. (files.Read ?? []).Select(static scope => "read:" + scope),
                        .. (files.Write ?? []).Select(static scope => "write:" + scope),
                    ]));
            }

            if (capabilities.Media is { } media && (media.Sessions || media.Control))
            {
                items.Add(Item(
                    PluginCapability.Media,
                    "Market.Consent.Media.Title",
                    "Market.Consent.Media.Description",
                    media.Reason,
                    [media.Sessions ? "sessions" : "no sessions", media.Control ? "control" : "no control"]));
            }

            if (capabilities.Notifications is { } notifications)
            {
                items.Add(Item(
                    PluginCapability.Notifications,
                    "Market.Consent.Notifications.Title",
                    "Market.Consent.Notifications.Description",
                    reason: null,
                    [notifications.Read ? "read" : "no read", notifications.Post ? "post" : "no post"]));
            }

            if (capabilities.Shell is { } shell)
            {
                items.Add(Item(
                    PluginCapability.Shell,
                    "Market.Consent.Shell.Title",
                    "Market.Consent.Shell.Description",
                    shell.Reason,
                    [shell.Open ? "open" : "no open", shell.Execute ? "execute" : "no execute"]));
            }

            if (capabilities.Hotkeys is { } hotkeys && hotkeys.Global is { Count: > 0 } global)
            {
                items.Add(Item(
                    PluginCapability.Hotkeys,
                    "Market.Consent.Hotkeys.Title",
                    "Market.Consent.Hotkeys.Description",
                    hotkeys.Reason,
                    [.. global]));
            }

            if (capabilities.Process is { } process)
            {
                items.Add(Item(
                    PluginCapability.Process,
                    "Market.Consent.Process.Title",
                    "Market.Consent.Process.Description",
                    process.Reason,
                    [process.Spawn ? "spawn" : "no spawn"]));
            }

            if (capabilities.Registry is { } registry)
            {
                items.Add(Item(
                    PluginCapability.Registry,
                    "Market.Consent.Registry.Title",
                    "Market.Consent.Registry.Description",
                    registry.Reason,
                    [
                        .. (registry.Read ?? []).Select(static path => "read:" + path),
                        .. (registry.Write ?? []).Select(static path => "write:" + path),
                    ]));
            }

            if (capabilities.Telemetry is { } telemetry && telemetry.Enabled)
            {
                items.Add(Item(
                    PluginCapability.Telemetry,
                    "Market.Consent.Telemetry.Title",
                    "Market.Consent.Telemetry.Description",
                    telemetry.Reason,
                    [telemetry.Endpoint ?? "no endpoint"]));
            }

            if (capabilities.Background is { } background)
            {
                items.Add(Item(
                    PluginCapability.Background,
                    "Market.Consent.Background.Title",
                    "Market.Consent.Background.Description",
                    background.Reason,
                    [background.IntervalMinMs is { } interval ? $"min interval {interval} ms" : "no interval declared"]));
            }

            if (capabilities.Ui is not null)
            {
                items.Add(Item(
                    PluginCapability.Ui,
                    "Market.Consent.Ui.Title",
                    "Market.Consent.Ui.Description",
                    reason: null,
                    ["declarative host-rendered UI only"]));
            }
        }

        return new CapabilityConsentData(manifest.Id, manifest.Name, items, WarningKeyName);
    }

    private static CapabilityConsentItem Item(
        PluginCapability capability,
        string titleKey,
        string descriptionKey,
        string? reason,
        IEnumerable<string> details) =>
        new(capability, titleKey, descriptionKey, capability.IsHighRisk(), reason, [.. details]);
}
