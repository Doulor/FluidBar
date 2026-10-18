using FluidBar.Sdk.Contributions;

namespace FluidBar.Sdk.Contributions;

/// <summary>
/// The host-owned contract between layout templates and the slot names they accept. The manifest validator,
/// the host renderer and the contributor documentation all read this table so they cannot drift apart.
/// </summary>
public static class LayoutTemplateCatalog
{
    private static readonly IReadOnlyDictionary<LayoutTemplate, IReadOnlySet<string>> SlotsByTemplate =
        new Dictionary<LayoutTemplate, IReadOnlySet<string>>
        {
            [LayoutTemplate.TextV1] = Set("title", "subtitle", "body", "icon", "badge", "progress", "accessory"),
            [LayoutTemplate.StatusV1] = Set("title", "subtitle", "body", "icon", "badge", "accessory"),
            [LayoutTemplate.ProgressV1] = Set("title", "subtitle", "icon", "badge", "progress", "accessory"),
            [LayoutTemplate.MediaV1] = Set("icon", "title", "subtitle", "badge", "progress", "accessory"),
            [LayoutTemplate.MediaHoverV1] = Set("title", "subtitle", "body", "artwork", "progress", "actions"),
            [LayoutTemplate.NotificationV1] = Set("icon", "title", "body", "badge", "actions"),
            [LayoutTemplate.AgentV1] = Set("icon", "title", "subtitle", "body", "badge", "progress", "actions"),
            [LayoutTemplate.ClockV1] = Set("title", "subtitle", "body"),
            [LayoutTemplate.ListV1] = Set("title", "items", "actions"),
        };

    /// <summary>Returns the slot names a template accepts.</summary>
    public static IReadOnlySet<string> GetAllowedSlots(LayoutTemplate template) =>
        SlotsByTemplate.TryGetValue(template, out var slots)
            ? slots
            : throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown layout template.");

    /// <summary>Returns <see langword="true"/> when <paramref name="slot"/> is accepted by <paramref name="template"/>.</summary>
    public static bool IsSlotAllowed(LayoutTemplate template, string slot) =>
        SlotsByTemplate.TryGetValue(template, out var slots) && slots.Contains(slot);

    /// <summary>All templates that a given surface accepts.</summary>
    public static IReadOnlySet<LayoutTemplate> GetAllowedTemplates(LayoutSurface surface) => surface switch
    {
        LayoutSurface.IslandCompact => new HashSet<LayoutTemplate>
        {
            LayoutTemplate.TextV1,
            LayoutTemplate.StatusV1,
            LayoutTemplate.ProgressV1,
            LayoutTemplate.MediaV1,
            LayoutTemplate.NotificationV1,
            LayoutTemplate.AgentV1,
            LayoutTemplate.ClockV1,
        },
        LayoutSurface.IslandExpanded => new HashSet<LayoutTemplate>
        {
            LayoutTemplate.TextV1,
            LayoutTemplate.MediaV1,
            LayoutTemplate.NotificationV1,
            LayoutTemplate.AgentV1,
            LayoutTemplate.ListV1,
        },
        LayoutSurface.HoverCard => new HashSet<LayoutTemplate>
        {
            LayoutTemplate.MediaHoverV1,
            LayoutTemplate.AgentV1,
            LayoutTemplate.TextV1,
            LayoutTemplate.ListV1,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, "Unknown layout surface."),
    };

    private static IReadOnlySet<string> Set(params string[] slots) => new HashSet<string>(slots, StringComparer.Ordinal);
}
