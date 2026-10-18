using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FluidBar.Platform.Windows.Accessibility;

/// <summary>
/// UI Automation provider COM contracts, transcribed from the Windows SDK
/// <c>UIAutomationCore.h</c>. Only the interfaces the island provider implements are declared here;
/// the GUIDs and vtable order are the on-the-wire contract and must never be reordered.
/// </summary>
internal static class UiaConstants
{
    /// <summary><c>UiaRootObjectId</c> (WM_GETOBJECT lParam for the provider root).</summary>
    internal const int RootObjectId = -25;

    /// <summary><c>UiaAppendRuntimeId</c> (first element of a fragment runtime id).</summary>
    internal const int AppendRuntimeId = 3;

    /// <summary><c>ProviderOptions_ServerSideProvider</c>.</summary>
    internal const int ProviderOptionsServerSideProvider = 0x2;

    /// <summary><c>ProviderOptions_UseComThreading</c>.</summary>
    internal const int ProviderOptionsUseComThreading = 0x20;

    // UIA property ids (UIAutomationClient.h).
    internal const int ProcessIdPropertyId = 30002;
    internal const int ControlTypePropertyId = 30003;
    internal const int NamePropertyId = 30005;
    internal const int HasKeyboardFocusPropertyId = 30008;
    internal const int IsKeyboardFocusablePropertyId = 30009;
    internal const int IsEnabledPropertyId = 30010;
    internal const int AutomationIdPropertyId = 30011;
    internal const int ClassNamePropertyId = 30012;
    internal const int HelpTextPropertyId = 30013;
    internal const int IsControlElementPropertyId = 30016;
    internal const int IsContentElementPropertyId = 30017;
    internal const int IsOffscreenPropertyId = 30022;
    internal const int FrameworkIdPropertyId = 30024;
    internal const int ItemStatusPropertyId = 30026;
    internal const int IsExpandCollapsePatternAvailablePropertyId = 30028;
    internal const int IsInvokePatternAvailablePropertyId = 30031;
    internal const int IsRangeValuePatternAvailablePropertyId = 30033;
    internal const int IsSelectionItemPatternAvailablePropertyId = 30036;
    internal const int IsTogglePatternAvailablePropertyId = 30041;
    internal const int IsValuePatternAvailablePropertyId = 30043;
    internal const int ValueValuePropertyId = 30045;
    internal const int RangeValueValuePropertyId = 30047;
    internal const int RangeValueMinimumPropertyId = 30049;
    internal const int RangeValueMaximumPropertyId = 30050;
    internal const int RangeValueLargeChangePropertyId = 30051;
    internal const int RangeValueSmallChangePropertyId = 30052;
    internal const int ExpandCollapseExpandCollapseStatePropertyId = 30070;
    internal const int SelectionItemIsSelectedPropertyId = 30079;
    internal const int ToggleToggleStatePropertyId = 30086;

    // UIA pattern ids.
    internal const int InvokePatternId = 10000;
    internal const int RangeValuePatternId = 10003;
    internal const int ExpandCollapsePatternId = 10005;
    internal const int SelectionItemPatternId = 10010;
    internal const int TogglePatternId = 10015;
    internal const int ValuePatternId = 10002;

    // UIA control type ids.
    internal const int ButtonControlTypeId = 50000;
    internal const int ListItemControlTypeId = 50007;
    internal const int ListControlTypeId = 50008;
    internal const int SliderControlTypeId = 50015;
    internal const int TabControlTypeId = 50018;
    internal const int TabItemControlTypeId = 50019;
    internal const int TextControlTypeId = 50020;
    internal const int GroupControlTypeId = 50026;

    // UIA navigation directions.
    internal const int NavigateParent = 0;
    internal const int NavigateNextSibling = 1;
    internal const int NavigatePreviousSibling = 2;
    internal const int NavigateFirstChild = 3;
    internal const int NavigateLastChild = 4;

    /// <summary><c>StructureChangeType_ChildrenInvalidated</c>.</summary>
    internal const int StructureChangeTypeChildrenInvalidated = 2;

    /// <summary><c>UIA_AutomationFocusChangedEventId</c>.</summary>
    internal const int AutomationFocusChangedEventId = 20005;
}

/// <summary><c>UiaRect</c> (double screen coordinates).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct UiaRectNative
{
    /// <summary>Left edge in screen pixels.</summary>
    public double Left;

    /// <summary>Top edge in screen pixels.</summary>
    public double Top;

    /// <summary>Width in pixels.</summary>
    public double Width;

    /// <summary>Height in pixels.</summary>
    public double Height;

    /// <summary>Creates a native rect from the managed DIP/screen rect.</summary>
    public static UiaRectNative From(in UiaRect rect) => new()
    {
        Left = rect.X,
        Top = rect.Y,
        Width = rect.Width,
        Height = rect.Height,
    };
}

