namespace FluidBar.Island;

/// <summary>Which host renderer/model backs a page slot.</summary>
public enum PageSlotKind
{
    Home = 0,
    Media = 1,
    Notifications = 2,
    Clipboard = 3,
    Agent = 4,
    Widgets = 5,
    Plugin = 6,
}

/// <summary>One ordered page slot: built-ins use native templates, plugins carry host list data.</summary>
public sealed record PageSlot
{
    public required string PageId { get; init; }

    public required PageSlotKind Kind { get; init; }

    public required string TemplateId { get; init; }

    public required string Title { get; init; }

    public string? ShortTitle { get; init; }

    public string? IconToken { get; init; }

    public PluginPageModel? PluginPage { get; init; }
}
