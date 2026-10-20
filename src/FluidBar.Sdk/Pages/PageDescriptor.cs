using FluidBar.Sdk.Manifest;

namespace FluidBar.Sdk.Pages;

/// <summary>Public template ids the host can render without plugin pixels.</summary>
public static class PageTemplates
{
    public const string List = "list";
    public const string StatGrid = "stat-grid";
    public const string Lyric = "lyric";
    public const string Calendar = "calendar";
    public const string Custom = "custom";
}

/// <summary>Where a page badge value comes from.</summary>
public enum PageBadgeSource
{
    None = 0,
    Count = 1,
    Text = 2,
    Dot = 3,
}

/// <summary>
/// One page contributed by a plugin (or, later, by a built-in provider). The host owns layout and pixels;
/// the plugin supplies only this descriptor plus template data.
/// </summary>
public sealed record PageDescriptor
{
    public required string PageId { get; init; }

    /// <summary>Default position weight; lower sorts first.</summary>
    public required int Order { get; init; }

    public required LocalizedString Title { get; init; }

    public LocalizedString? ShortTitle { get; init; }

    /// <summary>Host icon token (for example "list", "calendar", "agent").</summary>
    public required string IconToken { get; init; }

    public required string TemplateId { get; init; }

    public PageBadgeSource BadgeSource { get; init; } = PageBadgeSource.None;

    public bool ShowInRail { get; init; } = true;

    public bool ShowInSettings { get; init; } = true;
}
