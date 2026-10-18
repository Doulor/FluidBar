using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluidBar.Core.Settings;

/// <summary>Diff summary between two settings snapshots (spec 8.3 import confirmation).</summary>
public sealed record SettingsDiff
{
    /// <summary>Number of added leaf values.</summary>
    public int Added { get; init; }

    /// <summary>Number of modified leaf values.</summary>
    public int Modified { get; init; }

    /// <summary>Number of removed leaf values.</summary>
    public int Removed { get; init; }

    /// <summary>Bounded list of changed paths (max 200).</summary>
    public ImmutableArray<string> ChangedPaths { get; init; } = [];

    /// <summary>Total number of changes.</summary>
    public int TotalChanges => Added + Modified + Removed;
}

/// <summary>Computes a leaf-level settings diff for the import confirmation UI.</summary>
public static class SettingsDiffCalculator
{
    /// <summary>Maximum number of changed paths retained.</summary>
    public const int MaxPaths = 200;

    /// <summary>Compares <paramref name="current"/> with <paramref name="imported"/>.</summary>
    public static SettingsDiff Compare(FluidBarSettings current, FluidBarSettings imported)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(imported);
        var left = Flatten(current);
        var right = Flatten(imported);

        var added = 0;
        var modified = 0;
        var removed = 0;
        var paths = new List<string>();

        foreach (var (path, value) in right)
        {
            if (!left.TryGetValue(path, out var previous))
            {
                added++;
                AddPath(paths, "+" + path);
            }
            else if (!string.Equals(previous, value, StringComparison.Ordinal))
            {
                modified++;
                AddPath(paths, "~" + path);
            }
        }

        foreach (var path in left.Keys)
        {
            if (!right.ContainsKey(path))
            {
                removed++;
                AddPath(paths, "-" + path);
            }
        }

        paths.Sort(StringComparer.Ordinal);
        return new SettingsDiff
        {
            Added = added,
            Modified = modified,
            Removed = removed,
            ChangedPaths = [.. paths],
        };
    }

    private static void AddPath(List<string> paths, string path)
    {
        if (paths.Count < MaxPaths)
        {
            paths.Add(path);
        }
    }

    private static Dictionary<string, string> Flatten(FluidBarSettings settings)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var node = JsonSerializer.SerializeToNode(settings, SettingsJson.Options);
        if (node is not null)
        {
            Flatten(node, string.Empty, result);
        }

        return result;
    }

    private static void Flatten(JsonNode? node, string prefix, Dictionary<string, string> result)
    {
        switch (node)
        {
            case null:
                result[prefix] = "null";
                break;
            case JsonObject obj:
                foreach (var (key, child) in obj)
                {
                    var path = prefix.Length == 0 ? key : prefix + "." + key;
                    Flatten(child, path, result);
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    Flatten(array[i], $"{prefix}[{i}]", result);
                }

                break;
            default:
                result[prefix] = node.ToJsonString();
                break;
        }
    }
}

/// <summary>
/// Save debounce policy (spec 8.1: 500 ms). Pure: the host ticks it with the injected
/// <see cref="TimeProvider"/>; no timers or locks are allocated in the core.
/// </summary>
public sealed class SettingsSaveDebounce
{
    /// <summary>Spec debounce interval (500 ms).</summary>
    public static TimeSpan DefaultInterval { get; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Creates a debounce policy.</summary>
    /// <param name="interval">Debounce interval; defaults to <see cref="DefaultInterval"/>.</param>
    public SettingsSaveDebounce(TimeSpan? interval = null)
    {
        var value = interval ?? DefaultInterval;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
        Interval = value;
    }

    /// <summary>Configured debounce interval.</summary>
    public TimeSpan Interval { get; }

    /// <summary>Deadline at which a pending save must be flushed.</summary>
    public DateTimeOffset? Deadline { get; private set; }

    /// <summary>Whether a save is pending.</summary>
    public bool IsPending => Deadline is not null;

    /// <summary>Requests a save; the deadline restarts on every request.</summary>
    public void Request(DateTimeOffset now) => Deadline = now + Interval;

    /// <summary>Whether the debounce elapsed and the pending save must be flushed.</summary>
    public bool ShouldFlush(DateTimeOffset now) => Deadline is { } deadline && now >= deadline;

    /// <summary>Clears the pending state after a successful flush.</summary>
    public void MarkFlushed() => Deadline = null;
}
