using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.State;
using FluidBar.App.Theme;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using AppTheme = FluidBar.App.Theme;
using CoreIslandPage = FluidBar.Core.Activities.PageRef;

namespace FluidBar.App.Island.Expanded;

/// <summary>
/// Expanded shell pager + detached Page Archipelago (user-chosen variant A, revised 2026-10-18).
/// The shell itself is 440×244 (token-derived) with a 20/16 viewport; six page controls live in a translated strip
/// (transform only). The mini-island archipelago floats BELOW the panel, outside the shell, over the
/// desktop (440×336 content layer inside a 488×384 host window; see <see cref="HostLayout"/>).
/// Its reveal/hide/active morph are Core springs driven by a render frame loop; no layout runs during
/// navigation.
/// </summary>
public sealed class ExpandedPanel : Panel, ILocalizable
{
    /// <summary>Expanded shell width.</summary>
    public const double PanelWidth = LayoutTokens.ExpandedWidth;

    /// <summary>Expanded shell height (the island itself).</summary>
    public const double PanelHeight = LayoutTokens.ExpandedHeight;

    /// <summary>Expanded content layer height including the detached archipelago band (440×336).</summary>
    public const double TotalHeight = HostLayout.ExpandedLayerHeight;

    /// <summary>Horizontal shell inset to the page viewport.</summary>
    public const double ViewportInset = 20;

    /// <summary>Vertical shell inset to the page viewport.</summary>
    public const double ViewportInsetY = 16;

    /// <summary>Page viewport width (400, Core constant).</summary>
    public const double ViewportWidth = 400;

    /// <summary>Page viewport height, derived from the expanded shell and vertical inset.</summary>
    public const double ViewportHeight = LayoutTokens.ContentUsableHeight;

    /// <summary>Bottom hover band of the shell that reveals the archipelago (DIP).</summary>
    public const double BottomHoverBand = 12;

    /// <summary>How far a strip drag maps to page travel.</summary>
    public const double StripScrubFactor = 5.0;

    /// <summary>Incoming-page parallax translation (DIP).</summary>
    public const double ParallaxDip = 6;

    /// <summary>Incoming-page scale delta.</summary>
    public const double ParallaxScale = 0.015;

    private static readonly CoreIslandPage[] PageOrder =
    [
        CoreIslandPage.Home,
        CoreIslandPage.Media,
        CoreIslandPage.Notifications,
        CoreIslandPage.Clipboard,
        CoreIslandPage.Widgets,
    ];

    private readonly Dictionary<CoreIslandPage, IslandPageView> _pages = new();
    private readonly Panel _strip = new() { ClipToBounds = false };
    private readonly PagerOverlay _overlay;
    private readonly ArchipelagoStrip _archipelago = new();
    private readonly ArchipelagoMotion _archMotion = new();
    private readonly PagerMotion _motion;
    private readonly WheelScrollChain _wheelChain = new();
    private readonly DispatcherTimer _frameTimer;
    private readonly List<string> _titles = [];
    private ExpandedPageContext? _context;
    private ILocalizer? _localizer;

    private long _lastTickMs;
    private bool _archVisible;
    private long _archHideAt;
    private long? _archHoverAt;
    private bool _stripScrub;
    private double _stripOriginX;
    private int _stripPressedIsland = -1;
    private double _previewPhase;

    /// <summary>Creates the pager.</summary>
    public ExpandedPanel()
    {
        Background = null;
        ClipToBounds = false;
        _overlay = new PagerOverlay(this);
        _motion = new PagerMotion(PageOrder.Length, ViewportWidth, initialPage: 0);

        foreach (var page in new IslandPageView[]
                 {
                     new HomePage(), new MediaPage(), new NotificationsPage(), new ClipboardPage(), new WidgetsPage(),
                 })
        {
            AddPage(page);
        }

        Children.Add(_strip);
        Children.Add(_archipelago);
        Children.Add(_overlay);
        _frameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
        _frameTimer.Tick += (_, _) => TickArchipelago(Environment.TickCount64);
        _motion.PageChanged += index =>
        {
            UpdateVisual();
            PageRequested?.Invoke(PageOrder[index]);
        };
    }

    /// <summary>Raised when a page action should be executed by the host.</summary>
    public event Action<IslandPageActionRequest>? ActionRequested;

