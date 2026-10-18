using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FluidBar.Platform.Windows.Accessibility;

/// <summary>
/// Immutable bridge between the provider and the host: live geometry, action dispatch and focus.
/// The provider is created on the UI thread and then called from UIA client threads, so every
/// delegate must be thread-safe; the host implementations only read an atomically replaced geometry
/// snapshot and post mutations to the UI thread.
/// </summary>
public sealed class UiaProviderContext
{
    /// <summary>Creates a context for one snapshot.</summary>
    /// <param name="snapshot">Element tree snapshot.</param>
    /// <param name="toScreen">Host-local DIP → physical screen pixel transform.</param>
    /// <param name="perform">Dispatches an action to the UI thread (must not block).</param>
    /// <param name="setFocus">Moves logical/OS focus to an element id.</param>
    /// <param name="isWindowVisible">Whether the island HWND is visible.</param>
    public UiaProviderContext(
        UiaElementSnapshot snapshot,
        Func<UiaRect, UiaRect> toScreen,
        Action<UiaElementActionRequest> perform,
        Action<string> setFocus,
        Func<bool> isWindowVisible)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        ToScreen = toScreen ?? throw new ArgumentNullException(nameof(toScreen));
        Perform = perform ?? throw new ArgumentNullException(nameof(perform));
        SetFocus = setFocus ?? throw new ArgumentNullException(nameof(setFocus));
        IsWindowVisible = isWindowVisible ?? throw new ArgumentNullException(nameof(isWindowVisible));
    }

    /// <summary>The snapshot this provider tree was built from.</summary>
    public UiaElementSnapshot Snapshot { get; }

    /// <summary>Host-local DIP → screen pixel transform (reads live window geometry).</summary>
    public Func<UiaRect, UiaRect> ToScreen { get; }

    /// <summary>Dispatches an action request without blocking the caller.</summary>
    public Action<UiaElementActionRequest> Perform { get; }

    /// <summary>Moves focus to an element id.</summary>
    public Action<string> SetFocus { get; }

    /// <summary>Whether the island HWND is currently visible.</summary>
    public Func<bool> IsWindowVisible { get; }
}

