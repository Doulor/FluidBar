using System.Text.Json;
using System.Text.Json.Serialization;
using FluidBar.Core.Layout;

namespace FluidBar.Core.Settings;

/// <summary>Serializes <see cref="IslandMode"/> with the spec tokens (float/flush/dock).</summary>
internal sealed class IslandModeJsonConverter : JsonConverter<IslandMode>
{
    public override IslandMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        return text?.Trim().ToLowerInvariant() switch
        {
            "float" => IslandMode.Float,
            "flush" => IslandMode.Flush,
            "dock" => IslandMode.Dock,
            _ => throw new JsonException($"Unknown island mode '{text}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, IslandMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            IslandMode.Float => "float",
            IslandMode.Flush => "flush",
            IslandMode.Dock => "dock",
            _ => throw new JsonException($"Unknown island mode '{value}'."),
        });
    }
}

/// <summary>
/// Serializes <see cref="IslandMaterial"/> with the shipped tokens (obsidian / acrylic / liquid-glass).
/// Reading also accepts the compact and legacy spellings so a hand-edited settings file can never
/// silently reset a glass material back to obsidian.
/// </summary>
internal sealed class IslandMaterialJsonConverter : JsonConverter<IslandMaterial>
{
    public override IslandMaterial Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        return text?.Trim().ToLowerInvariant() switch
        {
            "obsidian" => IslandMaterial.Obsidian,
            "acrylic" => IslandMaterial.Acrylic,
            "liquid-glass" or "liquidglass" or "liquid" or "glass" => IslandMaterial.LiquidGlass,
            _ => throw new JsonException($"Unknown island material '{text}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, IslandMaterial value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            IslandMaterial.Obsidian => "obsidian",
            IslandMaterial.Acrylic => "acrylic",
            IslandMaterial.LiquidGlass => "liquid-glass",
            _ => throw new JsonException($"Unknown island material '{value}'."),
        });
    }
}

/// <summary>Serializes <see cref="IslandAnchor"/> with the spec tokens (top-center, bottom-left...).</summary>
internal sealed class IslandAnchorJsonConverter : JsonConverter<IslandAnchor>
{
    public override IslandAnchor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        return text?.Trim().ToLowerInvariant() switch
        {
            "top-left" or "topleft" => IslandAnchor.TopLeft,
            "top-center" or "topcenter" => IslandAnchor.TopCenter,
            "top-right" or "topright" => IslandAnchor.TopRight,
            "bottom-left" or "bottomleft" => IslandAnchor.BottomLeft,
            "bottom-center" or "bottomcenter" => IslandAnchor.BottomCenter,
            "bottom-right" or "bottomright" => IslandAnchor.BottomRight,
            _ => throw new JsonException($"Unknown island anchor '{text}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, IslandAnchor value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            IslandAnchor.TopLeft => "top-left",
            IslandAnchor.TopCenter => "top-center",
            IslandAnchor.TopRight => "top-right",
            IslandAnchor.BottomLeft => "bottom-left",
            IslandAnchor.BottomCenter => "bottom-center",
            IslandAnchor.BottomRight => "bottom-right",
            _ => throw new JsonException($"Unknown island anchor '{value}'."),
        });
    }
}