    /// <summary>Raised when the committed page changes.</summary>
    public event Action<CoreIslandPage>? PageRequested;

    /// <summary>Raised when the collapse chevron is clicked.</summary>
    public event Action? CollapseRequested;

    /// <summary>Current committed page.</summary>
    public CoreIslandPage CurrentPage => PageOrder[_motion.SelectedIndex];

    /// <summary>Current page index.</summary>
    public int CurrentIndex => _motion.SelectedIndex;

    /// <summary>Pager motion model (headless tests drive it directly).</summary>
    public PagerMotion Motion => _motion;

    /// <summary>Archipelago strip control (headless tests query geometry).</summary>
    public ArchipelagoStrip Archipelago => _archipelago;

    /// <summary>Archipelago spring motion (tests).</summary>
    public ArchipelagoMotion ArchipelagoMotion => _archMotion;

    /// <summary>Whether the archipelago is logically visible.</summary>
    public bool IsArchipelagoVisible => _archVisible;

    /// <summary>Current archipelago reveal opacity (spring-driven).</summary>
    public double ArchipelagoOpacity => _archMotion.Opacity;

    /// <summary>Current archipelago translateY (8→0 DIP).</summary>
    public double ArchipelagoTranslateY => _archMotion.TranslateY;

    /// <summary>Current archipelago scale (.96→1).</summary>
    public double ArchipelagoScale => _archMotion.Scale;

    /// <summary>Smoothed active-island page position.</summary>
    public double ArchipelagoActivePosition => _archMotion.ActivePosition;

    /// <summary>Whether the expanded layer paints the detached strip (window region/hit-test helper).</summary>
    public bool HasExternalOverlay => _archMotion.Progress > 0.001;

    /// <summary>Page instance for <paramref name="page"/> (tests assert page roots are stable).</summary>
    public IslandPageView PageFor(CoreIslandPage page) => _pages[page];

    /// <summary>Number of pages.</summary>
    public int PageCount => PageOrder.Length;

    /// <summary>Title text shown for the current page.</summary>
    public string CurrentTitle => _titles.Count > _motion.SelectedIndex ? _titles[_motion.SelectedIndex] : string.Empty;

    /// <summary>Applies a data snapshot to every cached page root (no visual-tree rebuild).</summary>
    public void Update(ExpandedPageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _localizer = context.Localizer;
        _titles.Clear();
        foreach (var page in PageOrder)
        {
            _titles.Add(context.Localizer.Get(PageTitleKey(page)));
        }

        foreach (var page in _pages.Values)
        {
            page.Update(context);
        }

        _motion.SelectPage(IndexFor(context.SelectedPage));
        UpdateVisual();
    }

    /// <summary>Re-resolves every localized string (page roots retained).</summary>
    public void ApplyLocalization(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
        _titles.Clear();
        foreach (var page in PageOrder)
        {
            _titles.Add(localizer.Get(PageTitleKey(page)));
        }

        foreach (var page in _pages.Values)
        {
            page.ApplyLocalization(localizer);
        }

        UpdateVisual();
    }

    /// <summary>Jumps to a page with the snap spring (archipelago/keyboard).</summary>
    public void GoToPage(CoreIslandPage page) => SelectIndex(IndexFor(page));

    /// <summary>Jumps to a page index with the snap spring.</summary>
    public void SelectIndex(int index)
    {
        _motion.SelectPage(index);
        StartFrameLoop();
    }

    /// <summary>Handles keyboard navigation; returns whether the key was consumed.</summary>
    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.D1 or Key.NumPad1:
                SelectIndex(0);
                return true;
            case Key.D2 or Key.NumPad2:
                SelectIndex(1);
                return true;
            case Key.D3 or Key.NumPad3:
                SelectIndex(2);
                return true;
            case Key.D4 or Key.NumPad4:
                SelectIndex(3);
                return true;
            case Key.D5 or Key.NumPad5:
                SelectIndex(4);
                return true;
            case Key.D6 or Key.NumPad6:
                SelectIndex(5);
                return true;
            case Key.Left:
                _motion.MoveBy(-1);
                StartFrameLoop();
                return true;
            case Key.Right:
                _motion.MoveBy(1);
                StartFrameLoop();
                return true;
            case Key.Home:
                SelectIndex(0);
                return true;
            case Key.End:
                SelectIndex(PageOrder.Length - 1);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Starts the render frame loop (pager snap + archipelago springs).</summary>
    public void StartFrameLoop()
    {
        _lastTickMs = Environment.TickCount64;
        if (!_frameTimer.IsEnabled)
        {
            _frameTimer.Start();
        }
    }

