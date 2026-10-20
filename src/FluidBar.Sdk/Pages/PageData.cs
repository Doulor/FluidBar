using FluidBar.Sdk.Canvas;

namespace FluidBar.Sdk.Pages;

/// <summary>One declarative list row. Actions are manifest-declared command ids.</summary>
public sealed record PageListRow
{
    public required string RowId { get; init; }

    public string? IconToken { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public string? Value { get; init; }

    public double? Progress { get; init; }

    public string? Tone { get; init; }

    public string? Badge { get; init; }

    /// <summary>Command invoked when the row is activated.</summary>
    public string? RowCommandId { get; init; }

    /// <summary>Command invoked by the row's trailing button.</summary>
    public string? TrailingCommandId { get; init; }

    public string? TrailingLabel { get; init; }
}

/// <summary>Declarative list template data.</summary>
public sealed record PageListData
{
    public required IReadOnlyList<PageListRow> Rows { get; init; }

    public string? EmptyTitle { get; init; }

    public string? EmptyHint { get; init; }
}

/// <summary>Data published for a registered page. M0 renders the list template only.</summary>
public sealed record PageData
{
    public required string PageId { get; init; }

    public long Revision { get; init; }

    public required string TemplateId { get; init; }

    public PageListData? List { get; init; }

    /// <summary>Custom canvas page payload (when TemplateId is <see cref="PageTemplates.Custom"/>).</summary>
    public PageCanvasData? Canvas { get; init; }

    public string? BadgeText { get; init; }

    public int? BadgeCount { get; init; }
}
