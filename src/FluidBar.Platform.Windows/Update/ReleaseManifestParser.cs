using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluidBar.Platform.Windows.Update;

/// <summary>
/// Pure GitHub Releases manifest parser. No network here: the client passes response text and gets an
/// immutable release back. Malformed input yields <c>null</c> (never throws), so a broken feed can only
/// degrade the update feature.
/// </summary>
public static class ReleaseManifestParser
{
    /// <summary>
    /// Selects the newest release for the channel and its installer asset.
    /// <para>
    /// When the stable channel has no stable release at all, the newest prerelease is returned instead
    /// (<paramref name="allowPrereleaseFallback"/>). A preview product publishes prereleases only, and
    /// treating that feed as "no usable release" made every shipped build report a check *failure* —
    /// a self-inflicted bug report on the About page. A stable release, when one exists, always wins.
    /// </para>
    /// </summary>
    public static UpdateRelease? SelectRelease(string json, UpdateChannel channel, string installerAssetPattern,
        bool allowPrereleaseFallback = true)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var pattern = new Regex(installerAssetPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            UpdateRelease? bestStable = null;
            UpdateRelease? bestPrerelease = null;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var release = TryParseRelease(element, pattern);
                if (release is null)
                {
                    continue;
                }

                // Channel filtering happens in the caller: the two candidates let the stable channel fall
                // back to a prerelease feed instead of reporting "nothing usable".
                if (release.IsPrerelease)
                {
                    if (bestPrerelease is null || release.Version.IsNewerThan(bestPrerelease.Version))
                    {
                        bestPrerelease = release;
                    }
                }
                else if (bestStable is null || release.Version.IsNewerThan(bestStable.Version))
                {
                    bestStable = release;
                }
            }

            if (channel == UpdateChannel.Stable)
            {
                return bestStable ?? (allowPrereleaseFallback ? bestPrerelease : null);
            }

            // The prerelease channel accepts both kinds; the newest wins regardless of stability.
            return bestPrerelease is null || (bestStable is not null && bestStable.Version.IsNewerThan(bestPrerelease.Version))
                ? bestStable
                : bestPrerelease;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UpdateRelease? TryParseRelease(JsonElement element, Regex installerPattern)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var isPrerelease = element.TryGetProperty("prerelease", out var prereleaseElement)
                           && prereleaseElement.ValueKind == JsonValueKind.True;

        if (element.TryGetProperty("draft", out var draftElement) && draftElement.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        var tag = GetString(element, "tag_name");
        if (!SemanticVersion.TryParse(tag, out var version))
        {
            return null;
        }

        var assets = ImmutableArray.CreateBuilder<UpdateAsset>();
        UpdateAsset? installer = null;
        if (element.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var assetElement in assetsElement.EnumerateArray())
            {
                if (assetElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = GetString(assetElement, "name");
                var url = GetString(assetElement, "browser_download_url");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)
                    || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    continue;
                }

                var size = assetElement.TryGetProperty("size", out var sizeElement)
                           && sizeElement.ValueKind == JsonValueKind.Number
                           && sizeElement.TryGetInt64(out var parsedSize)
                    ? parsedSize
                    : 0L;
                var sha256 = ExtractSha256(assetElement);
                var asset = new UpdateAsset(name, uri, size, sha256);
                assets.Add(asset);
                if (installer is null && installerPattern.IsMatch(name))
                {
                    installer = asset;
                }
            }
        }

        Uri? releasePage = null;
        if (Uri.TryCreate(GetString(element, "html_url"), UriKind.Absolute, out var htmlUri))
        {
            releasePage = htmlUri;
        }

        // Feed timestamps are ISO-8601/RFC-3339. Parse them with the invariant culture and round-trip
        // semantics: the current culture must never reinterpret a release date (e.g. non-Gregorian
        // calendars or different date separators), and the feed offset must be preserved as-is.
        DateTimeOffset? publishedAt = null;
        if (DateTimeOffset.TryParse(GetString(element, "published_at"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsedPublished))
        {
            publishedAt = parsedPublished;
        }

        return new UpdateRelease(
            Tag: tag ?? version.ToString(),
            Version: version,
            Name: GetString(element, "name") ?? version.ToString(),
            Notes: GetString(element, "body"),
            ReleasePageUrl: releasePage,
            IsPrerelease: isPrerelease,
            PublishedAt: publishedAt,
            Assets: assets.ToImmutable(),
            Installer: installer,
            InstallerSha256Hex: installer?.Sha256Hex);
    }

    /// <summary>
    /// Extracts a SHA-256 digest from an asset. GitHub exposes it as <c>digest: "sha256:…"</c>; some
    /// feeds use <c>sha256</c>, <c>sha256sum</c> or an <c>integrity</c> string. Returns lowercase hex.
    /// </summary>
    internal static string? ExtractSha256(JsonElement asset)
    {
        foreach (var propertyName in new[] { "digest", "sha256", "sha256sum", "integrity" })
        {
            if (!asset.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var index = text.IndexOf("sha256", StringComparison.OrdinalIgnoreCase);
            var candidate = index >= 0 ? text[(index + 6)..] : text;
            candidate = candidate.TrimStart(':', '-', ' ', '=');
            var hex = new string(candidate.Where(Uri.IsHexDigit).ToArray());
            if (hex.Length == 64)
            {
                return hex.ToLowerInvariant();
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