    // ----- archipelago automation/tests -----

    /// <summary>Reveals the detached archipelago strip (drag/wheel/hover path) and resets auto-hide.</summary>
    public void RevealArchipelago(long nowMs)
    {
        if (!_archVisible)
        {
            _archVisible = true;
            _archMotion.Reveal();
        }

        _archHideAt = nowMs + ArchipelagoStrip.AutoHideMs;
        _lastTickMs = nowMs;
        StartFrameLoop();
    }

    /// <summary>Hides the archipelago (spring-driven fade/translate/scale out).</summary>
    public void HideArchipelago()
    {
        _archVisible = false;
        _archHideAt = 0;
        _archHoverAt = null;
        _archMotion.Hide();
        StartFrameLoop();
    }

    /// <summary>Steps the frame loop with an explicit timestamp (headless deterministic tests).</summary>
    public void TickArchipelago(long nowMs)
    {
        var dt = _lastTickMs == 0 ? 16.0 : Math.Clamp(nowMs - _lastTickMs, 1, 100);
        _lastTickMs = nowMs;

        if (_motion.IsAnimating)
        {
            _motion.Step(dt / 1000.0);
        }

        _archMotion.Follow(_motion.PagePosition, PageOrder.Length);
        _archMotion.Step(dt / 1000.0);
        _previewPhase += dt / 1000.0;

        if (_archVisible && _archHideAt > 0 && nowMs >= _archHideAt)
        {
            HideArchipelago();
        }
        else if (!_archVisible && _archHoverAt is { } hover && nowMs - hover >= ArchipelagoStrip.HoverRevealMs)
        {
            RevealArchipelago(nowMs);
        }

        UpdateVisual();
        if (!_archVisible && _archMotion.IsAtRest && _archHoverAt is null && !_motion.IsAnimating)
        {
            _frameTimer.Stop();
        }
    }

    /// <summary>Reveals the strip when the pointer rests in the bottom band or on the strip; keeps it open.</summary>
    public void NotifyPointerMoved(Point point, long nowMs)
    {
        if (_archVisible && _archipelago.Contains(point))
        {
            _archHoverAt = null;
            _archHideAt = nowMs + ArchipelagoStrip.AutoHideMs;
            StartFrameLoop();
            return;
        }

        if (point.Y >= PanelHeight - BottomHoverBand)
        {
            _archHoverAt ??= nowMs;
            _lastTickMs = nowMs;
            StartFrameLoop();
            return;
        }

        _archHoverAt = null;
    }

    /// <summary>Clicks a mini-island and jumps to that page.</summary>
    public void ClickMiniIsland(int index)
    {
        if (index < 0 || index >= PageCount)
        {
            return;
        }

        RevealArchipelago(Environment.TickCount64);
        _motion.SelectPage(index);
        StartFrameLoop();
    }

    /// <summary>Starts scrubbing the archipelago strip at panel-local <paramref name="x"/>.</summary>
    public void BeginStripScrub(double x, long nowMs)
    {
        _stripScrub = true;
        _stripOriginX = x;
        _stripPressedIsland = _archipelago.HitTest(new Point(x, _archipelago.StripRect.Center.Y));
        RevealArchipelago(nowMs);
        _motion.BeginDrag(0, nowMs);
        StartFrameLoop();
    }

    /// <summary>Scrubs the strip to panel-local <paramref name="x"/> (damped page travel).</summary>
    public void ScrubStripTo(double x, long nowMs)
    {
        if (!_stripScrub)
        {
            return;
        }

        // Dragging the strip left advances pages; PagerMotion expects pointer x decreasing forward.
        var dx = x - _stripOriginX;
        _motion.DragTo(dx * StripScrubFactor, nowMs);
        UpdateVisual();
    }

