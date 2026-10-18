using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.State;
using FluidBar.App.Theme;
using FluidBar.Core.Activities;
using AppTheme = FluidBar.App.Theme;
using CoreIslandPage = FluidBar.Core.Activities.IslandPage;

namespace FluidBar.App.Island.Expanded;

/// <summary>
/// Expanded shell pager + Page Archipelago navigation (user-chosen variant A, 2026-10-18).
/// The shell stays uniform 440×284 with a 20/16 DIP inset and a 400×252 page viewport; the six page
/// controls live in a single horizontally translated strip (transform only — never resize, never
/// per-frame layout). Overlays: title top-left, collapse chevron top-right, and the archipelago strip
/// (no page dots) that appears on drag/wheel or bottom-hover and auto-hides after 1.2 s.
/// </summary>
public sealed class ExpandedPanel : Panel, ILocalizable
{
    /// <summary>Expanded shell width.</summary>
    public const double PanelWidth = 440;

    /// <summary>Expanded shell height.</summary>
    public const double PanelHeight = 284;

    /// <summary>Horizontal shell inset to the page viewport.</summary>
    public const double ViewportInset = 20;

    /// <summary>Vertical shell inset to the page viewport.</summary>
    public const double ViewportInsetY = 16;

    /// <summary>Page viewport width (440 − 2×20 = 400, Core constant).</summary>
    public const double ViewportWidth = 400;

    /// <summary>Page viewport height (284 − 2×16 = 252, Core constant).</summary>
    public const double ViewportHeight = 252;

    /// <summary>Bottom hover band that reveals the archipelago (DIP).</summary>
    public const double BottomHoverBand = 12;

    /// <summary>How far the strip drag maps to page travel (1 strip pixel ≈ 5 px page content).</summary>
    public const double StripScrubFactor = 5.0;

    /// <summary>Incoming-page parallax translation (DIP).</summary>
    public const double ParallaxDip = 6;

    /// <summary>Incoming-page scale delta (0.985 at one full page away).</summary>
    public const double ParallaxScale = 0.015;

    private static readonly CoreIslandPage[] PageOrder =
    [
        CoreIslandPage.Home,
        CoreIslandPage.Media,
        CoreIslandPage.Notifications,
        CoreIslandPage.Clipboard,
        CoreIslandPage.Agent,
        CoreIslandPage.Widgets,
    ];

    private readonly Dictionary<CoreIslandPage, IslandPageView> _pages = new();
    private readonly Panel _strip = new() { ClipToBounds = false };
    private readonly PagerOverlay _overlay;
    private readonly ArchipelagoStrip _archipelago = new();
    private readonly PagerMotion _motion;
    private readonly WheelScrollChain _wheelChain = new();
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _archTimer;
    private readonly List<string> _titles = [];
    private ExpandedPageContext? _context;
    private ILocalizer? _localizer;

    private Point _dragOrigin;
    private long _gestureStartTimestamp;
    private bool _pointerCaptured;
    private long _lastTimestamp;
    private long _lastArchTickMs;
    private bool _archVisible;
    private double _archOpacity;
    private double _laggedPageOffset;
    private long _archHideAt;
    private long? _archHoverAt;
    private bool _stripScrub;
    private double _stripOriginX;
    private int _stripPressedIsland = -1;

