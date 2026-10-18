using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluidBar.Marketplace.Index;

/// <summary>
/// Deterministic JSON canonicalization used for signed payloads: object properties are sorted by ordinal name,
/// there is no insignificant whitespace and the properties listed in <c>excludedProperties</c> are removed
/// (e.g. <c>signatures</c>). Inspired by RFC 8785; the marketplace generator, CI signer and client verifier
/// all call this one implementation so signatures cannot drift.
/// </summary>
public static class CanonicalJson
{
    /// <summary>Canonicalizes JSON text, removing signature properties before hashing/signing.</summary>
    public static byte[] Canonicalize(string json, params string[] excludedProperties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var node = JsonNode.Parse(
            json,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }) ?? throw new JsonException("JSON payload is null.");

        if (node is JsonObject root)
        {
            foreach (var property in excludedProperties)
            {
                root.Remove(property);
            }
        }

        return Canonicalize(node);
    }

    /// <summary>Canonicalizes an in-memory JSON node.</summary>
    public static byte[] Canonicalize(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            WriteNode(writer, node);
        }

        return stream.ToArray();
    }

    /// <summary>Convenience: canonical JSON as UTF-8 text (for diagnostics/tests).</summary>
    public static string CanonicalizeToString(string json, params string[] excludedProperties) =>
        Encoding.UTF8.GetString(Canonicalize(json, excludedProperties));

    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                return;
            case JsonObject jsonObject:
                writer.WriteStartObject();
                foreach (var property in jsonObject.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteNode(writer, property.Value);
                }

                writer.WriteEndObject();
                return;
            case JsonArray jsonArray:
                writer.WriteStartArray();
                foreach (var item in jsonArray)
                {
                    WriteNode(writer, item);
                }

                writer.WriteEndArray();
                return;
            case JsonValue jsonValue:
                WriteValue(writer, jsonValue);
                return;
            default:
                throw new JsonException($"Unsupported JSON node type '{node.GetType().Name}'.");
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonValue value)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    writer.WriteStringValue(element.GetString());
                    return;
                case JsonValueKind.Number:
                    writer.WriteRawValue(element.GetRawText());
                    return;
                case JsonValueKind.True:
                    writer.WriteBooleanValue(true);
                    return;
                case JsonValueKind.False:
                    writer.WriteBooleanValue(false);
                    return;
                case JsonValueKind.Null:
                    writer.WriteNullValue();
                    return;
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                    WriteNode(writer, JsonNode.Parse(element.GetRawText()));
                    return;
                default:
                    writer.WriteRawValue(element.GetRawText());
                    return;
            }
        }

        if (value.TryGetValue<string>(out var text))
        {
            writer.WriteStringValue(text);
            return;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            writer.WriteBooleanValue(boolean);
            return;
        }

        if (value.TryGetValue<long>(out var integer))
        {
            writer.WriteNumberValue(integer);
            return;
        }

        if (value.TryGetValue<double>(out var number))
        {
            writer.WriteNumberValue(number);
            return;
        }

        writer.WriteRawValue(value.ToJsonString());
    }
}
