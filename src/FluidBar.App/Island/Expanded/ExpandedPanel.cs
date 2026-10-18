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
using AppTheme = FluidBar.App.Theme;
using CoreIslandPage = FluidBar.Core.Activities.IslandPage;

namespace FluidBar.App.Island.Expanded;

/// <summary>
/// Expanded shell pager (frozen design 2026-10-18: horizontal mouse-drag pager replaces the vertical
/// rail). The shell stays a uniform 440×284 with a 20 DIP inset and a 400×244 page viewport; the six
/// page controls live in a single horizontally translated strip (transform only — never resize, never
/// per-frame layout). Overlays draw the page title (top-left), collapse chevron (top-right) and six
/// clickable page dots (bottom-centre, active 20 DIP pill).
/// </summary>
public sealed class ExpandedPanel : Panel, ILocalizable
{
    /// <summary>Expanded shell width.</summary>
    public const double PanelWidth = 440;

    /// <summary>Expanded shell height.</summary>
    public const double PanelHeight = 284;

    /// <summary>Horizontal shell inset to the page viewport (Core constant).</summary>
    public const double ViewportInset = 20;

    /// <summary>Vertical shell inset to the page viewport (Core constant).</summary>
    public const double ViewportInsetY = 16;

    /// <summary>Page viewport width (440 − 2×20 = 400, Core constant).</summary>
    public const double ViewportWidth = 400;

    /// <summary>Page viewport height (284 − 2×16 = 252, Core constant).</summary>
    public const double ViewportHeight = 252;

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
    private readonly PagerMotion _motion;
    private readonly DispatcherTimer _frameTimer;
    private readonly List<string> _titles = [];
    private ExpandedPageContext? _context;
    private ILocalizer? _localizer;
    private Point _dragOrigin;
    private bool _pointerCaptured;
    private long _lastTimestamp;
    private long _gestureStartTimestamp;
    private bool _collapseRequested;

    /// <summary>Creates the pager.</summary>
    public ExpandedPanel()
    {
        Background = null;
        ClipToBounds = true;
        _overlay = new PagerOverlay(this);
        _motion = new PagerMotion(PageOrder.Length, ViewportWidth, initialPage: 0);

        foreach (var page in new IslandPageView[]
                 {
                     new HomePage(), new MediaPage(), new NotificationsPage(), new ClipboardPage(), new AgentPage(), new WidgetsPage(),
                 })
        {
            AddPage(page);
        }

        Children.Add(_strip);
        Children.Add(_overlay);
        _frameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
        _frameTimer.Tick += OnFrameTick;
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

    /// <summary>Page instance for <paramref name="page"/> (tests assert page roots are stable).</summary>
    public IslandPageView PageFor(CoreIslandPage page) => _pages[page];

    /// <summary>Number of page dots.</summary>
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
        _overlay.InvalidateVisual();
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
    }

    /// <summary>Jumps to a page with the snap spring (dots/1–6/Home/End).</summary>
    public void GoToPage(CoreIslandPage page) => SelectIndex(IndexFor(page));

    /// <summary>Jumps to a page index.</summary>
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

    /// <summary>Starts the snap spring loop (also used by headless tests).</summary>
    public void StartFrameLoop()
    {
        if (!_frameTimer.IsEnabled && _motion.IsAnimating)
        {
            _lastTimestamp = Environment.TickCount64;
            _frameTimer.Start();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var page in _pages.Values)
        {
            page.Measure(new Size(ViewportWidth, ViewportHeight));
        }

        _strip.Measure(new Size(ViewportWidth * PageOrder.Length, ViewportHeight));
        _overlay.Measure(new Size(PanelWidth, PanelHeight));
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

        _overlay.Arrange(new Rect(0, 0, PanelWidth, PanelHeight));
        UpdateStripOffset();
        return new Size(PanelWidth, PanelHeight);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_context is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        // Collapse chevron and dots are overlay hit targets first.
        var overlayAction = _overlay.HitTest(point);
        if (overlayAction == PagerOverlayHit.Collapse)
        {
            CollapseRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (overlayAction is PagerOverlayHit.Dot0 or PagerOverlayHit.Dot1 or PagerOverlayHit.Dot2
            or PagerOverlayHit.Dot3 or PagerOverlayHit.Dot4 or PagerOverlayHit.Dot5)
        {
            SelectIndex((int)overlayAction - (int)PagerOverlayHit.Dot0);
            e.Handled = true;
            return;
        }

        if (!InViewport(point))
        {
            return;
        }

        _dragOrigin = point;
        _gestureStartTimestamp = Environment.TickCount64;
        _motion.BeginDrag(point.X, _gestureStartTimestamp);
        e.Pointer.Capture(this);
        _pointerCaptured = true;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_motion.IsDragging)
        {
            return;
        }

        var point = e.GetPosition(this);
        _motion.DragTo(point.X, Environment.TickCount64);
        UpdateStripOffset();
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_motion.IsDragging)
        {
            return;
        }

