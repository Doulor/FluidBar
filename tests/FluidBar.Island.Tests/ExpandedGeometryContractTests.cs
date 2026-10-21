using System.Text.RegularExpressions;
using FluidBar.Core.Layout;
using FluidBar.Island;
using Xunit;

namespace FluidBar.Island.Tests;

public sealed class ExpandedGeometryContractTests
{
    [Fact]
    public void Expanded_shell_tokens_are_440x284_with_radius_36()
    {
        Assert.Equal(440, LayoutTokens.ExpandedWidth);
        Assert.Equal(284, LayoutTokens.ExpandedHeight);
        Assert.Equal(new VisualSize(440, 284), LayoutTokens.ExpandedSize());
        Assert.Equal(36, LayoutTokens.ExpandedRadius);
        Assert.Equal(36, IslandMetrics.ExpandedPanelRadius);
    }

    [Fact]
    public void Product_sources_do_not_use_the_old_expanded_radius()
    {
        var root = IslandTestFixtures.RepoRoot;
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains(Path.Combine("src", "FluidBar.Core", "Layout"), StringComparison.Ordinal)
                || path.Contains(Path.Combine("src", "FluidBar.Island"), StringComparison.Ordinal)
                || path.Contains(Path.Combine("src", "FluidBar.App", "Island", "Expanded"), StringComparison.Ordinal))
            .Where(path => !path.EndsWith("SplitSatelliteStyle.cs", StringComparison.Ordinal)
                && !path.EndsWith("SplitSatelliteMotion.cs", StringComparison.Ordinal));

        foreach (var file in files)
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                var code = line;
                var comment = code.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0)
                {
                    code = code[..comment];
                }

                Assert.False(Regex.IsMatch(code, @"Expanded(Panel)?Radius\s*=\s*30\b"),
                    $"{Path.GetRelativePath(root, file)}:{lineNumber} hard-codes the old 30 DIP expanded radius");
            }
        }
    }
}