    /// <summary>Creates the pager.</summary>
    public ExpandedPanel()
    {
        Background = null;
        ClipToBounds = true;
        _overlay = new PagerOverlay(this);
        _motion = new PagerMotion(PageOrder.Length, ViewportWidth, initialPage: 0);
        _laggedPageOffset = 0;

        foreach (var page in new IslandPageView[]
                 {
                     new HomePage(), new MediaPage(), new NotificationsPage(), new ClipboardPage(), new AgentPage(), new WidgetsPage(),
                 })
        {
            AddPage(page);
        }

        Children.Add(_strip);
        Children.Add(_archipelago);
        Children.Add(_overlay);
        _frameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
        _frameTimer.Tick += OnFrameTick;
        _archTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
        _archTimer.Tick += (_, _) => TickArchipelago(Environment.TickCount64);
        _motion.PageChanged += index =>
        {
            QueueLayoutStrip();
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

    /// <summary>Archipelago strip control (headless tests query geometry/visibility).</summary>
    public ArchipelagoStrip Archipelago => _archipelago;

    /// <summary>Whether the archipelago strip is logically visible.</summary>
    public bool IsArchipelagoVisible => _archVisible;

    /// <summary>Current strip opacity (fade state).</summary>
    public double ArchipelagoOpacity => _archOpacity;

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
        QueueLayoutStrip();
        UpdateArchipelago();
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

        _overlay.InvalidateVisual();
        UpdateArchipelago();
    }

    /// <summary>Jumps to a page with the snap spring (archipelago/1–6/Home/End).</summary>
    public void GoToPage(CoreIslandPage page) => SelectIndex(IndexFor(page));

    /// <summary>Jumps to a page index with the snap spring.</summary>
    public void SelectIndex(int index)
    {
        if (_motion != null)
        {
            _motion.SelectPage(index);
            StartFrameLoop();
        }
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

    /// <summary>Starts the snap spring loop.</summary>
    public void StartFrameLoop()
    {
        if (!_frameTimer.IsEnabled && _motion.IsAnimating)
        {
            _lastTimestamp = Environment.TickCount64;
            _frameTimer.Start();
        }

        EnsureArchTimer();
    }

    // ----- archipelago test/automation hooks (headless deterministic) -----

    /// <summary>Reveals the archipelago strip (drag/wheel/hover path) and resets the auto-hide timer.</summary>
    public void RevealArchipelago(long nowMs)
    {
        _archVisible = true;
        _archHideAt = nowMs + ArchipelagoStrip.AutoHideMs;
        _lastArchTickMs = nowMs;
        EnsureArchTimer();
        UpdateArchipelago();
    }

    /// <summary>Hides the archipelago strip immediately.</summary>
    public void HideArchipelago()
    {
        _archVisible = false;
        _archHideAt = 0;
        _archHoverAt = null;
    }

    /// <summary>Advances strip lag/fade/auto-hide using an explicit timestamp (headless tests).</summary>
    public void TickArchipelago(long nowMs)
    {
        var dt = _lastArchTickMs == 0 ? 16.0 : Math.Clamp(nowMs - _lastArchTickMs, 1, 100);
        _lastArchTickMs = nowMs;
        _laggedPageOffset += (_motion.PagePosition - _laggedPageOffset) * Math.Min(1.0, dt / 30.0);

        var target = _archVisible ? 1.0 : 0.0;
        var step = dt / 120.0;
        _archOpacity = target > _archOpacity
            ? Math.Min(target, _archOpacity + step)
            : Math.Max(target, _archOpacity - step);

        if (_archVisible && _archHideAt > 0 && nowMs >= _archHideAt)
        {
            HideArchipelago();
        }
        else if (!_archVisible && _archOpacity <= 0.001 && _archHoverAt is { } hover && nowMs - hover >= ArchipelagoStrip.HoverRevealMs)
        {
            RevealArchipelago(nowMs);
        }

        UpdateArchipelago();
        if (!_archVisible && _archOpacity <= 0.001 && _archHoverAt is null && !_motion.IsAnimating)
        {
            _archTimer.Stop();
        }
    }

    /// <summary>Reveals the strip when the pointer rests in the bottom hover band; keeps it open on activity.</summary>
    public void NotifyPointerMoved(Point point, long nowMs)
    {
        if (_archipelago.Contains(point) || _archVisible)
        {
            _archHoverAt = null;
            if (_archVisible)
            {
                _archHideAt = nowMs + ArchipelagoStrip.AutoHideMs;
            }

            EnsureArchTimer();
            return;
        }

        if (point.Y >= PanelHeight - BottomHoverBand)
        {
            _archHoverAt ??= nowMs;
            _lastArchTickMs = nowMs;
            EnsureArchTimer();
            return;
        }

        _archHoverAt = null;
    }

    /// <summary>Clicks a mini-island (revealed strip) and jumps to that page.</summary>
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
    }

    /// <summary>Scrubs the strip to panel-local <paramref name="x"/> (damped page travel).</summary>
    public void ScrubStripTo(double x, long nowMs)
    {
        if (!_stripScrub)
        {
            return;
        }

        var dx = x - _stripOriginX;
        // Dragging the strip left advances pages; PagerMotion expects a pointer x whose decrease is forward.
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

    // ----- layout -----

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var page in _pages.Values)
        {
            page.Measure(new Size(ViewportWidth, ViewportHeight));
        }

        _strip.Measure(new Size(ViewportWidth * PageOrder.Length, ViewportHeight));
        _overlay.Measure(new Size(PanelWidth, PanelHeight));
        _archipelago.Measure(new Size(PanelWidth, PanelHeight));
        return new Size(PanelWidth, PanelHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _strip.Arrange(new Rect(ViewportInset, ViewportInsetY, ViewportWidth * PageOrder.Length, ViewportHeight));
        for (var i = 0; i < PageOrder.Length; i++)
        {
            _pages[PageOrder[i]].Arrange(new Rect(i * ViewportWidth, 0, ViewportWidth, ViewportHeight));
        }

        _archipelago.Arrange(new Rect(0, 0, PanelWidth, PanelHeight));
        _overlay.Arrange(new Rect(0, 0, PanelWidth, PanelHeight));
        UpdateVisual();
        return new Size(PanelWidth, PanelHeight);
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
            _pointerCaptured = true;
            e.Handled = true;
            return;
        }

        if (!InViewport(point))
        {
            return;
        }

        _dragOrigin = point;
        _gestureStartTimestamp = now;
        _motion.BeginDrag(point.X, now);
        e.Pointer.Capture(this);
        _pointerCaptured = true;
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
        UpdateVisual();
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
            UpdateVisual();
            StartFrameLoop();
        }

