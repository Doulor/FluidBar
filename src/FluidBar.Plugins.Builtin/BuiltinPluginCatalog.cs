using FluidBar.Sdk;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Plugins.Builtin;

/// <summary>Descriptor the App uses to register a first-party plugin (id, capabilities, contributions, factory).</summary>
public sealed record BuiltinPluginDescriptor(
    string Id,
    LocalizedString DisplayName,
    PluginCategory Category,
    IReadOnlyList<PluginCapability> Capabilities,
    PluginContributions Contributions,
    Func<BuiltinPlatformServices, IFluidBarPlugin?> Factory);

/// <summary>A descriptor paired with a constructed plugin instance.</summary>
public sealed record BuiltinPluginInstance(BuiltinPluginDescriptor Descriptor, IFluidBarPlugin Plugin);

/// <summary>
/// The first-party plugin catalog: descriptors for registration + a factory that maps platform services to
/// plugin instances. Plugins whose required platform services are unavailable are skipped (never
/// half-initialized), so a missing radio/permission cannot crash host startup.
/// </summary>
public static class BuiltinPluginCatalog
{
    /// <summary>All built-in plugin descriptors (stable order for UI registration).</summary>
    public static IReadOnlyList<BuiltinPluginDescriptor> Descriptors { get; } =
    [
        new BuiltinPluginDescriptor(
            BuiltinIds.MediaPlugin,
            LocalizedString.From("plugin.media.name"),
            PluginCategory.Media,
            [PluginCapability.Media, PluginCapability.Background],
            BuiltinContributions.ForMedia(),
            static services => services.Media is null
                ? null
                : new MediaPlugin(services.Media, services.ArtworkTempStore, services.Lyrics)),
        new BuiltinPluginDescriptor(
            BuiltinIds.ClipboardPlugin,
            LocalizedString.From("plugin.clipboard.name"),
            PluginCategory.Productivity,
            [PluginCapability.Clipboard, PluginCapability.Background],
            BuiltinContributions.ForClipboard(),
            static services => services.Clipboard is null ? null : new ClipboardPlugin(services.Clipboard)),
        new BuiltinPluginDescriptor(
            BuiltinIds.NotificationsPlugin,
            LocalizedString.From("plugin.notifications.name"),
            PluginCategory.System,
            [PluginCapability.Notifications, PluginCapability.Background],
            BuiltinContributions.ForNotifications(),
            static services => services.Notifications is null
                ? null
                : new NotificationsPlugin(services.Notifications,
                    services.Clipboard is null
                        ? null
                        : code => services.Clipboard.SetTextAsync(code))),
        new BuiltinPluginDescriptor(
            BuiltinIds.AgentPlugin,
            LocalizedString.From("plugin.agent.name"),
            PluginCategory.Developer,
            [PluginCapability.Files, PluginCapability.Background],
            BuiltinContributions.ForAgent(),
            static services => services.AgentInbox is null || services.AgentHookInstaller is null
                ? null
                : new AgentStatusPlugin(services.AgentInbox, services.AgentHookInstaller)),
        new BuiltinPluginDescriptor(
            BuiltinIds.DevicesPlugin,
            LocalizedString.From("plugin.devices.name"),
            PluginCategory.System,
            [PluginCapability.Background],
            BuiltinContributions.ForDevices(),
            static services => services.UsbDevices is null && services.BluetoothDevices is null
                ? null
                : new DevicesPlugin(services.UsbDevices, services.BluetoothDevices)),
    ];

    /// <summary>Constructs every plugin whose platform dependencies are available.</summary>
    public static IReadOnlyList<BuiltinPluginInstance> CreateAll(BuiltinPlatformServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var instances = new List<BuiltinPluginInstance>(Descriptors.Count);
        foreach (var descriptor in Descriptors)
        {
            var plugin = descriptor.Factory(services);
            if (plugin is not null)
            {
                instances.Add(new BuiltinPluginInstance(descriptor, plugin));
            }
        }

        return instances;
    }

    /// <summary>Constructs one plugin by id, or null when unknown/unavailable.</summary>
    public static BuiltinPluginInstance? Create(BuiltinPlatformServices services, string pluginId)
    {
        ArgumentNullException.ThrowIfNull(services);
        var descriptor = Descriptors.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, pluginId, StringComparison.Ordinal));
        if (descriptor is null)
        {
            return null;
        }

        var plugin = descriptor.Factory(services);
        return plugin is null ? null : new BuiltinPluginInstance(descriptor, plugin);
    }
}
