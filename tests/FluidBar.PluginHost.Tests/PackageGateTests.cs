using FluidBar.PluginHost.Loader;

namespace FluidBar.PluginHost.Tests;

/// <summary>M4 load-time package gate contract.</summary>
public sealed class PackageGateTests
{
    [Fact]
    public async Task LoadAll_RejectsAPackageWhenTheLoadGateFailsClosed()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Root);
        var (host, _, _) = HostFactory.Create(workspace.Root, options => options with
        {
            PackageGate = static (_, _) => Task.FromResult(
                PluginPackageGateResult.Reject("market signature is missing")),
        });
        await using var scope = host;

        var results = await host.LoadAllAsync();

        var failure = Assert.Single(results);
        Assert.False(failure.Success);
        Assert.Equal(PluginLoadFailureKind.Untrusted, failure.FailureKind);
        Assert.Contains("signature", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadFromDirectory_AlsoUsesTheLoadGate()
    {
        using var workspace = TempPluginRoot.Create();
        FixturePackageFactory.Create(workspace.Combine("source"));
        var (host, _, _) = HostFactory.Create(workspace.Combine("host-root"), options => options with
        {
            PackageGate = static (_, _) => Task.FromResult(PluginPackageGateResult.Reject("not reviewed")),
        });
        await using var scope = host;

        var result = await host.LoadFromDirectoryAsync(workspace.Combine("source", "test-plugin"));

        Assert.False(result.Success);
        Assert.Equal(PluginLoadFailureKind.Untrusted, result.FailureKind);
    }
}

