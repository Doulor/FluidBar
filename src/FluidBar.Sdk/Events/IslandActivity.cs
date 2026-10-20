using System.Collections.Immutable;

namespace FluidBar.Sdk;

/// <summary>Generic activity surface a plugin can publish without owning a compact layout.</summary>
public enum IslandActivityKind
{
    Status = 0,
    Progress = 1,
}

/// <summary>
/// Generic plugin activity (status or progress) rendered by the host's shared compact templates.
/// This is intentionally UI-free: icon/title/subtitle/status/progress are data, never layout.
/// </summary>
public sealed record IslandActivity
{
    public required string Source { get; init; }

    public required string Title { get; init; }

    public IslandActivityKind Kind { get; init; } = IslandActivityKind.Status;

    public string? Subtitle { get; init; }

    public string? Body { get; init; }

    public string? IconToken { get; init; }

    public string? Badge { get; init; }

    public string? StatusText { get; init; }

    public bool IsActive { get; init; }

    /// <summary>Progress in the range 0..1 (progress activity only).</summary>
    public double? Progress { get; init; }

    public IReadOnlyList<string>? DetailLines { get; init; }
}
