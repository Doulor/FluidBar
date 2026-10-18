using System.Text.RegularExpressions;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Localization;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Sdk.Manifest;

/// <summary>Severity of a manifest validation issue.</summary>
public enum ManifestIssueSeverity
{
    Error = 0,
    Warning = 1,
}

/// <summary>One deterministic validation finding. <see cref="Code"/> is stable and tooling-friendly.</summary>
public sealed record ManifestValidationIssue(string Code, ManifestIssueSeverity Severity, string Path, string Message)
{
    public override string ToString() => $"[{Severity}] {Code} at {Path}: {Message}";
}

/// <summary>The aggregate validation result for one manifest.</summary>
public sealed class ManifestValidationResult
{
    private readonly IReadOnlyList<ManifestValidationIssue> _issues;

    internal ManifestValidationResult(IReadOnlyList<ManifestValidationIssue> issues)
    {
        _issues = issues;
        Errors = issues.Where(static issue => issue.Severity == ManifestIssueSeverity.Error).ToArray();
        Warnings = issues.Where(static issue => issue.Severity == ManifestIssueSeverity.Warning).ToArray();
    }

    public IReadOnlyList<ManifestValidationIssue> Issues => _issues;

    public IReadOnlyList<ManifestValidationIssue> Errors { get; }

    public IReadOnlyList<ManifestValidationIssue> Warnings { get; }

    public bool IsValid => Errors.Count == 0;

    /// <summary>Throws <see cref="PluginManifestException"/> when any error exists.</summary>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new PluginManifestException(
                $"Manifest validation failed with {Errors.Count} error(s): {string.Join("; ", Errors.Select(static issue => issue.ToString()))}",
                Errors.Select(static issue => issue.ToString()).ToArray());
        }
    }
}

/// <summary>Policy options for manifest validation (marketplace submissions are stricter than local dev).</summary>
public sealed class ManifestValidationOptions
{
    public static ManifestValidationOptions Default { get; } = new();

    /// <summary>The locale every localized string must provide (source language of the marketplace).</summary>
    public string DefaultLocale { get; init; } = "en-US";

    /// <summary>The SDK ABI version of the validating host.</summary>
    public SemanticVersion HostApiVersion { get; init; } = PluginApiVersion.Current;

    /// <summary>Allow <c>entry.runtime: outofproc</c> (reserved until protocol v1 ships).</summary>
    public bool AllowOutOfProcess { get; init; }

    /// <summary>Allow <c>capabilities.ui.xaml: true</c> (first-party/verified only; never for marketplace).</summary>
    public bool AllowXaml { get; init; }

    /// <summary>Require the default-locale translation on every localized string.</summary>
    public bool RequireDefaultLocaleTranslation { get; init; } = true;

    /// <summary>Reject platform-reserved plugin ids.</summary>
    public bool EnforceReservedIds { get; init; } = true;

    /// <summary>Require <c>publisher.keyId</c> (marketplace submissions).</summary>
    public bool RequirePublisherKeyId { get; init; }

    /// <summary>Maximum locale count declared by <c>locales.available</c>.</summary>
    public int MaxLocales { get; init; } = PluginManifestLimits.MaxLocales;
}