    /// <summary>Ends a strip scrub: a tap on an island jumps, otherwise the pager snaps by distance.</summary>
    public void EndStripScrub(long nowMs)
    {
        if (!_stripScrub)
        {
            return;
        }

        _stripScrub = false;
        if (!_motion.MovedPastClickThreshold && _stripPressedIsland >= 0)
        {
            _motion.SnapToPage(_stripPressedIsland);
            _motion.SelectPage(_stripPressedIsland);
            StartFrameLoop();
            return;
        }

        _motion.EndDrag(nowMs);
        StartFrameLoop();
    }

    /// <summary>Wheel over the archipelago strip: pages only (never content scroll).</summary>
    public int WheelArchipelago(double deltaX, double deltaY, long nowMs)
    {
        RevealArchipelago(nowMs);
        var target = _motion.Wheel(deltaX, deltaY, nowMs);
        StartFrameLoop();
        return target;
    }

    /// <summary>Single-owner wheel scroll chaining for wheel events over page content.</summary>
    public WheelChainAction WheelContent(double deltaX, double deltaY, bool shiftForced, bool contentCanScrollFurther, long nowMs)
        => _wheelChain.Accumulate(deltaX, deltaY, shiftForced, contentCanScrollFurther, _motion.IsAnimating, nowMs);

    /// <summary>Window-level hit-test extension: the detached strip below the shell is interactive.</summary>
    public bool HitTestExternal(Point panelLocal)
        => _archMotion.Progress > 0.01 && _archipelago.Contains(panelLocal);

