using FluidBar.Sdk.Versioning;
using FluidBar.App.Settings;

namespace FluidBar.App.Plugins;

internal static class PluginRuntimeVersion
{
    internal static SemanticVersion Current
    {
        get
        {
            if (SemanticVersion.TryParse(AppUpdateCoordinator.InformationalVersion(), out var version)) return version;
            var assemblyVersion = typeof(PluginRuntimeVersion).Assembly.GetName().Version!;
            return new SemanticVersion(assemblyVersion.Major, assemblyVersion.Minor, Math.Max(0, assemblyVersion.Build));
        }
    }
}
