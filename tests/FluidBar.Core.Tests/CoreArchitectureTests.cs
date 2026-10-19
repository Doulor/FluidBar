using System.Reflection;

namespace FluidBar.Core.Tests;

/// <summary>
/// Guard rails for the M1 contract: FluidBar.Core must stay UI-free, Win32-free and free of
/// precompiled UI engine dependencies. These are deliberately reflection-only tests so they run
/// on every platform and every CI job.
/// </summary>
public class CoreArchitectureTests
{
    private static readonly Assembly CoreAssembly = typeof(Settings.FluidBarSettings).Assembly;

    [Fact]
    public void Core_ReferencesOnlyBclAssemblies()
    {
        var forbiddenPrefixes = new[]
        {
            "PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows",
            "Avalonia", "System.Windows.Forms", "Microsoft.Win32.Registry", "Microsoft.Extensions",
            "Windows.UI", "WinRT",
        };

        foreach (var reference in CoreAssembly.GetReferencedAssemblies())
        {
            var name = reference.Name ?? string.Empty;
            Assert.DoesNotContain(forbiddenPrefixes, prefix =>
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            Assert.True(
                name.StartsWith("System", StringComparison.Ordinal)
                || name.Equals("netstandard", StringComparison.Ordinal)
                || name.Equals("mscorlib", StringComparison.Ordinal),
                $"Unexpected non-BCL reference in FluidBar.Core: {name}");
        }
    }

    [Fact]
    public void Core_HasNoPInvoke()
    {
        foreach (var type in CoreAssembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                foreach (var attribute in method.CustomAttributes)
                {
                    var name = attribute.AttributeType.Name;
                    Assert.False(
                        name is "DllImportAttribute" or "LibraryImportAttribute",
                        $"FluidBar.Core must not P/Invoke: {type.FullName}.{method.Name}");
                }
            }
        }
    }

    [Fact]
    public void Core_PublicTypesLiveInCoreNamespaces()
    {
        foreach (var type in CoreAssembly.GetExportedTypes())
        {
            var ns = type.Namespace ?? string.Empty;
            Assert.True(
                ns.Equals("FluidBar.Core", StringComparison.Ordinal)
                || ns.StartsWith("FluidBar.Core.", StringComparison.Ordinal),
                $"Public type '{type.FullName}' escaped the FluidBar.Core namespace tree.");
        }
    }

    [Fact]
    public void Core_ExposesExpectedContractAreas()
    {
        var namespaces = CoreAssembly.GetExportedTypes()
            .Select(t => t.Namespace)
            .Where(ns => ns is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var expected in new[]
                 {
                     "FluidBar.Core",
                     "FluidBar.Core.Events",
                     "FluidBar.Core.Activities",
                     "FluidBar.Core.Layout",
                     "FluidBar.Core.Motion",
                     "FluidBar.Core.Presentation",
                     "FluidBar.Core.Settings",
                     "FluidBar.Core.Diagnostics",
                 })
        {
            Assert.Contains(expected, namespaces);
        }
    }

    [Fact]
    public void LayoutTokens_MatchConfirmedLeadTokens()
    {
        Assert.Equal(440, Layout.LayoutTokens.ExpandedWidth, 1e-9);
        Assert.Equal(284, Layout.LayoutTokens.ExpandedHeight, 1e-9);
        Assert.Equal(488, Layout.LayoutTokens.WindowWidth, 1e-9);
        // WindowHeight is the legacy shell-envelope height; the host surfaces are the contract now.
        Assert.Equal(332, Layout.LayoutTokens.WindowHeight, 1e-9);
        Assert.Equal(488, Layout.LayoutTokens.HostSurfaceWidth, 1e-9);
        Assert.Equal(384, Layout.LayoutTokens.HostSurfaceFloatHeight, 1e-9);
        Assert.Equal(444, Layout.LayoutTokens.HostSurfaceDockHeight, 1e-9);
        Assert.Equal(24, Layout.LayoutTokens.HostTopPadding, 1e-9);
        Assert.Equal(36, Layout.LayoutTokens.HostBottomPadding, 1e-9);
        Assert.Equal(76, Layout.LayoutTokens.HostFloatBottomAllowance, 1e-9);
        Assert.Equal(136, Layout.LayoutTokens.HostDockBottomAllowance, 1e-9);
        Assert.Equal(8, Layout.LayoutTokens.ArchipelagoGap, 1e-9);
        Assert.Equal(32, Layout.LayoutTokens.ArchipelagoStripHeight, 1e-9);
        Assert.Equal(352, Layout.LayoutTokens.ArchipelagoTrackWidth, 1e-9);
        Assert.Equal(208, Layout.LayoutTokens.CompactSize(Activities.ActivityKind.Volume).Width, 1e-9);
        Assert.Equal(40, Layout.LayoutTokens.CompactSize(Activities.ActivityKind.Volume).Height, 1e-9);
        Assert.Equal(208, Layout.LayoutTokens.CompactSize(Activities.ActivityKind.BatteryLow).Width, 1e-9);
        Assert.Equal(36, Layout.LayoutTokens.CompactSize(Activities.ActivityKind.BatteryLow).Height, 1e-9);
    }
}
