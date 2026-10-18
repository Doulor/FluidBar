using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FluidBar.Architecture.Tests;

/// <summary>
/// Repository-level architecture rules for FluidBar v2.
/// These tests are intentionally source-based: they must fail fast in CI when a
/// forbidden dependency direction, window-bounds animation, or UI-thread blocking
/// pattern is introduced. See docs/rewrite/specs/2026-10-18-fluidbar-v2-design.md §5.2/§5.8.
/// </summary>
public sealed class ArchitectureRulesTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly Regex WindowBoundsAnimation = new(
        @"(?:Window\.(?:Left|Top|Width|Height)\s*=|BeginAnimation\(\s*(?:Left|Top|Width|Height)Property|AllowsTransparency\s*=|SystemParameters\.(?:WorkArea|PrimaryScreen))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UiThreadBlocking = new(
        @"(?:\.GetAwaiter\(\)\.GetResult\(\)|\bThread\.Sleep\s*\(|GetStringAsync\([^)]*\)\.Result|\.Wait\(\s*\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Repository_root_is_discoverable()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot, "FluidBar.sln")), $"FluidBar.sln not found under {RepoRoot}");
    }

    [Theory]
    [MemberData(nameof(ProjectDependencyRules))]
    public void Project_dependencies_follow_allowed_direction(string projectRelativePath, string[] forbiddenReferences)
    {
        var projectPath = Path.Combine(RepoRoot, projectRelativePath);
        if (!File.Exists(projectPath))
        {
            return; // project not created yet (milestone not reached)
        }

        var doc = XDocument.Load(projectPath);
        var references = doc.Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', '/')))
            .ToArray();

        var violations = references
            .Where(r => forbiddenReferences.Contains(r, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"{projectRelativePath} references forbidden project(s): {string.Join(", ", violations)}. Allowed: {string.Join(", ", references)}");
    }

    public static TheoryData<string, string[]> ProjectDependencyRules() => new()
    {
        { "src/FluidBar.Core/FluidBar.Core.csproj", new[] { "FluidBar.Platform.Windows", "FluidBar.App", "FluidBar.PluginHost", "FluidBar.Marketplace", "FluidBar.Cli" } },
        { "src/FluidBar.Sdk/FluidBar.Sdk.csproj", new[] { "FluidBar.Core", "FluidBar.Platform.Windows", "FluidBar.App", "FluidBar.PluginHost", "FluidBar.Marketplace", "FluidBar.Cli" } },
        { "src/FluidBar.Platform.Windows/FluidBar.Platform.Windows.csproj", new[] { "FluidBar.App", "FluidBar.PluginHost", "FluidBar.Marketplace", "FluidBar.Cli" } },
        { "src/FluidBar.PluginHost/FluidBar.PluginHost.csproj", new[] { "FluidBar.App", "FluidBar.Platform.Windows", "FluidBar.Cli" } },
        { "src/FluidBar.Marketplace/FluidBar.Marketplace.csproj", new[] { "FluidBar.App", "FluidBar.Platform.Windows", "FluidBar.Cli" } },
        { "src/FluidBar.Plugins.Builtin/FluidBar.Plugins.Builtin.csproj", new[] { "FluidBar.Core", "FluidBar.App", "FluidBar.PluginHost", "FluidBar.Marketplace", "FluidBar.Cli" } },
        { "src/FluidBar.App/FluidBar.App.csproj", new[] { "FluidBar.Cli", "FluidBar.Soak" } },
    };

    [Fact]
    public void Sdk_project_has_no_package_or_project_dependencies()
    {
        var projectPath = Path.Combine(RepoRoot, "src/FluidBar.Sdk/FluidBar.Sdk.csproj");
        if (!File.Exists(projectPath))
        {
            return;
        }

        var doc = XDocument.Load(projectPath);
        var packages = doc.Descendants("PackageReference").ToArray();
        var projects = doc.Descendants("ProjectReference").ToArray();

        Assert.True(packages.Length == 0, "FluidBar.Sdk must stay dependency-free (no PackageReference).");
        Assert.True(projects.Length == 0, "FluidBar.Sdk must not reference other FluidBar projects.");
    }

    [Fact]
    public void No_window_bounds_animation_in_product_sources()
    {
        var violations = ScanSourceFiles(WindowBoundsAnimation, static path =>
            path.Contains("\\tests\\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\docs\\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\prototype\\", StringComparison.OrdinalIgnoreCase));

        Assert.True(
            violations.Count == 0,
            "Window-bounds animation / AllowsTransparency / SystemParameters usage found (must animate inside a fixed-size host):\n"
            + string.Join("\n", violations.Select(v => $"{v.File}:{v.Line}: {v.Text}")));
    }

    [Fact]
    public void No_ui_thread_blocking_calls_in_product_sources()
    {
        var violations = ScanSourceFiles(UiThreadBlocking, static path =>
            path.Contains("\\tests\\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\FluidBar.Soak\\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\FluidBar.Cli\\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\docs\\", StringComparison.OrdinalIgnoreCase));

        Assert.True(
            violations.Count == 0,
            "UI-thread blocking pattern found (use async + cancellation instead):\n"
            + string.Join("\n", violations.Select(v => $"{v.File}:{v.Line}: {v.Text}")));
    }

    private static List<(string File, int Line, string Text)> ScanSourceFiles(Regex pattern, Func<string, bool> skip)
    {
        var results = new List<(string, int, string)>();
        var srcRoot = Path.Combine(RepoRoot, "src");
        if (!Directory.Exists(srcRoot))
        {
            return results;
        }

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (skip(file))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                if (pattern.IsMatch(line))
                {
                    results.Add((Path.GetRelativePath(RepoRoot, file), i + 1, line.Trim()));
                }
            }
        }

        return results;
    }

    private static string FindRepoRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("FLUIDBAR_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot) && Directory.Exists(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot);
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FluidBar.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
