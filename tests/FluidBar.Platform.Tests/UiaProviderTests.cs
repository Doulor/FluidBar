using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Accessibility;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Pure UI Automation provider tests: element tree, control types, patterns, bounds translation,
/// navigation and action dispatch. No HWND and no UIAutomationCore call is required.
/// </summary>
public sealed class UiaProviderTests
{
    private static UiaElementSnapshot Snapshot(bool focused = false) => new()
    {
        Id = "island.root",
        Role = UiaElementRole.Group,
        Name = "FluidBar",
        BoundsDip = new UiaRect(24, 24, 440, 284),
        Patterns = UiaPatternKind.Invoke | UiaPatternKind.ExpandCollapse,
        State = new UiaElementState
        {
            IsFocusable = true,
            IsFocused = focused,
            ExpandCollapse = UiaExpandCollapseState.Expanded,
        },
        Children =
        [
            new UiaElementSnapshot
            {
                Id = "island.page.1",
                Role = UiaElementRole.TabItem,
                Name = "Now playing",
                BoundsDip = new UiaRect(40, 40, 100, 30),
                Patterns = UiaPatternKind.SelectionItem | UiaPatternKind.Invoke,
                State = new UiaElementState { IsFocusable = true, IsSelected = true },
            },
            new UiaElementSnapshot
            {
                Id = "island.page.1.seek",
                Role = UiaElementRole.Slider,
                Name = "Seek",
                BoundsDip = new UiaRect(40, 80, 200, 20),
                Patterns = UiaPatternKind.RangeValue,
                State = new UiaElementState
                {
                    IsFocusable = true,
                    RangeValue = 46,
                    RangeMinimum = 0,
                    RangeMaximum = 100,
                    RangeSmallChange = 5,
                    RangeLargeChange = 10,
                },
            },
            new UiaElementSnapshot
            {
                Id = "island.page.1.play",
                Role = UiaElementRole.Button,
                Name = "Pause",
                BoundsDip = new UiaRect(40, 120, 30, 30),
                Patterns = UiaPatternKind.Invoke,
                State = new UiaElementState { IsFocusable = true },
            },
            new UiaElementSnapshot
            {
                Id = "island.page.1.toggle",
                Role = UiaElementRole.Button,
                Name = "Wi-Fi",
                BoundsDip = new UiaRect(40, 160, 80, 38),
                Patterns = UiaPatternKind.Toggle | UiaPatternKind.Invoke,
                State = new UiaElementState { IsFocusable = true, IsToggled = true },
            },
        ],
    };

    private static UiaProviderContext Context(
        UiaElementSnapshot snapshot,
        List<UiaElementActionRequest>? requests = null,
        bool visible = true,
        double scale = 1.0,
        int originX = 0,
        int originY = 0)
    {
        var geometry = new UiaWindowGeometry(originX, originY, scale, 488, 384, visible);
        return new UiaProviderContext(
            snapshot,
            local => geometry.ToScreen(local),
            request => requests?.Add(request),
            _ => { },
            () => visible);
    }

    [Fact]
    public void Root_ExposesGroupNameAndOnlyItsPatterns()
    {
        var snapshot = Snapshot();
        var provider = UiaElementProvider.CreateRoot(snapshot, Context(snapshot));

        Assert.Equal(50026, provider.GetPropertyValue(30003));
        Assert.Equal("FluidBar", provider.GetPropertyValue(30005));
        Assert.Equal("FluidBar.LayeredIslandHost", provider.GetPropertyValue(30012));
        Assert.Equal(true, provider.GetPropertyValue(30009));
        Assert.Same(provider, provider.GetPatternProvider(10000));
        Assert.Same(provider, provider.GetPatternProvider(10005));
        Assert.Null(provider.GetPatternProvider(10003));
        Assert.Null(provider.GetPatternProvider(10010));
        Assert.Null(provider.HostRawElementProvider);
        Assert.Null(provider.GetEmbeddedFragmentRoots());
        Assert.Null(provider.FragmentRoot);
    }