    // ----- layout -----

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var page in _pages.Values)
        {
            page.Measure(new Size(ViewportWidth, ViewportHeight));
        }

        _strip.Measure(new Size(ViewportWidth * PageOrder.Length, ViewportHeight));
        _overlay.Measure(new Size(PanelWidth, TotalHeight));
        _archipelago.Measure(new Size(PanelWidth, TotalHeight));
        return new Size(PanelWidth, TotalHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _strip.Arrange(new Rect(ViewportInset, ViewportInsetY, ViewportWidth * PageOrder.Length, ViewportHeight));
        for (var i = 0; i < PageOrder.Length; i++)
        {
            _pages[PageOrder[i]].Arrange(new Rect(i * ViewportWidth, 0, ViewportWidth, ViewportHeight));
        }

        _archipelago.Arrange(new Rect(0, 0, PanelWidth, TotalHeight));
        _overlay.Arrange(new Rect(0, 0, PanelWidth, TotalHeight));
        UpdateVisual();
        return new Size(PanelWidth, TotalHeight);
    }

    // ----- input -----

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_context is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var now = Environment.TickCount64;
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_overlay.HitTest(point) == PagerOverlayHit.Collapse)
        {
            CollapseRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (_archVisible && _archipelago.Contains(point))
        {
            BeginStripScrub(point.X, now);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!InViewport(point))
        {
            return;
        }

        _motion.BeginDrag(point.X, now);
        e.Pointer.Capture(this);
        RevealArchipelago(now);
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        var now = Environment.TickCount64;
        NotifyPointerMoved(point, now);

        if (_stripScrub)
        {
            ScrubStripTo(point.X, now);
            e.Handled = true;
            return;
        }

        if (!_motion.IsDragging)
        {
            return;
        }

        _motion.DragTo(point.X, now);
        UpdateVisual();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _archHoverAt = null;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_stripScrub)
        {
            EndStripScrub(Environment.TickCount64);
            e.Handled = true;
            return;
        }

        if (!_motion.IsDragging)
        {
            return;
        }

        var now = Environment.TickCount64;
        var moved = _motion.MovedPastClickThreshold;
        _motion.EndDrag(now);
        StartFrameLoop();
        Cursor = Cursor.Default;

        if (moved)
        {
            e.Handled = true;
            return;
        }

        var point = e.GetPosition(this);
        var pageLocal = new Point(point.X - ViewportInset, point.Y - ViewportInsetY);
        var action = _pages[PageOrder[_motion.SelectedIndex]].HitTest(pageLocal);
        if (action.Action != IslandPageAction.None)
        {
            ActionRequested?.Invoke(action);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_stripScrub)
        {
            EndStripScrub(Environment.TickCount64);
        }
        else if (_motion.IsDragging)
        {
            _motion.EndDrag(Environment.TickCount64);
            StartFrameLoop();
        }

        Cursor = Cursor.Default;
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_context is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var now = Environment.TickCount64;
        var delta = e.Delta;
        if (_archVisible && _archipelago.Contains(point))
        {
            WheelArchipelago(delta.X, delta.Y, now);
            e.Handled = true;
            return;
        }

        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        // Custom-drawn pages are not scrollable yet, so the gate always reaches the boundary path.
        var action = WheelContent(delta.X, delta.Y, shift, contentCanScrollFurther: false, now);
        if (action == WheelChainAction.Page)
        {
            RevealArchipelago(now);
            _motion.Wheel(delta.X, delta.Y, now);
            StartFrameLoop();
            e.Handled = true;
        }
        else if (action == WheelChainAction.Locked)
        {
            e.Handled = true;
        }
        else
        {
            e.Handled = true;
        }
    }

    // ----- visuals -----

    private void UpdateVisual()
    {
        _strip.RenderTransform = new TranslateTransform(-_motion.Offset, 0);
        var pitch = _motion.PageWidth;
        for (var i = 0; i < PageOrder.Length; i++)
        {
            var page = _pages[PageOrder[i]];
            var distance = Math.Clamp(Math.Abs(i - (_motion.Offset / pitch)), 0, 1);
            var y = ParallaxDip * distance;
            var scale = 1.0 - (ParallaxScale * distance);
            var center = new Point(ViewportWidth / 2, ViewportHeight / 2);
            var matrix = Matrix.CreateTranslation(0, y)
                * Matrix.CreateTranslation(-center.X, -center.Y)
                * Matrix.CreateScale(scale, scale)
                * Matrix.CreateTranslation(center.X, center.Y);
            page.RenderTransform = new MatrixTransform(matrix);
        }

        _archipelago.Update(_context, _titles, _archMotion, _previewPhase);
        _overlay.InvalidateVisual();
    }

    private bool InViewport(Point point)
        => point.X >= ViewportInset && point.X <= ViewportInset + ViewportWidth
           && point.Y >= ViewportInsetY && point.Y <= ViewportInsetY + ViewportHeight;

    private void AddPage(IslandPageView page)
    {
        page.Opacity = 1;
        page.IsHitTestVisible = false;
        _pages[page.Page] = page;
        _strip.Children.Add(page);
    }

    private static int IndexFor(CoreIslandPage page) => Array.IndexOf(PageOrder, page) is var index && index >= 0 ? index : 0;

    private static string PageTitleKey(CoreIslandPage page)
    {
        if (page == CoreIslandPage.Home) return LocKeys.Page.Home.Title;
        if (page == CoreIslandPage.Media) return LocKeys.Page.Media.Title;
        if (page == CoreIslandPage.Notifications) return LocKeys.Page.Notifications.Title;
        if (page == CoreIslandPage.Clipboard) return LocKeys.Page.Clipboard.Title;
        if (page == CoreIslandPage.Agent) return LocKeys.Page.Agent.Title;
        return LocKeys.Page.Widgets.Title;
    }

    private enum PagerOverlayHit
    {
        None,
        Collapse,
    }

    /// <summary>Draws the pager chrome: page title and collapse chevron (no dots; archipelago is external).</summary>
    private sealed class PagerOverlay : Control
    {
        private readonly ExpandedPanel _owner;

        public PagerOverlay(ExpandedPanel owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
        }

        public PagerOverlayHit HitTest(Point point) => CollapseRect().Contains(point) ? PagerOverlayHit.Collapse : PagerOverlayHit.None;

        public override void Render(DrawingContext context)
        {
            var palette = _owner._context?.Settings.Colors ?? IslandColors.Obsidian;
            var foreground = new SolidColorBrush(palette.Foreground);
            var secondary = new SolidColorBrush(palette.ForegroundSecondary);

            var title = _owner.CurrentTitle;
            if (!string.IsNullOrEmpty(title))
            {
                IslandDraw.Text(context, title, new Rect(ViewportInset, ViewportInsetY - 2, ViewportWidth - 60, 20),
                    AppTheme.Fonts.DisplaySemiBold, AppTheme.Typography.PageTitleSize, foreground);
            }

            var collapse = CollapseRect();
            Icons.Draw(context, IconKind.ChevronDown, collapse.Deflate(4), secondary);
        }

        private static Rect CollapseRect() => new(PanelWidth - ViewportInset - 20, ViewportInsetY - 2, 20, 20);
    }
}

