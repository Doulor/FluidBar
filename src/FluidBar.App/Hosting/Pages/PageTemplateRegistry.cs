using FluidBar.Sdk.Pages;

namespace FluidBar.App.Hosting.Pages;

/// <summary>
/// Single dispatch table for host page templates. Home and media are host-private native templates;
/// list/calendar/stat-grid are shared templates. Plugin manifests may only opt into the shared list
/// template in this build (M0/M1 disclosure rule).
/// </summary>
public static class PageTemplateRegistry
{
    /// <summary>Host-private native templates rendered by the island renderer.</summary>
    public static IReadOnlyList<string> HostNativeTemplates { get; } = ["builtin.home", "builtin.media"];

    /// <summary>Templates a plugin package may declare.</summary>
    public static IReadOnlyList<string> PluginTemplates { get; } = [PageTemplates.List];

    /// <summary>Returns true for a host-private native template.</summary>
    public static bool IsHostNative(string templateId)
        => HostNativeTemplates.Contains(templateId, StringComparer.Ordinal);

    /// <summary>Returns true when a plugin package may declare the template.</summary>
    public static bool IsPluginTemplateAllowed(string templateId)
        => PluginTemplates.Contains(templateId, StringComparer.Ordinal);
}