    [Fact]
    public void Child_ControlTypesAndPatternAvailabilityMatchTheSnapshot()
    {
        var snapshot = Snapshot();
        var root = UiaElementProvider.CreateRoot(snapshot, Context(snapshot));
        var tab = (UiaElementProvider)root.Navigate(3)!;
        var seek = (UiaElementProvider)tab.Navigate(1)!;
        var play = (UiaElementProvider)seek.Navigate(1)!;
        var toggle = (UiaElementProvider)play.Navigate(1)!;

        Assert.Equal(50019, tab.GetPropertyValue(30003));
        Assert.Equal(50015, seek.GetPropertyValue(30003));
        Assert.Equal(50000, play.GetPropertyValue(30003));
        Assert.Equal(true, seek.GetPropertyValue(30033));
        Assert.Equal(true, play.GetPropertyValue(30031));
        Assert.Equal(true, toggle.GetPropertyValue(30041));
        Assert.Equal(100.0, seek.GetPropertyValue(30050));
        Assert.Equal(0.0, seek.GetPropertyValue(30049));
        Assert.Equal(46.0, seek.GetPropertyValue(30047));
        Assert.Equal(true, tab.GetPropertyValue(30079));
        Assert.Equal(1, toggle.GetPropertyValue(30086));
        Assert.Null(toggle.GetPropertyValue(30079));
    }

    [Fact]
    public void BoundingRectangle_TranslatesDipToPhysicalScreenPixels()
    {
        var snapshot = Snapshot();
        var provider = UiaElementProvider.CreateRoot(snapshot, Context(snapshot, scale: 1.5, originX: 100, originY: 200));

        var rect = provider.BoundingRectangle;

        Assert.Equal(136.0, rect.Left, 3);
        Assert.Equal(236.0, rect.Top, 3);
        Assert.Equal(660.0, rect.Width, 3);
        Assert.Equal(426.0, rect.Height, 3);
    }

    [Fact]
    public void Navigate_ExposesParentChildrenAndSiblings()
    {
        var snapshot = Snapshot();
        var root = UiaElementProvider.CreateRoot(snapshot, Context(snapshot));

        var tab = (UiaElementProvider)root.Navigate(3)!;
        var seek = (UiaElementProvider)tab.Navigate(1)!;
        var play = (UiaElementProvider)seek.Navigate(1)!;
        var toggle = (UiaElementProvider)play.Navigate(1)!;

        Assert.Equal("island.page.1", tab.Node.Id);
        Assert.Equal("island.page.1.seek", seek.Node.Id);
        Assert.Equal("island.page.1.play", play.Node.Id);
        Assert.Same(root, tab.Navigate(0));
        Assert.Null(tab.Navigate(2));
        Assert.Same(seek, tab.Navigate(1));
        Assert.Same(tab, seek.Navigate(2));
        Assert.Same(play, seek.Navigate(1));
        Assert.Same(toggle, root.Navigate(4));
        Assert.Null(root.Navigate(0));
        Assert.Same(root, tab.FragmentRoot);
        Assert.Null(root.FragmentRoot);
    }

