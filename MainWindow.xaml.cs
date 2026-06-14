using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace FluidBar;

public partial class MainWindow : Window
{
    private readonly EventBus _bus;
    private readonly FluidBarSettings _settings;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _scrollTimer;
    private readonly DispatcherTimer _waveTimer;
    private readonly SpringValue _hoverWidthSpring = new();
    private readonly SpringValue _hoverHeightSpring = new();
    private HoverCardMotionPlan? _hoverMotionPlan;
    private readonly List<IslandStackItem> _islandStack = new();
    private readonly List<IslandSnapshotWindow> _snapshotWindows = new();
    private bool _hoverRenderingAttached;
    private bool _hoverSpringHasRenderTime;
    private TimeSpan _hoverSpringLastRenderTime;
    private double _hoverHostWidth;
    private double _hoverHostHeight;
    private bool _isExpanded;
    private bool _settingsPanelOpen;
    private bool _isHoverCard;
    private string? _currentIconKind;
    private string? _currentSource;
    private IslandEvent? _lastEvent;
    private IslandViewPresentation? _currentView;
    private double _activeTargetWidth;
    private double _activeTargetHeight;
    private double _wavePhase;
    private const double ShellBleedMargin = 14;
    private const double ShellBleed = ShellBleedMargin * 2;
    private const double HoverHostPadding = 16;

    private ClipboardPluginSettings? _clipboardPluginSettings;

    // Segoe MDL2 Assets 图标映射
    private static readonly Dictionary<string, string> IconGlyphs = new()
    {
        ["clipboard"]     = "\uE16F",
        ["volume"]        = "\uE767",
        ["volume_mute"]   = "\uE74F",
        ["battery"]       = "\uE850",
        ["battery_charge"]= "\uEBA9",
        ["battery_low"]   = "\uEBAF",
        ["inputmethod"]   = "\uE765",
        ["lockkey"]       = "\uE72E",
        ["network"]       = "\uE701",
        ["network_off"]   = "\uE8D9",
        ["usb"]           = "\uE88E",
        ["brightness"]    = "\uE706",
        ["bluetooth"]     = "\uE702",
        ["clock"]         = "\uE121",
        ["media"]         = "\uE768",
        ["notification"]  = "\uE7F4",
        ["agent"]         = "\uE8F2",
        ["info"]          = "\uE946",
    };

    // 各功能图标背景色
    private static readonly Dictionary<string, MediaColor> IconColors = new()
    {
        ["clipboard"]     = MediaColor.FromRgb(10, 132, 255),
        ["volume"]        = MediaColor.FromRgb(10, 132, 255),
        ["volume_mute"]   = MediaColor.FromRgb(142, 142, 147),
        ["battery"]       = MediaColor.FromRgb(48, 209, 88),
        ["battery_charge"]= MediaColor.FromRgb(48, 209, 88),
        ["battery_low"]   = MediaColor.FromRgb(255, 69, 58),
        ["inputmethod"]   = MediaColor.FromRgb(10, 132, 255),
        ["lockkey"]       = MediaColor.FromRgb(191, 90, 242),
        ["network"]       = MediaColor.FromRgb(48, 209, 88),
        ["network_off"]   = MediaColor.FromRgb(255, 69, 58),
        ["usb"]           = MediaColor.FromRgb(255, 159, 10),
        ["brightness"]    = MediaColor.FromRgb(255, 214, 10),
        ["bluetooth"]     = MediaColor.FromRgb(10, 132, 255),
        ["clock"]         = MediaColor.FromRgb(142, 142, 147),
        ["media"]         = MediaColor.FromRgb(255, 45, 85),
        ["notification"]  = MediaColor.FromRgb(90, 200, 250),
        ["agent"]         = MediaColor.FromRgb(191, 90, 242),
        ["info"]          = MediaColor.FromRgb(142, 142, 147),
    };

    private static readonly Dictionary<string, MediaColor> GlowColors = new()
    {
        ["clipboard"]     = MediaColor.FromArgb(76, 10, 132, 255),
        ["volume"]        = MediaColor.FromArgb(76, 10, 132, 255),
        ["volume_mute"]   = MediaColor.FromArgb(50, 142, 142, 147),
        ["battery"]       = MediaColor.FromArgb(76, 48, 209, 88),
        ["battery_charge"]= MediaColor.FromArgb(100, 48, 209, 88),
        ["battery_low"]   = MediaColor.FromArgb(100, 255, 69, 58),
        ["inputmethod"]   = MediaColor.FromArgb(76, 10, 132, 255),
        ["lockkey"]       = MediaColor.FromArgb(76, 191, 90, 242),
        ["network"]       = MediaColor.FromArgb(76, 48, 209, 88),
        ["network_off"]   = MediaColor.FromArgb(76, 255, 69, 58),
        ["usb"]           = MediaColor.FromArgb(76, 255, 159, 10),
        ["brightness"]    = MediaColor.FromArgb(100, 255, 214, 10),
        ["bluetooth"]     = MediaColor.FromArgb(76, 10, 132, 255),
        ["clock"]         = MediaColor.FromArgb(50, 142, 142, 147),
        ["media"]         = MediaColor.FromArgb(96, 255, 45, 85),
        ["notification"]  = MediaColor.FromArgb(86, 90, 200, 250),
        ["agent"]         = MediaColor.FromArgb(86, 191, 90, 242),
        ["info"]          = MediaColor.FromArgb(50, 142, 142, 147),
    };

    public event Action? RequestOpenSettings;