        _pointerCaptured = false;
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
        else if (action is WheelChainAction.Locked or WheelChainAction.ContentScroll)
        {
            e.Handled = action == WheelChainAction.Locked;
        }
        else
        {
            e.Handled = true;
        }
    }

    // ----- frame/visual updates -----

    private void OnFrameTick(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;
        var dt = Math.Clamp((now - _lastTimestamp) / 1000.0, 0.001, 0.05);
        _lastTimestamp = now;
        _motion.Step(dt);
        UpdateVisual();
        if (!_motion.IsAnimating && !_motion.IsDragging && !_archVisible)
        {
            _frameTimer.Stop();
        }
        else if (!_motion.IsAnimating && !_motion.IsDragging)
        {
            _frameTimer.Stop();
        }
    }

    private void EnsureArchTimer()
    {
        if (_archTimer.IsEnabled)
        {
            return;
        }

        _lastArchTickMs = Environment.TickCount64;
        _archTimer.Start();
    }

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

        _overlay.InvalidateVisual();
        UpdateArchipelago();
    }

    private void UpdateArchipelago()
    {
        _archipelago.Update(_context, _titles, _laggedPageOffset, _archOpacity, Environment.TickCount64);
        if (_archVisible || _archOpacity > 0.001)
        {
            _archipelago.IsHitTestVisible = false;
        }
    }

    private void QueueLayoutStrip() => UpdateVisual();

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

    private static string PageTitleKey(CoreIslandPage page) => page switch
    {
        CoreIslandPage.Home => LocKeys.Page.Home.Title,
        CoreIslandPage.Media => LocKeys.Page.Media.Title,
        CoreIslandPage.Notifications => LocKeys.Page.Notifications.Title,
        CoreIslandPage.Clipboard => LocKeys.Page.Clipboard.Title,
        CoreIslandPage.Agent => LocKeys.Page.Agent.Title,
        _ => LocKeys.Page.Widgets.Title,
    };

    private enum PagerOverlayHit
    {
        None,
        Collapse,
    }

    /// <summary>Draws the pager chrome: page title and collapse chevron (no dots — archipelago replaces them).</summary>
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
