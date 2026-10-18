using System.Globalization;
using System.Text.RegularExpressions;

namespace FluidBar.Sdk.Versioning;

/// <summary>
/// Immutable semantic version (semver 2.0.0) used by the plugin manifest, the marketplace index and
/// the compatibility policy. Build metadata is preserved for round-tripping but ignored for precedence.
/// </summary>
public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private static readonly Regex VersionPattern = new(
        @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+(?<build>[0-9A-Za-z.-]+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Creates a semantic version.</summary>
    public SemanticVersion(int major, int minor, int patch, string? preRelease = null, string? buildMetadata = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
        if (string.IsNullOrWhiteSpace(preRelease))
        {
            preRelease = null;
        }

        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
        BuildMetadata = string.IsNullOrWhiteSpace(buildMetadata) ? null : buildMetadata;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string? PreRelease { get; }

    public string? BuildMetadata { get; }

    public bool IsPreRelease => PreRelease is not null;

    /// <summary>Parses a semver string. Returns <see langword="false"/> instead of throwing for malformed input.</summary>
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionPattern.Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new SemanticVersion(
            major,
            minor,
            patch,
            match.Groups["pre"].Success ? match.Groups["pre"].Value : null,
            match.Groups["build"].Success ? match.Groups["build"].Value : null);
        return true;
    }

    /// <summary>Parses a semver string or throws <see cref="FormatException"/>.</summary>
    public static SemanticVersion Parse(string value) =>
        TryParse(value, out var version)
            ? version
            : throw new FormatException($"'{value}' is not a valid semantic version.");

    public int CompareTo(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0)
        {
            return minor;
        }

        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0)
        {
            return patch;
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    private static int ComparePreRelease(string? left, string? right)
    {
        // A version without a pre-release has higher precedence than one with a pre-release.
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var length = Math.Min(leftParts.Length, rightParts.Length);
        for (var i = 0; i < length; i++)
        {
            var result = ComparePreReleasePart(leftParts[i], rightParts[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static int ComparePreReleasePart(string left, string right)
    {
        var leftNumeric = long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
        var rightNumeric = long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
        if (leftNumeric && rightNumeric)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        // Numeric identifiers always have lower precedence than alphanumeric identifiers.
        if (leftNumeric)
        {
            return -1;
        }

        if (rightNumeric)
        {
            return 1;
        }

        return string.CompareOrdinal(left, right);
    }

    public bool Equals(SemanticVersion other) =>
        Major == other.Major &&
        Minor == other.Minor &&
        Patch == other.Patch &&
        string.Equals(PreRelease, other.PreRelease, StringComparison.Ordinal) &&
        string.Equals(BuildMetadata, other.BuildMetadata, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease, BuildMetadata);

    public override string ToString()
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        if (PreRelease is not null)
        {
            text += "-" + PreRelease;
        }

        if (BuildMetadata is not null)
        {
            text += "+" + BuildMetadata;
        }

        return text;
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public static bool operator ==(SemanticVersion left, SemanticVersion right) => left.Equals(right);

    public static bool operator !=(SemanticVersion left, SemanticVersion right) => !left.Equals(right);

    /// <summary>Copies the major/minor/patch into a <see cref="Version"/> for interop with the BCL.</summary>
    public Version ToVersion() => new(Major, Minor, Patch);

    /// <summary>Creates a semantic version from a BCL <see cref="Version"/>.</summary>
    public static SemanticVersion FromVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new SemanticVersion(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0));
    }
}
