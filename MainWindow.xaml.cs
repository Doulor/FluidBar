using System.Windows;
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
    private bool _isExpanded;
    private bool _settingsPanelOpen;

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
        ["info"]          = MediaColor.FromRgb(142, 142, 147),
    };

    private static readonly Dictionary<string, MediaColor> GlowColors = new()
    {
        ["clipboard"]     = MediaColor.FromArgb(76, 10, 132, 255),
        ["volume"]        = MediaColor.FromArgb(76, 10, 132, 255),
        ["battery"]       = MediaColor.FromArgb(76, 48, 209, 88),
        ["battery_low"]   = MediaColor.FromArgb(100, 255, 69, 58),
        ["inputmethod"]   = MediaColor.FromArgb(76, 10, 132, 255),
        ["lockkey"]       = MediaColor.FromArgb(76, 191, 90, 242),
        ["network"]       = MediaColor.FromArgb(76, 48, 209, 88),
        ["network_off"]   = MediaColor.FromArgb(76, 255, 69, 58),
        ["usb"]           = MediaColor.FromArgb(76, 255, 159, 10),
        ["brightness"]    = MediaColor.FromArgb(100, 255, 214, 10),
        ["bluetooth"]     = MediaColor.FromArgb(76, 10, 132, 255),
        ["clock"]         = MediaColor.FromArgb(50, 142, 142, 147),
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
    }

    public void SetClipboardPluginSettings(ClipboardPluginSettings s)
    {
        _clipboardPluginSettings = s;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        ApplySettings();
        _bus.EventTriggered += OnEventTriggered;

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

    public void OnSettingsPanelOpened()
    {
        _settingsPanelOpen = true;
        _collapseTimer.Stop();
        _scrollTimer.Stop();

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
        if (!_settings.AlwaysVisible)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    public void ApplySettings()
    {
        PillBorder.CornerRadius = new CornerRadius(_settings.CornerRadius);
        PillBorder.Opacity = _settings.Opacity;

        try
        {
            PillBackground.Color =
                (MediaColor)MediaColorConverter.ConvertFromString(_settings.BackgroundColor);
        }
        catch { PillBackground.Color = MediaColor.FromArgb(0xE0, 0x20, 0x20, 0x22); }

        try
        {
            IconBackground.Color =
                (MediaColor)MediaColorConverter.ConvertFromString(_settings.AccentColor);
        }
        catch { IconBackground.Color = MediaColor.FromRgb(10, 132, 255); }

        PillBorder.MinWidth = _settings.CollapsedWidth;
        PillBorder.MaxWidth = _settings.ExpandedMaxWidth;
        Topmost = _settings.AlwaysOnTop;
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(_settings.AutoHideDelayMs);

        // 设置面板打开时实时更新位置（清除动画后用当前尺寸定位）
        if (_settingsPanelOpen)
        {
            ClearPositionAnimations();
            PositionAtCurrentSize();
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

    public void PositionWindow()
    {
        CalculatePosition(_settings.CollapsedWidth, _settings.CollapsedHeight,
            out double x, out double y);
        ClearPositionAnimations();
        Left = x;
        Top = y;
        Width = _settings.CollapsedWidth;
        Height = _settings.CollapsedHeight;
    }

    /// <summary>设置面板打开时用当前实际尺寸定位，清除动画后直接设值</summary>
    private void PositionAtCurrentSize()
    {
        var w = _isExpanded ? ActualWidth : _settings.CollapsedWidth;
        var h = _isExpanded ? ActualHeight : _settings.CollapsedHeight;
        if (w < 10) w = _settings.CollapsedWidth;
        if (h < 10) h = _settings.CollapsedHeight;

        CalculatePosition(w, h, out double x, out double y);
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
        UpdateIcon(evt.IconKind);
        HideAllPanels();

        switch (evt.IconKind)
        {
            case "volume":
            case "volume_mute":
            case "brightness":
                ShowProgressBar(evt);
                break;
            case "battery":
            case "battery_charge":
            case "battery_low":
            case "network":
            case "network_off":
            case "usb":
            case "bluetooth":
                ShowStatusIndicator(evt);
                break;
            case "lockkey":
                ShowLockKeyIndicator(evt);
                break;
            case "inputmethod":
                ShowImeIndicator(evt);
                break;
            case "clock":
                ShowClockContent(evt);
                break;
            default:
                ShowTextContent(evt);
                break;
        }

        if (!_isExpanded)
            Expand();
        else
            RefreshDisplay();
    }

    /// <summary>已展开时刷新内容（弹入动画 + 重置隐藏计时器）</summary>
    private void RefreshDisplay()
    {
        // 清除旧动画占用
        ContentPanel.BeginAnimation(OpacityProperty, null);
        ContentPanel.Opacity = 0;
        ContentPanel.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        ContentPanel.RenderTransform = new ScaleTransform(0.95, 0.95);

        ContentPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        var s = (ScaleTransform)ContentPanel.RenderTransform;
        s.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(300))
            { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 3 } });
        s.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(300))
            { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 3 } });

        ResetCollapseTimer();
    }

    /// <summary>重置自动隐藏计时器</summary>
    private void ResetCollapseTimer()
    {
        if (!_settings.AlwaysVisible)
        {
            var d = _clipboardPluginSettings?.DisplayDurationMs ?? 3000;
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(d);
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void HideAllPanels()
    {
        ContentText.Visibility = Visibility.Collapsed;
        ProgressBarPanel.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Collapsed;
        LockKeyPanel.Visibility = Visibility.Collapsed;
        ImePanel.Visibility = Visibility.Collapsed;
        ScrollCanvas.Visibility = Visibility.Collapsed;
    }

    private void ShowProgressBar(IslandEvent evt)
    {
        var percent = ParsePercent(evt.Content);
        TitleText.Text = evt.Title;
        ProgressBarPanel.Visibility = Visibility.Visible;

        var maxBarWidth = Math.Min(220, _settings.ExpandedMaxWidth - 100);
        ProgressFill.Width = Math.Max(0, percent / 100.0 * maxBarWidth);

        if (evt.IconKind == "brightness")
        {
            ProgressFill.Background = new LinearGradientBrush(
                MediaColor.FromRgb(255, 214, 10), MediaColor.FromRgb(255, 179, 0), 0);
            var s = (DropShadowEffect)ProgressFill.Effect;
            s.Color = MediaColor.FromRgb(255, 214, 10);
            s.Opacity = 0.5;
        }
        else if (evt.IconKind == "volume_mute")
        {
            ProgressFill.Background = new LinearGradientBrush(
                MediaColor.FromRgb(142, 142, 147), MediaColor.FromRgb(99, 99, 102), 0);
            var s = (DropShadowEffect)ProgressFill.Effect;
            s.Color = MediaColor.FromRgb(142, 142, 147);
            s.Opacity = 0.2;
        }
        else
        {
            ProgressFill.Background = new LinearGradientBrush(
                MediaColor.FromRgb(10, 132, 255), MediaColor.FromRgb(90, 200, 250), 0);
            var s = (DropShadowEffect)ProgressFill.Effect;
            s.Color = MediaColor.FromRgb(10, 132, 255);
            s.Opacity = 0.5;
        }
    }

    private void ShowStatusIndicator(IslandEvent evt)
    {
        TitleText.Text = evt.Title;
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = evt.Content;

        var isError = evt.IconKind is "battery_low" or "network_off";
        if (isError)
        {
            StatusIconText.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 69, 58));
            StatusIconText.Text = "\uE711";
        }
        else
        {
            StatusIconText.Foreground = new SolidColorBrush(MediaColor.FromRgb(48, 209, 88));
            StatusIconText.Text = "\uE930";
        }
    }

    private void ShowLockKeyIndicator(IslandEvent evt)
    {
        LockKeyPanel.Visibility = Visibility.Visible;
        LockKeyText.Text = evt.Title;
        LockKeyStatus.Text = evt.Content.Contains("ON") ? "开" : "关";

        var isOn = evt.Content.Contains("ON");
        LedColor.Color = isOn
            ? MediaColor.FromRgb(52, 199, 89)
            : MediaColor.FromRgb(58, 58, 60);

        LedShadow.Color = isOn
            ? MediaColor.FromRgb(52, 199, 89)
            : MediaColor.FromRgb(58, 58, 60);
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

    private void ShowTextContent(IslandEvent evt)
    {
        TitleText.Text = evt.Title;
        var minChars = _clipboardPluginSettings?.MinFullDisplayChars ?? 20;

        if (evt.Content.Length > minChars)
        {
            ScrollCanvas.Visibility = Visibility.Visible;
            ScrollText.Text = evt.Content;
        }
        else
        {
            ContentText.Visibility = Visibility.Visible;
            ContentText.Text = evt.Content;
        }
    }

    private void ShowIdleClock()
    {
        var now = DateTime.Now;
        UpdateIcon("clock");
        HideAllPanels();
        TitleText.Text = now.ToString("HH:mm");
        ContentText.Visibility = Visibility.Visible;
        ContentText.Text = now.ToString("M月d日 dddd");
    }

    private void UpdateIcon(string? iconKind)
    {
        var kind = iconKind ?? "info";
        IconText.Text = IconGlyphs.TryGetValue(kind, out var g) ? g : IconGlyphs["info"];

        var bgColor = IconColors.TryGetValue(kind, out var c) ? c : IconColors["info"];
        IconBackground.Color = bgColor;

        var glowColor = GlowColors.TryGetValue(kind, out var gc) ? gc : GlowColors["info"];
        IconGlow.Color = glowColor;

        var scaleAnim = new DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new ElasticEase
            {
                EasingMode = EasingMode.EaseOut,
                Oscillations = 2,
                Springiness = 4
            }
        };
        IconScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        IconScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private static int ParsePercent(string content)
    {
        var match = System.Text.RegularExpressions.Regex.Match(content, @"(\d+)%");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var p))
            return Math.Clamp(p, 0, 100);
        return 0;
    }

    // ===========================================================
    // 展开 / 收缩 动画
    // ===========================================================

    private void ExpandWithContent(string text, string? iconKind = null)
    {
        _isExpanded = true;
        StopScrolling();

        if (iconKind != null) UpdateIcon(iconKind);
        HideAllPanels();
        ContentText.Visibility = Visibility.Visible;
        ContentText.Text = text;
        TitleText.Text = "FluidBar";

        var targetWidth = Math.Min(_settings.ExpandedMaxWidth,
            Math.Max(_settings.CollapsedWidth, 280));
        CalculatePosition(targetWidth, _settings.ExpandedHeight,
            out double tl, out double tt);
        AnimateExpand(targetWidth, _settings.ExpandedHeight, tl, tt);
        _collapseTimer.Stop();
    }

    private void Expand()
    {
        _isExpanded = true;
        StopScrolling();

        var targetWidth = Math.Min(_settings.ExpandedMaxWidth,
            Math.Max(_settings.CollapsedWidth, 260));
        CalculatePosition(targetWidth, _settings.ExpandedHeight,
            out double tl, out double tt);
        AnimateExpand(targetWidth, _settings.ExpandedHeight, tl, tt);

        ResetCollapseTimer();
    }

    private void AnimateExpand(double tw, double th, double tl, double tt)
    {
        ClearPositionAnimations();

        var dur = TimeSpan.FromMilliseconds(350);
        var spring = new ExponentialEase { EasingMode = EasingMode.EaseOut };

        AnimateProperty(WidthProperty, tw, dur, spring);
        AnimateProperty(HeightProperty, th, dur, spring);
        AnimateProperty(LeftProperty, tl, dur, spring);
        AnimateProperty(TopProperty, tt, dur, spring);

        PillBorder.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));

        var pillScale = new ScaleTransform(0.96, 0.96);
        PillBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        PillBorder.RenderTransform = pillScale;
        pillScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(400))
            { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 3 } });
        pillScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(400))
            { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 3 } });

        ContentPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
    }

    private void Collapse()
    {
        if (!_isExpanded || _settingsPanelOpen || _settings.AlwaysVisible) return;

        _isExpanded = false;
        StopScrolling();

        ClearPositionAnimations();

        var collapseDur = TimeSpan.FromMilliseconds(280);
        var easeIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };

        CalculatePosition(_settings.CollapsedWidth, _settings.CollapsedHeight,
            out double tl, out double tt);

        AnimateProperty(WidthProperty, _settings.CollapsedWidth, collapseDur, easeIn);
        AnimateProperty(HeightProperty, _settings.CollapsedHeight, collapseDur, easeIn);
        AnimateProperty(LeftProperty, tl, collapseDur, easeIn);
        AnimateProperty(TopProperty, tt, collapseDur, easeIn);

        PillBorder.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)));
        ContentPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
    }

    // ===========================================================
    // 广告牌滚动文字
    // ===========================================================

    private double _scrollOffset;

    private void StartScrolling(double canvasWidth)
    {
        _scrollOffset = canvasWidth;
        ScrollText.RenderTransform = new TranslateTransform(_scrollOffset, 0);
        _scrollTimer.Start();
    }

    private void StopScrolling() => _scrollTimer.Stop();

    private void ScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (ScrollText.ActualWidth <= 0) return;
        var speed = _clipboardPluginSettings?.ScrollSpeed ?? 2.0;
        _scrollOffset -= speed;

        if (_scrollOffset < -ScrollText.ActualWidth)
            _scrollOffset = ScrollCanvas.ActualWidth > 0 ? ScrollCanvas.ActualWidth : 240;

        ScrollText.RenderTransform = new TranslateTransform(_scrollOffset, 0);
    }

    private void AnimateProperty(DependencyProperty prop, double to, Duration dur,
        IEasingFunction easing)
    {
        BeginAnimation(prop, new DoubleAnimation(to, dur) { EasingFunction = easing });
    }
}