/// <summary>
/// Managed UI Automation provider for one island element. The same object implements every pattern
/// interface; <see cref="IRawElementProviderSimple.GetPatternProvider"/> returns it only for the
/// patterns the element actually supports, which is what UIA clients use to decide availability.
/// <para>
/// The tree is immutable and built from a <see cref="UiaElementSnapshot"/>; it is never rebuilt per
/// frame. Bounding rectangles are translated from snapshot DIP bounds with the live window geometry,
/// so moving or rescaling the island does not require a new provider tree.
/// </para>
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class UiaElementProvider :
    IRawElementProviderSimple,
    IRawElementProviderFragment,
    IRawElementProviderFragmentRoot,
    IInvokeProvider,
    IExpandCollapseProvider,
    IValueProvider,
    IRangeValueProvider,
    ISelectionItemProvider,
    IToggleProvider
{
    private readonly UiaProviderContext _context;
    private readonly UiaElementSnapshot _node;
    private readonly UiaElementProvider? _parent;
    private readonly UiaElementProvider[] _children;
    private readonly bool _isRoot;

    private UiaElementProvider(UiaElementSnapshot node, UiaProviderContext context,
        UiaElementProvider? parent, bool isRoot)
    {
        _node = node;
        _context = context;
        _parent = parent;
        _isRoot = isRoot;
        Root = isRoot ? this : parent!.Root;
        _children = new UiaElementProvider[node.Children.Count];
        for (var i = 0; i < _children.Length; i++)
        {
            _children[i] = new UiaElementProvider(node.Children[i], context, this, isRoot: false);
        }
    }

    /// <summary>Creates the provider tree for a snapshot.</summary>
    /// <param name="snapshot">Root element snapshot.</param>
    /// <param name="context">Provider context.</param>
    public static UiaElementProvider CreateRoot(UiaElementSnapshot snapshot, UiaProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        return new UiaElementProvider(snapshot, context, parent: null, isRoot: true);
    }

    /// <summary>The root provider of this tree.</summary>
    public UiaElementProvider Root { get; }

    /// <summary>The element snapshot this provider exposes (tests/diagnostics).</summary>
    public UiaElementSnapshot Node => _node;

    /// <summary>Whether this provider is the fragment root.</summary>
    public bool IsRoot => _isRoot;

    // ------------------------------------------------------------------ IRawElementProviderSimple

    /// <inheritdoc />
    public int ProviderOptions => UiaConstants.ProviderOptionsServerSideProvider | UiaConstants.ProviderOptionsUseComThreading;

    /// <inheritdoc />
    public object? GetPatternProvider(int patternId)
    {
        var pattern = patternId switch
        {
            UiaConstants.InvokePatternId => UiaPatternKind.Invoke,
            UiaConstants.ExpandCollapsePatternId => UiaPatternKind.ExpandCollapse,
            UiaConstants.ValuePatternId => UiaPatternKind.Value,
            UiaConstants.RangeValuePatternId => UiaPatternKind.RangeValue,
            UiaConstants.SelectionItemPatternId => UiaPatternKind.SelectionItem,
            UiaConstants.TogglePatternId => UiaPatternKind.Toggle,
            _ => UiaPatternKind.None,
        };
        return pattern != UiaPatternKind.None && Has(pattern) ? this : null;
    }

    /// <inheritdoc />
    public object? GetPropertyValue(int propertyId)
    {
        switch (propertyId)
        {
            case UiaConstants.ControlTypePropertyId:
                return ControlTypeId(_node.Role);
            case UiaConstants.NamePropertyId:
                return _node.Name;
            case UiaConstants.AutomationIdPropertyId:
                return _node.AutomationId ?? _node.Id;
            case UiaConstants.ClassNamePropertyId:
                return "FluidBar.LayeredIslandHost";
            case UiaConstants.FrameworkIdPropertyId:
                return "FluidBar";
            case UiaConstants.ProcessIdPropertyId:
                return Environment.ProcessId;
            case UiaConstants.HelpTextPropertyId:
                return _node.HelpText;
            case UiaConstants.IsEnabledPropertyId:
                return _node.State.IsEnabled;
            case UiaConstants.IsKeyboardFocusablePropertyId:
                return _node.State.IsFocusable;
            case UiaConstants.HasKeyboardFocusPropertyId:
                return _node.State.IsFocused;
            case UiaConstants.IsOffscreenPropertyId:
                return IsOffscreen;
            case UiaConstants.IsControlElementPropertyId:
                return _node.Role != UiaElementRole.Text || _node.State.IsFocusable;
            case UiaConstants.IsContentElementPropertyId:
                return true;
            case UiaConstants.IsInvokePatternAvailablePropertyId:
                return Has(UiaPatternKind.Invoke);
            case UiaConstants.IsExpandCollapsePatternAvailablePropertyId:
                return Has(UiaPatternKind.ExpandCollapse);
            case UiaConstants.IsValuePatternAvailablePropertyId:
                return Has(UiaPatternKind.Value);
            case UiaConstants.IsRangeValuePatternAvailablePropertyId:
                return Has(UiaPatternKind.RangeValue);
            case UiaConstants.IsSelectionItemPatternAvailablePropertyId:
                return Has(UiaPatternKind.SelectionItem);
            case UiaConstants.IsTogglePatternAvailablePropertyId:
                return Has(UiaPatternKind.Toggle);
            case UiaConstants.ValueValuePropertyId:
                return Has(UiaPatternKind.Value) ? _node.State.Value : null;
            case UiaConstants.RangeValueValuePropertyId:
                return Has(UiaPatternKind.RangeValue) ? _node.State.RangeValue ?? 0 : null;
            case UiaConstants.RangeValueMinimumPropertyId:
                return Has(UiaPatternKind.RangeValue) ? _node.State.RangeMinimum : null;
            case UiaConstants.RangeValueMaximumPropertyId:
                return Has(UiaPatternKind.RangeValue) ? _node.State.RangeMaximum : null;
            case UiaConstants.RangeValueSmallChangePropertyId:
                return Has(UiaPatternKind.RangeValue) ? _node.State.RangeSmallChange : null;
            case UiaConstants.RangeValueLargeChangePropertyId:
                return Has(UiaPatternKind.RangeValue) ? _node.State.RangeLargeChange : null;
            case UiaConstants.ExpandCollapseExpandCollapseStatePropertyId:
                return Has(UiaPatternKind.ExpandCollapse) ? (int)_node.State.ExpandCollapse : null;
            case UiaConstants.SelectionItemIsSelectedPropertyId:
                return Has(UiaPatternKind.SelectionItem) ? _node.State.IsSelected : null;
            case UiaConstants.ToggleToggleStatePropertyId:
                return Has(UiaPatternKind.Toggle) ? (int)ToggleState : null;
            case UiaConstants.ItemStatusPropertyId:
                return _node.State.ItemStatus;
            default:
                return null;
        }
    }

    /// <inheritdoc />
    public IRawElementProviderSimple? HostRawElementProvider => null;

    // ------------------------------------------------------------------ IRawElementProviderFragment

    /// <inheritdoc />
    public IRawElementProviderFragment? Navigate(int direction)
    {
        return direction switch
        {
            UiaConstants.NavigateParent => _parent,
            UiaConstants.NavigateFirstChild => _children.Length > 0 ? _children[0] : null,
            UiaConstants.NavigateLastChild => _children.Length > 0 ? _children[^1] : null,
            UiaConstants.NavigateNextSibling => Sibling(+1),
            UiaConstants.NavigatePreviousSibling => Sibling(-1),
            _ => null,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// The fragment root returns null (the HWND identifies it). Children return a full, stable
    /// single-int runtime id rather than <c>[UiaAppendRuntimeId, id]</c>: the root has no runtime id
    /// to append, and UIA rejects the append form in that case (observed with the real UIA client).
    /// </remarks>
    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
    public int[]? GetRuntimeId()
        => _isRoot ? null : [StableRuntimeId(_node.Id)];

    /// <inheritdoc />
    public UiaRectNative BoundingRectangle
    {
        get
        {
            if (IsOffscreen)
            {
                return default;
            }

            return UiaRectNative.From(_context.ToScreen(_node.BoundsDip));
        }
    }

    /// <inheritdoc />
    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    /// <inheritdoc />
    public void SetFocus() => _context.SetFocus(_node.Id);

    /// <inheritdoc />
    public IRawElementProviderFragmentRoot? FragmentRoot => _isRoot ? null : Root;

    // ------------------------------------------------------------------ IRawElementProviderFragmentRoot

    /// <inheritdoc />
    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y)
    {
        if (!_isRoot)
        {
            return null;
        }

        return FindAt(x, y);
    }

    /// <inheritdoc />
    public IRawElementProviderFragment? GetFocus()
    {
        if (!_isRoot)
        {
            return null;
        }

        return FindFocused();
    }

    // ------------------------------------------------------------------ IInvokeProvider

    /// <inheritdoc />
    public void Invoke() => Perform(UiaElementAction.Invoke);

    // ------------------------------------------------------------------ IExpandCollapseProvider

    /// <inheritdoc />
    public void Expand() => Perform(UiaElementAction.Expand);

    /// <inheritdoc />
    public void Collapse() => Perform(UiaElementAction.Collapse);

    /// <inheritdoc />
    public UiaExpandCollapseState ExpandCollapseState => _node.State.ExpandCollapse;

    // ------------------------------------------------------------------ IValueProvider

    /// <inheritdoc />
    public void SetValue(string value)
    {
        // The island MVP has no editable text elements; the pattern is never advertised.
    }

    /// <inheritdoc />
    string IValueProvider.Value => _node.State.Value ?? string.Empty;

    /// <inheritdoc />
    bool IValueProvider.IsReadOnly => true;

    // ------------------------------------------------------------------ IRangeValueProvider

    /// <inheritdoc />
    public void SetValue(double value)
    {
        if (!Has(UiaPatternKind.RangeValue) || !_node.State.IsEnabled)
        {
            return;
        }

        var clamped = Math.Clamp(value, _node.State.RangeMinimum, _node.State.RangeMaximum);
        _context.Perform(new UiaElementActionRequest(UiaElementAction.SetRangeValue, _node.Id, clamped));
    }

    /// <inheritdoc />
    double IRangeValueProvider.Value => _node.State.RangeValue ?? _node.State.RangeMinimum;

    /// <inheritdoc />
    bool IRangeValueProvider.IsReadOnly => !_node.State.IsEnabled;

    /// <inheritdoc />
    double IRangeValueProvider.Maximum => _node.State.RangeMaximum;

    /// <inheritdoc />
    double IRangeValueProvider.Minimum => _node.State.RangeMinimum;

    /// <inheritdoc />
    double IRangeValueProvider.LargeChange => _node.State.RangeLargeChange;

    /// <inheritdoc />
    double IRangeValueProvider.SmallChange => _node.State.RangeSmallChange;

    // ------------------------------------------------------------------ ISelectionItemProvider

    /// <inheritdoc />
    public void Select() => Perform(UiaElementAction.Select);

    /// <inheritdoc />
    public void AddToSelection()
    {
        // Single selection only.
    }

    /// <inheritdoc />
    public void RemoveFromSelection()
    {
        // Single selection only.
    }

    /// <inheritdoc />
    public bool IsSelected => _node.State.IsSelected;

    /// <inheritdoc />
    public IRawElementProviderSimple? SelectionContainer
        => _parent is { } parent && parent._node.Role is UiaElementRole.Tab or UiaElementRole.List ? parent : null;

    // ------------------------------------------------------------------ IToggleProvider

    /// <inheritdoc />
    public void Toggle() => Perform(UiaElementAction.Toggle);

    /// <inheritdoc />
    public UiaToggleState ToggleState => _node.State.IsToggled switch
    {
        true => UiaToggleState.On,
        false => UiaToggleState.Off,
        null => UiaToggleState.Indeterminate,
    };

    // ------------------------------------------------------------------ internals

    /// <summary>Whether the element reports as offscreen to UIA clients.</summary>
    public bool IsOffscreen
        => _node.State.IsOffscreen || _node.BoundsDip.IsEmpty || !_context.IsWindowVisible();

    private bool Has(UiaPatternKind pattern) => (_node.Patterns & pattern) != 0;

    private void Perform(UiaElementAction action)
    {
        if (!_node.State.IsEnabled)
        {
            return;
        }

        _context.Perform(new UiaElementActionRequest(action, _node.Id));
    }

    private UiaElementProvider? Sibling(int delta)
    {
        if (_parent is null)
        {
            return null;
        }

        var index = Array.IndexOf(_parent._children, this);
        var target = index + delta;
        return target >= 0 && target < _parent._children.Length ? _parent._children[target] : null;
    }

    private UiaElementProvider? FindAt(double x, double y)
    {
        // Children win over their parent (deepest hit), and offscreen subtrees are skipped.
        for (var i = _children.Length - 1; i >= 0; i--)
        {
            if (_children[i].FindAt(x, y) is { } found)
            {
                return found;
            }
        }

        if (IsOffscreen)
        {
            return null;
        }

        var bounds = _context.ToScreen(_node.BoundsDip);
        return bounds.Contains(x, y) ? this : null;
    }

    private UiaElementProvider? FindFocused()
    {
        if (_node.State.IsFocused && _node.State.IsFocusable)
        {
            return this;
        }

        foreach (var child in _children)
        {
            if (child.FindFocused() is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static int ControlTypeId(UiaElementRole role) => role switch
    {
        UiaElementRole.Button => UiaConstants.ButtonControlTypeId,
        UiaElementRole.Slider => UiaConstants.SliderControlTypeId,
        UiaElementRole.Tab => UiaConstants.TabControlTypeId,
        UiaElementRole.TabItem => UiaConstants.TabItemControlTypeId,
        UiaElementRole.List => UiaConstants.ListControlTypeId,
        UiaElementRole.ListItem => UiaConstants.ListItemControlTypeId,
        UiaElementRole.Text => UiaConstants.TextControlTypeId,
        _ => UiaConstants.GroupControlTypeId,
    };

    private static int StableRuntimeId(string id)
    {
        // FNV-1a over the stable element id: deterministic across provider rebuilds, so a client can
        // keep a runtime id while the island state changes.
        uint hash = 2166136261;
        foreach (var c in id)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return unchecked((int)hash);
    }
}
