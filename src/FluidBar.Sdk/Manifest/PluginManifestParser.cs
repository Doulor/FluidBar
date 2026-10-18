using System.Text.Json;
using FluidBar.Sdk.Json;

namespace FluidBar.Sdk.Manifest;

/// <summary>Raised when a manifest cannot be read or does not match the schema.</summary>
public sealed class PluginManifestException : Exception
{
    public PluginManifestException(string message, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Errors = errors ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// Strict <c>manifest.json</c> reader. Unknown properties are rejected (the normative JSON Schema is closed),
/// which catches typos and silent drift between the app, the tooling and the docs.
/// </summary>
public static class PluginManifestParser
{
    private static readonly JsonSerializerOptions Options = PluginJson.CreateStrictOptions();

    /// <summary>Parses manifest JSON; throws <see cref="PluginManifestException"/> on malformed input.</summary>
    public static PluginManifest Parse(string json) =>
        TryParse(json, out var manifest, out var errors) && manifest is not null
            ? manifest
            : throw new PluginManifestException($"Invalid plugin manifest: {string.Join("; ", errors)}", errors);

    /// <summary>Parses manifest JSON without throwing.</summary>
    public static bool TryParse(string json, out PluginManifest? manifest, out IReadOnlyList<string> errors)
    {
        manifest = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            errors = ["Manifest is empty."];
            return false;
        }

        if (json.Length > PluginManifestLimits.MaxManifestBytes)
        {
            errors = [$"Manifest exceeds the {PluginManifestLimits.MaxManifestBytes} character limit."];
            return false;
        }

        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(json, Options);
        }
        catch (JsonException exception)
        {
            errors = [$"Manifest is not valid JSON for schema v2: {exception.Message}"];
            return false;
        }
        catch (NotSupportedException exception)
        {
            errors = [$"Manifest could not be deserialized: {exception.Message}"];
            return false;
        }

        if (manifest is null)
        {
            errors = ["Manifest deserialized to null."];
            return false;
        }

        errors = Array.Empty<string>();
        return true;
    }

    /// <summary>Reads and parses a manifest file with a hard size limit; fully async and cancellable.</summary>
    public static async Task<PluginManifest> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var info = new FileInfo(filePath);
        if (!info.Exists)
        {
            throw new PluginManifestException($"Manifest file '{filePath}' does not exist.");
        }

        if (info.Length > PluginManifestLimits.MaxManifestBytes)
        {
            throw new PluginManifestException(
                $"Manifest file '{filePath}' is {info.Length} bytes; the limit is {PluginManifestLimits.MaxManifestBytes}.");
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        return Parse(json);
    }
}
