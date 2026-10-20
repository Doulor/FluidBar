using FluidBar.Sdk.Pages;

namespace FluidBar.App.Hosting.Pages;

/// <summary>
/// Single dispatch table for host page templates. Home and media are host-private native templates;
/// list/calendar/stat-grid are shared templates. Plugin manifests may opt into the shared list or
/// the M3 command-canvas custom template.
/// </summary>
public static class PageTemplateRegistry
{
    /// <summary>Host-private native templates rendered by the island renderer.</summary>
    public static IReadOnlyList<string> HostNativeTemplates { get; } = ["builtin.home", "builtin.media"];

    /// <summary>Templates a plugin package may declare.</summary>
    public static IReadOnlyList<string> PluginTemplates { get; } = [PageTemplates.List, PageTemplates.Custom];

    /// <summary>Returns true for a host-private native template.</summary>
    public static bool IsHostNative(string templateId)
        => HostNativeTemplates.Contains(templateId, StringComparer.Ordinal);

    /// <summary>Returns true when a plugin package may declare the template.</summary>
    public static bool IsPluginTemplateAllowed(string templateId)
        => PluginTemplates.Contains(templateId, StringComparer.Ordinal);
}
