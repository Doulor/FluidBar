namespace FluidBar.Island;

/// <summary>One host-rendered plugin page (M0: list template only).</summary>
public sealed record PluginPageModel
{
    public required string PageId { get; init; }

    public required string OwnerPluginId { get; init; }

    public required string Title { get; init; }

    public string? ShortTitle { get; init; }

    public string? IconToken { get; init; }

    public IReadOnlyList<PluginPageRowModel> Rows { get; init; } = [];

    public string? EmptyTitle { get; init; }

    public string? EmptyHint { get; init; }
}

/// <summary>One row of a plugin list page.</summary>
public sealed record PluginPageRowModel
{
    public required string RowId { get; init; }

    public string? IconToken { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public string? Value { get; init; }

    public double? Progress { get; init; }

    public string? Badge { get; init; }

    public string? RowCommandId { get; init; }

    public string? TrailingCommandId { get; init; }

    public string? TrailingLabel { get; init; }
}
