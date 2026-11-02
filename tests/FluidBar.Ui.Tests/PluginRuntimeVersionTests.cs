using FluidBar.App.Plugins;

namespace FluidBar.Ui.Tests;

public sealed class PluginRuntimeVersionTests
{
    [Fact]
    public void Plugin_compatibility_uses_the_running_host_version_instead_of_a_legacy_constant()
    {
        var assemblyVersion = typeof(PluginRuntime).Assembly.GetName().Version!;
        var current = PluginRuntimeVersion.Current;
        Assert.Equal(assemblyVersion.Major, current.Major);
        Assert.Equal(assemblyVersion.Minor, current.Minor);
        Assert.Equal(assemblyVersion.Build, current.Patch);
    }
}
