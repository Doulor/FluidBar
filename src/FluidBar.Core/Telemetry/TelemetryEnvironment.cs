namespace FluidBar.Core.Telemetry;

/// <summary>
/// The fixed, non-identifying description of the machine that accompanies every batch: what build is running on
/// which Windows version, in which UI language.
/// </summary>
public sealed record TelemetryEnvironment
{
    /// <summary>App version as shipped in the build stamp (for example <c>4.0.0-preview.3</c>).</summary>
    public required string AppVersion { get; init; }

    /// <summary>Release channel (<c>stable</c> or <c>preview</c>).</summary>
    public required string Channel { get; init; }

    /// <summary>True when the build ships its own .NET runtime (single-file, self-contained).</summary>
    public bool SelfContained { get; init; }

    /// <summary>Windows version as <c>major.minor.build</c> (for example <c>10.0.22631</c>).</summary>
    public required string OsRelease { get; init; }

    /// <summary>Process architecture (<c>x64</c>, <c>arm64</c>, …).</summary>
    public required string Arch { get; init; }

    /// <summary>UI language tag in effect (for example <c>zh-Hans</c>).</summary>
    public required string Locale { get; init; }
}
