using FluidBar.PluginHost.Loader;
using FluidBar.PluginHost.Runtime;
using FluidBar.Sdk.Versioning;

namespace FluidBar.PluginHost.Tests;

/// <summary>
/// Plugin roots contain more than packages: per-plugin data/settings folders and the unsigned local
/// root. Those layouts must not surface as rejected packages (five Error lines on every launch).
/// </summary>
public sealed class PluginDiscoveryLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fluidbar-discovery-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DataAndContainerDirectories_AreNotReportedAsInvalidPackages()
    {
        Directory.CreateDirectory(Path.Combine(_root, "fluidbar-media", "data"));
        await File.WriteAllTextAsync(Path.Combine(_root, "fluidbar-media", "settings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_root, "local", "sample"));
        await File.WriteAllTextAsync(Path.Combine(_root, "local", "sample", "manifest.json"), "{}");

        var options = new PluginHostOptions
        {
            PluginRootDirectory = _root,
            AppVersion = new SemanticVersion(2, 0, 0),
        };

        var results = await new PluginDiscoverer(options).DiscoverAsync(CancellationToken.None);

        Assert.DoesNotContain(results, entry => entry.PluginId == "fluidbar-media");
        Assert.DoesNotContain(results, entry => entry.PluginId == "local");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