    public MainWindow(EventBus bus, FluidBarSettings settings)
    {
        _bus = bus;
        _settings = settings;
        InitializeComponent();

        _collapseTimer = new DispatcherTimer();
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!_settingsPanelOpen)
            {
                if (_settings.AlwaysVisible)
                    ShowIdleClock();
                else
                    Collapse();
            }
        };

        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _scrollTimer.Tick += ScrollTimer_Tick;
        
        _waveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _waveTimer.Tick += WaveTimer_Tick;

        // 提前绑定事件，避免 StartAll 时 Window_Loaded 尚未触发的竞态
        _bus.EventTriggered += OnEventTriggered;
    }

    public void SetClipboardPluginSettings(ClipboardPluginSettings s)
    {
        _clipboardPluginSettings = s;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        ApplySettings();

        if (_settings.AlwaysVisible)
        {
            Dispatcher.BeginInvoke(() => ShowIdleClock(),
                DispatcherPriority.Loaded);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _bus.EventTriggered -= OnEventTriggered;
        _collapseTimer.Stop();
        _scrollTimer.Stop();
        _waveTimer.Stop();
        StopHoverRendering();
        StopRimBreathing();
        CloseSnapshotWindows(immediate: true);
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            e.Handled = true;
            RequestOpenSettings?.Invoke();
        }
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ShowHoverCard();
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        HideHoverCard();
    }

    public void OnSettingsPanelOpened()
    {
        _settingsPanelOpen = true;
        _collapseTimer.Stop();
        _scrollTimer.Stop();
        ClearIslandStack(animated: false);

        Dispatcher.BeginInvoke(() =>
        {
            // 如果没展开，则展开（保持空闲状态显示）
            if (!_isExpanded)
            {
                if (_settings.AlwaysVisible)
                    ShowIdleClock();
                else
                    ExpandWithContent("FluidBar", "info");
            }
            PositionAtCurrentSize();
        });
    }

    public void OnSettingsPanelClosed()
    {
        _settingsPanelOpen = false;
        if (_isExpanded && _lastEvent != null)
        {
            SeedCurrentStackFromActiveView();
            PositionAtCurrentSize();
        }

        if (!_settings.AlwaysVisible && !_settingsPanelOpen)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    public void ApplySettings()
    {
        CoerceLayoutSettings();
        CoerceMultiIslandSettings();

        if (_settings.DisplayStrategy != IslandDisplayStrategy.Multiple)
        {
            _islandStack.Clear();
            CloseSnapshotWindows(immediate: _settingsPanelOpen);
        }
        else if (_settingsPanelOpen)
        {
            ClearIslandStack(animated: false);
        }

        PillBorder.CornerRadius = new CornerRadius(Math.Max(18, _settings.CornerRadius));
        PillBorder.Opacity = (_isExpanded || _settings.AlwaysVisible || _settingsPanelOpen)
            ? _settings.Opacity
            : 0;

        try
        {
            PillBackground.Color =
                (MediaColor)MediaColorConverter.ConvertFromString(_settings.BackgroundColor);
        }
        catch { PillBackground.Color = MediaColor.FromArgb(0xE6, 0x00, 0x00, 0x00); }

        try
        {
            var accentColor = (MediaColor)MediaColorConverter.ConvertFromString(_settings.AccentColor);
            IconBackground.BeginAnimation(SolidColorBrush.ColorProperty, null);
            IconPulseBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            IconBackground.Color = accentColor;
            IconPulseBrush.Color = accentColor;
            UpdateRimColors(accentColor);
        }
        catch
        {
            IconBackground.BeginAnimation(SolidColorBrush.ColorProperty, null);
            IconPulseBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            IconBackground.Color = MediaColor.FromRgb(10, 132, 255);
            IconPulseBrush.Color = MediaColor.FromRgb(10, 132, 255);
            UpdateRimColors(MediaColor.FromRgb(10, 132, 255));
        }

        PillBorder.MinWidth = _settings.CollapsedWidth;
        PillBorder.MinHeight = _settings.CollapsedHeight;
        PillBorder.MaxWidth = _settings.ExpandedMaxWidth;
        Topmost = _settings.AlwaysOnTop;
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(_settings.AutoHideDelayMs);

        // 应用环绕微光模式
        ApplyRimMode();

        if (_isExpanded && _lastEvent != null)
        {
            _currentView = IslandPresentation.FromEvent(
                _lastEvent,
                _settings,
                _clipboardPluginSettings?.MinFullDisplayChars ?? 20);
            SeedCurrentStackFromActiveView();
            if (_isHoverCard)
            {
                var card = HoverCardPresentation.FromCompact(_currentView, _settings);
                ApplyHoverCardContent(card);
                MorphHoverCard(HoverCardMotionPlan.CreateOpening(
                    CurrentVisualWidth(_currentView.TargetWidth),
                    CurrentVisualHeight(_currentView.TargetHeight),
                    card.TargetWidth,
                    card.TargetHeight));
            }
            else
            {
                MorphToView(_currentView);
            }
        }

        // 设置面板打开时实时更新位置（清除动画后用当前尺寸定位）
        if (_settingsPanelOpen)
        {
            if (!_isExpanded)
            {
                ClearPositionAnimations();
                PositionAtCurrentSize();
            }
            PillBorder.BeginAnimation(OpacityProperty,
                new DoubleAnimation(_settings.Opacity, TimeSpan.FromMilliseconds(150)));
        }

        // AlwaysVisible 模式切换
        if (_settings.AlwaysVisible && !_isExpanded)
        {
            Dispatcher.BeginInvoke(() => ShowIdleClock());
        }
        else if (!_settings.AlwaysVisible && _isExpanded && !_settingsPanelOpen)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void CoerceLayoutSettings()
    {
        _settings.CollapsedWidth = Math.Max(
            _settings.CollapsedWidth, IslandPresentationFactory.MinimumCollapsedWidth);
        _settings.CollapsedHeight = Math.Max(
            _settings.CollapsedHeight, IslandPresentationFactory.MinimumCollapsedHeight);
        _settings.ExpandedMaxWidth = Math.Max(
            _settings.ExpandedMaxWidth, IslandPresentationFactory.MinimumExpandedWidth);
        _settings.ExpandedHeight = Math.Clamp(
            Math.Max(_settings.ExpandedHeight, IslandPresentationFactory.MinimumExpandedHeight),
            IslandPresentationFactory.MinimumExpandedHeight,
            IslandPresentationFactory.MaximumExpandedHeight);
    }

    private void CoerceMultiIslandSettings()
    {
        _settings.MaxVisibleIslands = Math.Clamp(_settings.MaxVisibleIslands, 1, 8);
        _settings.MultiIslandGap = Math.Clamp(_settings.MultiIslandGap, 0, 28);
    }

    public void PositionWindow()
    {
        CoerceLayoutSettings();
        if (!TryCalculateStackedMainPosition(
                _settings.CollapsedWidth,
                _settings.CollapsedHeight,
                out double x,
                out double y,
                out var layout))
        {
            CalculatePosition(_settings.CollapsedWidth, _settings.CollapsedHeight,
                out x, out y);
        }

        SyncSnapshotWindows(layout, animated: false);
        ClearPositionAnimations();
        Left = x;
        Top = y;
        Width = ToWindowSize(_settings.CollapsedWidth);
        Height = ToWindowSize(_settings.CollapsedHeight);
    }

    /// <summary>设置面板打开时用当前实际尺寸定位，清除动画后直接设值</summary>
    private void PositionAtCurrentSize()
    {
        var w = _isExpanded ? ToVisualSize(ActualWidth) : _settings.CollapsedWidth;
        var h = _isExpanded ? ToVisualSize(ActualHeight) : _settings.CollapsedHeight;
        if (w < 10) w = _settings.CollapsedWidth;
        if (h < 10) h = _settings.CollapsedHeight;

        if (!TryCalculateStackedMainPosition(w, h, out double x, out double y, out var layout))
            CalculatePosition(w, h, out x, out y);

        SyncSnapshotWindows(layout, animated: false);
        ClearPositionAnimations();
        Left = x;
        Top = y;
    }

    /// <summary>清除所有位置/尺寸上的动画占用，否则直接赋值不生效</summary>
    private void ClearPositionAnimations()
    {
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
    }

    private void CalculatePosition(double w, double h, out double x, out double y)
    {
        w = ToWindowSize(w);
        h = ToWindowSize(h);
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;

        switch (_settings.Position)
        {
            case "Top":
                x = (screenW - w) / 2 + _settings.OffsetX;
                y = 8 + _settings.OffsetY;
                break;
            case "Bottom":
                x = (screenW - w) / 2 + _settings.OffsetX;
                y = screenH - h - 12 + _settings.OffsetY;
                break;
            case "TopLeft":
                x = 16 + _settings.OffsetX;
                y = 8 + _settings.OffsetY;
                break;
            case "TopRight":
                x = screenW - w - 16 + _settings.OffsetX;
                y = 8 + _settings.OffsetY;
                break;
            case "BottomLeft":
                x = 16 + _settings.OffsetX;
                y = screenH - h - 12 + _settings.OffsetY;
                break;
            case "BottomRight":
                x = screenW - w - 16 + _settings.OffsetX;
                y = screenH - h - 12 + _settings.OffsetY;
                break;
            default:
                x = (screenW - w) / 2 + _settings.OffsetX;
                y = 8 + _settings.OffsetY;
                break;
        }

        const double margin = 8;
        x = Math.Clamp(x, margin, Math.Max(margin, screenW - w - margin));
        y = Math.Clamp(y, margin, Math.Max(margin, screenH - h - margin));
    }

    // ===========================================================
    // 事件处理 - 永远处理事件，不丢弃，不使用队列
    // ===========================================================

    private void OnEventTriggered(IslandEvent evt)
    {
        // 事件已在 UI 线程上（来自 DispatcherTimer），直接处理
        ProcessEvent(evt);
    }

    private void ProcessEvent(IslandEvent evt)
    {
        var view = IslandPresentation.FromEvent(
            evt,
            _settings,
            _clipboardPluginSettings?.MinFullDisplayChars ?? 20);

        // 时钟监控只在常驻/已展开时更新，避免每 10 秒主动弹出。
        if (view.Kind == IslandViewKind.Clock && !_settings.AlwaysVisible && !_isExpanded)
            return;

        ApplyStackPolicy(evt, view);

        _currentView = view;
        _currentSource = evt.Source;
        _lastEvent = evt;
        UpdateIcon(view.IconKind);
        HideAllPanels();

        switch (view.Kind)
        {
            case IslandViewKind.Progress:
                ShowProgressBar(evt, view);
                break;
            case IslandViewKind.Status:
                ShowStatusIndicator(evt, view);
                break;
            case IslandViewKind.Media:
                ShowMediaContent(evt, view);
                break;
            case IslandViewKind.Notification:
            case IslandViewKind.Agent:
                ShowRichStatusContent(evt, view);
                break;
            case IslandViewKind.LockKey:
                ShowLockKeyIndicator(evt);
                break;
            case IslandViewKind.InputMethod:
                ShowImeIndicator(evt);
                break;
            case IslandViewKind.Clock:
                ShowClockContent(evt);
                break;
            case IslandViewKind.ScrollingText:
                ShowTextContent(evt, view);
                break;
            default:
                ShowTextContent(evt, view);
                break;
        }

        // 环绕微光触发
        TriggerRimPulse(evt.Source);

        if (!_isExpanded)
            Expand(view);
        else if (_isHoverCard)
        {
            var card = HoverCardPresentation.FromCompact(view, _settings);
            ApplyHoverCardContent(card);
            MorphHoverCard(HoverCardMotionPlan.CreateOpening(
                CurrentVisualWidth(view.TargetWidth),
                CurrentVisualHeight(view.TargetHeight),
                card.TargetWidth,
                card.TargetHeight));
            ResetCollapseTimer();

            if (view.Kind != IslandViewKind.Progress && ShouldEmphasizeSource(evt.Source))
                NudgePill();
        }
        else
        {
            MorphToView(view);
            // 微动弹性：仅离散事件触发（进度类高频事件跳过）
            if (view.Kind != IslandViewKind.Progress && ShouldEmphasizeSource(evt.Source))
                NudgePill();
        }
    }

    private void ApplyStackPolicy(IslandEvent evt, IslandViewPresentation view)
    {
        if (view.Kind == IslandViewKind.Clock || evt.Source == "clock")
        {
            ClearIslandStack(animated: true);
            return;
        }

        if (_settings.DisplayStrategy != IslandDisplayStrategy.Multiple)
        {
            _islandStack.Clear();
            CloseSnapshotWindows(immediate: _settingsPanelOpen);
            return;
        }

        if (_settingsPanelOpen)
        {
            ClearIslandStack(animated: false);
            return;
        }

        if (_isHoverCard)
            ExitHoverCardForIncomingStack();

        var next = IslandStackPolicy.Apply(_islandStack, view, evt.Source, _settings);
        _islandStack.Clear();
        _islandStack.AddRange(next);
    }

    private bool IsStackedIslandActive()
    {
        return IslandStackVisibilityPolicy.ShouldRender(
            _settings,
            _islandStack.Count,
            _settingsPanelOpen,
            _currentView?.Kind);
    }

    private void ClearIslandStack(bool animated)
    {
        _islandStack.Clear();
        CloseSnapshotWindows(immediate: !animated || _settingsPanelOpen);
    }

    private void SeedCurrentStackFromActiveView()
    {
        if (_settings.DisplayStrategy != IslandDisplayStrategy.Multiple
            || _settingsPanelOpen
            || !_isExpanded
            || _currentView is null
            || string.IsNullOrWhiteSpace(_currentSource)
            || _currentSource is "clock" or "app"
            || _currentView.Kind == IslandViewKind.Clock)
        {
            return;
        }

        if (_islandStack.Count == 0 || _islandStack[^1].Source != _currentSource)
            _islandStack.Add(new IslandStackItem(_currentSource, _currentView, DateTimeOffset.UtcNow));
        else
            _islandStack[^1] = _islandStack[^1] with { View = _currentView };

        var max = Math.Clamp(_settings.MaxVisibleIslands, 1, 8);
        if (_islandStack.Count > max)
            _islandStack.RemoveRange(0, _islandStack.Count - max);
    }

    private void ExitHoverCardForIncomingStack()
    {
        _isHoverCard = false;
        StopHoverSpring();
        HoverCardGrid.BeginAnimation(OpacityProperty, null);
        HoverCardTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        IslandContent.BeginAnimation(OpacityProperty, null);
        HoverCardGrid.Visibility = Visibility.Collapsed;
        HoverCardGrid.Opacity = 0;
        HoverCardTranslate.Y = 0;
        IslandContent.Opacity = 1;
        PillBorder.CornerRadius = new CornerRadius(Math.Max(18, _settings.CornerRadius));
        PillBorder.MaxWidth = _settings.ExpandedMaxWidth;
    }

    private double SnapshotWidth(IslandViewPresentation view)
    {
        var max = Math.Min(280, Math.Max(180, _settings.ExpandedMaxWidth * 0.72));
        var preferred = view.Kind switch
        {
            IslandViewKind.Progress => 210,
            IslandViewKind.Status => 224,
            IslandViewKind.ScrollingText => 240,
            IslandViewKind.LockKey => 176,
            IslandViewKind.InputMethod => 156,
            _ => 198
        };

        return Math.Clamp(Math.Min(view.TargetWidth, preferred), 148, max);
    }

    private double SnapshotHeight(IslandViewPresentation view)
    {
        return Math.Clamp(view.TargetHeight, _settings.CollapsedHeight, _settings.ExpandedHeight);
    }

    private IReadOnlyList<IslandSlotMetrics> BuildStackedSlotMetrics(
        double latestWidth,
        double latestHeight)
    {
        var slots = new List<IslandSlotMetrics>(_islandStack.Count);
        for (var i = 0; i < Math.Max(0, _islandStack.Count - 1); i++)
        {
            var view = _islandStack[i].View;
            slots.Add(new IslandSlotMetrics(SnapshotWidth(view), SnapshotHeight(view)));
        }

        slots.Add(new IslandSlotMetrics(latestWidth, latestHeight));
        return slots;
    }

    private bool TryCalculateStackedMainPosition(
        double latestWidth,
        double latestHeight,
        out double left,
        out double top,
        out IslandGroupLayoutResult? layout)
    {
        left = 0;
        top = 0;
        layout = null;

        if (!IsStackedIslandActive())
            return false;

        layout = IslandGroupLayout.Calculate(
            BuildStackedSlotMetrics(latestWidth, latestHeight),
            _settings.Position,
            SystemParameters.PrimaryScreenWidth,
            SystemParameters.PrimaryScreenHeight,
            _settings.OffsetX,
            _settings.OffsetY,
            _settings.MultiIslandGap);

        var currentSlot = layout.Slots[^1];
        left = layout.Left + currentSlot.OffsetX - ShellBleedMargin;
        top = layout.Top + currentSlot.OffsetY;
        return true;
    }

    private void SyncSnapshotWindows(
        IslandGroupLayoutResult? layout,
        bool animated)
    {
        if (!IsStackedIslandActive() || layout == null)
        {
            CloseSnapshotWindows(immediate: _settingsPanelOpen);
            return;
        }

        var snapshotCount = _islandStack.Count - 1;
        while (_snapshotWindows.Count < snapshotCount)
            _snapshotWindows.Add(new IslandSnapshotWindow());

        while (_snapshotWindows.Count > snapshotCount)
        {
            var last = _snapshotWindows[^1];
            _snapshotWindows.RemoveAt(_snapshotWindows.Count - 1);
            if (_settingsPanelOpen)
            {
                try { last.Close(); }
                catch (InvalidOperationException) { }
            }
            else
            {
                last.Dismiss();
            }
        }

        for (var i = 0; i < snapshotCount; i++)
        {
            var item = _islandStack[i];
            var slot = layout.Slots[i];
            var window = _snapshotWindows[i];
            window.Topmost = _settings.AlwaysOnTop;
            window.SetView(item, _settings);
            window.Place(
                layout.Left + slot.OffsetX - ShellBleedMargin,
                layout.Top + slot.OffsetY,
                slot.Width,
                slot.Height,
                animated);
            window.Reveal();
        }
    }

    private void CloseSnapshotWindows(bool immediate)
    {
        foreach (var window in _snapshotWindows.ToArray())
        {
            try
            {
                if (immediate)
                    window.Close();
                else
                    window.Dismiss();
            }
            catch (InvalidOperationException)
            {
            }
        }

        _snapshotWindows.Clear();
    }

    private MonitorFeatureSettings? GetCurrentMonitorFeatureSettings()
    {
        if (string.IsNullOrWhiteSpace(_currentSource))
            return null;
        if (_currentSource is "clipboard" or "app")
            return null;
        return _settings.GetMonitorFeatureSettings(_currentSource);
    }

    private bool ShouldEmphasizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return true;
        if (source is "clipboard" or "app")
            return true;
        return _settings.GetMonitorFeatureSettings(source).EmphasizeTransitions;
    }

    private bool CanShowHoverCard()
    {
        return HoverCardPolicy.CanShow(
            _isExpanded,
            _settingsPanelOpen,
            _currentSource,
            _currentView != null,
            _settings);
    }

    private void ShowHoverCard()
    {
        if (_isHoverCard || !CanShowHoverCard() || _currentView == null)
            return;

        _isHoverCard = true;
        _collapseTimer.Stop();
        StopScrolling();

        var card = HoverCardPresentation.FromCompact(_currentView, _settings);
        ApplyHoverCardContent(card);
        var fromWidth = ToVisualSize(ActualWidth);
        var fromHeight = ToVisualSize(ActualHeight);
        if (fromWidth < 10) fromWidth = _currentView.TargetWidth;
        if (fromHeight < 10) fromHeight = _currentView.TargetHeight;
        var plan = HoverCardMotionPlan.CreateOpening(
            fromWidth,
            fromHeight,
            card.TargetWidth,
            card.TargetHeight);
        MorphHoverCard(plan);

        PillBorder.CornerRadius = new CornerRadius(30);
        IslandContent.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        HoverCardGrid.Visibility = Visibility.Visible;
        HoverCardGrid.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(260))
            {
                BeginTime = TimeSpan.FromMilliseconds(plan.ContentRevealDelayMilliseconds),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
            });
        HoverCardTranslate.Y = 6;
        HoverCardTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
            {
                BeginTime = TimeSpan.FromMilliseconds(Math.Max(70, plan.ContentRevealDelayMilliseconds - 35)),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
            });
        OuterBloom.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.86, TimeSpan.FromMilliseconds(260)));
    }

    private void HideHoverCard()
    {
        if (!_isHoverCard) return;
        _isHoverCard = false;

        if (_currentView != null)
        {
            MorphHoverCard(HoverCardMotionPlan.CreateClosing(
                CurrentVisualWidth(_currentView.TargetWidth),
                CurrentVisualHeight(_currentView.TargetHeight),
                _currentView.TargetWidth,
                _currentView.TargetHeight));
        }
        else
        {
            StopHoverSpring();
        }

        PillBorder.CornerRadius = new CornerRadius(Math.Max(18, _settings.CornerRadius));
        HoverCardGrid.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        HoverCardTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
        IslandContent.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(70),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        OuterBloom.BeginAnimation(OpacityProperty,
            new DoubleAnimation(_isExpanded ? 0.62 : 0, TimeSpan.FromMilliseconds(220)));

        var collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
        collapseTimer.Tick += (_, _) =>
        {
            collapseTimer.Stop();
            if (!_isHoverCard)
                HoverCardGrid.Visibility = Visibility.Collapsed;
        if (_currentView?.Kind == IslandViewKind.ScrollingText && ScrollCanvas.Visibility == Visibility.Visible)
        {
            var width = ScrollCanvas.ActualWidth > 0 ? ScrollCanvas.ActualWidth : ScrollCanvas.Width;
            StartScrolling(width);
        }
        ResetCollapseTimer();
        };
        collapseTimer.Start();
    }

    private void MorphWindowTo(double targetWidth, double targetHeight, TimeSpan duration)
    {
        StopHoverSpring();
        _activeTargetWidth = targetWidth;
        _activeTargetHeight = targetHeight;
        PillBorder.MaxWidth = Math.Max(_settings.ExpandedMaxWidth, targetWidth);
        if (!TryCalculateStackedMainPosition(targetWidth, targetHeight, out var left, out var top, out var layout))
            CalculatePosition(targetWidth, targetHeight, out left, out top);

        SyncSnapshotWindows(layout, animated: true);
        ClearPositionAnimations();

        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
        AnimateProperty(WidthProperty, ToWindowSize(targetWidth), duration, ease);
        AnimateProperty(HeightProperty, ToWindowSize(targetHeight), duration, ease);
        AnimateProperty(LeftProperty, left, duration, ease);
        AnimateProperty(TopProperty, top, duration, ease);
    }

    private double CurrentVisualWidth(double fallback)
    {
        if ((_hoverMotionPlan is not null || _isHoverCard) && _hoverWidthSpring.Value >= 10)
            return _hoverWidthSpring.Value;

        var width = ToVisualSize(ActualWidth);
        return double.IsFinite(width) && width >= 10 ? width : fallback;
    }

    private double CurrentVisualHeight(double fallback)
    {
        if ((_hoverMotionPlan is not null || _isHoverCard) && _hoverHeightSpring.Value >= 10)
            return _hoverHeightSpring.Value;

        var height = ToVisualSize(ActualHeight);
        return double.IsFinite(height) && height >= 10 ? height : fallback;
    }

    private void MorphHoverCard(HoverCardMotionPlan plan)
    {
        _activeTargetWidth = plan.ToWidth;
        _activeTargetHeight = plan.ToHeight;
        var hostWidth = Math.Max(plan.FromWidth, plan.ToWidth) + HoverHostPadding * 2;
        var hostHeight = Math.Max(plan.FromHeight, plan.ToHeight) + HoverHostPadding * 2;
        _hoverHostWidth = Math.Max(_hoverHostWidth, hostWidth);
        _hoverHostHeight = Math.Max(_hoverHostHeight, hostHeight);
        PillBorder.MaxWidth = Math.Max(_settings.ExpandedMaxWidth, _hoverHostWidth);

        var shouldReset = !_hoverRenderingAttached || _hoverMotionPlan is null;
        if (shouldReset)
        {
            _hoverWidthSpring.Reset(CurrentVisualWidth(plan.FromWidth));
            _hoverHeightSpring.Reset(CurrentVisualHeight(plan.FromHeight));
        }

        _hoverWidthSpring.Target = plan.ToWidth;
        _hoverHeightSpring.Target = plan.ToHeight;
        _hoverMotionPlan = plan;
        _hoverSpringHasRenderTime = false;

        ClearPositionAnimations();
        if (!TryCalculateStackedMainPosition(
                _hoverHostWidth,
                _hoverHostHeight,
                out var hostLeft,
                out var hostTop,
                out var layout))
        {
            CalculatePosition(_hoverHostWidth, _hoverHostHeight, out hostLeft, out hostTop);
        }

        SyncSnapshotWindows(layout, animated: true);
        Left = hostLeft;
        Top = hostTop;
        Width = ToWindowSize(_hoverHostWidth);
        Height = ToWindowSize(_hoverHostHeight);
        ApplyHoverHostAlignment();

        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PillSkew.BeginAnimation(SkewTransform.AngleXProperty, null);
        PillScale.ScaleX = 1;
        PillScale.ScaleY = 1;
        PillSkew.AngleX = 0;

        ApplyHoverSpringFrame(_hoverWidthSpring.Value, _hoverHeightSpring.Value);
        StartHoverRendering();
    }

    private void HoverSpringRendering_Tick(object? sender, EventArgs e)
    {
        if (_hoverMotionPlan is not { } plan)
        {
            StopHoverRendering();
            return;
        }

        var renderTime = RenderingEventArgsToTime(e);
        var dt = 1.0 / 60.0;
        if (_hoverSpringHasRenderTime)
            dt = Math.Clamp((renderTime - _hoverSpringLastRenderTime).TotalSeconds, 0.001, 0.050);
        _hoverSpringLastRenderTime = renderTime;
        _hoverSpringHasRenderTime = true;

        StepHoverSpring(_hoverWidthSpring, dt, plan);
        StepHoverSpring(_hoverHeightSpring, dt, plan);
        ApplyHoverSpringFrame(_hoverWidthSpring.Value, _hoverHeightSpring.Value);

        if (!_hoverWidthSpring.IsSettled || !_hoverHeightSpring.IsSettled)
            return;

        ApplyHoverSpringFrame(plan.ToWidth, plan.ToHeight);
        StopHoverRendering();
        _hoverMotionPlan = null;

        if (plan.Kind == HoverCardMotionKind.WarpClose && !_isHoverCard)
        {
            PillBorder.MaxWidth = _settings.ExpandedMaxWidth;
            RestoreWindowToCurrentView();
        }
    }

    private static void StepHoverSpring(SpringValue spring, double dt, HoverCardMotionPlan plan)
    {
        var expanding = spring.Target >= spring.Value;
        spring.Step(
            dt,
            expanding ? plan.ExpandingStiffness : plan.ContractingStiffness,
            expanding ? plan.ExpandingDamping : plan.ContractingDamping);
    }

    private void ApplyHoverSpringFrame(double visualWidth, double visualHeight)
    {
        visualWidth = Math.Max(10, visualWidth);
        visualHeight = Math.Max(10, visualHeight);
        IslandRoot.Width = visualWidth;
        IslandRoot.Height = visualHeight;
    }

    private void ApplyHoverHostAlignment()
    {
        IslandRoot.HorizontalAlignment = IsStackedIslandActive()
            ? System.Windows.HorizontalAlignment.Left
            : _settings.Position.Contains("Left", StringComparison.OrdinalIgnoreCase)
            ? System.Windows.HorizontalAlignment.Left
            : _settings.Position.Contains("Right", StringComparison.OrdinalIgnoreCase)
                ? System.Windows.HorizontalAlignment.Right
                : System.Windows.HorizontalAlignment.Center;

        IslandRoot.VerticalAlignment = _settings.Position.StartsWith("Bottom", StringComparison.OrdinalIgnoreCase)
            ? System.Windows.VerticalAlignment.Bottom
            : System.Windows.VerticalAlignment.Top;
        IslandRoot.Margin = new Thickness(ShellBleedMargin);
    }

    private void StopHoverSpring()
    {
        StopHoverRendering();
        _hoverMotionPlan = null;
        ClearHoverHostLayout();
    }

    private void StartHoverRendering()
    {
        if (_hoverRenderingAttached)
            return;

        CompositionTarget.Rendering += HoverSpringRendering_Tick;
        _hoverRenderingAttached = true;
    }

    private void StopHoverRendering()
    {
        if (!_hoverRenderingAttached)
            return;

        CompositionTarget.Rendering -= HoverSpringRendering_Tick;
        _hoverRenderingAttached = false;
        _hoverSpringHasRenderTime = false;
    }

    private void RestoreWindowToCurrentView()
    {
        if (_currentView is null)
            return;

        ClearHoverHostLayout();
        if (!TryCalculateStackedMainPosition(
                _currentView.TargetWidth,
                _currentView.TargetHeight,
                out var left,
                out var top,
                out var layout))
        {
            CalculatePosition(_currentView.TargetWidth, _currentView.TargetHeight, out left, out top);
        }

        SyncSnapshotWindows(layout, animated: true);
        Left = left;
        Top = top;
        Width = ToWindowSize(_currentView.TargetWidth);
        Height = ToWindowSize(_currentView.TargetHeight);
    }

    private void ClearHoverHostLayout()
    {
        IslandRoot.Width = double.NaN;
        IslandRoot.Height = double.NaN;
        IslandRoot.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        IslandRoot.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        IslandRoot.Margin = new Thickness(ShellBleedMargin);
        PillBorder.Width = double.NaN;
        PillBorder.Height = double.NaN;
        OuterBloom.Width = double.NaN;
        OuterBloom.Height = double.NaN;
        _hoverHostWidth = 0;
        _hoverHostHeight = 0;
    }

    private static TimeSpan RenderingEventArgsToTime(EventArgs e)
    {
        return e is RenderingEventArgs renderingArgs
            ? renderingArgs.RenderingTime
            : TimeSpan.FromTicks(DateTime.UtcNow.Ticks);
    }

    private static double ToWindowSize(double visualSize) => visualSize + ShellBleed;

    private static double ToVisualSize(double windowSize) => Math.Max(0, windowSize - ShellBleed);

    private void ApplyHoverCardContent(HoverCardPresentation card)
    {
        HoverIconText.Text = IconGlyphs.TryGetValue(card.IconKind, out var glyph)
            ? glyph
            : IconGlyphs["info"];
        HoverTitleText.Text = card.Title;
        HoverSubtitleText.Text = BuildHoverSubtitle(card);
        HoverBadgeText.Text = string.IsNullOrWhiteSpace(card.StatusBadge)
            ? ModeLabel(card.Kind)
            : card.StatusBadge;
        SetHoverBadgeColors(IconColors.TryGetValue(card.IconKind, out var c)
            ? c
            : IconColors["info"]);

        // Style lyrics in subtitle for media
        if (card.Kind == IslandViewKind.Media && !string.IsNullOrWhiteSpace(card.LyricLine))
        {
            HoverSubtitleText.FontSize = 13;
            HoverSubtitleText.FontStyle = FontStyles.Italic;
            HoverSubtitleText.Foreground = new SolidColorBrush(
                MediaColor.FromRgb(200, 200, 210));
        }
        else
        {
            HoverSubtitleText.FontSize = 11.5;
            HoverSubtitleText.FontStyle = FontStyles.Normal;
            HoverSubtitleText.Foreground = new SolidColorBrush(
                MediaColor.FromRgb(143, 143, 150));
        }

        HoverProgressPanel.Visibility = card.Kind is IslandViewKind.Progress or IslandViewKind.Media
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (card.Kind == IslandViewKind.Progress)
        {
            HoverBodyText.Text = card.IconKind == "volume_mute"
                ? "当前输出已静音"
                : $"{card.Title} · {card.ProgressPercent}%";
            var trackWidth = Math.Max(220, card.TargetWidth - 40);
            HoverProgressFill.Width = trackWidth * card.ProgressPercent / 100.0;
        }
        else if (card.Kind == IslandViewKind.Media)
        {
            HoverBodyText.Text = card.Content;
            var trackWidth = Math.Max(220, card.TargetWidth - 40);
            HoverProgressFill.Width = trackWidth * card.ProgressPercent / 100.0;
        }
        else if (card.Kind == IslandViewKind.Status)
        {
            HoverBodyText.Text = card.StatusText;
        }
        else
        {
            HoverBodyText.Text = card.Content;
        }

        HoverBodyText.MaxHeight = card.DetailLines * 20;
        HoverMetaText.Text = _currentSource switch
        {
            "clipboard" => "复制内容详情",
            "clock" => "空闲状态",
            "volume" => "音量指示",
            "brightness" => "亮度指示",
            "battery" => "电池状态",
            "network" => "网络状态",
            "usb" => "USB 设备",
            "bluetooth" => "蓝牙设备",
            "lockkey" => "锁键状态",
            "inputmethod" => "输入法状态",
            "media" => "媒体播放",
            "agent-status" => "Agent 任务",
            "notifications" => "系统通知",
            _ => "FluidBar"
        };
    }

    private static string ModeLabel(IslandViewKind kind)
    {
        return kind switch
        {
            IslandViewKind.Progress => "进度",
            IslandViewKind.Status => "状态",
            IslandViewKind.Clock => "时钟",
            IslandViewKind.InputMethod => "输入法",
            IslandViewKind.LockKey => "锁键",
            IslandViewKind.Media => "媒体",
            IslandViewKind.Agent => "Agent",
            IslandViewKind.Notification => "通知",
            _ => "详情"
        };
    }

    private static string BuildHoverSubtitle(HoverCardPresentation card)
    {
        if (card.Kind == IslandViewKind.Progress)
            return card.IconKind == "brightness" ? "屏幕亮度变化" : "系统音量变化";
        if (card.Kind == IslandViewKind.Status)
            return card.StatusBadge;
        if (card.Kind == IslandViewKind.Media)
        {
            // Show lyrics prominently when available, fall back to artist·album
            if (!string.IsNullOrWhiteSpace(card.LyricLine))
            {
                var lyricText = card.LyricLine;
                if (!string.IsNullOrWhiteSpace(card.SecondaryLyricLine))
                    lyricText += $"  ›  {card.SecondaryLyricLine}";
                return lyricText;
            }
            return string.IsNullOrWhiteSpace(card.Subtitle) ? "媒体播放" : card.Subtitle;
        }
        if (card.Kind == IslandViewKind.Agent)
            return string.IsNullOrWhiteSpace(card.SourceName) ? "Agent 任务状态" : card.SourceName;
        if (card.Kind == IslandViewKind.Notification)
            return string.IsNullOrWhiteSpace(card.SourceName) ? "系统通知" : card.SourceName;
        if (card.AllowsMultilineContent)
            return $"可显示 {card.DetailLines} 行内容";
        return ModeLabel(card.Kind);
    }

    private void SetHoverBadgeColors(MediaColor color)
    {
        HoverBadgeBorder.Background = new SolidColorBrush(
            MediaColor.FromArgb(42, color.R, color.G, color.B));
        HoverBadgeBorder.BorderBrush = new SolidColorBrush(
            MediaColor.FromArgb(76, color.R, color.G, color.B));
        HoverBadgeText.Foreground = new SolidColorBrush(
            MediaColor.FromArgb(238,
                (byte)Math.Min(255, color.R + 90),
                (byte)Math.Min(255, color.G + 90),
                (byte)Math.Min(255, color.B + 90)));
    }

    /// <summary>微动弹性 — 新事件到达时给药丸一个微小的缩放脉冲</summary>
    private void NudgePill()
    {
        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PillScale.ScaleX = 1.045;
        PillScale.ScaleY = 0.985;

        var nudgeAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new ElasticEase
            {
                EasingMode = EasingMode.EaseOut,
                Oscillations = 2,
                Springiness = 5
            }
        };

        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, nudgeAnim);
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, nudgeAnim);
    }

    // ===========================================================
    // 环绕微光旋转
    // ===========================================================

    private bool _rimContinuousRunning;
    private bool _rimPulseRunning;
    private int _rimAnimationToken;

    /// <summary>更新微光颜色（跟随 accent 色）</summary>
    private void UpdateRimColors(MediaColor accent)
    {
        var glow = MediaColor.FromArgb(0xC0, accent.R, accent.G, accent.B);
        var dim  = MediaColor.FromArgb(0x0A, 0xFF, 0xFF, 0xFF);
        RimStop0.Color = dim;
        RimStop1.Color = dim;
        RimStop2.Color = glow;
        RimStop3.Color = dim;
        RimStop4.Color = dim;
    }

    /// <summary>根据配置应用环绕微光模式</summary>
    private void ApplyRimMode()
    {
        if (_settings.RimMode == "Always")
        {
            StartRimContinuous();
        }
        else
        {
            StopRimContinuous();
        }
    }

    /// <summary>始终旋转模式 — 纯 WPF 动画，避免计时器抢写 Opacity。</summary>
    private void StartRimContinuous()
    {
        if (_rimContinuousRunning) return;
        ++_rimAnimationToken;
        _rimPulseRunning = false;
        _rimContinuousRunning = true;

        RimBrush.BeginAnimation(LinearGradientBrush.OpacityProperty, null);
        RimRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        RimRotation.Angle = 0;
        RimBrush.Opacity = 0.72;

        RimBrush.BeginAnimation(LinearGradientBrush.OpacityProperty,
            new DoubleAnimation(0.52, 1.0, TimeSpan.FromSeconds(1.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });

        var rotateAnim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(12))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        RimRotation.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
    }

    private void StopRimContinuous()
    {
        if (!_rimContinuousRunning)
        {
            if (!_rimPulseRunning)
                SetRimIdle();
            return;
        }

        var token = ++_rimAnimationToken;
        _rimContinuousRunning = false;

        var currentOpacity = RimBrush.Opacity;
        RimBrush.BeginAnimation(LinearGradientBrush.OpacityProperty, null);
        RimRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        RimBrush.Opacity = currentOpacity;

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (token != _rimAnimationToken) return;
            RimRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            RimRotation.Angle = 0;
        };
        RimBrush.BeginAnimation(LinearGradientBrush.OpacityProperty, fadeOut);
    }

    /// <summary>脉冲旋转 — 触发一次 360° 旋转后淡出</summary>
    private void TriggerRimPulse(string? source)
    {
        var mode = _settings.RimMode;
        if (mode == "Always") return;
        if (!ShouldEmphasizeSource(source)) return;

        var isPlugin = source == "clipboard";
        if (mode == "Plugin" && !isPlugin) return;
        if (source == "clock") return;

        if (_rimPulseRunning) return;
        var token = ++_rimAnimationToken;
        _rimPulseRunning = true;

        RimBrush.BeginAnimation(LinearGradientBrush.OpacityProperty, null);
        RimRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        RimBrush.Opacity = 0;

        RimBrush.BeginAnimation(LinearGradientBrush.OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        var startAngle = RimRotation.Angle % 360;
        var rotateAnim = new DoubleAnimation(startAngle, startAngle + 360, TimeSpan.FromSeconds(2.8))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
        };
        rotateAnim.Completed += (_, _) =>
        {
            if (token != _rimAnimationToken) return;
            _rimPulseRunning = false;
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeOut.Completed += (_, _) =>
            {
                if (token != _rimAnimationToken) return;
                RimRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                RimRotation.Angle = 0;
            };
            RimBrush.BeginAnimation(LinearGradientBrush.OpacityProperty, fadeOut);
        };
        RimRotation.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
    }

    private void SetRimIdle()
    {
        RimBrush.BeginAnimation(LinearGradientBrush.OpacityProperty, null);
        RimRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        RimBrush.Opacity = 0;
        RimRotation.Angle = 0;
    }

    private void StopRimBreathing()
    {
        ++_rimAnimationToken;
        _rimContinuousRunning = false;
        _rimPulseRunning = false;
        SetRimIdle();
    }

    /// <summary>已展开时刷新内容（柔和的淡入过渡 + 重置隐藏计时器）</summary>
    private void RefreshDisplay()
    {
        // 清除旧动画
        ContentPanel.BeginAnimation(OpacityProperty, null);
        ContentTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        ContentTranslate.Y = 2;

        var fadeOverlay = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ContentPanel.BeginAnimation(OpacityProperty, fadeOverlay);
        ContentTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        ResetCollapseTimer();
    }

    /// <summary>重置自动隐藏计时器</summary>
    private void ResetCollapseTimer()
    {
        if (_isHoverCard)
        {
            _collapseTimer.Stop();
            return;
        }

        if (!_settings.AlwaysVisible)
        {
            var d = _currentSource == "clipboard"
                ? _clipboardPluginSettings?.DisplayDurationMs ?? _settings.AutoHideDelayMs
                : GetCurrentMonitorFeatureSettings()?.DisplayDurationMs ?? _settings.AutoHideDelayMs;
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(d);
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void HideAllPanels()
    {
        StopScrolling();
        ContentText.Visibility = Visibility.Collapsed;
        ProgressBarPanel.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Collapsed;
        LockKeyPanel.Visibility = Visibility.Collapsed;
        ImePanel.Visibility = Visibility.Collapsed;
        ScrollCanvas.Visibility = Visibility.Collapsed;
        AccessoryGrid.Visibility = Visibility.Collapsed;
        AudioWavePanel.Visibility = Visibility.Collapsed;
        _waveTimer.Stop();
    }

    private void ShowProgressBar(IslandEvent evt, IslandViewPresentation view)
    {
        TitleText.Text = evt.Title;
        ProgressBarPanel.Visibility = Visibility.Visible;

        // 音量等显示波形
        if (view.ShowsAudioWave)
        {
            AccessoryGrid.Visibility = Visibility.Visible;
            AudioWavePanel.Visibility = Visibility.Visible;
            AudioWavePanel.Opacity = evt.IconKind == "volume_mute" ? 0.42 : 1;
            if (evt.IconKind == "volume_mute")
            {
                _waveTimer.Stop();
                SetWaveHeights(5, 5, 5, 5, TimeSpan.FromMilliseconds(180));
            }
            else
            {
                _waveTimer.Start();
            }
        }
        else
        {
            AccessoryGrid.Visibility = Visibility.Collapsed;
            AudioWavePanel.Visibility = Visibility.Collapsed;
            _waveTimer.Stop();
        }

        var maxBarWidth = Math.Max(128, view.TargetWidth - 126 - (view.ShowsAudioWave ? 38 : 0));
        ProgressTrack.Width = maxBarWidth;
        var targetWidth = Math.Max(0, view.ProgressPercent / 100.0 * maxBarWidth);

        // 从上一值动画到新值（避免从0跳起）
        var currentWidth = ProgressFill.Width;
        ProgressFill.BeginAnimation(System.Windows.Controls.Border.WidthProperty, null);
        ProgressFill.Width = currentWidth;

        var isIncreasing = targetWidth > currentWidth;
        var duration = isIncreasing
            ? TimeSpan.FromMilliseconds(250)
            : TimeSpan.FromMilliseconds(400);
        var ease = isIncreasing
            ? (IEasingFunction)new CubicEase { EasingMode = EasingMode.EaseOut }
            : new QuarticEase { EasingMode = EasingMode.EaseOut };

        ProgressFill.BeginAnimation(System.Windows.Controls.Border.WidthProperty,
            new DoubleAnimation(targetWidth, duration) { EasingFunction = ease });

        // 进度条颜色
        if (evt.IconKind == "brightness")
        {
            ProgressFill.Background = new LinearGradientBrush(
                MediaColor.FromRgb(255, 214, 10), MediaColor.FromRgb(255, 179, 0), 0);
        }
        else if (evt.IconKind == "volume_mute")
        {
            ProgressFill.Background = new LinearGradientBrush(
                MediaColor.FromRgb(142, 142, 147), MediaColor.FromRgb(99, 99, 102), 0);
        }
        else
        {
            ProgressFill.Background = new LinearGradientBrush(
                MediaColor.FromRgb(10, 132, 255), MediaColor.FromRgb(90, 200, 250), 0);
        }
    }

    private void ShowStatusIndicator(IslandEvent evt, IslandViewPresentation view)
    {
        TitleText.Text = evt.Title;
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = view.StatusText;
        StatusBadgeText.Text = view.StatusBadge;

        var isError = evt.IconKind is "battery_low" or "network_off";
        if (isError)
        {
            StatusIconText.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 69, 58));
            StatusIconText.Text = "\uE711"; // Warning
            SetStatusBadgeColors(MediaColor.FromRgb(255, 69, 58));
        }
        else if (evt.IconKind == "battery_charge")
        {
            StatusIconText.Foreground = new SolidColorBrush(MediaColor.FromRgb(48, 209, 88));
            StatusIconText.Text = "\uE9A6"; // Plug outline
            SetStatusBadgeColors(MediaColor.FromRgb(48, 209, 88));
        }
        else if (evt.IconKind == "usb")
        {
            var c = MediaColor.FromRgb(255, 159, 10);
            StatusIconText.Foreground = new SolidColorBrush(c);
            StatusIconText.Text = "\uE88E";
            SetStatusBadgeColors(c);
        }
        else if (evt.IconKind == "bluetooth")
        {
            var c = MediaColor.FromRgb(10, 132, 255);
            StatusIconText.Foreground = new SolidColorBrush(c);
            StatusIconText.Text = "\uE702";
            SetStatusBadgeColors(c);
        }
        else
        {
            StatusIconText.Foreground = new SolidColorBrush(MediaColor.FromRgb(48, 209, 88));
            StatusIconText.Text = "\uE930"; // Checkmark
            SetStatusBadgeColors(MediaColor.FromRgb(48, 209, 88));
        }
    }

    private void ShowMediaContent(IslandEvent evt, IslandViewPresentation view)
    {
        // Compact title line: source name · artist, or song title as fallback
        var sourceLabel = string.IsNullOrWhiteSpace(view.SourceName) ? "" : view.SourceName;
        var subtitleLabel = string.IsNullOrWhiteSpace(view.Subtitle) ? "" : view.Subtitle;
        TitleText.Text = string.IsNullOrWhiteSpace(sourceLabel)
            ? view.Content
            : string.IsNullOrWhiteSpace(subtitleLabel)
                ? sourceLabel
                : $"{sourceLabel} · {subtitleLabel}";

        ProgressBarPanel.Visibility = Visibility.Visible;

        AccessoryGrid.Visibility = Visibility.Visible;
        AudioWavePanel.Visibility = Visibility.Visible;
        AudioWavePanel.Opacity = view.ShowsAudioWave ? 1 : 0.42;
        if (view.ShowsAudioWave)
            _waveTimer.Start();
        else
            SetWaveHeights(5, 5, 5, 5, TimeSpan.FromMilliseconds(180));

        var maxBarWidth = Math.Max(150, view.TargetWidth - 154);
        ProgressTrack.Width = maxBarWidth;
        var targetWidth = Math.Max(0, view.ProgressPercent / 100.0 * maxBarWidth);
        ProgressFill.BeginAnimation(System.Windows.Controls.Border.WidthProperty, null);
        ProgressFill.Width = targetWidth;
        ProgressFill.Background = new LinearGradientBrush(
            MediaColor.FromRgb(255, 45, 85), MediaColor.FromRgb(255, 149, 0), 0);
    }

    private void ShowRichStatusContent(IslandEvent evt, IslandViewPresentation view)
    {
        TitleText.Text = evt.Title;
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = view.StatusText;
        StatusBadgeText.Text = view.StatusBadge;

        var color = IconColors.TryGetValue(view.IconKind, out var c)
            ? c
            : MediaColor.FromRgb(10, 132, 255);
        StatusIconText.Foreground = new SolidColorBrush(color);
        StatusIconText.Text = view.Kind == IslandViewKind.Agent ? "\uE930" : "\uE7F4";
        SetStatusBadgeColors(color);
    }

    private void SetStatusBadgeColors(MediaColor color)
    {
        StatusBadgeBorder.Background = new SolidColorBrush(
            MediaColor.FromArgb(38, color.R, color.G, color.B));
        StatusBadgeBorder.BorderBrush = new SolidColorBrush(
            MediaColor.FromArgb(70, color.R, color.G, color.B));
        StatusBadgeText.Foreground = new SolidColorBrush(
            MediaColor.FromArgb(238,
                (byte)Math.Min(255, color.R + 90),
                (byte)Math.Min(255, color.G + 90),
                (byte)Math.Min(255, color.B + 90)));
    }

    private void ShowLockKeyIndicator(IslandEvent evt)
    {
        LockKeyPanel.Visibility = Visibility.Visible;
        LockKeyText.Text = evt.Title;
        LockKeyStatus.Text = evt.Content.Contains("ON") ? "开" : "关";

        var isOn = evt.Content.Contains("ON");
        var targetColor = isOn
            ? MediaColor.FromRgb(52, 199, 89)
            : MediaColor.FromRgb(58, 58, 60);

        LedColor.Color = targetColor;
        LedShadow.Color = targetColor;
        LedShadow.Opacity = isOn ? 0.8 : 0;
    }

    private void ShowImeIndicator(IslandEvent evt)
    {
        ImePanel.Visibility = Visibility.Visible;
        ImeText.Text = evt.Content;

        var isChinese = evt.Content == "中";
        ImeBadgeColor.Color = isChinese
            ? MediaColor.FromRgb(10, 132, 255)
            : MediaColor.FromRgb(142, 142, 147);
        ImeBorderColor.Color = isChinese
            ? MediaColor.FromArgb(50, 255, 255, 255)
            : MediaColor.FromArgb(30, 255, 255, 255);
    }

    private void ShowClockContent(IslandEvent evt)
    {
        TitleText.Text = evt.Title;
        ContentText.Visibility = Visibility.Visible;
        ContentText.Text = evt.Content; // e.g. "6月14日 周日"
    }

    private void ShowTextContent(IslandEvent evt, IslandViewPresentation view)
    {
        TitleText.Text = evt.Title;

        if (view.Kind == IslandViewKind.ScrollingText)
        {
            ScrollCanvas.Visibility = Visibility.Visible;
            ScrollCanvas.Width = Math.Max(160, view.TargetWidth - 118);
            ScrollText.Text = evt.Content;
            Dispatcher.BeginInvoke(() =>
            {
                var canvasWidth = ScrollCanvas.ActualWidth > 0
                    ? ScrollCanvas.ActualWidth
                    : ScrollCanvas.Width;
                StartScrolling(canvasWidth);
            }, DispatcherPriority.Background);
        }
        else
        {
            ContentText.Visibility = Visibility.Visible;
            ContentText.Text = evt.Content;
        }
    }

    private void ShowIdleClock()
    {
        ClearIslandStack(animated: true);
        var now = DateTime.Now;
        var evt = new IslandEvent(
            "clock",
            now.ToString("HH:mm"),
            now.ToString("M月d日 dddd"),
            "clock");
        var view = IslandPresentation.FromEvent(evt, _settings);

        _currentView = view;
        _currentSource = evt.Source;
        _lastEvent = evt;
        UpdateIcon(view.IconKind);
        HideAllPanels();
        TitleText.Text = evt.Title;
        ContentText.Visibility = Visibility.Visible;
        ContentText.Text = evt.Content;

        // 如果是首次显示（未展开），触发完整展开动画
        if (!_isExpanded)
        {
            _isExpanded = true;
            MorphToView(view, opening: true);
        }
        else
        {
            // 已展开：平滑更新内容
            ContentPanel.BeginAnimation(OpacityProperty, null);
            var fadeIn = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ContentPanel.BeginAnimation(OpacityProperty, fadeIn);
            MorphToView(view);
        }
    }

    private void UpdateIcon(string? iconKind)
    {
        var kind = iconKind ?? "info";
        IconText.Text = IconGlyphs.TryGetValue(kind, out var g) ? g : IconGlyphs["info"];

        var bgColor = IconColors.TryGetValue(kind, out var c) ? c : IconColors["info"];
        AnimateBrushColor(IconBackground, bgColor, 220);
        AnimateBrushColor(IconPulseBrush, bgColor, 220);

        var glowColor = GlowColors.TryGetValue(kind, out var gc) ? gc : GlowColors["info"];
        IconGlow.BeginAnimation(DropShadowEffect.ColorProperty,
            new ColorAnimation(glowColor, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        IconGlow.BeginAnimation(DropShadowEffect.OpacityProperty,
            new DoubleAnimation(kind == "clock" ? 0.22 : 0.5, TimeSpan.FromMilliseconds(220)));

        // 图标没变时跳过弹跳动画（避免 AlwaysVisible 时钟每隔几秒跳一下）
        if (kind == _currentIconKind) return;
        _currentIconKind = kind;

        // 图标切换时播放精致缩放过渡
        IconScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        IconScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        IconScale.ScaleX = 0.72;
        IconScale.ScaleY = 0.72;

        var scaleAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new ElasticEase
            {
                EasingMode = EasingMode.EaseOut,
                Oscillations = 2,
                Springiness = 5
            }
        };
        IconScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        IconScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

        IconPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        IconPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        IconPulse.BeginAnimation(OpacityProperty, null);
        IconPulseScale.ScaleX = 0.8;
        IconPulseScale.ScaleY = 0.8;
        IconPulse.Opacity = 0.34;
        IconPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.55, TimeSpan.FromMilliseconds(460))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            });
        IconPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1.55, TimeSpan.FromMilliseconds(460))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            });
        IconPulse.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(460))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private static void AnimateBrushColor(SolidColorBrush brush, MediaColor color, int milliseconds)
    {
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(color, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    // ===========================================================
    // 展开 / 收缩 动画
    // ===========================================================

    private void ExpandWithContent(string text, string? iconKind = null)
    {
        ClearIslandStack(animated: true);
        var evt = new IslandEvent("app", "FluidBar", text, iconKind ?? "info");
        var view = IslandPresentation.FromEvent(evt, _settings);
        _currentView = view;
        _currentSource = evt.Source;
        _lastEvent = evt;
        _isExpanded = true;

        UpdateIcon(view.IconKind);

        HideAllPanels();
        ShowTextContent(evt, view);
        MorphToView(view, opening: true);

        _collapseTimer.Stop();
    }

    private void Expand(IslandViewPresentation view)
    {
        _isExpanded = true;
        MorphToView(view, opening: true);

        ResetCollapseTimer();
    }

    private void MorphToView(IslandViewPresentation view, bool opening = false)
    {
        StopHoverSpring();
        _activeTargetWidth = view.TargetWidth;
        _activeTargetHeight = view.TargetHeight;
        if (!TryCalculateStackedMainPosition(
                _activeTargetWidth,
                _activeTargetHeight,
                out double tl,
                out double tt,
                out var layout))
        {
            CalculatePosition(_activeTargetWidth, _activeTargetHeight,
                out tl, out tt);
        }

        SyncSnapshotWindows(layout, animated: !opening);

        AnimateShell(_activeTargetWidth, _activeTargetHeight, tl, tt, opening);
        AnimateContentIn(opening);

        if (!opening)
            ResetCollapseTimer();
    }

    private void AnimateShell(double tw, double th, double tl, double tt, bool opening)
    {
        ClearPositionAnimations();

        var duration = opening
            ? TimeSpan.FromMilliseconds(620)
            : TimeSpan.FromMilliseconds(380);
        var sizeEase = opening
            ? (IEasingFunction)new ElasticEase
            {
                EasingMode = EasingMode.EaseOut,
                Oscillations = 1,
                Springiness = 4
            }
            : new BackEase
            {
                EasingMode = EasingMode.EaseOut,
                Amplitude = 0.22
            };
        var positionEase = new QuarticEase { EasingMode = EasingMode.EaseOut };

        AnimateProperty(WidthProperty, ToWindowSize(tw), duration, sizeEase);
        AnimateProperty(HeightProperty, ToWindowSize(th), duration, sizeEase);
        AnimateProperty(LeftProperty, tl, TimeSpan.FromMilliseconds(420), positionEase);
        AnimateProperty(TopProperty, tt, TimeSpan.FromMilliseconds(420), positionEase);

        PillBorder.BeginAnimation(OpacityProperty,
            new DoubleAnimation(_settings.Opacity, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        OuterBloom.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.62, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PillSkew.BeginAnimation(SkewTransform.AngleXProperty, null);

        PillScale.ScaleX = opening ? 0.78 : 1.035;
        PillScale.ScaleY = opening ? 1.12 : 0.985;
        PillSkew.AngleX = opening ? -1.8 : 0.7;

        var spring = new ElasticEase
        {
            EasingMode = EasingMode.EaseOut,
            Oscillations = opening ? 2 : 1,
            Springiness = opening ? 4 : 5
        };

        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(opening ? 680 : 420))
            {
                EasingFunction = spring
            });
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(opening ? 680 : 420))
            {
                EasingFunction = spring
            });
        PillSkew.BeginAnimation(SkewTransform.AngleXProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void AnimateContentIn(bool opening)
    {
        ContentPanel.BeginAnimation(OpacityProperty, null);
        ContentTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        ContentPanel.Opacity = opening ? 0 : 0.52;
        ContentTranslate.Y = opening ? 8 : 3;

        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(opening ? 300 : 210))
        {
            BeginTime = TimeSpan.FromMilliseconds(opening ? 90 : 30),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(opening ? 360 : 230))
        {
            BeginTime = TimeSpan.FromMilliseconds(opening ? 70 : 20),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };

        ContentPanel.BeginAnimation(OpacityProperty, fade);
        ContentTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void Collapse()
    {
        if (!_isExpanded || _settingsPanelOpen || _settings.AlwaysVisible) return;

        _isExpanded = false;
        _isHoverCard = false;
        _currentIconKind = null; // 收起后重置，下次展开时动画正常播放
        StopScrolling();
        StopHoverSpring();
        PillBorder.MaxWidth = _settings.ExpandedMaxWidth;
        HoverCardGrid.Visibility = Visibility.Collapsed;
        HoverCardGrid.Opacity = 0;
        IslandContent.Opacity = 1;

        ClearPositionAnimations();

        var collapseDur = TimeSpan.FromMilliseconds(340);
        var easeOut = new QuarticEase { EasingMode = EasingMode.EaseInOut };

        ClearIslandStack(animated: true);
        CalculatePosition(_settings.CollapsedWidth, _settings.CollapsedHeight,
            out double tl, out double tt);

        AnimateProperty(WidthProperty, ToWindowSize(_settings.CollapsedWidth), collapseDur, easeOut);
        AnimateProperty(HeightProperty, ToWindowSize(_settings.CollapsedHeight), collapseDur, easeOut);
        AnimateProperty(LeftProperty, tl, collapseDur, easeOut);
        AnimateProperty(TopProperty, tt, collapseDur, easeOut);

        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.88, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.76, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });

        ContentPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(105)));
        OuterBloom.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(190))
        {
            BeginTime = TimeSpan.FromMilliseconds(80)
        };
        fade.Completed += (_, _) =>
        {
            PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            PillScale.ScaleX = 1;
            PillScale.ScaleY = 1;
            PillSkew.AngleX = 0;
            HideAllPanels();
        };
        PillBorder.BeginAnimation(OpacityProperty, fade);
    }

    // ===========================================================
    // 广告牌滚动文字
    // ===========================================================

    private double _scrollOffset;
    private DateTime _scrollHoldUntil = DateTime.MinValue;

    private void StartScrolling(double canvasWidth)
    {
        var plan = ScrollingTextMotionPlan.CreateInitial();
        _scrollOffset = plan.InitialOffset;
        _scrollHoldUntil = DateTime.UtcNow.AddMilliseconds(plan.HoldMilliseconds);
        ScrollText.RenderTransform = new TranslateTransform(_scrollOffset, 0);
        _scrollTimer.Start();
    }

    private void StopScrolling() => _scrollTimer.Stop();

    private void ScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (ScrollText.ActualWidth <= 0) return;
        if (DateTime.UtcNow < _scrollHoldUntil) return;

        var speed = _clipboardPluginSettings?.ScrollSpeed ?? 2.0;
        _scrollOffset -= speed;

        if (_scrollOffset < -ScrollText.ActualWidth)
            _scrollOffset = ScrollCanvas.ActualWidth > 0 ? ScrollCanvas.ActualWidth : 240;

        ScrollText.RenderTransform = new TranslateTransform(_scrollOffset, 0);
    }

    private void WaveTimer_Tick(object? sender, EventArgs e)
    {
        _wavePhase += 0.42;
        var h1 = 7 + Math.Sin(_wavePhase) * 4;
        var h2 = 12 + Math.Sin(_wavePhase + 1.4) * 7;
        var h3 = 8 + Math.Sin(_wavePhase + 2.6) * 5;
        var h4 = 11 + Math.Sin(_wavePhase + 3.5) * 6;
        SetWaveHeights(h1, h2, h3, h4, TimeSpan.FromMilliseconds(90));
    }

    private void SetWaveHeights(double h1, double h2, double h3, double h4, TimeSpan duration)
    {
        AnimateWaveBar(Wave1, h1, duration);
        AnimateWaveBar(Wave2, h2, duration);
        AnimateWaveBar(Wave3, h3, duration);
        AnimateWaveBar(Wave4, h4, duration);
    }

    private static void AnimateWaveBar(Border bar, double height, TimeSpan duration)
    {
        bar.BeginAnimation(HeightProperty,
            new DoubleAnimation(Math.Clamp(height, 4, 22), duration)
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
    }

    private void AnimateProperty(DependencyProperty prop, double to, Duration dur,
        IEasingFunction easing)
    {
        BeginAnimation(prop, new DoubleAnimation(to, dur) { EasingFunction = easing });
    }
}
