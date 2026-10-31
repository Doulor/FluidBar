namespace FluidBar.Plugins.FileShelf;

/// <summary>Reference-only bounded list: never reads, moves or deletes the original files.</summary>
public static class FileReferenceShelf
{
    public static string[] Normalize(IEnumerable<string>? paths) => (paths ?? [])
        .Where(static path => !string.IsNullOrWhiteSpace(path) && path.Length <= 2048 && Path.IsPathFullyQualified(path))
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToArray();
}
