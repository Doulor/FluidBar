using FluidBar.Plugins.Builtin;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.App.Plugins;

/// <summary>
/// Builds the synthetic manifest for a compiled-in plugin. The App owns the descriptor (id, name,
/// capabilities, declarative contributions), so the manifest is trusted by construction and is never
/// validated or inspected like a downloaded package.
/// </summary>
public static class BuiltinPluginManifests
{
    /// <summary>Creates the manifest for <paramref name="descriptor"/>.</summary>
    public static PluginManifest Create(BuiltinPluginDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new PluginManifest
        {
            SchemaVersion = 2,
            Id = descriptor.Id,
            Version = "2.0.0",
            ApiVersion = PluginApiVersion.Current.ToString(),
            MinAppVersion = "2.0.0",
            Publisher = new PublisherInfo
            {
                Id = "fluidbar",
                DisplayName = "FluidBar",
                Url = "https://fluidbar.app",
            },
            Name = descriptor.DisplayName,
            Summary = LocalizedString.From("Built-in FluidBar plugin."),
            License = "MIT",
            Categories = [descriptor.Category],
            Entry = new PluginEntryPoint
            {
                Runtime = PluginEntryRuntime.InProcess,
                Assembly = "FluidBar.Plugins.Builtin.dll",
                Type = descriptor.Id,
                TargetFramework = "net10.0",
            },
            Platforms = ["win-x64"],
            Activation = ["startup"],
            Contributes = descriptor.Contributions,
        };
    }
}
