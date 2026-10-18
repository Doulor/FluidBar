namespace FluidBar.Platform.Windows.Accessibility;

/// <summary>
/// UI Automation element roles the island exposes. The role selects the UIA control type; the
/// provider never invents a control type that is not in this list.
/// </summary>
public enum UiaElementRole
{
    /// <summary>A structural container (root island, page body, page strip).</summary>
    Group,

    /// <summary>An invokable button.</summary>
    Button,

    /// <summary>A range-value slider (volume/brightness/seek).</summary>
    Slider,

    /// <summary>A tab list (the six pages).</summary>
    Tab,

    /// <summary>A single page tab.</summary>
    TabItem,

    /// <summary>A list container (notifications/clipboard/agent).</summary>
    List,

    /// <summary>A list row.</summary>
    ListItem,

    /// <summary>Read-only text (clock, track, status).</summary>
    Text,
}

/// <summary>UIA patterns an element supports. Only patterns listed here are returned by the provider.</summary>
[Flags]
public enum UiaPatternKind
{
    /// <summary>No pattern.</summary>
    None = 0,

    /// <summary><c>IInvokeProvider</c>.</summary>
    Invoke = 1,

    /// <summary><c>IExpandCollapseProvider</c>.</summary>
    ExpandCollapse = 2,

    /// <summary><c>IValueProvider</c>.</summary>
    Value = 4,

    /// <summary><c>IRangeValueProvider</c>.</summary>
    RangeValue = 8,

    /// <summary><c>ISelectionItemProvider</c>.</summary>
    SelectionItem = 16,

    /// <summary><c>IToggleProvider</c>.</summary>
    Toggle = 32,
}

/// <summary>Mirrors <c>ExpandCollapseState</c> from UIAutomationCore.h.</summary>
public enum UiaExpandCollapseState
{
    /// <summary>Collapsed (compact pill).</summary>
    Collapsed = 0,

    /// <summary>Expanded (page panel).</summary>
    Expanded = 1,

    /// <summary>Partially expanded.</summary>
    PartiallyExpanded = 2,

    /// <summary>Not expandable.</summary>
    LeafNode = 3,
}

/// <summary>Mirrors <c>ToggleState</c> from UIAutomationCore.h.</summary>
public enum UiaToggleState
{
    /// <summary>Off/false.</summary>
    Off = 0,

    /// <summary>On/true.</summary>
    On = 1,

    /// <summary>Indeterminate.</summary>
    Indeterminate = 2,
}

/// <summary>Keyboard commands the host forwards from the island HWND.</summary>
public enum UiaKeyCommand
{
    /// <summary>Tab: focus the next element.</summary>
    Tab,

    /// <summary>Shift+Tab: focus the previous element.</summary>
    ShiftTab,

    /// <summary>Left arrow.</summary>
    Left,

    /// <summary>Right arrow.</summary>
    Right,

    /// <summary>Up arrow.</summary>
    Up,

    /// <summary>Down arrow.</summary>
    Down,

    /// <summary>Home.</summary>
    Home,

    /// <summary>End.</summary>
    End,

    /// <summary>Enter.</summary>
    Enter,

    /// <summary>Space.</summary>
    Space,

    /// <summary>Escape.</summary>
    Escape,
}

/// <summary>Actions a UIA client can request on one element.</summary>
public enum UiaElementAction
{
    /// <summary>Default action (Invoke pattern).</summary>
    Invoke,

    /// <summary>Expand (ExpandCollapse pattern).</summary>
    Expand,

    /// <summary>Collapse (ExpandCollapse pattern).</summary>
    Collapse,

    /// <summary>Select a tab/row (SelectionItem pattern).</summary>
    Select,

    /// <summary>Toggle a switch (Toggle pattern).</summary>
    Toggle,

    /// <summary>Move logical focus to the element (Fragment.SetFocus).</summary>
    SetFocus,

    /// <summary>Set a slider value (RangeValue pattern).</summary>
    SetRangeValue,
}

/// <summary>
/// One provider → App request. Requests cross from a UIA client thread to the UI thread through the
/// host dispatcher; the App implementation must never block.
/// </summary>
/// <param name="Action">Requested action.</param>
/// <param name="ElementId">Stable element id from the snapshot.</param>
/// <param name="Value">Range value for <see cref="UiaElementAction.SetRangeValue"/>.</param>
public readonly record struct UiaElementActionRequest(UiaElementAction Action, string ElementId, double Value = 0);

/// <summary>Rect in either host-local DIP or physical screen pixels; the context decides.</summary>
public readonly record struct UiaRect(double X, double Y, double Width, double Height)
{
    /// <summary>An empty rect (no visible bounds).</summary>
    public static UiaRect Empty => default;

    /// <summary>Whether the rect has no positive area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Whether a point is inside the rect (inclusive edges).</summary>
    public bool Contains(double x, double y)
        => x >= X && x <= X + Width && y >= Y && y <= Y + Height;

    /// <summary>Grows the rect by the given amount on every side.</summary>
    public UiaRect Inflate(double dx, double dy)
        => new(X - dx, Y - dy, Width + (2 * dx), Height + (2 * dy));
}

/// <summary>Element state exposed as UIA properties.</summary>
public sealed record UiaElementState
{
    /// <summary>A default enabled state.</summary>
    public static UiaElementState Enabled { get; } = new();

    /// <summary>Whether the element can be acted on.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Whether the element can receive logical focus.</summary>
    public bool IsFocusable { get; init; }

    /// <summary>Whether the element currently owns logical focus.</summary>
    public bool IsFocused { get; init; }

    /// <summary>Whether the element is outside the visible island surface.</summary>
    public bool IsOffscreen { get; init; }

    /// <summary>Whether a tab/row is the selected item.</summary>
    public bool IsSelected { get; init; }

