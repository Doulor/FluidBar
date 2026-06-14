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
        catch { PillBackground.Color = MediaColor.FromArgb(0xE6, 0x00, 0x00, 0x00); }

        try
        {
            var accentColor = (MediaColor)MediaColorConverter.ConvertFromString(_settings.AccentColor);
            IconBackground.Color = accentColor;
            PillRimBrush.Color = MediaColor.FromArgb(25, accentColor.R, accentColor.G, accentColor.B);
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

        // 时钟事件：已展开时静默更新，未展开时忽略
        if (evt.IconKind == "clock")
        {
            if (!_isExpanded) return;
            TitleText.Text = evt.Title;
            ContentText.Text = evt.Content;
            return;
        }

        if (!_isExpanded)
            Expand();
        else
        {
            RefreshDisplay();
            // 微动弹性：仅离散事件触发（进度类高频事件跳过）
            if (evt.IconKind is not ("volume" or "volume_mute" or "brightness"))
                NudgePill();
        }
    }

    /// <summary>微动弹性 — 新事件到达时给药丸一个微小的缩放脉冲</summary>
    private void NudgePill()
    {
        var currentTransform = PillBorder.RenderTransform as ScaleTransform;
        var startScale = currentTransform != null
            ? currentTransform.ScaleX
            : 1.0;

        // 动画在自身基础上缩放，不替换 RenderTransform
        PillBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        var st = PillBorder.RenderTransform as ScaleTransform
                 ?? new ScaleTransform(startScale, startScale);

        if (!ReferenceEquals(st, PillBorder.RenderTransform))
            PillBorder.RenderTransform = st;

        st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        st.ScaleX = startScale;
        st.ScaleY = startScale;

        var nudgeAnim = new DoubleAnimation(startScale * 0.97, startScale, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new ElasticEase
            {
                EasingMode = EasingMode.EaseOut,
                Oscillations = 1,
                Springiness = 3
            }
        };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, nudgeAnim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, nudgeAnim);
    }

    /// <summary>已展开时刷新内容（柔和的淡入过渡 + 重置隐藏计时器）</summary>
    private void RefreshDisplay()
    {
        // 清除旧动画
        ContentPanel.BeginAnimation(OpacityProperty, null);
        ContentPanel.RenderTransform = null;

        var fadeOverlay = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ContentPanel.BeginAnimation(OpacityProperty, fadeOverlay);

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

        // 进度条容器宽度 = Pill宽 - 左右padding(36) - 图标(32) - 图标margin(12)
        var maxBarWidth = Math.Max(80, PillBorder.ActualWidth - 80);
        var targetWidth = Math.Max(0, percent / 100.0 * maxBarWidth);

        // 从上一值动画到新值（避免从0跳起）
        var currentWidth = ProgressFill.Width;
        ProgressFill.BeginAnimation(System.Windows.Controls.Border.WidthProperty, null);
        ProgressFill.Width = currentWidth;

        var isIncreasing = targetWidth > currentWidth;
        var duration = isIncreasing
            ? TimeSpan.FromMilliseconds(180)
            : TimeSpan.FromMilliseconds(280);
        var ease = isIncreasing
            ? (IEasingFunction)new CubicEase { EasingMode = EasingMode.EaseOut }
            : new QuadraticEase { EasingMode = EasingMode.EaseOut };

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

        // 如果是首次显示（未展开），触发完整展开动画
        if (!_isExpanded)
        {
            _isExpanded = true;
            var targetWidth = Math.Min(_settings.ExpandedMaxWidth,
                Math.Max(_settings.CollapsedWidth, 200));
            CalculatePosition(targetWidth, _settings.ExpandedHeight,
                out double tl, out double tt);
            AnimateExpand(targetWidth, _settings.ExpandedHeight, tl, tt);
        }
        else
        {
            // 已展开：平滑更新内容
            ContentPanel.BeginAnimation(OpacityProperty, null);
            ContentPanel.RenderTransform = null;
            var fadeIn = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ContentPanel.BeginAnimation(OpacityProperty, fadeIn);
        }
    }

    private void UpdateIcon(string? iconKind)
    {
        var kind = iconKind ?? "info";
        IconText.Text = IconGlyphs.TryGetValue(kind, out var g) ? g : IconGlyphs["info"];

        var bgColor = IconColors.TryGetValue(kind, out var c) ? c : IconColors["info"];
        IconBackground.Color = bgColor;

        var glowColor = GlowColors.TryGetValue(kind, out var gc) ? gc : GlowColors["info"];
        IconGlow.Color = glowColor;

        // 图标精致缩放过渡 — 微妙的弹性
        var scaleAnim = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new ElasticEase
            {
                EasingMode = EasingMode.EaseOut,
                Oscillations = 1,
                Springiness = 3
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

    /// <summary>
    /// 苹果灵动岛风格展开动画。
    /// 借鉴 SpringValue 自适应刚度：扩大时更快/更紧，缩小时更慢/更柔。
    /// 单次连续缓动替代分阶段动画，更自然流畅。
    /// </summary>
    private void AnimateExpand(double tw, double th, double tl, double tt)
    {
        ClearPositionAnimations();

        // 主尺寸动画：CubicEaseOut 模拟弹簧快速响应 + 平滑减速
        var expandDur = TimeSpan.FromMilliseconds(350);
        var expandEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        AnimateProperty(WidthProperty, tw, expandDur, expandEase);
        AnimateProperty(HeightProperty, th, expandDur, expandEase);
        AnimateProperty(LeftProperty, tl, expandDur, expandEase);
        AnimateProperty(TopProperty, tt, expandDur, expandEase);

        // PillBorder 透明度快速淡入
        PillBorder.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));

        // Pill 整体缩放：BackEase 提供微妙的弹性（仅一次回弹，不振荡）
        var pillScale = new ScaleTransform(0.94, 0.94);
        PillBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        PillBorder.RenderTransform = pillScale;
        var scaleAnim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }
        };
        pillScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        pillScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

        // 内容在 pill 形变开始后 60ms 渐进淡入（形状先行，内容后随）
        ContentPanel.BeginAnimation(OpacityProperty, null);
        ContentPanel.Opacity = 0;
        var fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        fadeTimer.Tick += (_, _) =>
        {
            fadeTimer.Stop();
            if (!_isExpanded) return;
            ContentPanel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        };
        fadeTimer.Start();
    }

    /// <summary>
    /// 收起动画。借鉴 SpringValue 的低刚度/高阻尼策略：缓慢平滑地收缩。
    /// </summary>
    private void Collapse()
    {
        if (!_isExpanded || _settingsPanelOpen || _settings.AlwaysVisible) return;

        _isExpanded = false;
        StopScrolling();

        ClearPositionAnimations();

        var collapseDur = TimeSpan.FromMilliseconds(280);
        var easeInOut = new CircleEase { EasingMode = EasingMode.EaseInOut };

        CalculatePosition(_settings.CollapsedWidth, _settings.CollapsedHeight,
            out double tl, out double tt);

        AnimateProperty(WidthProperty, _settings.CollapsedWidth, collapseDur, easeInOut);
        AnimateProperty(HeightProperty, _settings.CollapsedHeight, collapseDur, easeInOut);
        AnimateProperty(LeftProperty, tl, collapseDur, easeInOut);
        AnimateProperty(TopProperty, tt, collapseDur, easeInOut);

        // Pill 透明度平滑消失 — 延迟于尺寸动画，避免突兀
        PillBorder.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));

        // 内容先于 pill 淡出
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
