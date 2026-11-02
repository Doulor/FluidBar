namespace FluidBar.Plugins.FileShelf;

/// <summary>Reference-only bounded list: never reads, moves or deletes the original files.</summary>
public static class FileReferenceShelf
{
    public static string[] Normalize(IEnumerable<string>? paths)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths ?? [])
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || !Path.IsPathFullyQualified(path)) continue;
            string normalized;
            try { normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
            catch (ArgumentException) { continue; }
            catch (NotSupportedException) { continue; }
            catch (PathTooLongException) { continue; }
            if (normalized.Length > 2048 || !seen.Add(normalized)) continue;
            result.Add(normalized);
            if (result.Count == 32) break;
        }
        return result.ToArray();
    }
}