    [Fact]
    public void RuntimeId_IsNullForRootAndStableForChildren()
    {
        var snapshot = Snapshot();
        var first = UiaElementProvider.CreateRoot(snapshot, Context(snapshot));
        var second = UiaElementProvider.CreateRoot(snapshot, Context(snapshot));

        Assert.Null(first.GetRuntimeId());
        var id1 = ((UiaElementProvider)first.Navigate(3)!).GetRuntimeId();
        var id2 = ((UiaElementProvider)second.Navigate(3)!).GetRuntimeId();

        Assert.NotNull(id1);
        Assert.Single(id1!);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void ElementProviderFromPoint_ReturnsTheDeepestVisibleElement()
    {
        var snapshot = Snapshot();
        var root = UiaElementProvider.CreateRoot(snapshot, Context(snapshot));

        Assert.Same(root, root.ElementProviderFromPoint(300, 300));
        Assert.Equal("island.page.1.seek", ((UiaElementProvider)root.ElementProviderFromPoint(60, 90)!).Node.Id);
        Assert.Equal("island.page.1.play", ((UiaElementProvider)root.ElementProviderFromPoint(45, 125)!).Node.Id);
    }

    [Fact]
    public void GetFocus_ReturnsTheFocusedFocusableElement()
    {
        var snapshot = Snapshot() with
        {
            Children = Snapshot().Children.Select(child => child.Id == "island.page.1.seek"
                ? child with { State = child.State with { IsFocused = true } }
                : child).ToArray(),
        };
        var root = UiaElementProvider.CreateRoot(snapshot, Context(snapshot));

        var focused = root.GetFocus();

        Assert.NotNull(focused);
        Assert.Equal("island.page.1.seek", ((UiaElementProvider)focused!).Node.Id);
    }

    [Fact]
    public void Patterns_DispatchTheExpectedRequests()
    {
        var snapshot = Snapshot();
        var requests = new List<UiaElementActionRequest>();
        var root = UiaElementProvider.CreateRoot(snapshot, Context(snapshot, requests));
        var tab = (UiaElementProvider)root.Navigate(3)!;
        var seek = (UiaElementProvider)tab.Navigate(1)!;
        var play = (UiaElementProvider)seek.Navigate(1)!;
        var toggle = (UiaElementProvider)play.Navigate(1)!;

        ((IInvokeProvider)root).Invoke();
        ((IExpandCollapseProvider)root).Expand();
        ((IExpandCollapseProvider)root).Collapse();
        ((ISelectionItemProvider)tab).Select();
        ((IRangeValueProvider)seek).SetValue(150);
        ((IRangeValueProvider)seek).SetValue(-10);
        ((IInvokeProvider)play).Invoke();
        ((IToggleProvider)toggle).Toggle();

        Assert.Equal(
            new[]
            {
                new UiaElementActionRequest(UiaElementAction.Invoke, "island.root"),
                new UiaElementActionRequest(UiaElementAction.Expand, "island.root"),
                new UiaElementActionRequest(UiaElementAction.Collapse, "island.root"),
                new UiaElementActionRequest(UiaElementAction.Select, "island.page.1"),
                new UiaElementActionRequest(UiaElementAction.SetRangeValue, "island.page.1.seek", 100),
                new UiaElementActionRequest(UiaElementAction.SetRangeValue, "island.page.1.seek", 0),
                new UiaElementActionRequest(UiaElementAction.Invoke, "island.page.1.play"),
                new UiaElementActionRequest(UiaElementAction.Toggle, "island.page.1.toggle"),
            },
            requests);
    }

    [Fact]
    public void HiddenWindow_ReportsOffscreenAndEmptyBounds()
    {
        var snapshot = Snapshot();
        var provider = UiaElementProvider.CreateRoot(snapshot, Context(snapshot, visible: false));

        Assert.Equal(true, provider.GetPropertyValue(30022));
        Assert.Equal(0.0, provider.BoundingRectangle.Width);
        Assert.Equal(0.0, provider.BoundingRectangle.Height);
    }

    [Fact]
    public void ExplicitlyOffscreenElement_ReportsOffscreen()
    {
        var snapshot = Snapshot() with
        {
            Children =
            [
                Snapshot().Children[0] with { State = Snapshot().Children[0].State with { IsOffscreen = true } },
            ],
        };
        var root = UiaElementProvider.CreateRoot(snapshot, Context(snapshot));
        var tab = (UiaElementProvider)root.Navigate(3)!;

        Assert.Equal(true, tab.GetPropertyValue(30022));
        Assert.Equal(0.0, tab.BoundingRectangle.Width);
    }

    [Fact]
    public void ProviderCcw_ExposesTheComInterfacesUiaQueries()
    {
        var snapshot = Snapshot();
        var provider = UiaElementProvider.CreateRoot(snapshot, Context(snapshot));
        var unknown = Marshal.GetIUnknownForObject(provider);
        try
        {
            foreach (var type in new[]
            {
                typeof(IRawElementProviderSimple),
                typeof(IRawElementProviderFragment),
                typeof(IRawElementProviderFragmentRoot),
                typeof(IInvokeProvider),
                typeof(IExpandCollapseProvider),
                typeof(IRangeValueProvider),
                typeof(ISelectionItemProvider),
                typeof(IToggleProvider),
            })
            {
                var iid = type.GUID;
                var hr = Marshal.QueryInterface(unknown, in iid, out var pattern);
                Assert.Equal(0, hr);
                Assert.NotEqual(IntPtr.Zero, pattern);
                if (pattern != IntPtr.Zero)
                {
                    _ = Marshal.Release(pattern);
                }
            }
        }
        finally
        {
            _ = Marshal.Release(unknown);
        }
    }

    [Fact]
    public void SetFocus_InvokesTheFocusDelegate()
    {
        var snapshot = Snapshot();
        string? focusedId = null;
        var context = new UiaProviderContext(
            snapshot,
            rect => rect,
            _ => { },
            id => focusedId = id,
            () => true);
        var root = UiaElementProvider.CreateRoot(snapshot, context);
        var tab = (UiaElementProvider)root.Navigate(3)!;
        var seek = (UiaElementProvider)tab.Navigate(1)!;
        var play = (UiaElementProvider)seek.Navigate(1)!;

        play.SetFocus();

        Assert.Equal("island.page.1.play", focusedId);
    }
}
