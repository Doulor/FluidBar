using System.IO;
using System.Text.Json;

namespace FluidBar;

public sealed record PluginCatalogEntry(
    string Id,
    string Name,
    string Description,
    string Category,
    string EntryPoint,
    string Status,
    bool BuiltIn);

public sealed record PluginCatalog(IReadOnlyList<PluginCatalogEntry> Plugins)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public bool Contains(string id)
    {
        return Plugins.Any(plugin => string.Equals(
            plugin.Id,
            id,
            StringComparison.OrdinalIgnoreCase));
    }

    public static PluginCatalog Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PluginCatalog>(json, JsonOptions)
               ?? new PluginCatalog(Array.Empty<PluginCatalogEntry>());
    }
}

