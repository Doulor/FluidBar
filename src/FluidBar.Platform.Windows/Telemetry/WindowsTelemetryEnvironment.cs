using System.Globalization;
using System.Runtime.InteropServices;
using FluidBar.Core.Telemetry;

namespace FluidBar.Platform.Windows.Telemetry;

/// <summary>Builds the non-identifying environment block that travels with every telemetry batch.</summary>
public static class WindowsTelemetryEnvironment
{
    /// <summary>Describes this build and this machine.</summary>
    /// <param name="appVersion">Version stamp of the running build.</param>
    /// <param name="selfContained">True when the build ships its own runtime.</param>
    /// <param name="locale">UI language in effect.</param>
    /// <returns>The environment block.</returns>
    public static TelemetryEnvironment Create(string appVersion, bool selfContained, string locale)
    {
        var version = Environment.OSVersion.Version;
        return new TelemetryEnvironment
        {
            AppVersion = string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion.Trim(),
            Channel = ChannelOf(appVersion),
            SelfContained = selfContained,
            OsRelease = string.Format(
                CultureInfo.InvariantCulture, "{0}.{1}.{2}", version.Major, version.Minor, version.Build),
            Arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                Architecture.Arm => "arm",
                _ => "x64",
            },
            Locale = string.IsNullOrWhiteSpace(locale) ? "unknown" : locale.Trim(),
        };
    }

    /// <summary>Maps a version stamp to a release channel (<c>preview</c> when the version carries a suffix).</summary>
    /// <param name="appVersion">Version stamp of the running build.</param>
    public static string ChannelOf(string? appVersion)
    {
        if (string.IsNullOrWhiteSpace(appVersion))
        {
            return "unknown";
        }

        var trimmed = appVersion.Trim();
        if (trimmed.Contains('+', StringComparison.Ordinal))
        {
            return "development";
        }

        return trimmed.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("rc", StringComparison.OrdinalIgnoreCase)
            ? "preview"
            : "stable";
    }
}
