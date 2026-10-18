using System.Globalization;

namespace FluidBar.Platform.Windows.Update;

/// <summary>Pure semantic version (major.minor.patch with optional prerelease labels).</summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? Prerelease)
    : IComparable<SemanticVersion>
{
    /// <summary>Parses "2.1.0", "v2.1.0", "2.1.0-beta.2" (case-insensitive). Returns false on garbage.</summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var buildIndex = trimmed.IndexOf('+', StringComparison.Ordinal);
        if (buildIndex >= 0)
        {
            trimmed = trimmed[..buildIndex];
        }

        string? prerelease = null;
        var dashIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            prerelease = trimmed[(dashIndex + 1)..];
            trimmed = trimmed[..dashIndex];
        }

        var parts = trimmed.Split('.');
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) || major < 0)
        {
            return false;
        }

        var minor = 0;
        var patch = 0;
        if (parts.Length > 1
            && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor) || minor < 0))
        {
            return false;
        }

        if (parts.Length > 2
            && (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch) || patch < 0))
        {
            return false;
        }

        // The 4th component (e.g. Windows-style revision) is tolerated but not compared.
        version = new SemanticVersion(major, minor, patch, string.IsNullOrWhiteSpace(prerelease) ? null : prerelease);
        return true;
    }

    /// <summary>Parses or throws <see cref="FormatException"/>.</summary>
    public static SemanticVersion Parse(string text)
        => TryParse(text, out var version) ? version : throw new FormatException($"Invalid semantic version '{text}'.");

    /// <summary>Converts to <see cref="Version"/> (prerelease ignored).</summary>
    public Version ToVersion() => new(Major, Minor, Patch);

    /// <summary>True when this version is newer than <paramref name="other"/>.</summary>
    public bool IsNewerThan(SemanticVersion other) => CompareTo(other) > 0;

    /// <summary>True when this version is newer than a <see cref="Version"/>.</summary>
    public bool IsNewerThan(Version other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Major != other.Major)
        {
            return Major > other.Major;
        }

        if (Minor != other.Minor)
        {
            return Minor > other.Minor;
        }

        if (Patch != other.Build)
        {
            return Patch > (other.Build < 0 ? 0 : other.Build);
        }

        // Same numeric release: a stable version is newer than the prerelease of the same numbers.
        return Prerelease is null && other.Revision >= 0;
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

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

        // Stable > prerelease; two prereleases compare by ordinal label (docs: numeric identifiers
        // compare numerically, but ordinal ordering is deterministic and sufficient for channel gating).
        if (Prerelease is null && other.Prerelease is null)
        {
            return 0;
        }

        if (Prerelease is null)
        {
            return 1;
        }

        if (other.Prerelease is null)
        {
            return -1;
        }

        return string.CompareOrdinal(Prerelease, other.Prerelease);
    }

    public override string ToString()
        => Prerelease is null
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
