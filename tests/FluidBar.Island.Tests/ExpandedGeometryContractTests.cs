using System.Text.RegularExpressions;
using FluidBar.Core.Layout;
using FluidBar.Island;
using Xunit;

namespace FluidBar.Island.Tests;

public sealed class ExpandedGeometryContractTests
{
    [Fact]
    public void Expanded_shell_tokens_are_440x244_with_radius_32()
    {
        Assert.Equal(440, LayoutTokens.ExpandedWidth);
        Assert.Equal(244, LayoutTokens.ExpandedHeight);
        Assert.Equal(new VisualSize(440, 244), LayoutTokens.ExpandedSize());
        Assert.Equal(36, LayoutTokens.ExpandedRadius);
        Assert.Equal(32, IslandMetrics.ExpandedPanelRadius);
    }

    [Fact]
    public void Product_sources_do_not_hard_code_expanded_height_or_radius()
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

                Assert.False(Regex.IsMatch(code, @"284"),
                    $"{Path.GetRelativePath(root, file)}:{lineNumber} hard-codes the old 284 DIP expanded height");
                Assert.False(Regex.IsMatch(code, @"Expanded(Panel)?Radiuss*=s*30"),
                    $"{Path.GetRelativePath(root, file)}:{lineNumber} hard-codes the old 30 DIP expanded radius");
            }
        }
    }
}

