using System.Text.Json.Serialization;

namespace FluidBar.Core.Activities;

/// <summary>
/// Stable reference to an expanded page. Pages are identified by their PageId (for example
/// <c>builtin.home</c> or a plugin-owned id); the display index is resolved by the host from the
/// live registry/order and is intentionally NOT part of this value.
/// </summary>
[JsonConverter(typeof(PageRefJsonConverter))]
public readonly record struct PageRef(string PageId)
{
    /// <summary>Dashboard / idle home page.</summary>
    public static PageRef Home { get; } = new("builtin.home");

    /// <summary>Media page.</summary>
    public static PageRef Media { get; } = new("builtin.media");

    /// <summary>Notifications page.</summary>
    public static PageRef Notifications { get; } = new("builtin.notifications");

    /// <summary>Clipboard page.</summary>
    public static PageRef Clipboard { get; } = new("builtin.clipboard");

    /// <summary>Agent page.</summary>
    public static PageRef Agent { get; } = new("builtin.agent");

    /// <summary>Widgets page.</summary>
    public static PageRef Widgets { get; } = new("builtin.widgets");

    /// <summary>The six built-in page ids, in their default order.</summary>
    public static IReadOnlyList<PageRef> BuiltinPages { get; } =
        [Home, Media, Notifications, Clipboard, Agent, Widgets];

    /// <summary>Whether this reference names one of the built-in pages.</summary>
    public bool IsBuiltin => BuiltinPages.Contains(this);

    /// <inheritdoc />
    public override string ToString() => PageId;
}