        var now = Environment.TickCount64;
        var moved = _motion.MovedPastClickThreshold;
        _motion.EndDrag(now);
        UpdateStripOffset();
        StartFrameLoop();
        Cursor = Cursor.Default;

        if (moved)
        {
            // A drag (or flick) must not activate page-internal controls (spec pager rule e).
            e.Handled = true;
            return;
        }

        var point = e.GetPosition(this);
        var pageLocal = new Point(point.X - ViewportInset, point.Y - ViewportInset);
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
        if (_motion.IsDragging)
        {
            _motion.EndDrag(Environment.TickCount64);
            UpdateStripOffset();
            StartFrameLoop();
        }

        _pointerCaptured = false;
        Cursor = Cursor.Default;
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_context is null || _motion.IsWheelBlocked)
        {
            return;
        }

        var delta = e.Delta;
        var changed = _motion.Wheel(delta.X, delta.Y, Environment.TickCount64);
        if (changed != _motion.SelectedIndex || _motion.IsAnimating)
        {
            UpdateStripOffset();
            StartFrameLoop();
        }

        e.Handled = true;
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;
        var dt = Math.Clamp((now - _lastTimestamp) / 1000.0, 0.001, 0.05);
        _lastTimestamp = now;
        _motion.Step(dt);
        UpdateStripOffset();
        if (!_motion.IsAnimating && !_motion.IsDragging)
        {
            _frameTimer.Stop();
            _motion.SnapToPage(_motion.SelectedIndex);
            UpdateStripOffset();
        }
    }

    private void UpdateStripOffset()
    {
        _strip.RenderTransform = new TranslateTransform(-_motion.Offset, 0);
        _overlay.InvalidateVisual();
    }

    private void QueueLayoutStrip()
    {
        UpdateStripOffset();
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
        Dot0,
        Dot1,
        Dot2,
        Dot3,
        Dot4,
        Dot5,
    }

    /// <summary>Draws the pager chrome: page title, collapse chevron and page dots.</summary>
    private sealed class PagerOverlay : Control
    {
        private readonly ExpandedPanel _owner;

        public PagerOverlay(ExpandedPanel owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
        }

        public PagerOverlayHit HitTest(Point point)
        {
            if (CollapseRect().Contains(point))
            {
                return PagerOverlayHit.Collapse;
            }

            for (var i = 0; i < _owner.PageCount; i++)
            {
                if (DotRect(i).Contains(point))
                {
                    return PagerOverlayHit.Dot0 + i;
                }
            }

            return PagerOverlayHit.None;
        }

        public override void Render(DrawingContext context)
        {
            var palette = _owner._context?.Settings.Colors ?? IslandColors.Obsidian;
            var foreground = new SolidColorBrush(palette.Foreground);
            var secondary = new SolidColorBrush(palette.ForegroundSecondary);

            // Active page title (top-left overlay).
            var title = _owner.CurrentTitle;
            if (!string.IsNullOrEmpty(title))
            {
                IslandDraw.Text(context, title, new Rect(ViewportInset, ViewportInsetY - 2, ViewportWidth - 60, 20),
                    AppTheme.Fonts.DisplaySemiBold, AppTheme.Typography.PageTitleSize, foreground);
            }

            // Collapse chevron (top-right overlay).
            var collapse = CollapseRect();
            Icons.Draw(context, IconKind.ChevronDown, collapse.Deflate(4), secondary);

            // Six page dots, bottom-centre; active dot is a 20 DIP pill.
            for (var i = 0; i < _owner.PageCount; i++)
            {
                var rect = DotRect(i);
                var active = i == _owner.CurrentIndex;
                var fill = active ? new SolidColorBrush(Accents.Fallback) : new SolidColorBrush(palette.ForegroundTertiary);
                var dot = active
                    ? new RoundedRect(new Rect(rect.Center.X - 10, rect.Center.Y - 2.5, 20, 5), 2.5)
                    : new RoundedRect(new Rect(rect.Center.X - 2.5, rect.Center.Y - 2.5, 5, 5), 2.5);
                context.DrawRectangle(fill, null, dot);
            }
        }

        private static Rect CollapseRect() => new(PanelWidth - ViewportInset - 20, ViewportInsetY - 2, 20, 20);

        private static Rect DotRect(int index)
        {
            var centerX = PanelWidth / 2;
            const double spacing = 22;
            var start = centerX - (((6 - 1) * spacing) / 2);
            return new Rect(start + (index * spacing) - 11, PanelHeight - ViewportInsetY - 2, 22, 16);
        }
    }
}
