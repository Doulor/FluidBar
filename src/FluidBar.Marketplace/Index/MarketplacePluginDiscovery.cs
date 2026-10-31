namespace FluidBar.Marketplace.Index;

/// <summary>Finds plugin sources consistently across flat and nested repository layouts.</summary>
public static class MarketplacePluginDiscovery
{
    public static IReadOnlyList<string> Discover(string rootDirectory)
    {
        var found = new List<string>();
        Visit(Path.GetFullPath(rootDirectory));
        return found.Order(StringComparer.OrdinalIgnoreCase).ToArray();

        void Visit(string directory)
        {
            if (File.Exists(Path.Combine(directory, "manifest.json")))
            {
                found.Add(directory);
                return;
            }
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(child);
                if (name.StartsWith('.') || name.StartsWith('_') || name is "bin" or "obj"
                    || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                Visit(child);
            }
        }
    }

    /// <summary>Refuses an incomplete or ambiguous release rather than dropping catalog entries silently.</summary>
    public static void RequirePlugins(IEnumerable<string> discoveredIds, IEnumerable<string> requiredIds)
    {
        var actual = discoveredIds.ToArray();
        if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length)
            throw new MarketplaceIndexException("Duplicate plugin ids were found in the publish sources.");
        var missing = requiredIds.Except(actual, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new MarketplaceIndexException($"Required marketplace plugins are missing: {string.Join(", ", missing)}. Check --plugins-root and --catalog.");
    }
}
