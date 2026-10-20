using System.Text.Json;
using System.Text.Json.Serialization;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Json;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Sdk.Manifest;

/// <summary>
/// The complete plugin manifest (<c>manifest.json</c>), schema version 2. This model is the wire contract:
/// it mirrors <c>marketplace/schemas/plugin.schema.json</c> one-to-one and is parsed strictly
/// (unknown properties are rejected, matching <c>additionalProperties: false</c>).
/// </summary>
public sealed record PluginManifest
{
    /// <summary>The JSON Schema reference (<c>$schema</c>), optional.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public int SchemaVersion { get; init; }

    public string Id { get; init; } = string.Empty;

    /// <summary>Optional page ownership namespace for trusted built-ins (e.g. <c>builtin</c>).</summary>
    public string? PageOwnerId { get; init; }

    public string Version { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = string.Empty;

    public string MinAppVersion { get; init; } = string.Empty;

    public string? MaxAppVersion { get; init; }

    public PublisherInfo? Publisher { get; init; }

    public AuthorInfo? Author { get; init; }

    public LocalizedString? Name { get; init; }

    public LocalizedString? Summary { get; init; }

    public LocalizedString? Description { get; init; }

    public string? NameKey { get; init; }

    public string? SummaryKey { get; init; }

    public string? DescriptionKey { get; init; }

    public string? License { get; init; }

    public string? Repository { get; init; }

    public string? Homepage { get; init; }

    public string? Changelog { get; init; }

    public IReadOnlyList<PluginCategory>? Categories { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public IconSet? Icon { get; init; }

    public IReadOnlyList<ScreenshotInfo>? Screenshots { get; init; }

    public PluginEntryPoint? Entry { get; init; }

    public IReadOnlyList<string>? Platforms { get; init; }

    public PluginLocales? Locales { get; init; }

    public PluginCapabilities? Capabilities { get; init; }

    public IReadOnlyList<string>? Activation { get; init; }

    public PluginContributions? Contributes { get; init; }

    public PluginDependencies? Dependencies { get; init; }

    public PluginSettingsReference? Settings { get; init; }

    public PluginIntegrity? Integrity { get; init; }

    public PluginSignatureReference? Signature { get; init; }

    public bool TryGetVersion(out SemanticVersion version) => SemanticVersion.TryParse(Version, out version);

    public bool TryGetApiVersion(out SemanticVersion version) => SemanticVersion.TryParse(ApiVersion, out version);

    public bool TryGetMinAppVersion(out SemanticVersion version) => SemanticVersion.TryParse(MinAppVersion, out version);

    public bool TryGetMaxAppVersion(out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(MaxAppVersion))
        {
            return true;
        }

        if (!SemanticVersion.TryParse(MaxAppVersion, out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }
}

/// <summary>Publisher identity. <c>keyId</c> is the key pinned on first install (TOFU).</summary>
public sealed record PublisherInfo
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? Url { get; init; }

    public string? KeyId { get; init; }
}

/// <summary>Human author metadata (may differ from the publishing entity).</summary>
public sealed record AuthorInfo
{
    public string Name { get; init; } = string.Empty;

    public string? Email { get; init; }

    public string? Url { get; init; }
}

/// <summary>Where the plugin entry assembly lives and how it is loaded.</summary>
public sealed record PluginEntryPoint
{
    public PluginEntryRuntime Runtime { get; init; } = PluginEntryRuntime.InProcess;

    /// <summary>Package-relative path, forward slashes, must start with <c>plugin/</c>.</summary>
    public string Assembly { get; init; } = string.Empty;

    /// <summary>Fully qualified entry type name implementing <c>IFluidBarPlugin</c>.</summary>
    public string Type { get; init; } = string.Empty;

    public string? TargetFramework { get; init; }
}

/// <summary>Supported plugin runtime lanes. v2.0 only loads <see cref="InProcess"/>.</summary>
public enum PluginEntryRuntime
{
    [WireName("inproc")] InProcess = 0,
    [WireName("outofproc")] OutOfProcess = 1,
}

/// <summary>Plugin locale bundle metadata.</summary>
public sealed record PluginLocales
{
    public string Default { get; init; } = "en-US";

    public IReadOnlyList<string>? Available { get; init; }

    public string? Path { get; init; }

    public string? Fallback { get; init; }
}

/// <summary>
/// Declarative marketplace icon set. Wire form is either a single <c>assets/...</c> path or an object with
/// <c>light</c>/<c>dark</c> (required) and optional <c>monochrome</c> paths.
/// </summary>
public sealed record IconSet
{
    public string? Path { get; init; }

    public string? Light { get; init; }

    public string? Dark { get; init; }

    public string? Monochrome { get; init; }

    public IReadOnlyList<string> GetAllPaths()
    {
        var paths = new List<string>(4);
        if (Path is not null)
        {
            paths.Add(Path);
        }

        if (Light is not null)
        {
            paths.Add(Light);
        }

        if (Dark is not null)
        {
            paths.Add(Dark);
        }

        if (Monochrome is not null)
        {
            paths.Add(Monochrome);
        }

        return paths;
    }
}

/// <summary>Marketplace screenshot with localized alt text.</summary>
public sealed record ScreenshotInfo
{
    public string Src { get; init; } = string.Empty;

    public LocalizedString? Alt { get; init; }
}

/// <summary>Marketplace categories (closed list).</summary>
public enum PluginCategory
{
    [WireName("productivity")] Productivity = 0,
    [WireName("media")] Media = 1,
    [WireName("developer")] Developer = 2,
    [WireName("system")] System = 3,
    [WireName("communication")] Communication = 4,
    [WireName("information")] Information = 5,
    [WireName("fun")] Fun = 6,
    [WireName("integration")] Integration = 7,
    [WireName("accessibility")] Accessibility = 8,
    [WireName("theme")] Theme = 9,
}

/// <summary>
/// Declared capabilities: disclosure and informed consent, not enforcement. An in-process plugin runs with
/// the user's full rights; the UI must say so and must not use "sandbox" language.
/// </summary>
public sealed record PluginCapabilities
{
    public NetworkCapability? Network { get; init; }

    public ClipboardCapability? Clipboard { get; init; }

    public FilesCapability? Files { get; init; }

    public MediaCapability? Media { get; init; }

    public NotificationsCapability? Notifications { get; init; }

    public ShellCapability? Shell { get; init; }

    public HotkeysCapability? Hotkeys { get; init; }

    public ProcessCapability? Process { get; init; }

    public RegistryCapability? Registry { get; init; }

    public TelemetryCapability? Telemetry { get; init; }

    public BackgroundCapability? Background { get; init; }

    public UiCapability? Ui { get; init; }
}

/// <summary>Network access declaration with a host-visible domain allowlist.</summary>
public sealed record NetworkCapability
{
    public IReadOnlyList<string>? Domains { get; init; }

    public IReadOnlyList<NetworkRequestMethod>? Methods { get; init; }

    public string? Reason { get; init; }
}

/// <summary>HTTP verbs a plugin may declare.</summary>
public enum NetworkRequestMethod
{
    [WireName("GET")] Get = 0,
    [WireName("POST")] Post = 1,
    [WireName("PUT")] Put = 2,
    [WireName("PATCH")] Patch = 3,
    [WireName("DELETE")] Delete = 4,
    [WireName("HEAD")] Head = 5,
    [WireName("WEBSOCKET")] WebSocket = 6,
}

/// <summary>Clipboard read/write declaration.</summary>
public sealed record ClipboardCapability
{
    public bool Read { get; init; }

    public bool Write { get; init; }

    public string? Reason { get; init; }
}

/// <summary>File-system scopes the plugin intends to touch.</summary>
public sealed record FilesCapability
{
    public IReadOnlyList<FileScope>? Read { get; init; }

    public IReadOnlyList<FileScope>? Write { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Abstract file scopes; the host maps them to real paths. No arbitrary paths in the manifest.</summary>
public enum FileScope
{
    [WireName("$PLUGIN_DATA")] PluginData = 0,
    [WireName("$PLUGIN_INSTALL")] PluginInstall = 1,
    [WireName("$DOWNLOADS")] Downloads = 2,
    [WireName("$DOCUMENTS")] Documents = 3,
    [WireName("$PICTURES")] Pictures = 4,
    [WireName("$MUSIC")] Music = 5,
    [WireName("$VIDEOS")] Videos = 6,
    [WireName("$DESKTOP")] Desktop = 7,
    [WireName("$TEMP")] Temp = 8,
}

/// <summary>Media-session access declaration.</summary>
public sealed record MediaCapability
{
    public bool Sessions { get; init; }

    public bool Control { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Notification access declaration.</summary>
public sealed record NotificationsCapability
{
    public bool Read { get; init; }

    public bool Post { get; init; }
}

/// <summary>Shell integration declaration.</summary>
public sealed record ShellCapability
{
    public bool Open { get; init; }

    public bool Execute { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Global hotkey declaration. Hotkeys are user-visible and user-remappable in the host UI.</summary>
public sealed record HotkeysCapability
{
    public IReadOnlyList<string>? Global { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Process spawn declaration (always security-reviewed in the marketplace).</summary>
public sealed record ProcessCapability
{
    public bool Spawn { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Registry access declaration (always security-reviewed in the marketplace).</summary>
public sealed record RegistryCapability
{
    public IReadOnlyList<string>? Read { get; init; }

    public IReadOnlyList<string>? Write { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Telemetry declaration; disabled by default and must be disclosed.</summary>
public sealed record TelemetryCapability
{
    public bool Enabled { get; init; }

    public string? Endpoint { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Background work declaration; the host enforces the minimum interval.</summary>
public sealed record BackgroundCapability
{
    public int? IntervalMinMs { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Declarative UI surfaces the plugin contributes to. <c>xaml</c> is false for all marketplace plugins.</summary>
public sealed record UiCapability
{
    public bool Island { get; init; }

    public bool HoverCard { get; init; }

    public bool Settings { get; init; }

    public bool ContextMenu { get; init; }

    public bool Commands { get; init; }

    public bool Widgets { get; init; }

    /// <summary>Allows this plugin to register host-rendered island pages.</summary>
    public bool Pages { get; init; }

    /// <summary>Template ids the plugin may use for its pages.</summary>
    public IReadOnlyList<string>? PageTemplates { get; init; }

    public bool Xaml { get; init; }
}

/// <summary>Plugin dependency declarations.</summary>
public sealed record PluginDependencies
{
    /// <summary>Range against the SDK ABI, e.g. <c>^2.0.0</c>.</summary>
    public string? Sdk { get; init; }

    public IReadOnlyList<PluginDependency>? Plugins { get; init; }
}

/// <summary>A dependency on another plugin (v2.0 does not auto-install dependencies).</summary>
public sealed record PluginDependency
{
    public string Id { get; init; } = string.Empty;

    public string Range { get; init; } = string.Empty;

    public bool Optional { get; init; }
}

/// <summary>Reference to the host-owned settings storage for this plugin.</summary>
public sealed record PluginSettingsReference
{
    public string? Schema { get; init; }

    public string? Defaults { get; init; }

    public PluginSettingsStorage Storage { get; init; } = PluginSettingsStorage.Host;
}

/// <summary>Who owns the settings file.</summary>
public enum PluginSettingsStorage
{
    [WireName("host")] Host = 0,
    [WireName("plugin")] Plugin = 1,
}

/// <summary>Integrity declarations inside the manifest (the package hash lives in the detached signature).</summary>
public sealed record PluginIntegrity
{
    public string? EntrySha256 { get; init; }

    public string? Files { get; init; }
}

/// <summary>
/// Expected signature scheme and key. The actual signature values live in the detached <c>signature.json</c>
/// next to the package (a signature cannot cover the ZIP it is embedded in).
/// </summary>
public sealed record PluginSignatureReference
{
    public PluginSignatureScheme Scheme { get; init; } = PluginSignatureScheme.DetachedV1;

    public PluginSignatureAlgorithm Algorithm { get; init; } = PluginSignatureAlgorithm.EcdsaP256Sha256;

    public string? PublisherKeyId { get; init; }

    public string? SignatureFile { get; init; }
}

/// <summary>Signature envelope scheme.</summary>
public enum PluginSignatureScheme
{
    [WireName("detached-v1")] DetachedV1 = 0,
}

/// <summary>Supported signature algorithms. ECDSA P-256 is the default; Ed25519 is reserved for later.</summary>
public enum PluginSignatureAlgorithm
{
    [WireName("ECDSA-P256-SHA256")] EcdsaP256Sha256 = 0,
    [WireName("Ed25519")] Ed25519 = 1,
}

/// <summary>Hard limits shared by the parser, the validator, the packager and the installer.</summary>
public static class PluginManifestLimits
{
    public const int MaxManifestBytes = 256 * 1024;
    public const int MaxIdLength = 40;
    public const int MaxNameLength = 2000;
    public const int MaxLicenseLength = 64;
    public const int MaxLayouts = 12;
    public const int MaxSettingsPages = 8;
    public const int MaxGroupsPerPage = 8;
    public const int MaxControlsPerGroup = 20;
    public const int MaxControlsPerPage = 40;
    public const int MaxCommands = 32;
    public const int MaxContextMenuItems = 16;
    public const int MaxWidgets = 8;
    public const int MaxPluginDependencies = 16;
    public const int MaxLocales = 64;
}

/// <summary>Manifest identifier rules shared by the validator and the marketplace.</summary>
public static class PluginIdentifiers
{
    /// <summary>Plugin id pattern: lower kebab-case, 1-40 characters.</summary>
    public const string IdPattern = "^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$";

    /// <summary>Ids the platform reserves; the marketplace rejects them for third-party plugins.</summary>
    public static IReadOnlySet<string> ReservedIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "fluidbar",
        "system",
        "settings",
        "marketplace",
        "core",
        "api",
        "plugin",
        "island",
        "monitor",
    };
}

/// <summary>JSON converter for the string-or-object <see cref="IconSet"/> wire form.</summary>
public sealed class IconSetJsonConverter : JsonConverter<IconSet>
{
    public override IconSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new IconSet { Path = reader.GetString() };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a string or object for icon, got {reader.TokenType}.");
        }

        string? light = null;
        string? dark = null;
        string? monochrome = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a property name in icon object.");
            }

            var property = reader.GetString();
            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Icon property '{property}' must be a string.");
            }

            var value = reader.GetString();
            switch (property)
            {
                case "light":
                    light = value;
                    break;
                case "dark":
                    dark = value;
                    break;
                case "monochrome":
                    monochrome = value;
                    break;
                default:
                    throw new JsonException($"Unknown icon property '{property}'.");
            }
        }

        return new IconSet { Light = light, Dark = dark, Monochrome = monochrome };
    }

    public override void Write(Utf8JsonWriter writer, IconSet value, JsonSerializerOptions options)
    {
        if (value.Path is not null)
        {
            writer.WriteStringValue(value.Path);
            return;
        }

        writer.WriteStartObject();
        if (value.Light is not null)
        {
            writer.WriteString("light", value.Light);
        }

        if (value.Dark is not null)
        {
            writer.WriteString("dark", value.Dark);
        }

        if (value.Monochrome is not null)
        {
            writer.WriteString("monochrome", value.Monochrome);
        }

        writer.WriteEndObject();
    }
}
