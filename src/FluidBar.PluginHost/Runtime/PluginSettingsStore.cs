using System.Collections.Immutable;
using System.Text.Json;
using FluidBar.PluginHost.Logging;
using FluidBar.Sdk;
using FluidBar.Sdk.Json;

namespace FluidBar.PluginHost.Runtime;

/// <summary>
/// Host-owned per-plugin settings store. Values are typed JSON primitives, the file is schema-versioned, all
/// writes are atomic (temp file + replace) and corrupt files are quarantined with a log entry instead of
/// being silently discarded.
/// </summary>
internal sealed class PluginSettingsStore : IPluginSettings, IDisposable
{
    private const int SchemaVersion = 1;
    private const int MaxValueKeys = 1_024;
    private const int MaxSubscribers = 32;
    private const long MaxFileBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = PluginJson.CreateSettingsOptions();

    private readonly string _pluginId;
    private readonly string _settingsPath;
    private readonly IPluginHostLogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);
    private readonly List<Subscription> _subscriptions = [];
    private bool _loaded;
    private bool _disposed;

    internal PluginSettingsStore(string pluginId, string settingsPath, IPluginHostLogger logger)
    {
        _pluginId = pluginId;
        _settingsPath = settingsPath;
        _logger = logger;
    }

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _values.Keys.ToImmutableArray();
            }
        }
    }

    internal void Load() => EnsureLoaded();

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        try
        {
            var info = new FileInfo(_settingsPath);
            if (info.Length > MaxFileBytes)
            {
                _logger.Error(_pluginId, $"Settings file '{_settingsPath}' is {info.Length} bytes; refusing to load values.");
                return;
            }

            using var stream = File.OpenRead(_settingsPath);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                _logger.Error(_pluginId, $"Settings file '{_settingsPath}' is not a JSON object; starting with defaults.");
                return;
            }

            if (document.RootElement.TryGetProperty("pluginId", out var pluginId) &&
                pluginId.ValueKind == JsonValueKind.String &&
                !string.Equals(pluginId.GetString(), _pluginId, StringComparison.Ordinal))
            {
                _logger.Error(_pluginId, $"Settings file '{_settingsPath}' belongs to plugin '{pluginId.GetString()}'; ignoring it.");
                return;
            }

            if (document.RootElement.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Object)
            {
                var count = 0;
                foreach (var property in values.EnumerateObject())
                {
                    if (++count > MaxValueKeys)
                    {
                        _logger.Error(_pluginId, $"Settings file has more than {MaxValueKeys} values; ignoring the remainder.");
                        break;
                    }

                    _values[property.Name] = property.Value.Clone();
                }
            }
        }
        catch (JsonException exception)
        {
            QuarantineCorruptFile($"Settings file is not valid JSON: {exception.Message}");
        }
        catch (IOException exception)
        {
            _logger.Error(_pluginId, $"Settings file could not be read: {exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Error(_pluginId, $"Settings file could not be read: {exception.Message}", exception);
        }
    }

    private void QuarantineCorruptFile(string reason)
    {
        _logger.Error(_pluginId, $"{reason} The file will be preserved as '.corrupt' and defaults will be used.");
        try
        {
            var corruptPath = _settingsPath + ".corrupt";
            File.Move(_settingsPath, corruptPath, overwrite: true);
        }
        catch (IOException moveException)
        {
            _logger.Warn(_pluginId, $"Could not quarantine corrupt settings file: {moveException.Message}", moveException);
        }
        catch (UnauthorizedAccessException moveException)
        {
            _logger.Warn(_pluginId, $"Could not quarantine corrupt settings file: {moveException.Message}", moveException);
        }
    }

    public bool TryGetBoolean(string key, out bool value)
    {
        value = false;
        lock (_gate)
        {
            EnsureLoaded();
            if (!_values.TryGetValue(key, out var element))
            {
                return false;
            }

            if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            value = element.GetBoolean();
            return true;
        }
    }

    public bool TryGetInt32(string key, out int value)
    {
        value = 0;
        lock (_gate)
        {
            EnsureLoaded();
            return _values.TryGetValue(key, out var element) &&
                   element.ValueKind == JsonValueKind.Number &&
                   element.TryGetInt32(out value);
        }
    }

    public bool TryGetDouble(string key, out double value)
    {
        value = 0;
        lock (_gate)
        {
            EnsureLoaded();
            return _values.TryGetValue(key, out var element) &&
                   element.ValueKind == JsonValueKind.Number &&
                   element.TryGetDouble(out value);
        }
    }

    public bool TryGetString(string key, out string? value)
    {
        value = null;
        lock (_gate)
        {
            EnsureLoaded();
            if (!_values.TryGetValue(key, out var element))
            {
                return false;
            }

            if (element.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = element.GetString();
            return true;
        }
    }

    public bool TryGetStringArray(string key, out IReadOnlyList<string>? value)
    {
        value = null;
        lock (_gate)
        {
            EnsureLoaded();
            if (!_values.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    builder.Add(item.GetString() ?? string.Empty);
                }
            }

            value = builder.ToImmutable();
            return true;
        }
    }

    public bool GetBoolean(string key, bool fallback) => TryGetBoolean(key, out var value) ? value : fallback;

    public int GetInt32(string key, int fallback) => TryGetInt32(key, out var value) ? value : fallback;

    public double GetDouble(string key, double fallback) => TryGetDouble(key, out var value) ? value : fallback;

    public string GetString(string key, string fallback) => TryGetString(key, out var value) && value is not null ? value : fallback;

    public IReadOnlyList<string> GetStringArray(string key, IReadOnlyList<string>? fallback = null) =>
        TryGetStringArray(key, out var value) && value is not null ? value : fallback ?? [];

    public void SetBoolean(string key, bool value) => SetValue(key, JsonSerializer.SerializeToElement(value, SerializerOptions));

    public void SetInt32(string key, int value) => SetValue(key, JsonSerializer.SerializeToElement(value, SerializerOptions));

    public void SetDouble(string key, double value) => SetValue(key, JsonSerializer.SerializeToElement(value, SerializerOptions));

    public void SetString(string key, string? value) =>
        SetValue(key, value is null ? JsonSerializer.SerializeToElement<string?>(null, SerializerOptions) : JsonSerializer.SerializeToElement(value, SerializerOptions));

    public void SetStringArray(string key, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        SetValue(key, JsonSerializer.SerializeToElement(values, SerializerOptions));
    }

    private void SetValue(string key, JsonElement element)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        List<Subscription> subscriptions;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLoaded();
            if (!_values.ContainsKey(key) && _values.Count >= MaxValueKeys)
            {
                throw new InvalidOperationException($"Plugin '{_pluginId}' exceeded the {MaxValueKeys}-setting limit.");
            }

            _values[key] = element;
            subscriptions = [.. _subscriptions];
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Invoke(key);
        }
    }

    public bool Remove(string key)
    {
        bool removed;
        List<Subscription> subscriptions;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLoaded();
            removed = _values.Remove(key);
            subscriptions = [.. _subscriptions];
        }

        if (removed)
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Invoke(key);
            }
        }

        return removed;
    }

    public IDisposable Subscribe(Action<string> onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_subscriptions.Count >= MaxSubscribers)
            {
                throw new InvalidOperationException($"Plugin '{_pluginId}' exceeded the {MaxSubscribers}-subscriber limit.");
            }

            var subscription = new Subscription(onChanged, this);
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, JsonElement> snapshot;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLoaded();
            snapshot = new Dictionary<string, JsonElement>(_values, StringComparer.Ordinal);
        }

        var document = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = SchemaVersion,
            ["pluginId"] = _pluginId,
            ["values"] = snapshot,
            ["updatedAt"] = DateTimeOffset.UtcNow,
        };

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _settingsPath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, _settingsPath, overwrite: true);
        _logger.Debug(_pluginId, $"Saved {snapshot.Count} settings value(s).");
    }

    private void RemoveSubscription(Subscription subscription)
    {
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscriptions.Clear();
        }
    }

    private sealed class Subscription(Action<string> callback, PluginSettingsStore owner) : IDisposable
    {
        private int _disposed;

        internal void Invoke(string key)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            callback(key);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            owner.RemoveSubscription(this);
        }
    }
}