/// <summary>
/// Cross-field manifest validator implementing the rules that JSON Schema cannot express. The contributor
/// CLI, CI, the client installer and the host loader all call this same implementation.
/// </summary>
public static class PluginManifestValidator
{
    private static readonly Regex IdRegex = new(PluginIdentifiers.IdPattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PublisherIdRegex = new("^[a-z0-9][a-z0-9-]{1,38}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AssemblyRegex = new("^plugin/[A-Za-z0-9._/-]+\\.dll$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TypeRegex = new("^[A-Za-z_][A-Za-z0-9_.`+]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TargetFrameworkRegex = new("^net10\\.0(-windows[0-9.]+)?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NamespacedIdRegex = new("^[a-z0-9][a-z0-9.-]{2,63}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex MessageKeyRegex = new("^[A-Za-z][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TagRegex = new("^[a-z0-9][a-z0-9-]{0,23}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Regex = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex KeyIdRegex = new("^[A-Za-z0-9._:-]{3,80}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ActivationRegex = new(
        "^(startup|onMediaSession|onNotification|onSettingsOpen|onIslandEvent|onCommand:[a-z0-9.-]+|interval:[0-9]+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PathSafeRegex = new("^[A-Za-z0-9._/-]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SettingsSchemaRegex = new("^[A-Za-z0-9._/-]+\\.schema\\.json$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SettingsDefaultsRegex = new("^[A-Za-z0-9._/-]+\\.json$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly IReadOnlySet<string> AllowedPlatforms = new HashSet<string>(StringComparer.Ordinal)
    {
        "win-x64",
        "win-arm64",
        "win-x86",
    };

    /// <summary>Validates a manifest against schema v2 and the documented cross-field rules.</summary>
    public static ManifestValidationResult Validate(PluginManifest? manifest, ManifestValidationOptions? options = null)
    {
        options ??= ManifestValidationOptions.Default;
        var issues = new List<ManifestValidationIssue>();
        if (manifest is null)
        {
            issues.Add(new ManifestValidationIssue("manifest.null", ManifestIssueSeverity.Error, "$", "Manifest is null."));
            return new ManifestValidationResult(issues);
        }

        var context = new ValidationContext(manifest, options, issues);
        context.Run();
        return new ManifestValidationResult(issues);
    }

    private sealed class ValidationContext
    {
        private readonly PluginManifest _manifest;
        private readonly ManifestValidationOptions _options;
        private readonly List<ManifestValidationIssue> _issues;
        private readonly HashSet<string> _commandIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _settingKeys = new(StringComparer.Ordinal);

        internal ValidationContext(PluginManifest manifest, ManifestValidationOptions options, List<ManifestValidationIssue> issues)
        {
            _manifest = manifest;
            _options = options;
            _issues = issues;
        }

        internal void Run()
        {
            ValidateIdentity();
            ValidatePublisher();
            ValidateLocalizedCopy();
            ValidateLinksAndAssets();
            ValidateEntry();
            ValidatePlatforms();
            ValidateLocales();
            ValidateCapabilities();
            ValidateActivation();
            ValidateCommands();
            ValidateContributions();
            ValidateDependencies();
            ValidateSettingsReference();
            ValidateIntegrityAndSignature();
            ValidateUiConsistency();
        }

        private void Error(string code, string path, string message) =>
            _issues.Add(new ManifestValidationIssue(code, ManifestIssueSeverity.Error, path, message));

        private void Warning(string code, string path, string message) =>
            _issues.Add(new ManifestValidationIssue(code, ManifestIssueSeverity.Warning, path, message));

        private void ValidateIdentity()
        {
            if (_manifest.SchemaVersion != PluginApiVersion.SupportedManifestSchemaVersion)
            {
                Error(
                    "manifest.schemaVersion.unsupported",
                    "schemaVersion",
                    $"Unsupported manifest schemaVersion {_manifest.SchemaVersion}; this build supports {PluginApiVersion.SupportedManifestSchemaVersion}.");
            }

            if (string.IsNullOrWhiteSpace(_manifest.Id))
            {
                Error("manifest.id.missing", "id", "id is required.");
            }
            else
            {
                if (!IdRegex.IsMatch(_manifest.Id))
                {
                    Error("manifest.id.invalid", "id", $"id '{_manifest.Id}' must match {PluginIdentifiers.IdPattern}.");
                }

                if (_options.EnforceReservedIds && PluginIdentifiers.ReservedIds.Contains(_manifest.Id))
                {
                    Error("manifest.id.reserved", "id", $"id '{_manifest.Id}' is reserved for the platform.");
                }
            }

            if (!_manifest.TryGetVersion(out _))
            {
                Error("manifest.version.invalid", "version", $"version '{_manifest.Version}' is not a semantic version.");
            }

            if (!_manifest.TryGetApiVersion(out var apiVersion))
            {
                Error("manifest.apiVersion.invalid", "apiVersion", $"apiVersion '{_manifest.ApiVersion}' is not a semantic version.");
            }
            else if (!PluginApiVersion.IsApiVersionSupported(apiVersion))
            {
                Error(
                    "manifest.apiVersion.unsupported",
                    "apiVersion",
                    $"apiVersion {apiVersion} is not supported; this FluidBar build implements apiVersion {_options.HostApiVersion.Major}.x.");
            }

            SemanticVersion? minAppVersion = null;
            if (!_manifest.TryGetMinAppVersion(out var min))
            {
                Error("manifest.minAppVersion.invalid", "minAppVersion", $"minAppVersion '{_manifest.MinAppVersion}' is not a semantic version.");
            }
            else
            {
                minAppVersion = min;
            }

            if (!_manifest.TryGetMaxAppVersion(out var max))
            {
                Error("manifest.maxAppVersion.invalid", "maxAppVersion", $"maxAppVersion '{_manifest.MaxAppVersion}' is not a semantic version.");
            }
            else if (max is not null && minAppVersion is not null && minAppVersion > max)
            {
                Error("manifest.maxAppVersion.beforeMin", "maxAppVersion", "maxAppVersion must be greater than or equal to minAppVersion.");
            }
        }

        private void ValidatePublisher()
        {
            var publisher = _manifest.Publisher;
            if (publisher is null)
            {
                Error("manifest.publisher.missing", "publisher", "publisher is required.");
                return;
            }

            if (string.IsNullOrWhiteSpace(publisher.Id) || !PublisherIdRegex.IsMatch(publisher.Id))
            {
                Error("manifest.publisher.id.invalid", "publisher.id", $"publisher.id '{publisher.Id}' is invalid.");
            }

            if (string.IsNullOrWhiteSpace(publisher.DisplayName) || publisher.DisplayName.Length > 80)
            {
                Error("manifest.publisher.displayName.invalid", "publisher.displayName", "publisher.displayName must be 1-80 characters.");
            }

            if (publisher.Url is not null && !IsHttpUri(publisher.Url))
            {
                Error("manifest.publisher.url.invalid", "publisher.url", "publisher.url must be an absolute http(s) URI.");
            }

            if (publisher.KeyId is not null && !KeyIdRegex.IsMatch(publisher.KeyId))
            {
                Error("manifest.publisher.keyId.invalid", "publisher.keyId", "publisher.keyId is not a valid key id.");
            }

            if (_options.RequirePublisherKeyId && string.IsNullOrWhiteSpace(publisher.KeyId))
            {
                Error("manifest.publisher.keyId.missing", "publisher.keyId", "publisher.keyId is required for marketplace submissions.");
            }

            var author = _manifest.Author;
            if (author is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(author.Name) || author.Name.Length > 80)
            {
                Error("manifest.author.name.invalid", "author.name", "author.name must be 1-80 characters.");
            }

            if (author.Email is not null && (!author.Email.Contains('@', StringComparison.Ordinal) || author.Email.Contains(' ', StringComparison.Ordinal)))
            {
                Error("manifest.author.email.invalid", "author.email", "author.email is not a valid email address.");
            }

            if (author.Url is not null && !IsHttpUri(author.Url))
            {
                Error("manifest.author.url.invalid", "author.url", "author.url must be an absolute http(s) URI.");
            }
        }

        private void ValidateLocalizedCopy()
        {
            if (_manifest.Name is null)
            {
                Error("manifest.name.missing", "name", "name is required.");
            }

            if (_manifest.Summary is null)
            {
                Error("manifest.summary.missing", "summary", "summary is required.");
            }

            ValidateLocalized(_manifest.Name, "name", maxLength: 2000);
            ValidateLocalized(_manifest.Summary, "summary", maxLength: 2000);
            ValidateLocalized(_manifest.Description, "description", maxLength: 8000);
            ValidateMessageKey(_manifest.NameKey, "nameKey");
            ValidateMessageKey(_manifest.SummaryKey, "summaryKey");
            ValidateMessageKey(_manifest.DescriptionKey, "descriptionKey");

            if (string.IsNullOrWhiteSpace(_manifest.License) || _manifest.License.Length > PluginManifestLimits.MaxLicenseLength)
            {
                Error("manifest.license.invalid", "license", $"license must be 1-{PluginManifestLimits.MaxLicenseLength} characters.");
            }
        }

        private void ValidateLocalized(LocalizedString? value, string path, int maxLength, bool isKey = false)
        {
            if (value is null)
            {
                return;
            }

            if (value.Value is { } plain)
            {
                if (plain.Length > maxLength)
                {
                    Error($"manifest.{path}.tooLong", path, $"{path} exceeds {maxLength} characters.");
                }

                return;
            }

            foreach (var locale in value.Translations.Keys)
            {
                if (!LocaleTags.IsValid(locale))
                {
                    Error($"manifest.{path}.locale.invalid", path, $"Locale '{locale}' in {path} is not a canonical BCP-47 tag.");
                }

                if (value.Translations[locale].Length > maxLength)
                {
                    Error($"manifest.{path}.tooLong", path, $"Translation for '{locale}' in {path} exceeds {maxLength} characters.");
                }
            }

            if (_options.RequireDefaultLocaleTranslation && !value.HasTranslationFor(_options.DefaultLocale))
            {
                Error($"manifest.{path}.defaultLocale.missing", path, $"{path} must include the default locale '{_options.DefaultLocale}'.");
            }
        }

        private void ValidateMessageKey(string? key, string path)
        {
            if (key is null)
            {
                return;
            }

            if (!MessageKeyRegex.IsMatch(key))
            {
                Error("manifest.messageKey.invalid", path, $"{path} '{key}' is not a valid message key.");
            }
        }

        private void ValidateLinksAndAssets()
        {
            if (_manifest.Repository is not null && !IsHttpUri(_manifest.Repository))
            {
                Error("manifest.repository.invalid", "repository", "repository must be an absolute http(s) URI.");
            }

            if (_manifest.Homepage is not null && !IsHttpUri(_manifest.Homepage))
            {
                Error("manifest.homepage.invalid", "homepage", "homepage must be an absolute http(s) URI.");
            }

            if (_manifest.Changelog is not null && !PathSafeRegex.IsMatch(_manifest.Changelog))
            {
                Error("manifest.changelog.invalid", "changelog", "changelog must be a package-relative file name.");
            }

            if (_manifest.Categories is { } categories)
            {
                if (categories.Count > 3)
                {
                    Error("manifest.categories.tooMany", "categories", "At most 3 categories are allowed.");
                }

                if (categories.Distinct().Count() != categories.Count)
                {
                    Error("manifest.categories.duplicate", "categories", "categories must not contain duplicates.");
                }
            }

            if (_manifest.Tags is { } tags)
            {
                if (tags.Count > 10)
                {
                    Error("manifest.tags.tooMany", "tags", "At most 10 tags are allowed.");
                }

                foreach (var tag in tags)
                {
                    if (!TagRegex.IsMatch(tag))
                    {
                        Error("manifest.tags.invalid", "tags", $"Tag '{tag}' must match ^[a-z0-9][a-z0-9-]{{0,23}}$.");
                    }
                }

                if (tags.Distinct(StringComparer.Ordinal).Count() != tags.Count)
                {
                    Error("manifest.tags.duplicate", "tags", "tags must not contain duplicates.");
                }
            }

            if (_manifest.Icon is { } icon)
            {
                var paths = icon.GetAllPaths();
                if (paths.Count == 0)
                {
                    Error("manifest.icon.empty", "icon", "icon must declare at least one path.");
                }

                if (icon.Path is null && (icon.Light is null || icon.Dark is null))
                {
                    Error("manifest.icon.themePair", "icon", "Icon object form requires both light and dark paths.");
                }

                foreach (var assetPath in paths)
                {
                    ValidateAssetPath(assetPath, "icon");
                }
            }

            if (_manifest.Screenshots is { } screenshots)
            {
                if (screenshots.Count > 8)
                {
                    Error("manifest.screenshots.tooMany", "screenshots", "At most 8 screenshots are allowed.");
                }

                for (var index = 0; index < screenshots.Count; index++)
                {
                    var screenshot = screenshots[index];
                    ValidateAssetPath(screenshot.Src, $"screenshots[{index}].src");
                    ValidateLocalized(screenshot.Alt, $"screenshots[{index}].alt", maxLength: 300);
                }
            }
        }

        private void ValidateAssetPath(string? assetPath, string path)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("assets/", StringComparison.Ordinal) || assetPath.Contains("..", StringComparison.Ordinal))
            {
                Error("manifest.assetPath.invalid", path, "Asset paths must be package-relative and start with 'assets/' without '..'.");
            }
        }

        private void ValidateEntry()
        {
            var entry = _manifest.Entry;
            if (entry is null)
            {
                Error("manifest.entry.missing", "entry", "entry is required.");
                return;
            }

            if (entry.Runtime == PluginEntryRuntime.OutOfProcess && !_options.AllowOutOfProcess)
            {
                Error("manifest.entry.runtimeUnsupported", "entry.runtime", "entry.runtime 'outofproc' is not supported until the v2.1 protocol ships.");
            }

            if (string.IsNullOrWhiteSpace(entry.Assembly) ||
                !AssemblyRegex.IsMatch(entry.Assembly) ||
                entry.Assembly.Contains("..", StringComparison.Ordinal) ||
                entry.Assembly.Contains('\\', StringComparison.Ordinal))
            {
                Error("manifest.entry.assembly.invalid", "entry.assembly", "entry.assembly must match plugin/<name>.dll with forward slashes.");
            }

            if (string.IsNullOrWhiteSpace(entry.Type) || !TypeRegex.IsMatch(entry.Type))
            {
                Error("manifest.entry.type.invalid", "entry.type", "entry.type must be a fully qualified .NET type name.");
            }

            if (entry.TargetFramework is not null && !TargetFrameworkRegex.IsMatch(entry.TargetFramework))
            {
                Error("manifest.entry.targetFramework.invalid", "entry.targetFramework", $"entry.targetFramework '{entry.TargetFramework}' must target net10.0 or later.");
            }
        }

        private void ValidatePlatforms()
        {
            if (_manifest.Platforms is not { } platforms)
            {
                Warning("manifest.platforms.missing", "platforms", "platforms is not declared; the plugin will be treated as win-x64 only.");
                return;
            }

            if (platforms.Count == 0)
            {
                Error("manifest.platforms.empty", "platforms", "platforms must contain at least one value.");
            }

            foreach (var platform in platforms)
            {
                if (!AllowedPlatforms.Contains(platform))
                {
                    Error("manifest.platforms.invalid", "platforms", $"Platform '{platform}' is not supported.");
                }
            }

            if (platforms.Distinct(StringComparer.Ordinal).Count() != platforms.Count)
            {
                Error("manifest.platforms.duplicate", "platforms", "platforms must not contain duplicates.");
            }
        }

        private void ValidateLocales()
        {
            var locales = _manifest.Locales;
            if (locales is null)
            {
                Error("manifest.locales.missing", "locales", "locales is required.");
                return;
            }

            if (!LocaleTags.IsValid(locales.Default))
            {
                Error("manifest.locales.default.invalid", "locales.default", $"locales.default '{locales.Default}' is not a canonical BCP-47 tag.");
            }

            var available = locales.Available?.ToArray() ?? [];
            if (available.Length == 0)
            {
                Error("manifest.locales.available.empty", "locales.available", "locales.available must contain at least one locale.");
            }

            if (available.Length > _options.MaxLocales)
            {
                Error("manifest.locales.available.tooMany", "locales.available", $"At most {_options.MaxLocales} locales are allowed.");
            }

            foreach (var locale in available)
            {
                if (!LocaleTags.IsValid(locale))
                {
                    Error("manifest.locales.available.invalid", "locales.available", $"Locale '{locale}' is not a canonical BCP-47 tag.");
                }
            }

            if (available.Distinct(StringComparer.OrdinalIgnoreCase).Count() != available.Length)
            {
                Error("manifest.locales.available.duplicate", "locales.available", "locales.available must not contain duplicates.");
            }

            if (!available.Contains(locales.Default, StringComparer.OrdinalIgnoreCase))
            {
                Error("manifest.locales.default.notAvailable", "locales.default", "locales.default must appear in locales.available.");
            }

            if (locales.Fallback is not null)
            {
                if (!LocaleTags.IsValid(locales.Fallback))
                {
                    Error("manifest.locales.fallback.invalid", "locales.fallback", $"locales.fallback '{locales.Fallback}' is not a canonical BCP-47 tag.");
                }
                else if (!available.Contains(locales.Fallback, StringComparer.OrdinalIgnoreCase))
                {
                    Error("manifest.locales.fallback.notAvailable", "locales.fallback", "locales.fallback must appear in locales.available.");
                }
            }

            if (locales.Path is not null && locales.Path is not ("locales" or "locales/"))
            {
                Error("manifest.locales.path.invalid", "locales.path", "locales.path must be 'locales' or 'locales/'.");
            }
        }

        private void ValidateCapabilities()
        {
            var capabilities = _manifest.Capabilities;
            if (capabilities is null)
            {
                Error("manifest.capabilities.missing", "capabilities", "capabilities is required (use {} when the plugin needs none).");
                return;
            }

            if (capabilities.Network is { } network)
            {
                var domains = network.Domains?.ToArray() ?? [];
                if (domains.Length == 0)
                {
                    Error("manifest.capabilities.network.domains.missing", "capabilities.network.domains", "network.domains must contain at least one domain.");
                }

                foreach (var domain in domains)
                {
                    if (string.IsNullOrWhiteSpace(domain) || domain.Contains("://", StringComparison.Ordinal) || domain.Contains('/', StringComparison.Ordinal))
                    {
                        Error("manifest.capabilities.network.domains.invalid", "capabilities.network.domains", $"Network domain '{domain}' must be a bare host name.");
                    }
                }

                ValidateReason(network.Reason, "capabilities.network.reason", required: true, minLength: 10);
                if (capabilities.HasWildcardNetwork())
                {
                    Warning(
                        "manifest.capabilities.network.wildcard",
                        "capabilities.network.domains",
                        "Wildcard network allowlist requires a security review and an explicit reason in the PR.");
                }
            }

            if (capabilities.Clipboard is { } clipboard && !clipboard.Read && !clipboard.Write)
            {
                Warning("manifest.capabilities.clipboard.unused", "capabilities.clipboard", "clipboard declares neither read nor write.");
            }

            if (capabilities.Files is { } files)
            {
                ValidateReason(files.Reason, "capabilities.files.reason", required: true, minLength: 10);
                if (files.Write is { } writeScopes && writeScopes.Any(scope => scope is not (FileScope.PluginData or FileScope.PluginInstall or FileScope.Temp)))
                {
                    Warning(
                        "manifest.capabilities.files.writeOutsideData",
                        "capabilities.files.write",
                        "Writing outside $PLUGIN_DATA requires a security review.");
                }
            }

            if (capabilities.Media is { } media && (media.Sessions || media.Control))
            {
                ValidateReason(media.Reason, "capabilities.media.reason", required: true, minLength: 1);
            }

            if (capabilities.Shell is { } shell)
            {
                if (shell.Open || shell.Execute)
                {
                    ValidateReason(shell.Reason, "capabilities.shell.reason", required: true, minLength: 10);
                }

                if (shell.Execute)
                {
                    Warning("manifest.capabilities.shell.execute", "capabilities.shell.execute", "shell.execute is a high-risk capability requiring a security review.");
                }
            }

            if (capabilities.Hotkeys is { } hotkeys && hotkeys.Global is { Count: > 0 } globalHotkeys)
            {
                ValidateReason(hotkeys.Reason, "capabilities.hotkeys.reason", required: true, minLength: 10);
                foreach (var hotkey in globalHotkeys)
                {
                    if (string.IsNullOrWhiteSpace(hotkey) || hotkey.Length > 64 || hotkey.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or ' ')))
                    {
                        Error("manifest.capabilities.hotkeys.invalid", "capabilities.hotkeys.global", $"Hotkey '{hotkey}' is not a valid hotkey string.");
                    }
                }
            }

            if (capabilities.Process is { } process && process.Spawn)
            {
                ValidateReason(process.Reason, "capabilities.process.reason", required: true, minLength: 10);
                Warning("manifest.capabilities.process.spawn", "capabilities.process.spawn", "process.spawn is a high-risk capability requiring a security review.");
            }

            if (capabilities.Registry is { } registry)
            {
                if ((registry.Read?.Count ?? 0) > 0 || (registry.Write?.Count ?? 0) > 0)
                {
                    ValidateReason(registry.Reason, "capabilities.registry.reason", required: true, minLength: 10);
                }

                if ((registry.Write?.Count ?? 0) > 0)
                {
                    Warning("manifest.capabilities.registry.write", "capabilities.registry.write", "registry.write is a high-risk capability requiring a security review.");
                }
            }

            if (capabilities.Telemetry is { } telemetry)
            {
                if (telemetry.Enabled)
                {
                    ValidateReason(telemetry.Reason, "capabilities.telemetry.reason", required: true, minLength: 10);
                    if (telemetry.Endpoint is null || !IsHttpUri(telemetry.Endpoint))
                    {
                        Error("manifest.capabilities.telemetry.endpoint", "capabilities.telemetry.endpoint", "Enabled telemetry requires an absolute http(s) endpoint.");
                    }
                }
            }

            if (capabilities.Background is { } background)
            {
                ValidateReason(background.Reason, "capabilities.background.reason", required: true, minLength: 1);
                if (background.IntervalMinMs is { } interval && interval < 500)
                {
                    Error("manifest.capabilities.background.interval", "capabilities.background.intervalMinMs", "background.intervalMinMs must be >= 500.");
                }
            }

            if (capabilities.Ui is { Xaml: true } && !_options.AllowXaml)
            {
                Error("manifest.capabilities.ui.xaml", "capabilities.ui.xaml", "capabilities.ui.xaml is not allowed for marketplace plugins.");
            }
        }

        private void ValidateReason(string? reason, string path, bool required, int minLength)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                if (required)
                {
                    Error("manifest.capability.reason.missing", path, "A written reason is required for this capability.");
                }

                return;
            }

            if (reason.Length < minLength)
            {
                Error("manifest.capability.reason.tooShort", path, $"Reason must be at least {minLength} characters.");
            }

            if (reason.Length > 500)
            {
                Error("manifest.capability.reason.tooLong", path, "Reason must be at most 500 characters.");
            }
        }

        private void ValidateActivation()
        {
            if (_manifest.Activation is not { } activation)
            {
                return;
            }

            foreach (var trigger in activation)
            {
                if (!ActivationRegex.IsMatch(trigger))
                {
                    Error("manifest.activation.invalid", "activation", $"Activation trigger '{trigger}' is not supported.");
                }
            }

            if (activation.Distinct(StringComparer.Ordinal).Count() != activation.Count)
            {
                Error("manifest.activation.duplicate", "activation", "activation must not contain duplicates.");
            }
        }

        private void ValidateCommands()
        {
            var commands = _manifest.Contributes?.Commands;
            if (commands is null)
            {
                return;
            }

            if (commands.Count > PluginManifestLimits.MaxCommands)
            {
                Error("manifest.contributes.commands.tooMany", "contributes.commands", $"At most {PluginManifestLimits.MaxCommands} commands are allowed.");
            }

            foreach (var command in commands)
            {
                var path = "contributes.commands";
                if (string.IsNullOrWhiteSpace(command.Id) || !NamespacedIdRegex.IsMatch(command.Id))
                {
                    Error("manifest.contributes.command.id.invalid", path, $"Command id '{command.Id}' is invalid.");
                }
                else if (!IsNamespaced(command.Id))
                {
                    Error("manifest.contributes.command.id.namespace", path, $"Command id '{command.Id}' must start with the plugin id '{_manifest.Id}.'.");
                }
                else if (!_commandIds.Add(command.Id))
                {
                    Error("manifest.contributes.command.duplicate", path, $"Duplicate command id '{command.Id}'.");
                }

                if (command.Title is null)
                {
                    Error("manifest.contributes.command.title.missing", path, $"Command '{command.Id}' requires a title.");
                }
                else
                {
                    ValidateLocalized(command.Title, $"{path}[{command.Id}].title", maxLength: 200);
                }

                if (command.Icon is not null)
                {
                    ValidateAssetPath(command.Icon, $"{path}[{command.Id}].icon");
                }

                if (command.DefaultHotkey is { Length: > 64 })
                {
                    Error("manifest.contributes.command.hotkey.invalid", path, $"Command '{command.Id}' defaultHotkey exceeds 64 characters.");
                }
            }
        }

        private bool IsNamespaced(string id) =>
            !string.IsNullOrEmpty(_manifest.Id) &&
            id.StartsWith(_manifest.Id + ".", StringComparison.Ordinal);

        private void ValidateContributions()
        {
            var contributions = _manifest.Contributes;
            if (contributions is null)
            {
                Error("manifest.contributes.missing", "contributes", "contributes is required (use {} when the plugin contributes no UI).");
                return;
            }

            ValidateLayouts(contributions.Layouts);
            ValidateSettingsPages(contributions.SettingsPages);
            ValidateContextMenu(contributions.ContextMenu);
            ValidateWidgets(contributions.Widgets);
        }

        private void ValidateLayouts(IReadOnlyList<LayoutContribution>? layouts)
        {
            if (layouts is null)
            {
                return;
            }

            if (layouts.Count > PluginManifestLimits.MaxLayouts)
            {
                Error("manifest.contributes.layouts.tooMany", "contributes.layouts", $"At most {PluginManifestLimits.MaxLayouts} layouts are allowed.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < layouts.Count; index++)
            {
                var layout = layouts[index];
                var path = $"contributes.layouts[{index}]";
                ValidateNamespacedId(layout.Id, $"{path}.id", ids, "manifest.contributes.layout.id");

                if (!LayoutTemplateCatalog.GetAllowedTemplates(layout.Surface).Contains(layout.Template))
                {
                    Error(
                        "manifest.contributes.layout.templateSurface",
                        $"{path}.template",
                        $"Template '{layout.Template}' is not valid for surface '{layout.Surface}'.");
                }

                ValidateLayoutConstraints(layout.Constraints, $"{path}.constraints");

                if (layout.Slots is not { Count: > 0 } slots)
                {
                    Error("manifest.contributes.layout.slots.missing", $"{path}.slots", "A layout must bind at least one slot.");
                    continue;
                }

                foreach (var slot in slots)
                {
                    var slotPath = $"{path}.slots.{slot.Key}";
                    if (!LayoutTemplateCatalog.IsSlotAllowed(layout.Template, slot.Key))
                    {
                        Error(
                            "manifest.contributes.layout.slot.unknown",
                            slotPath,
                            $"Slot '{slot.Key}' is not accepted by template '{layout.Template}'.");
                    }

                    var binding = slot.Value;
                    var hasField = !string.IsNullOrWhiteSpace(binding.Field);
                    var hasLiteral = binding.Literal is not null;
                    var hasCommands = binding.Commands is { Count: > 0 };
                    if (!hasField && !hasLiteral && !hasCommands)
                    {
                        Error("manifest.contributes.layout.slot.empty", slotPath, "A slot needs a field, a literal or commands.");
                    }

                    if (hasField && binding.Field!.Length > 64)
                    {
                        Error("manifest.contributes.layout.slot.fieldLength", slotPath, "Slot field names are limited to 64 characters.");
                    }

                    if (binding.Literal is { Length: > 200 })
                    {
                        Error("manifest.contributes.layout.slot.literalLength", slotPath, "Slot literals are limited to 200 characters.");
                    }

                    if (binding.MaxLines is { } maxLines && maxLines is < 1 or > 8)
                    {
                        Error("manifest.contributes.layout.slot.maxLines", slotPath, "maxLines must be between 1 and 8.");
                    }

                    if (binding.Commands is { } commands)
                    {
                        if (commands.Count > 6)
                        {
                            Error("manifest.contributes.layout.slot.commands.tooMany", slotPath, "A slot may reference at most 6 commands.");
                        }

                        foreach (var commandId in commands)
                        {
                            if (!_commandIds.Contains(commandId))
                            {
                                Error(
                                    "manifest.contributes.layout.slot.command.unknown",
                                    slotPath,
                                    $"Slot references command '{commandId}' which is not declared in contributes.commands.");
                            }
                        }
                    }
                }
            }
        }

        private void ValidateLayoutConstraints(LayoutConstraints? constraints, string path)
        {
            if (constraints is null)
            {
                return;
            }

            if (constraints.MinWidth is { } minWidth && constraints.MaxWidth is { } maxWidth && minWidth > maxWidth)
            {
                Error("manifest.contributes.layout.constraints.width", path, "minWidth must be <= maxWidth.");
            }

            if (constraints.MinHeight is { } minHeight && constraints.MaxHeight is { } maxHeight && minHeight > maxHeight)
            {
                Error("manifest.contributes.layout.constraints.height", path, "minHeight must be <= maxHeight.");
            }

            if (constraints.MinWidth is { } minW && (minW < 80 || minW > 1000))
            {
                Error("manifest.contributes.layout.constraints.minWidth", path, "minWidth must be between 80 and 1000 DIP.");
            }

            if (constraints.MaxWidth is { } maxW && (maxW < 80 || maxW > 1000))
            {
                Error("manifest.contributes.layout.constraints.maxWidth", path, "maxWidth must be between 80 and 1000 DIP.");
            }

            if (constraints.MinHeight is { } minH && (minH < 32 || minH > 600))
            {
                Error("manifest.contributes.layout.constraints.minHeight", path, "minHeight must be between 32 and 600 DIP.");
            }

            if (constraints.MaxHeight is { } maxH && (maxH < 32 || maxH > 600))
            {
                Error("manifest.contributes.layout.constraints.maxHeight", path, "maxHeight must be between 32 and 600 DIP.");
            }
        }

        private void ValidateSettingsPages(IReadOnlyList<SettingsPageContribution>? pages)
        {
            if (pages is null)
            {
                return;
            }

            if (pages.Count > PluginManifestLimits.MaxSettingsPages)
            {
                Error("manifest.contributes.settingsPages.tooMany", "contributes.settingsPages", $"At most {PluginManifestLimits.MaxSettingsPages} settings pages are allowed.");
            }

            var pageIds = new HashSet<string>(StringComparer.Ordinal);
            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                var page = pages[pageIndex];
                var path = $"contributes.settingsPages[{pageIndex}]";
                if (string.IsNullOrWhiteSpace(page.Id) || !Regex.IsMatch(page.Id, "^[a-z0-9][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant))
                {
                    Error("manifest.contributes.settingsPage.id.invalid", $"{path}.id", $"Settings page id '{page.Id}' is invalid.");
                }
                else if (!pageIds.Add(page.Id))
                {
                    Error("manifest.contributes.settingsPage.duplicate", $"{path}.id", $"Duplicate settings page id '{page.Id}'.");
                }

                if (page.Title is null)
                {
                    Error("manifest.contributes.settingsPage.title.missing", $"{path}.title", "A settings page requires a title.");
                }
                else
                {
                    ValidateLocalized(page.Title, $"{path}.title", maxLength: 200);
                }

                var pageControlCount = 0;
                if (page.Controls is { Count: > 0 } looseControls)
                {
                    pageControlCount += looseControls.Count;
                    for (var controlIndex = 0; controlIndex < looseControls.Count; controlIndex++)
                    {
                        ValidateControl(looseControls[controlIndex], $"{path}.controls[{controlIndex}]");
                    }
                }

                if (page.Groups is { } groups)
                {
                    if (groups.Count > PluginManifestLimits.MaxGroupsPerPage)
                    {
                        Error("manifest.contributes.settingsPage.groups.tooMany", $"{path}.groups", $"At most {PluginManifestLimits.MaxGroupsPerPage} groups are allowed per page.");
                    }

                    var groupIds = new HashSet<string>(StringComparer.Ordinal);
                    for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                    {
                        var group = groups[groupIndex];
                        var groupPath = $"{path}.groups[{groupIndex}]";
                        if (string.IsNullOrWhiteSpace(group.Id) || !Regex.IsMatch(group.Id, "^[a-z0-9][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant))
                        {
                            Error("manifest.contributes.settingGroup.id.invalid", $"{groupPath}.id", $"Settings group id '{group.Id}' is invalid.");
                        }
                        else if (!groupIds.Add(group.Id))
                        {
                            Error("manifest.contributes.settingGroup.duplicate", $"{groupPath}.id", $"Duplicate settings group id '{group.Id}'.");
                        }

                        if (group.Title is null)
                        {
                            Error("manifest.contributes.settingGroup.title.missing", $"{groupPath}.title", "A settings group requires a title.");
                        }
                        else
                        {
                            ValidateLocalized(group.Title, $"{groupPath}.title", maxLength: 200);
                        }

                        if (group.Description is not null)
                        {
                            ValidateLocalized(group.Description, $"{groupPath}.description", maxLength: 500);
                        }

                        var groupControls = group.Controls;
                        if (groupControls is null || groupControls.Count == 0)
                        {
                            Error("manifest.contributes.settingGroup.controls.missing", $"{groupPath}.controls", "A settings group requires at least one control.");
                            continue;
                        }

                        if (groupControls.Count > PluginManifestLimits.MaxControlsPerGroup)
                        {
                            Error("manifest.contributes.settingGroup.controls.tooMany", $"{groupPath}.controls", $"At most {PluginManifestLimits.MaxControlsPerGroup} controls are allowed per group.");
                        }

                        pageControlCount += groupControls.Count;
                        for (var controlIndex = 0; controlIndex < groupControls.Count; controlIndex++)
                        {
                            ValidateControl(groupControls[controlIndex], $"{groupPath}.controls[{controlIndex}]");
                        }
                    }
                }

                if (pageControlCount == 0)
                {
                    Error("manifest.contributes.settingsPage.controls.missing", path, "A settings page requires at least one control or group.");
                }

                if (pageControlCount > PluginManifestLimits.MaxControlsPerPage)
                {
                    Error("manifest.contributes.settingsPage.controls.tooMany", path, $"At most {PluginManifestLimits.MaxControlsPerPage} controls are allowed per page.");
                }
            }
        }

        private void ValidateControl(SettingControlContribution control, string path)
        {
            if (string.IsNullOrWhiteSpace(control.Key) || control.Key.Length > 64 || !char.IsAsciiLetter(control.Key[0]))
            {
                Error("manifest.contributes.setting.key.invalid", $"{path}.key", $"Setting key '{control.Key}' is invalid.");
            }
            else if (!_settingKeys.Add(control.Key))
            {
                Error("manifest.contributes.setting.key.duplicate", $"{path}.key", $"Duplicate setting key '{control.Key}'.");
            }

            if (control.Title is null)
            {
                Error("manifest.contributes.setting.title.missing", $"{path}.title", "A setting control requires a title.");
            }
            else
            {
                ValidateLocalized(control.Title, $"{path}.title", maxLength: 200);
            }

            switch (control.Type)
            {
                case SettingControlType.Toggle:
                    if (control.Default is { } toggleDefault && toggleDefault.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False or System.Text.Json.JsonValueKind.Null))
                    {
                        Error("manifest.contributes.setting.default.type", $"{path}.default", "A toggle default must be a boolean.");
                    }

                    break;
                case SettingControlType.Select:
                    if (control.Options is not { Count: > 0 } options)
                    {
                        Error("manifest.contributes.setting.options.missing", $"{path}.options", "A select control requires options.");
                        break;
                    }

                    var values = new HashSet<string>(options.Select(static option => option.Value), StringComparer.Ordinal);
                    if (values.Count != options.Count)
                    {
                        Error("manifest.contributes.setting.options.duplicate", $"{path}.options", "Select option values must be unique.");
                    }

                    if (control.Default is { ValueKind: System.Text.Json.JsonValueKind.String } selectDefault &&
                        !values.Contains(selectDefault.GetString() ?? string.Empty))
                    {
                        Error("manifest.contributes.setting.default.unknown", $"{path}.default", "Select default must match one of the declared options.");
                    }

                    break;
                case SettingControlType.Slider:
                    if (control.Min is null || control.Max is null || control.Step is null)
                    {
                        Error("manifest.contributes.setting.slider.range", path, "A slider requires min, max and step.");
                        break;
                    }

                    if (control.Min >= control.Max)
                    {
                        Error("manifest.contributes.setting.slider.order", path, "A slider requires min < max.");
                    }

                    if (control.Step <= 0)
                    {
                        Error("manifest.contributes.setting.slider.step", path, "A slider requires step > 0.");
                    }

                    if (control.Default is { ValueKind: System.Text.Json.JsonValueKind.Number } defaultNumber &&
                        (defaultNumber.GetDouble() < control.Min || defaultNumber.GetDouble() > control.Max))
                    {
                        Error("manifest.contributes.setting.default.outOfRange", $"{path}.default", "Slider default must be inside [min, max].");
                    }

                    break;
                case SettingControlType.Number:
                    if (control.Min is { } min && control.Max is { } max && min > max)
                    {
                        Error("manifest.contributes.setting.number.range", path, "Number control min must be <= max.");
                    }

                    break;
                case SettingControlType.Button:
                    if (string.IsNullOrWhiteSpace(control.Command) || !_commandIds.Contains(control.Command))
                    {
                        Error("manifest.contributes.setting.button.command", $"{path}.command", "A button control requires a declared command id.");
                    }

                    break;
                case SettingControlType.Link:
                    if (control.Default is { ValueKind: System.Text.Json.JsonValueKind.String } linkDefault && !IsHttpUri(linkDefault.GetString()))
                    {
                        Error("manifest.contributes.setting.default.url", $"{path}.default", "A link default must be an absolute http(s) URI.");
                    }

                    break;
                case SettingControlType.Text:
                case SettingControlType.Path:
                case SettingControlType.Secret:
                    if (control.Validation?.Pattern is { } pattern)
                    {
                        try
                        {
                            _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                        }
                        catch (ArgumentException exception)
                        {
                            Error("manifest.contributes.setting.validation.pattern", $"{path}.validation.pattern", $"Invalid validation pattern: {exception.Message}");
                        }
                    }

                    if (control.Validation?.MinLength is { } minLength && control.Validation.MaxLength is { } maxLength && minLength > maxLength)
                    {
                        Error("manifest.contributes.setting.validation.length", $"{path}.validation", "minLength must be <= maxLength.");
                    }

                    break;
                case SettingControlType.Info:
                    break;
                case SettingControlType.Status:
                    // Read-only row: the plugin writes the value into the settings key and the host re-renders.
                    // Optional command provides an explicit refresh action next to the row.
                    if (control.Command is { Length: > 0 } statusCommand && !_commandIds.Contains(statusCommand))
                    {
                        Error("manifest.contributes.setting.status.command", $"{path}.command", $"Status refresh command '{statusCommand}' is not declared in contributes.commands.");
                    }

                    if (control.Options is not null || control.Min is not null || control.Max is not null || control.Step is not null)
                    {
                        Error("manifest.contributes.setting.status.shape", path, "A status row cannot declare options/min/max/step; it displays the setting value.");
                    }

                    break;
                default:
                    Error("manifest.contributes.setting.type.unknown", $"{path}.type", $"Unknown setting control type '{control.Type}'.");
                    break;
            }
        }

        private void ValidateContextMenu(IReadOnlyList<ContextMenuItemContribution>? items)
        {
            if (items is null)
            {
                return;
            }

            if (items.Count > PluginManifestLimits.MaxContextMenuItems)
            {
                Error("manifest.contributes.contextMenu.tooMany", "contributes.contextMenu", $"At most {PluginManifestLimits.MaxContextMenuItems} items are allowed.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var path = $"contributes.contextMenu[{index}]";
                ValidateNamespacedId(item.Id, $"{path}.id", ids, "manifest.contributes.contextMenu.id");

                if (!_commandIds.Contains(item.Command))
                {
                    Error("manifest.contributes.contextMenu.command.unknown", $"{path}.command", $"Command '{item.Command}' is not declared in contributes.commands.");
                }

                if (item.Title is null)
                {
                    Error("manifest.contributes.contextMenu.title.missing", $"{path}.title", "A context-menu item requires a title.");
                }
                else
                {
                    ValidateLocalized(item.Title, $"{path}.title", maxLength: 200);
                }

                if (!IsValidWhenExpression(item.When))
                {
                    Error("manifest.contributes.contextMenu.when.invalid", $"{path}.when", "The 'when' predicate contains unsupported characters or is empty.");
                }
            }
        }

        private void ValidateWidgets(IReadOnlyList<WidgetContribution>? widgets)
        {
            if (widgets is null)
            {
                return;
            }

            if (widgets.Count > PluginManifestLimits.MaxWidgets)
            {
                Error("manifest.contributes.widgets.tooMany", "contributes.widgets", $"At most {PluginManifestLimits.MaxWidgets} widgets are allowed.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < widgets.Count; index++)
            {
                var widget = widgets[index];
                ValidateNamespacedId(widget.Id, $"contributes.widgets[{index}].id", ids, "manifest.contributes.widget.id");
            }
        }

        private void ValidateNamespacedId(string? id, string path, HashSet<string> ids, string code)
        {
            if (string.IsNullOrWhiteSpace(id) || !NamespacedIdRegex.IsMatch(id))
            {
                Error(code + ".invalid", path, $"Id '{id}' is invalid.");
                return;
            }

            if (!IsNamespaced(id))
            {
                Error(code + ".namespace", path, $"Id '{id}' must start with the plugin id '{_manifest.Id}.'.");
            }

            if (!ids.Add(id))
            {
                Error(code + ".duplicate", path, $"Duplicate id '{id}'.");
            }
        }

        private void ValidateDependencies()
        {
            var dependencies = _manifest.Dependencies;
            if (dependencies?.Sdk is { } sdkRange)
            {
                var result = PluginApiVersion.CheckSdkDependency(sdkRange, _options.HostApiVersion);
                if (!result.IsCompatible)
                {
                    if (result.Failure == VersionCompatibilityFailure.InvalidVersion)
                    {
                        Error("manifest.dependencies.sdk.invalid", "dependencies.sdk", $"SDK dependency range '{sdkRange}' is not supported syntax.");
                    }
                    else
                    {
                        Error("manifest.dependencies.sdk.unsatisfied", "dependencies.sdk", result.Reason);
                    }
                }
            }

            if (dependencies?.Plugins is not { } plugins)
            {
                return;
            }

            if (plugins.Count > PluginManifestLimits.MaxPluginDependencies)
            {
                Error("manifest.dependencies.plugins.tooMany", "dependencies.plugins", $"At most {PluginManifestLimits.MaxPluginDependencies} plugin dependencies are allowed.");
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < plugins.Count; index++)
            {
                var dependency = plugins[index];
                var path = $"dependencies.plugins[{index}]";
                if (string.IsNullOrWhiteSpace(dependency.Id) || !IdRegex.IsMatch(dependency.Id))
                {
                    Error("manifest.dependencies.plugin.id.invalid", $"{path}.id", $"Dependency id '{dependency.Id}' is invalid.");
                }
                else
                {
                    if (string.Equals(dependency.Id, _manifest.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        Error("manifest.dependencies.plugin.self", $"{path}.id", "A plugin cannot depend on itself.");
                    }

                    if (!ids.Add(dependency.Id))
                    {
                        Error("manifest.dependencies.plugin.duplicate", $"{path}.id", $"Duplicate dependency on '{dependency.Id}'.");
                    }
                }

                if (!SemanticVersionRange.TryParse(dependency.Range, out _))
                {
                    Error("manifest.dependencies.plugin.range.invalid", $"{path}.range", $"Dependency range '{dependency.Range}' is not supported syntax.");
                }
            }
        }

        private void ValidateSettingsReference()
        {
            var settings = _manifest.Settings;
            if (settings is null)
            {
                return;
            }

            if (settings.Schema is not null && !SettingsSchemaRegex.IsMatch(settings.Schema))
            {
                Error("manifest.settings.schema.invalid", "settings.schema", "settings.schema must be a .schema.json file name.");
            }

            if (settings.Defaults is not null && !SettingsDefaultsRegex.IsMatch(settings.Defaults))
            {
                Error("manifest.settings.defaults.invalid", "settings.defaults", "settings.defaults must be a .json file name.");
            }
        }

        private void ValidateIntegrityAndSignature()
        {
            if (_manifest.Integrity is { } integrity)
            {
                if (integrity.EntrySha256 is not null && !Sha256Regex.IsMatch(integrity.EntrySha256))
                {
                    Error("manifest.integrity.entrySha256.invalid", "integrity.entrySha256", "integrity.entrySha256 must be 64 lowercase hex characters.");
                }

                if (integrity.Files is not null && integrity.Files != "checksums.json")
                {
                    Error("manifest.integrity.files.invalid", "integrity.files", "integrity.files must be 'checksums.json'.");
                }
            }

            if (_manifest.Signature is not { } signature)
            {
                return;
            }

            if (signature.Algorithm == PluginSignatureAlgorithm.Ed25519)
            {
                Warning("manifest.signature.algorithm.reserved", "signature.algorithm", "Ed25519 is reserved; v2.0 marketplace packages are signed with ECDSA P-256.");
            }

            if (signature.PublisherKeyId is { } publisherKeyId && !KeyIdRegex.IsMatch(publisherKeyId))
            {
                Error("manifest.signature.publisherKeyId.invalid", "signature.publisherKeyId", "signature.publisherKeyId is not a valid key id.");
            }

            if (signature.SignatureFile is not null && signature.SignatureFile != "signature.json")
            {
                Error("manifest.signature.file.invalid", "signature.signatureFile", "signature.signatureFile must be 'signature.json'.");
            }

            if (signature.PublisherKeyId is { } signatureKeyId &&
                _manifest.Publisher?.KeyId is { } publisherKey &&
                !string.Equals(signatureKeyId, publisherKey, StringComparison.Ordinal))
            {
                Warning(
                    "manifest.signature.publisherKeyId.mismatch",
                    "signature.publisherKeyId",
                    $"signature.publisherKeyId '{signatureKeyId}' differs from publisher.keyId '{publisherKey}'.");
            }
        }

        private void ValidateUiConsistency()
        {
            var ui = _manifest.Capabilities?.Ui;
            var contributions = _manifest.Contributes;
            if (contributions is null)
            {
                return;
            }

            if (contributions.Layouts is { Count: > 0 } && ui is not { Island: true })
            {
                Warning("manifest.contributes.layouts.uiCapabilityMissing", "capabilities.ui.island", "Layout contributions are declared without capabilities.ui.island = true.");
            }

            if (contributions.Layouts is { Count: > 0 } layouts && layouts.Any(static layout => layout.Surface == LayoutSurface.HoverCard) && ui is not { HoverCard: true })
            {
                Warning("manifest.contributes.hoverCard.uiCapabilityMissing", "capabilities.ui.hoverCard", "Hover-card layouts are declared without capabilities.ui.hoverCard = true.");
            }

            if (contributions.SettingsPages is { Count: > 0 } && ui is not { Settings: true })
            {
                Warning("manifest.contributes.settings.uiCapabilityMissing", "capabilities.ui.settings", "Settings pages are declared without capabilities.ui.settings = true.");
            }

            if (contributions.Commands is { Count: > 0 } && ui is not { Commands: true })
            {
                Warning("manifest.contributes.commands.uiCapabilityMissing", "capabilities.ui.commands", "Commands are declared without capabilities.ui.commands = true.");
            }

            if (contributions.ContextMenu is { Count: > 0 } && ui is not { ContextMenu: true })
            {
                Warning("manifest.contributes.contextMenu.uiCapabilityMissing", "capabilities.ui.contextMenu", "Context-menu contributions are declared without capabilities.ui.contextMenu = true.");
            }

            if (contributions.Widgets is { Count: > 0 } && ui is not { Widgets: true })
            {
                Warning("manifest.contributes.widgets.uiCapabilityMissing", "capabilities.ui.widgets", "Widgets are declared without capabilities.ui.widgets = true.");
            }
        }

        private static bool IsHttpUri(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        /// <summary>
        /// The host evaluates context-menu predicates with a restricted grammar, never by compiling code.
        /// Validation here is a conservative character whitelist plus length bound.
        /// </summary>
        private static bool IsValidWhenExpression(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (char.IsAsciiLetterOrDigit(character) || char.IsWhiteSpace(character))
                {
                    continue;
                }

                if (character is '\'' or '=' or '!' or '&' or '|' or '(' or ')' or '<' or '>' or '.' or '-' or '_' or '%' or '#' or '[' or ']' or '/' or '*')
                {
                    continue;
                }

                return false;
            }

            return true;
        }
    }
}
