using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluidBar.Sdk.Json;

/// <summary>
/// Declares the exact wire value of an enum member in plugin manifests, marketplace documents and
/// signature files. The manifest schema is closed and normative, so the wire names must not depend on a
/// naming policy that could change between releases.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class WireNameAttribute : Attribute
{
    public WireNameAttribute(string name) => Name = name;

    public string Name { get; }
}

/// <summary>Helpers for mapping wire enums without reflection at every call site.</summary>
public static class WireEnum
{
    /// <summary>Gets the wire name of an enum value.</summary>
    public static string GetName<TEnum>(TEnum value)
        where TEnum : struct, Enum => WireEnumCache<TEnum>.ToWire.TryGetValue(value, out var name)
            ? name
            : throw new ArgumentOutOfRangeException(nameof(value), value, "No wire name is declared.");

    /// <summary>Parses a wire value; returns <see langword="false"/> for unknown values.</summary>
    public static bool TryParse<TEnum>(string? wireValue, out TEnum value)
        where TEnum : struct, Enum
    {
        if (wireValue is null)
        {
            value = default;
            return false;
        }

        return WireEnumCache<TEnum>.FromWire.TryGetValue(wireValue, out value);
    }

    /// <summary>All declared wire names, in enum declaration order.</summary>
    public static IReadOnlyList<string> GetNames<TEnum>()
        where TEnum : struct, Enum => WireEnumCache<TEnum>.Names;

    /// <summary>Returns the wire name or falls back to the enum member name.</summary>
    public static string GetNameOrDefault<TEnum>(TEnum value)
        where TEnum : struct, Enum => WireEnumCache<TEnum>.ToWire.TryGetValue(value, out var name) ? name : value.ToString();
}

/// <summary>
/// JSON converter factory for enums decorated with <see cref="WireNameAttribute"/>. Enums without wire
/// names fall back to the member name. Registered once in <see cref="PluginJson"/>.
/// </summary>
public sealed class WireEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(WireEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class WireEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected a string for {typeof(TEnum).Name}, got {reader.TokenType}.");
            }

            var wireValue = reader.GetString();
            if (!WireEnumCache<TEnum>.FromWire.TryGetValue(wireValue ?? string.Empty, out var value))
            {
                throw new JsonException(
                    $"Unknown {typeof(TEnum).Name} value '{wireValue}'. Expected one of: {string.Join(", ", WireEnumCache<TEnum>.Names)}.");
            }

            return value;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteStringValue(WireEnumCache<TEnum>.GetWire(value));
    }
}

internal static class WireEnumCache<TEnum>
    where TEnum : struct, Enum
{
    internal static readonly ImmutableDictionary<TEnum, string> ToWire;
    internal static readonly ImmutableDictionary<string, TEnum> FromWire;
    internal static readonly ImmutableArray<string> Names;

    static WireEnumCache()
    {
        var toWire = ImmutableDictionary.CreateBuilder<TEnum, string>();
        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not TEnum value)
            {
                continue;
            }

            var wireName = field.GetCustomAttribute<WireNameAttribute>()?.Name ?? field.Name;
            toWire[value] = wireName;
            names.Add(wireName);
        }

        ToWire = toWire.ToImmutable();
        FromWire = ToWire.ToImmutableDictionary(static pair => pair.Value, static pair => pair.Key, StringComparer.Ordinal);
        Names = names.ToImmutable();
    }

    internal static string GetWire(TEnum value) =>
        ToWire.TryGetValue(value, out var wire) ? wire : throw new ArgumentOutOfRangeException(nameof(value), value, "No wire name is declared.");
}

/// <summary>
/// A small, invariant JSON serializer configuration shared by the SDK, the host, the marketplace and the
/// contributor tooling. Objects are closed by default (<see cref="JsonUnmappedMemberHandling.Disallow"/>)
/// because the manifest schema is <c>additionalProperties: false</c>.
/// </summary>
public static class PluginJson
{
    /// <summary>Strict configuration: camelCase properties, unknown members rejected, wire enums, invariant numbers.</summary>
    public static JsonSerializerOptions CreateStrictOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DictionaryKeyPolicy = null,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.Strict,
        };
        options.Converters.Add(new WireEnumConverterFactory());
        options.Converters.Add(new Manifest.LocalizedStringJsonConverter());
        options.Converters.Add(new Manifest.IconSetJsonConverter());
        return options;
    }

    /// <summary>Configuration for persisted per-plugin settings: unknown members are preserved, numbers lenient.</summary>
    public static JsonSerializerOptions CreateSettingsOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        options.Converters.Add(new WireEnumConverterFactory());
        return options;
    }
}