/// <summary><c>IRawElementProviderSimple</c>.</summary>
[ComImport]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface IRawElementProviderSimple
{
    /// <summary>Provider option flags.</summary>
    int ProviderOptions { get; }

    /// <summary>Returns the pattern interface object, or null when the pattern is unsupported.</summary>
    /// <param name="patternId">UIA pattern id.</param>
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object? GetPatternProvider(int patternId);

    /// <summary>Returns a UIA property value, or null when the property is unsupported.</summary>
    /// <param name="propertyId">UIA property id.</param>
    object? GetPropertyValue(int propertyId);

    /// <summary>Host provider for a windowless element; always null for the island.</summary>
    IRawElementProviderSimple? HostRawElementProvider { get; }
}

/// <summary><c>IRawElementProviderFragment</c>.</summary>
[ComImport]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface IRawElementProviderFragment
{
    /// <summary>Navigates the fragment tree.</summary>
    /// <param name="direction">UIA navigation direction.</param>
    IRawElementProviderFragment? Navigate(int direction);

    /// <summary>Runtime id; null for the fragment root.</summary>
    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
    int[]? GetRuntimeId();

    /// <summary>Bounding rectangle in physical screen coordinates.</summary>
    UiaRectNative BoundingRectangle { get; }

    /// <summary>Embedded fragment roots; null for the island.</summary>
    IRawElementProviderSimple[]? GetEmbeddedFragmentRoots();

    /// <summary>Moves logical focus to this element.</summary>
    void SetFocus();

    /// <summary>The fragment root this element belongs to (null on the root itself).</summary>
    IRawElementProviderFragmentRoot? FragmentRoot { get; }
}

/// <summary><c>IRawElementProviderFragmentRoot</c>.</summary>
[ComImport]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface IRawElementProviderFragmentRoot
{
    /// <summary>Hit-tests a screen point and returns the deepest provider.</summary>
    /// <param name="x">Screen X in pixels.</param>
    /// <param name="y">Screen Y in pixels.</param>
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);

    /// <summary>Returns the focused element, or null when the island does not own focus.</summary>
    IRawElementProviderFragment? GetFocus();
}

/// <summary><c>IInvokeProvider</c>.</summary>
[ComImport]
[Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface IInvokeProvider
{
    /// <summary>Invokes the element's default action.</summary>
    void Invoke();
}

/// <summary><c>IExpandCollapseProvider</c>.</summary>
[ComImport]
[Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface IExpandCollapseProvider
{
    /// <summary>Expands the element.</summary>
    void Expand();

    /// <summary>Collapses the element.</summary>
    void Collapse();

    /// <summary>Current expand/collapse state.</summary>
    UiaExpandCollapseState ExpandCollapseState { get; }
}

/// <summary><c>IValueProvider</c>.</summary>
[ComImport]
[Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface IValueProvider
{
    /// <summary>Sets the text value (never supported by the island MVP).</summary>
    /// <param name="value">New value.</param>
    void SetValue([MarshalAs(UnmanagedType.LPWStr)] string value);

    /// <summary>Current text value.</summary>
    string Value { get; }

    /// <summary>Whether the value is read-only.</summary>
    bool IsReadOnly { [return: MarshalAs(UnmanagedType.Bool)] get; }
}

/// <summary><c>IRangeValueProvider</c>.</summary>
[ComImport]
[Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface IRangeValueProvider
{
    /// <summary>Sets the numeric value.</summary>
    /// <param name="value">New value.</param>
    void SetValue(double value);

    /// <summary>Current value.</summary>
    double Value { get; }

    /// <summary>Whether the value is read-only.</summary>
    bool IsReadOnly { [return: MarshalAs(UnmanagedType.Bool)] get; }

    /// <summary>Maximum value.</summary>
    double Maximum { get; }

    /// <summary>Minimum value.</summary>
    double Minimum { get; }

    /// <summary>Large change step.</summary>
    double LargeChange { get; }

    /// <summary>Small change step.</summary>
    double SmallChange { get; }
}

/// <summary><c>ISelectionItemProvider</c>.</summary>
[ComImport]
[Guid("2acad808-b2d4-452d-a407-91ff1ad167b2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "The COM method name is fixed by UIAutomationCore.h.")]
public interface ISelectionItemProvider
{
    /// <summary>Selects the item.</summary>
    void Select();

    /// <summary>Adds the item to a multi-selection (single-selection island: no-op).</summary>
    void AddToSelection();

    /// <summary>Removes the item from a multi-selection (single-selection island: no-op).</summary>
    void RemoveFromSelection();

    /// <summary>Whether the item is selected.</summary>
    bool IsSelected { [return: MarshalAs(UnmanagedType.Bool)] get; }

    /// <summary>The owning selection container (the page tab list).</summary>
    IRawElementProviderSimple? SelectionContainer { get; }
}

/// <summary><c>IToggleProvider</c>.</summary>
[ComImport]
[Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface IToggleProvider
{
    /// <summary>Toggles the element.</summary>
    void Toggle();

    /// <summary>Current toggle state.</summary>
    UiaToggleState ToggleState { get; }
}