    /// <summary>Toggle state; null when the element is not a toggle.</summary>
    public bool? IsToggled { get; init; }

    /// <summary>Expand/collapse state for the root element.</summary>
    public UiaExpandCollapseState ExpandCollapse { get; init; } = UiaExpandCollapseState.LeafNode;

    /// <summary>Text value for <c>IValueProvider</c>.</summary>
    public string? Value { get; init; }

    /// <summary>Current numeric value for <c>IRangeValueProvider</c>; null when not a slider.</summary>
    public double? RangeValue { get; init; }

    /// <summary>Slider minimum.</summary>
    public double RangeMinimum { get; init; }

    /// <summary>Slider maximum.</summary>
    public double RangeMaximum { get; init; } = 100;

    /// <summary>Slider small change.</summary>
    public double RangeSmallChange { get; init; } = 1;

    /// <summary>Slider large change.</summary>
    public double RangeLargeChange { get; init; } = 10;

    /// <summary>Short status line (row status, track time, unsupported reason).</summary>
    public string? ItemStatus { get; init; }
}

/// <summary>
/// Immutable accessibility element snapshot. Bounds are host-local DIP; the provider translates them
/// to screen pixels with the live window geometry, so a snapshot survives window moves and DPI
/// changes without being rebuilt.
/// </summary>
public sealed record UiaElementSnapshot
{
    /// <summary>Stable element id (also the automation id fallback).</summary>
    public required string Id { get; init; }

    /// <summary>Element role (selects the UIA control type).</summary>
    public UiaElementRole Role { get; init; } = UiaElementRole.Group;

    /// <summary>Localized accessible name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional automation id; defaults to <see cref="Id"/>.</summary>
    public string? AutomationId { get; init; }

    /// <summary>Optional help text (keyboard hint, unsupported reason).</summary>
    public string? HelpText { get; init; }

    /// <summary>Element bounds in host-local DIP.</summary>
    public UiaRect BoundsDip { get; init; }

    /// <summary>Patterns the element supports.</summary>
    public UiaPatternKind Patterns { get; init; }

    /// <summary>Element state.</summary>
    public UiaElementState State { get; init; } = UiaElementState.Enabled;

    /// <summary>Child elements in keyboard/navigation order.</summary>
    public IReadOnlyList<UiaElementSnapshot> Children { get; init; } = [];

    /// <summary>Finds a descendant by id (depth-first).</summary>
    /// <param name="id">Element id.</param>
    public UiaElementSnapshot? Find(string id)
    {
        if (string.Equals(Id, id, StringComparison.Ordinal))
        {
            return this;
        }

        foreach (var child in Children)
        {
            if (child.Find(id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Enumerates this node and all descendants in depth-first order.</summary>
    public IEnumerable<UiaElementSnapshot> Descendants()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var node in child.Descendants())
            {
                yield return node;
            }
        }
    }
}

/// <summary>
/// Live window geometry used to turn host-local DIP bounds into physical screen pixels. Replaced
/// atomically on placement/DPI/visibility changes; read from UIA client threads.
/// </summary>
/// <param name="OriginX">Window client origin X in screen pixels.</param>
/// <param name="OriginY">Window client origin Y in screen pixels.</param>
/// <param name="Scale">DIP → pixel scale.</param>
/// <param name="WidthPx">Window width in pixels.</param>
/// <param name="HeightPx">Window height in pixels.</param>
/// <param name="IsVisible">Whether the island is currently shown.</param>
public sealed record UiaWindowGeometry(
    int OriginX,
    int OriginY,
    double Scale,
    int WidthPx,
    int HeightPx,
    bool IsVisible)
{
    /// <summary>Safe scale (never zero/NaN).</summary>
    public double EffectiveScale => double.IsFinite(Scale) && Scale > 0.01 ? Scale : 1.0;

    /// <summary>Transforms a host-local DIP rect into physical screen pixels.</summary>
    public UiaRect ToScreen(in UiaRect localDip)
    {
        var scale = EffectiveScale;
        return new UiaRect(
            OriginX + (localDip.X * scale),
            OriginY + (localDip.Y * scale),
            localDip.Width * scale,
            localDip.Height * scale);
    }

    /// <summary>Transforms a physical screen point into host-local DIP.</summary>
    public (double X, double Y) ToLocal(double screenX, double screenY)
    {
        var scale = EffectiveScale;
        return ((screenX - OriginX) / scale, (screenY - OriginY) / scale);
    }
}

/// <summary>
/// App-side seam that builds the island accessibility tree and executes provider requests. The host
/// owns the HWND and the UIA plumbing; it never knows about pages, media or settings.
/// </summary>
public interface IUiaElementSource
{
    /// <summary>Builds the current immutable element tree. Called on the UI thread only.</summary>
    UiaElementSnapshot BuildSnapshot();

    /// <summary>
    /// Executes one UIA action. Called on the UI thread (the host dispatches off-thread requests);
    /// implementations must return without blocking.
    /// </summary>
    /// <param name="request">Action request.</param>
    void Perform(in UiaElementActionRequest request);

    /// <summary>Raised when the source's own state (focus) changed and the snapshot must be rebuilt.</summary>
    event Action? Changed;
}

/// <summary>Keyboard seam for the island HWND (focus is logical while the window is focused).</summary>
public interface IUiaKeyboardSink
{
    /// <summary>Handles one key command; returns whether the island consumed it.</summary>
    /// <param name="command">Key command.</param>
    bool HandleKey(UiaKeyCommand command);

    /// <summary>Notifies the sink that the island HWND gained or lost keyboard focus.</summary>
    /// <param name="focused">Whether the HWND has focus.</param>
    void NotifyWindowFocus(bool focused);
}
