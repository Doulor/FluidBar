using System.Text.Json;

namespace FluidBar.App.Plugins;

/// <summary>Persists the one-time local-plugin risk acknowledgement keyed by <c>id@version</c>.</summary>
public sealed class LocalPluginRiskStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly HashSet<string> _acknowledged;

    public LocalPluginRiskStore(string localRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localRoot);
        _path = Path.Combine(localRoot, "risk-acknowledgements.json");
        _acknowledged = Load();
    }

    public bool IsAcknowledged(string pluginId, string version)
        => !string.IsNullOrWhiteSpace(pluginId) && !string.IsNullOrWhiteSpace(version)
            && _acknowledged.Contains(Key(pluginId, version));

    public void Acknowledge(string pluginId, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        lock (_gate)
        {
            if (!_acknowledged.Add(Key(pluginId, version)))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new RiskDocument
            {
                SchemaVersion = 1,
                Acknowledged = [.. _acknowledged.OrderBy(static value => value, StringComparer.Ordinal)],
            }, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
    }

    public static string Key(string pluginId, string version) => $"{pluginId}@{version}";

    private HashSet<string> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var document = JsonSerializer.Deserialize<RiskDocument>(File.ReadAllText(_path), JsonOptions);
            return new HashSet<string>(document?.Acknowledged ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record RiskDocument
    {
        public int SchemaVersion { get; init; } = 1;

        public IReadOnlyList<string> Acknowledged { get; init; } = [];
    }
}

