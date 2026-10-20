using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluidBar.Core.Activities;

/// <summary>JSON support for stable page ids; also accepts the pre-M1 enum names used by soak fixtures.</summary>
public sealed class PageRefJsonConverter : JsonConverter<PageRef>
{
    /// <inheritdoc />
    public override PageRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("PageRef must be a string.");
        }

        var value = reader.GetString();
        return value switch
        {
            null or "" => PageRef.Home,
            "Home" => PageRef.Home,
            "Media" => PageRef.Media,
            "Notifications" => PageRef.Notifications,
            "Clipboard" => PageRef.Clipboard,
            "Agent" => PageRef.Agent,
            "Widgets" => PageRef.Widgets,
            _ => new PageRef(value),
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, PageRef value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.PageId);
}
