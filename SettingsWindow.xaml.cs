using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Win32;
using WpfMessageBox = System.Windows.MessageBox;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using FluidBar.Monitors;
using MediaColor = System.Windows.Media.Color;

namespace FluidBar;

public partial class SettingsWindow : Window
{
    private readonly FluidBarSettings _settings;
    private readonly PluginManager _pluginManager;
    private readonly SystemMonitorManager _monitorManager;
    private readonly Action _onSettingsChanged;
    private bool _isLoading;
    private IIslandPlugin? _detailPlugin;
    private ISystemMonitor? _detailMonitor;
    private int _detailTransitionToken;
    private readonly DispatcherTimer _settingsApplyTimer;
    private readonly DispatcherTimer _settingsSaveTimer;
    private readonly DispatcherTimer _pluginSettingsSaveTimer;
    private IPluginConfig? _pendingPluginConfig;
    private const string StartupRegistryKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "FluidBar";

    // 位置预览 Border 引用
    private Dictionary<string, Border>? _previewBorders;

    // 当前主题: "Light" 或 "Dark"
    private string _currentTheme = "Light";
    private bool _isDark => _currentTheme == "Dark";

    public event Action<bool>? TrayIconVisibilityChanged;

    public SettingsWindow(FluidBarSettings settings, PluginManager pluginManager,
        SystemMonitorManager monitorManager, Action onSettingsChanged)
    {
        _isLoading = true;
        _settings = settings;
        _pluginManager = pluginManager;
        _monitorManager = monitorManager;
        _onSettingsChanged = onSettingsChanged;
        _settingsApplyTimer = CreateOneShotTimer(
            SettingsPerformancePolicy.SettingsApplyDebounceMs,
            () => _onSettingsChanged?.Invoke());
        _settingsSaveTimer = CreateOneShotTimer(
            SettingsPerformancePolicy.SettingsSaveDebounceMs,
            () =>
            {
                _settings.Save();
                ScheduleSettingsChanged();
            });
        _pluginSettingsSaveTimer = CreateOneShotTimer(
            SettingsPerformancePolicy.PluginSaveDebounceMs,
            () =>
            {
                _pendingPluginConfig?.Save();
                ScheduleSettingsChanged();
            });
        InitializeComponent();
        IsVisibleChanged += SettingsWindow_IsVisibleChanged;
    }

    private static DispatcherTimer CreateOneShotTimer(int milliseconds, Action tick)
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            tick();
        };
        return timer;
    }

    private void ScheduleSettingsChanged()
    {
        _settingsApplyTimer.Stop();
        _settingsApplyTimer.Start();
    }

    private void ScheduleSettingsSaveAndChanged()
    {
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void SchedulePluginSettingsSave(IPluginConfig config)
    {
        _pendingPluginConfig = config;
        _pluginSettingsSaveTimer.Stop();
        _pluginSettingsSaveTimer.Start();
    }

    private void FlushPendingSettingsWork()
    {
        var needsApply = _settingsApplyTimer.IsEnabled;
        if (_settingsSaveTimer.IsEnabled)
        {
            _settingsSaveTimer.Stop();
            _settings.Save();
            needsApply = true;
        }

        if (_pluginSettingsSaveTimer.IsEnabled)
        {
            _pluginSettingsSaveTimer.Stop();
            _pendingPluginConfig?.Save();
            needsApply = true;
        }

        if (needsApply)
        {
            _settingsApplyTimer.Stop();
            _onSettingsChanged?.Invoke();
        }
    }

    private void MainBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var border = (Border)sender;
        var rect = new Rect(0, 0, border.ActualWidth, border.ActualHeight);
        var radius = border.CornerRadius.TopLeft;
        border.Clip = new RectangleGeometry(rect, radius, radius);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 平滑淡入动画
        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                1, TimeSpan.FromMilliseconds(200)));

        // 收集位置预览 Border
        _previewBorders = new Dictionary<string, Border>
        {
            ["TopLeft"] = FindName("PrevTL") as Border ?? new Border(),
            ["Top"] = FindName("PrevT") as Border ?? new Border(),
            ["TopRight"] = FindName("PrevTR") as Border ?? new Border(),
            ["BottomLeft"] = FindName("PrevBL") as Border ?? new Border(),
            ["Bottom"] = FindName("PrevB") as Border ?? new Border(),
            ["BottomRight"] = FindName("PrevBR") as Border ?? new Border(),
        };

        LoadValuesFromSettings();
        LoadPluginList();
        LoadMonitorList();

        // 初始化主题（从设置读取，不播放动画）
        _currentTheme = string.IsNullOrEmpty(_settings.SettingsTheme) ? "Light" : _settings.SettingsTheme;
        UpdateThemeVisuals();

        // 同步主题切换按钮状态（IsChecked=true 表示夜间；_isLoading 期间不触发 ApplyTheme）
        if (ThemeToggle != null)
            ThemeToggle.IsChecked = _isDark;

        // 加载 GitHub 头像
        LoadGitHubAvatarAsync();

        // 初始化版本号显示
        if (VersionTag != null)
            VersionTag.Text = "v" + GetCurrentVersion();

        _isLoading = false;
        StartSettingsRimAnimation();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (sender is WpfRadioButton rb)
        {
            int index = rb == NavHome ? 0 : rb == NavIsland ? 1 : rb == NavFeatures ? 2 : rb == NavPlugins ? 3 : rb == NavMore ? 4 : -1;
            if (index < 0) return;

            // 详情页：先关闭详情面板，再直接跳转（无动画过渡）
            if (PluginDetailPanel.Visibility == Visibility.Visible)
            {
                CloseDetailPanelImmediate();

                if (index != MainTabs.SelectedIndex)
                    AnimateTabSwitch(index);
                return;
            }

            if (index != MainTabs.SelectedIndex)
                AnimateTabSwitch(index);
        }
    }

    private void CloseDetailPanelImmediate()
    {
        _detailTransitionToken++;
        PluginDetailPanel.BeginAnimation(OpacityProperty, null);
        DetailPanelTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PluginDetailPanel.Visibility = Visibility.Collapsed;
        PluginDetailPanel.Opacity = 0;
        MainTabs.BeginAnimation(OpacityProperty, null);
        MainTabsTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        MainTabs.Opacity = 1;
        MainTabsTranslate.X = 0;
        MainTabs.Visibility = Visibility.Visible;
        BackBtn.Visibility = Visibility.Collapsed;
        LogoIcon.Visibility = Visibility.Visible;
        HeaderTitle.Text = "FluidBar";
        PluginList.SelectedItem = null;
        MonitorList.SelectedItem = null;
        _detailPlugin = null;
        _detailMonitor = null;
    }

    // 主页卡片点击跳转到对应 tab
    private void HomeSpot1_Click(object sender, MouseButtonEventArgs e)
        => NavigateToTab(1);  // 灵动岛
    private void HomeSpot2_Click(object sender, MouseButtonEventArgs e)
        => NavigateToTab(3);  // 插件
    private void HomeSpot3_Click(object sender, MouseButtonEventArgs e)
        => NavigateToTab(2);  // 功能（系统监控在功能页）
    private void HomeProfileCard_Click(object sender, MouseButtonEventArgs e)
        => NavigateToTab(4);  // 更多（关于作者）

    private void NavigateToTab(int index)
    {
        if (index == MainTabs.SelectedIndex) return;
        AnimateTabSwitch(index);
        // 同步左侧导航按钮选中状态
        var nav = index switch
        {
            0 => NavHome,
            1 => NavIsland,
            2 => NavFeatures,
            3 => NavPlugins,
            4 => NavMore,
            _ => null
        };
        if (nav != null) nav.IsChecked = true;
    }

    // tab 切换滑动过渡（借鉴日夜切换的 PowerEase 风格：滑出 → 切换 → 滑入）
    private void AnimateTabSwitch(int newIndex)
    {
        // 停掉正在进行的动画
        MainTabs.BeginAnimation(OpacityProperty, null);
        MainTabsTranslate.BeginAnimation(TranslateTransform.XProperty, null);

        var ease = new PowerEase { EasingMode = EasingMode.EaseOut, Power = 2 };

        // 阶段1：当前页向左滑出 + 淡出
        var slideOut = new DoubleAnimation(-18, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
        slideOut.Completed += (_, _) =>
        {
            // 阶段2：切换 index，从右侧滑入 + 淡入
            MainTabs.SelectedIndex = newIndex;
            MainTabsTranslate.X = 18;
            MainTabs.Opacity = 0;
            MainTabs.BeginAnimation(OpacityProperty, null);
            MainTabsTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            var slideIn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
            MainTabsTranslate.BeginAnimation(TranslateTransform.XProperty, slideIn);
            MainTabs.BeginAnimation(OpacityProperty, fadeIn);
        };
        MainTabsTranslate.BeginAnimation(TranslateTransform.XProperty, slideOut);
        MainTabs.BeginAnimation(OpacityProperty, fadeOut);
    }

    // ============================================================
    //  主页卡片交互效果
    // ============================================================

    // Title Card: 鼠标移动时轻微缩放（景深变化）
    private void HomeTitleCard_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border card)
        {
            var pos = e.GetPosition(card);
            var cx = (pos.X / card.ActualWidth - 0.5) * 2;   // -1..1
            var cy = (pos.Y / card.ActualHeight - 0.5) * 2;
            var scale = HomeTitleScale;
            if (scale != null)
            {
                if (scale.IsFrozen) { scale = scale.Clone(); card.RenderTransform = scale; }
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1.0 + cx * 0.012;
                scale.ScaleY = 1.0 + cy * 0.012;
            }
        }
    }

    private void HomeCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border card && card.RenderTransform is ScaleTransform scale)
        {
            if (scale.IsFrozen) { scale = scale.Clone(); card.RenderTransform = scale; }
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }
    }

    // Profile Card: Reflective 镜面反光 + Spotlight 追光阴影
    private void HomeProfileCard_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border card)
        {
            var pos = e.GetPosition(card);
            var nx = pos.X / card.ActualWidth;    // 0..1（用于阴影方向）
            var ny = pos.Y / card.ActualHeight;

            // 镜面反光高光层：径向渐变中心跟随鼠标（绝对坐标，正圆形）
            if (ProfileReflectBrush != null)
            {
                if (ProfileReflectBrush.IsFrozen)
                {
                    ProfileReflectBrush = ProfileReflectBrush.Clone();
                    if (ProfileReflect != null) ProfileReflect.Background = ProfileReflectBrush;
                }
                ProfileReflectBrush.Center = new System.Windows.Point(pos.X, pos.Y);
                ProfileReflectBrush.GradientOrigin = new System.Windows.Point(pos.X, pos.Y);
            }
            if (ProfileReflect != null)
                ProfileReflect.Opacity = 0.15;

            // 追光阴影：方向跟随鼠标（DropShadowEffect 也可能冻结）
            if (ProfileShadow != null)
            {
                if (ProfileShadow.IsFrozen) { card.Effect = ProfileShadow.Clone(); }
                if (card.Effect is DropShadowEffect shadow)
                {
                    var cx = (nx - 0.5) * 2;
                    var cy = (ny - 0.5) * 2;
                    shadow.Direction = Math.Atan2(-cy, cx) * 180 / Math.PI;
                    shadow.Opacity = 0.10 + Math.Min(0.08, Math.Sqrt(cx * cx + cy * cy) * 0.05);
                }
            }
        }
    }

    private void HomeProfileCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (ProfileReflect != null) ProfileReflect.Opacity = 0;
        if (ProfileShadow != null)
        {
            if (ProfileShadow.IsFrozen && HomeProfileCard?.Effect is DropShadowEffect) { HomeProfileCard.Effect = ProfileShadow.Clone(); }
            if (HomeProfileCard?.Effect is DropShadowEffect s) { s.Direction = 270; s.Opacity = 0.10; }
        }
    }

    // Spotlight Card: 追光灯（径向高光 + 阴影方向）
    private void HomeSpotlight_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border card)
        {
            var pos = e.GetPosition(card);
            // 绝对坐标用于圆形光斑（MappingMode=Absolute），归一化用于阴影方向
            var nx = pos.X / card.ActualWidth;
            var ny = pos.Y / card.ActualHeight;

            // 找到对应的 Glow 层（Spot1Glow/Spot2Glow/Spot3Glow）
            var glowName = card.Name switch
            {
                "HomeSpot1" => "Spot1Glow",
                "HomeSpot2" => "Spot2Glow",
                "HomeSpot3" => "Spot3Glow",
                _ => null
            };
            if (glowName != null && FindName(glowName) is Border glow)
            {
                if (glow.Background is RadialGradientBrush brush)
                {
                    // 冻结的 brush 只读，克隆可写副本替换
                    if (brush.IsFrozen)
                    {
                        brush = brush.Clone();
                        glow.Background = brush;
                    }
                    // 绝对坐标：光斑中心跟随鼠标，正圆形
                    brush.Center = new System.Windows.Point(pos.X, pos.Y);
                    brush.GradientOrigin = new System.Windows.Point(pos.X, pos.Y);
                }
                glow.Opacity = 0.18;
            }

            // 阴影方向跟随鼠标（DropShadowEffect 可能冻结，需克隆）
            if (card.Effect is DropShadowEffect shadow)
            {
                if (shadow.IsFrozen) { card.Effect = shadow.Clone(); shadow = (DropShadowEffect)card.Effect; }
                var cx = (nx - 0.5) * 2;
                var cy = (ny - 0.5) * 2;
                shadow.Direction = Math.Atan2(-cy, cx) * 180 / Math.PI;
                shadow.Opacity = 0.10 + Math.Min(0.06, Math.Sqrt(cx * cx + cy * cy) * 0.04);
            }
        }
    }

    private void HomeSpotlight_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border card)
        {
            if (card.Effect is DropShadowEffect shadow)
            {
                if (shadow.IsFrozen) { card.Effect = shadow.Clone(); shadow = (DropShadowEffect)card.Effect; }
                shadow.Direction = 270;
                shadow.Opacity = 0.08;
            }
            var glowName = card.Name switch
            {
                "HomeSpot1" => "Spot1Glow",
                "HomeSpot2" => "Spot2Glow",
                "HomeSpot3" => "Spot3Glow",
                _ => null
            };
            if (glowName != null && FindName(glowName) is Border glow)
                glow.Opacity = 0;
        }
    }

    // ============================================================
    //  日/夜主题切换（ToggleButton + WpfGorgeousThemeSwitch 样式）
    // ============================================================

    // ToggleButton.Checked 触发 → 切换到夜间
    private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        ApplyTheme("Dark");
    }

    // ToggleButton.Unchecked 触发 → 切换到日间
    private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        ApplyTheme("Light");
    }

    private void ApplyTheme(string theme)
    {
        _currentTheme = theme;
        _settings.SettingsTheme = theme;
        _settings.Save();
        // 主题画刷瞬间切换（按钮的滑动/星星/云朵动画由 XAML Storyboard 处理）
        UpdateThemeVisuals();
    }

    // 主题画刷瞬间切换的核心逻辑（UpdateThemeVisuals 和 AnimateThemeColors 共用）
    private void UpdateThemeVisualsCore()
    {
        var pal = _isDark ? DarkPalette : LightPalette;
        string[] keys =
        {
            "ThemeBg", "ThemeSurface", "ThemeCardBg", "ThemeSidebar",
            "ThemeFg", "ThemeFgSecondary", "ThemeFgTertiary",
            "ThemeBorder", "ThemeBorderStrong", "ThemeAccent",
            "ThemeAccentBg", "ThemeHover", "ThemeTagBg", "ThemeIconTile"
        };
        for (int i = 0; i < keys.Length && i < pal.Length; i++)
        {
            Resources[keys[i]] = new SolidColorBrush(pal[i]);
        }
        Resources["BgGradient"] = _isDark ? CreateDarkBgGradient() : CreateLightBgGradient();
        Resources["SidebarGradient"] = _isDark ? CreateDarkSidebarGradient() : CreateLightSidebarGradient();
    }

    private static void AnimateColorStop(GradientStop? stop, MediaColor to, TimeSpan duration, IEasingFunction ease)
    {
        if (stop == null) return;
        if (stop.IsFrozen)
        {
            stop = new GradientStop(stop.Color, stop.Offset);
        }
        var anim = new ColorAnimation(to, duration) { EasingFunction = ease };
        stop.BeginAnimation(GradientStop.ColorProperty, anim);
    }

    // 主题颜色定义
    private static readonly MediaColor[] LightPalette =
    {
        MediaColor.FromRgb(0xF5, 0xF5, 0xF7),  // ThemeBg
        MediaColor.FromRgb(0xFF, 0xFF, 0xFF),  // ThemeSurface
        MediaColor.FromRgb(0xFF, 0xFF, 0xFF),  // ThemeCardBg
        MediaColor.FromRgb(0xED, 0xED, 0xF1),  // ThemeSidebar
        MediaColor.FromRgb(0x1D, 0x1D, 0x1F),  // ThemeFg
        MediaColor.FromRgb(0x6E, 0x6E, 0x73),  // ThemeFgSecondary
        MediaColor.FromRgb(0x8E, 0x8E, 0x93),  // ThemeFgTertiary
        MediaColor.FromRgb(0xE5, 0xE5, 0xEA),  // ThemeBorder
        MediaColor.FromRgb(0xD8, 0xD9, 0xDF),  // ThemeBorderStrong
        MediaColor.FromRgb(0x46, 0x85, 0xC0),  // ThemeAccent
        MediaColor.FromRgb(0xE8, 0xF0, 0xF8),  // ThemeAccentBg
        MediaColor.FromRgb(0xF0, 0xF0, 0xF2),  // ThemeHover
        MediaColor.FromRgb(0xF0, 0xF0, 0xF2),  // ThemeTagBg
        MediaColor.FromRgb(0xF5, 0xF5, 0xF7),  // ThemeIconTile
    };
    private static readonly MediaColor[] DarkPalette =
    {
        MediaColor.FromRgb(0x1E, 0x1E, 0x24),  // ThemeBg
        MediaColor.FromRgb(0x2A, 0x2A, 0x32),  // ThemeSurface
        MediaColor.FromRgb(0x2E, 0x2E, 0x36),  // ThemeCardBg
        MediaColor.FromRgb(0x1A, 0x1A, 0x20),  // ThemeSidebar
        MediaColor.FromRgb(0xF5, 0xF5, 0xF7),  // ThemeFg
        MediaColor.FromRgb(0xA1, 0xA1, 0xA8),  // ThemeFgSecondary
        MediaColor.FromRgb(0x7A, 0x7A, 0x82),  // ThemeFgTertiary
        MediaColor.FromRgb(0x3A, 0x3A, 0x44),  // ThemeBorder
        MediaColor.FromRgb(0x4A, 0x4A, 0x54),  // ThemeBorderStrong
        MediaColor.FromRgb(0x6B, 0xB0, 0xE8),  // ThemeAccent (深色下更亮)
        MediaColor.FromRgb(0x2A, 0x3A, 0x52),  // ThemeAccentBg
        MediaColor.FromRgb(0x36, 0x36, 0x40),  // ThemeHover
        MediaColor.FromRgb(0x36, 0x36, 0x40),  // ThemeTagBg
        MediaColor.FromRgb(0x36, 0x36, 0x40),  // ThemeIconTile
    };

    private void UpdateThemeVisuals()
    {
        UpdateThemeVisualsCore();
        // Title Card 渐变（夜间用深色，避免浅色背景与文字融合）
        Resources["TitleCardBg"] = _isDark ? CreateDarkTitleGradient() : CreateLightTitleGradient();
    }

    private static RadialGradientBrush CreateLightBgGradient() => new()
    {
        GradientOrigin = new System.Windows.Point(0.85, 0.12), Center = new System.Windows.Point(0.85, 0.12),
        RadiusX = 0.9, RadiusY = 0.7,
        GradientStops = new GradientStopCollection
        {
            new(MediaColor.FromRgb(0xFF, 0xFD, 0xF6), 0),
            new(MediaColor.FromRgb(0xFB, 0xFA, 0xF5), 0.35),
            new(MediaColor.FromRgb(0xF4, 0xF4, 0xF8), 0.75),
            new(MediaColor.FromRgb(0xEE, 0xEF, 0xF3), 1),
        }
    };

    private static RadialGradientBrush CreateDarkBgGradient() => new()
    {
        GradientOrigin = new System.Windows.Point(0.85, 0.12), Center = new System.Windows.Point(0.85, 0.12),
        RadiusX = 0.9, RadiusY = 0.7,
        GradientStops = new GradientStopCollection
        {
            new(MediaColor.FromRgb(0x26, 0x26, 0x30), 0),
            new(MediaColor.FromRgb(0x22, 0x22, 0x2A), 0.35),
            new(MediaColor.FromRgb(0x1E, 0x1E, 0x24), 0.75),
            new(MediaColor.FromRgb(0x1A, 0x1A, 0x20), 1),
        }
    };

    private static LinearGradientBrush CreateLightSidebarGradient() => new()
    {
        StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1),
        GradientStops = new GradientStopCollection
        {
            new(MediaColor.FromRgb(0xE9, 0xE9, 0xEE), 0),
            new(MediaColor.FromRgb(0xE2, 0xE2, 0xE8), 1),
        }
    };

    private static LinearGradientBrush CreateDarkSidebarGradient() => new()
    {
        StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1),
        GradientStops = new GradientStopCollection
        {
            new(MediaColor.FromRgb(0x1A, 0x1A, 0x20), 0),
            new(MediaColor.FromRgb(0x16, 0x16, 0x1C), 1),
        }
    };

    private static LinearGradientBrush CreateLightTitleGradient() => new()
    {
        StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 1),
        GradientStops = new GradientStopCollection
        {
            new(MediaColor.FromRgb(0xFF, 0xFB, 0xF3), 0),
            new(MediaColor.FromRgb(0xF3, 0xF6, 0xFB), 0.55),
            new(MediaColor.FromRgb(0xEA, 0xF0, 0xF8), 1),
        }
    };

    private static LinearGradientBrush CreateDarkTitleGradient() => new()
    {
        StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 1),
        GradientStops = new GradientStopCollection
        {
            new(MediaColor.FromRgb(0x2E, 0x32, 0x40), 0),
            new(MediaColor.FromRgb(0x28, 0x2C, 0x38), 0.55),
            new(MediaColor.FromRgb(0x22, 0x26, 0x30), 1),
        }
    };

    private void UpdateThemeToggleVisual()
    {
        // 主题切换按钮的视觉状态由 ToggleButton.IsChecked + XAML Storyboard 自动处理
        // 这里只需同步 IsChecked（在 Window_Loaded 里设置）
    }


    // ============================================================
    //  GitHub 头像加载
    // ============================================================

    private async void LoadGitHubAvatarAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(6);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FluidBar-Settings");
            // GitHub 头像 URL: https://avatars.githubusercontent.com/Doulor
            var bytes = await http.GetByteArrayAsync("https://avatars.githubusercontent.com/Doulor?v=4&s=120");
            if (bytes.Length > 0)
            {
                var image = new BitmapImage();
                using (var stream = new System.IO.MemoryStream(bytes))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                }
                image.Freeze();
                if (AvatarImage != null)
                {
                    AvatarImage.Source = image;
                    AvatarImage.Visibility = Visibility.Visible;
                }
                if (AvatarPlaceholder != null)
                    AvatarPlaceholder.Visibility = Visibility.Collapsed;
            }
        }
        catch { /* 加载失败保留占位字母 */ }
    }

    // ============================================================
    //  GitHub 链接 + 检查更新
    // ============================================================

    private void GitHubLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/Doulor/FluidBar",
                UseShellExecute = true
            });
        }
        catch { /* 忽略打开失败 */ }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (CheckUpdateBtn == null || UpdateStatusText == null) return;
        UpdateStatusText.Text = "正在检查更新…";
        CheckUpdateBtn.IsEnabled = false;
        try
        {
            var info = await FetchLatestReleaseInfoAsync();
            if (info == null)
            {
                UpdateStatusText.Text = "检查更新失败，请稍后重试";
            }
            else if (info.HasUpdate)
            {
                if (!string.IsNullOrEmpty(info.AssetDownloadUrl))
                {
                    UpdateStatusText.Text = $"发现新版本 v{info.LatestVersion}，正在后台下载…";
                    var downloaded = await DownloadAndInstallAsync(info.AssetDownloadUrl, info.AssetName);
                    UpdateStatusText.Text = downloaded
                        ? $"已下载 v{info.LatestVersion}，正在启动安装…"
                        : $"下载失败，请前往 GitHub 手动下载";
                }
                else
                {
                    // 没有安装包资源，回退到打开 Release 页面
                    Process.Start(new ProcessStartInfo { FileName = info.HtmlUrl, UseShellExecute = true });
                    UpdateStatusText.Text = $"已打开 v{info.LatestVersion} 下载页面";
                }
            }
            else
            {
                UpdateStatusText.Text = "已是最新版本";
            }
        }
        catch
        {
            UpdateStatusText.Text = "检查更新失败，请稍后重试";
        }
        finally
        {
            CheckUpdateBtn.IsEnabled = true;
        }
    }

    // 获取最新 Release 信息（版本号 + 是否有更新 + Release 页面链接 + 安装包下载链接）
    private async Task<ReleaseInfo?> FetchLatestReleaseInfoAsync()
    {
        var current = GetCurrentVersion();
        using var http = new System.Net.Http.HttpClient();
        http.Timeout = TimeSpan.FromSeconds(8);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FluidBar-Settings");
        var resp = await http.GetStringAsync("https://api.github.com/repos/Doulor/FluidBar/releases/latest");
        using var json = System.Text.Json.JsonDocument.Parse(resp);
        if (!json.RootElement.TryGetProperty("tag_name", out var tag)) return null;
        var latestStr = tag.GetString()?.TrimStart('v') ?? "";
        var htmlUrl = json.RootElement.TryGetProperty("html_url", out var u)
            ? u.GetString() ?? "https://github.com/Doulor/FluidBar/releases/latest"
            : "https://github.com/Doulor/FluidBar/releases/latest";
        var hasUpdate = Version.TryParse(latestStr, out var latest) &&
                        Version.TryParse(current, out var cur) && latest > cur;

        // 从 assets 找安装包（.exe 或 .msi）
        string? assetUrl = null;
        string? assetName = null;
        if (json.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name != null && (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                     name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)))
                {
                    assetUrl = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                    assetName = name;
                    break;
                }
            }
        }
        return new ReleaseInfo(latestStr, htmlUrl, hasUpdate, assetUrl, assetName);
    }

    // 后台下载安装包到临时目录，下载完启动安装并退出当前程序
    private async Task<bool> DownloadAndInstallAsync(string assetUrl, string? assetName)
    {
        try
        {
            var fileName = string.IsNullOrEmpty(assetName) ? "FluidBar-update.exe" : assetName;
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);  // 下载大文件超时放宽
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FluidBar-Settings");
            using var resp = await http.GetAsync(assetUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var fs = System.IO.File.Create(tempPath);
            await resp.Content.CopyToAsync(fs);
            await fs.FlushAsync();

            // 启动安装包并退出当前程序（安装包会替换文件）
            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });
            // 退出当前程序，让安装包能替换文件
            System.Windows.Application.Current.Shutdown();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record ReleaseInfo(
        string LatestVersion,
        string HtmlUrl,
        bool HasUpdate,
        string? AssetDownloadUrl = null,
        string? AssetName = null);

    private static string GetCurrentVersion()
    {
        try
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "2.0.0";
        }
        catch { return "2.0.0"; }
    }

    private void SettingsWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            StartSettingsRimAnimation();
        else
            StopSettingsRimAnimation();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(
            0, TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += (_, _) =>
        {
            FlushPendingSettingsWork();
            StopSettingsRimAnimation();
            Hide();
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void StartSettingsRimAnimation()
    {
        // 白色主题移除了环绕微光动画
    }

    private void StopSettingsRimAnimation()
    {
        // 白色主题移除了环绕微光动画
    }

    #region 主面板

    private void LoadValuesFromSettings()
    {
        _isLoading = true;
        CoerceLayoutSettings();

        CornerRadiusSlider.Value = _settings.CornerRadius;
        CornerRadiusValue.Text = _settings.CornerRadius.ToString("F0");

        OpacitySlider.Value = _settings.Opacity;
        OpacityValue.Text = ((int)(_settings.Opacity * 100)) + "%";

        BackgroundOpacitySlider.Value = _settings.BackgroundOpacity;
        BackgroundOpacityValue.Text = ((int)(_settings.BackgroundOpacity * 100)) + "%";

        IslandWidthSlider.Value = _settings.ExpandedMaxWidth;
        IslandWidthValue.Text = _settings.ExpandedMaxWidth.ToString("F0");

        IslandHeightSlider.Value = _settings.ExpandedHeight;
        IslandHeightValue.Text = _settings.ExpandedHeight.ToString("F0");

        OffsetXSlider.Value = _settings.OffsetX;
        OffsetXValue.Text = _settings.OffsetX.ToString("F0");

        OffsetYSlider.Value = _settings.OffsetY;
        OffsetYValue.Text = _settings.OffsetY.ToString("F0");

        HideDelaySlider.Value = _settings.AutoHideDelayMs / 1000.0;
        HideDelayValue.Text =
            (_settings.AutoHideDelayMs / 1000.0).ToString("F1") + "s";

        AlwaysVisibleToggle.IsChecked = _settings.AlwaysVisible;
        HideTrayToggle.IsChecked = _settings.HideTrayIcon;
        SetDisplayStrategyCombo(_settings.DisplayStrategy);
        SetHoldToHideKeyCombo(_settings.HoldToHideKey);
        AutoUpdateToggle.IsChecked = _settings.AutoUpdateCheck;

        // 环绕微光模式
        SetRimModeCombo(_settings.RimMode);

        SetPositionRadio(_settings.Position);
        UpdatePositionPreview(_settings.Position);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            StartupToggle.IsChecked = key?.GetValue(AppName) != null;
        }
        catch { StartupToggle.IsChecked = false; }

        _isLoading = false;
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
        _settings.CornerRadius = Math.Max(18, _settings.CornerRadius);
    }

    private void SetPositionRadio(string position)
    {
        PosTop.IsChecked = position == "Top";
        PosBottom.IsChecked = position == "Bottom";
        PosTopLeft.IsChecked = position == "TopLeft";
        PosTopRight.IsChecked = position == "TopRight";
        PosBottomLeft.IsChecked = position == "BottomLeft";
        PosBottomRight.IsChecked = position == "BottomRight";
    }

    private void UpdatePositionPreview(string position)
    {
        if (_previewBorders == null) return;

        var activeColor = MediaColor.FromRgb(0, 122, 255);
        var inactiveColor = (MediaColor)((SolidColorBrush)FindResource("ThemeHover")).Color;
        var activeBorder = MediaColor.FromArgb(80, 0, 122, 255);
        var inactiveBorder = (MediaColor)((SolidColorBrush)FindResource("ThemeBorder")).Color;

        foreach (var kv in _previewBorders)
        {
            var isActive = kv.Key == position;
            kv.Value.Background = new SolidColorBrush(
                isActive ? activeColor : inactiveColor);
            kv.Value.BorderBrush = new SolidColorBrush(
                isActive ? activeBorder : inactiveBorder);
        }
    }

    private void Setting_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;

        if (sender == CornerRadiusSlider)
        {
            _settings.CornerRadius = Math.Max(18, Math.Round(e.NewValue));
            CornerRadiusValue.Text = _settings.CornerRadius.ToString("F0");
        }
        else if (sender == OpacitySlider)
        {
            _settings.Opacity = Math.Round(e.NewValue, 2);
            OpacityValue.Text = ((int)(_settings.Opacity * 100)) + "%";
        }
        else if (sender == BackgroundOpacitySlider)
        {
            _settings.BackgroundOpacity = Math.Round(e.NewValue, 2);
            BackgroundOpacityValue.Text = ((int)(_settings.BackgroundOpacity * 100)) + "%";
        }
        else if (sender == IslandWidthSlider)
        {
            _settings.ExpandedMaxWidth = Math.Max(
                IslandPresentationFactory.MinimumExpandedWidth,
                Math.Round(e.NewValue));
            _settings.CollapsedWidth = Math.Clamp(
                Math.Round(_settings.ExpandedMaxWidth * 0.34),
                IslandPresentationFactory.MinimumCollapsedWidth,
                220);
            IslandWidthValue.Text = _settings.ExpandedMaxWidth.ToString("F0");
        }
        else if (sender == IslandHeightSlider)
        {
            _settings.ExpandedHeight = Math.Clamp(
                Math.Round(e.NewValue),
                IslandPresentationFactory.MinimumExpandedHeight,
                IslandPresentationFactory.MaximumExpandedHeight);
            _settings.CollapsedHeight = Math.Max(
                IslandPresentationFactory.MinimumCollapsedHeight,
                Math.Round(_settings.ExpandedHeight * 0.53));
            IslandHeightValue.Text = _settings.ExpandedHeight.ToString("F0");
        }
        else if (sender == OffsetXSlider)
        {
            _settings.OffsetX = Math.Round(e.NewValue);
            OffsetXValue.Text = _settings.OffsetX.ToString("F0");
        }
        else if (sender == OffsetYSlider)
        {
            _settings.OffsetY = Math.Round(e.NewValue);
            OffsetYValue.Text = _settings.OffsetY.ToString("F0");
        }
        else if (sender == HideDelaySlider)
        {
            var seconds = Math.Round(e.NewValue, 1);
            _settings.AutoHideDelayMs = (int)(seconds * 1000);
            HideDelayValue.Text = seconds.ToString("F1") + "s";
        }

        ScheduleSettingsSaveAndChanged();
    }

    private void PositionRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        if (sender is WpfRadioButton rb && rb.Tag is string pos)
        {
            _settings.Position = pos;
            _settings.Save();
            UpdatePositionPreview(pos);
            _onSettingsChanged?.Invoke();
        }
    }

    private void AlwaysVisibleToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.AlwaysVisible = AlwaysVisibleToggle.IsChecked == true;
        _settings.Save();
        _onSettingsChanged?.Invoke();
    }

    private void SetDisplayStrategyCombo(IslandDisplayStrategy strategy)
    {
        var tag = strategy.ToString();
        foreach (ComboBoxItem item in DisplayStrategyCombo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                DisplayStrategyCombo.SelectedItem = item;
                return;
            }
        }

        DisplayStrategyCombo.SelectedIndex = 0;
    }

    private void DisplayStrategyCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (DisplayStrategyCombo.SelectedItem is not ComboBoxItem item)
            return;

        var tag = item.Tag?.ToString();
        var nextStrategy = tag == nameof(IslandDisplayStrategy.Multiple)
            ? IslandDisplayStrategy.Multiple
            : IslandDisplayStrategy.LatestOnly;
        if (_settings.DisplayStrategy == nextStrategy)
            return;

        _settings.DisplayStrategy = nextStrategy;
        _settings.Save();
        ScheduleSettingsChanged();
    }

    private void SetRimModeCombo(string mode)
    {
        foreach (ComboBoxItem item in RimModeCombo.Items)
        {
            if (item.Tag?.ToString() == mode)
            {
                RimModeCombo.SelectedItem = item;
                return;
            }
        }
        RimModeCombo.SelectedIndex = 1; // 默认 "Event"
    }

    private void RimModeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (RimModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string mode)
        {
            if (_settings.RimMode == mode)
                return;

            _settings.RimMode = mode;
            _settings.Save();
            ScheduleSettingsChanged();
        }
    }

    private void HideTrayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        var hide = HideTrayToggle.IsChecked == true;
        if (hide)
        {
            var result = WpfMessageBox.Show(
                "隐藏托盘图标后，可通过 Ctrl+Alt+Click 灵动岛本体重新打开设置面板。\n\n确认隐藏？",
                "确认隐藏托盘图标",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                _isLoading = true;
                HideTrayToggle.IsChecked = false;
                _isLoading = false;
                return;
            }
        }

        _settings.HideTrayIcon = hide;
        _settings.Save();
        TrayIconVisibilityChanged?.Invoke(hide);
    }

    private void SetHoldToHideKeyCombo(string key)
    {
        HoldToHideKeyCombo.Items.Clear();
        foreach (var value in HoldToHideKeyPolicy.Values)
        {
            HoldToHideKeyCombo.Items.Add(new ComboBoxItem
            {
                Content = HoldToHideKeyPolicy.DisplayName(value),
                Tag = value
            });
        }

        var coerced = HoldToHideKeyPolicy.Coerce(key);
        foreach (ComboBoxItem item in HoldToHideKeyCombo.Items)
        {
            if (item.Tag?.ToString() == coerced)
            {
                HoldToHideKeyCombo.SelectedItem = item;
                return;
            }
        }
        HoldToHideKeyCombo.SelectedIndex = 1;
    }

    private void HoldToHideKeyCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (HoldToHideKeyCombo.SelectedItem is not ComboBoxItem item)
            return;

        var key = HoldToHideKeyPolicy.Coerce(item.Tag?.ToString());
        if (_settings.HoldToHideKey == key)
            return;

        _settings.HoldToHideKey = key;
        _settings.Save();
        ScheduleSettingsChanged();
    }

    private void AutoUpdateToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.AutoUpdateCheck = AutoUpdateToggle.IsChecked == true;
        _settings.Save();
    }

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null) return;
            if (StartupToggle.IsChecked == true)
                key.SetValue(AppName, Environment.ProcessPath ?? "");
            else
                key.DeleteValue(AppName, false);
        }
        catch { }
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        _settings.Save();
        LoadValuesFromSettings();
        _onSettingsChanged?.Invoke();
    }

    #endregion

    #region 插件

    private void LoadPluginList()
    {
        PluginList.ItemsSource = _pluginManager.Plugins;
    }

    private void PluginList_SelectionChanged(object sender,
        SelectionChangedEventArgs e)
    {
        if (PluginList.SelectedItem is IIslandPlugin plugin)
            ShowPluginDetail(plugin);
    }

    private void ShowPluginDetail(IIslandPlugin plugin)
    {
        _detailPlugin = plugin;
        _detailMonitor = null;

        BackBtn.Visibility = Visibility.Visible;
        LogoIcon.Visibility = Visibility.Collapsed;
        HeaderTitle.Text = plugin.Name;
        DetailEnabledLabel.Text = "启用插件";
        DetailSettingsHeader.Text = "插件设置";

        DetailIcon.Text = plugin.Icon;
        DetailName.Text = plugin.Name;
        DetailDesc.Text = plugin.Description;
        _isLoading = true;
        DetailEnabledToggle.IsChecked = plugin.Enabled;
        _isLoading = false;

        PluginSettingsContainer.Children.Clear();

        if (plugin.Id == "clipboard")
        {
            BuildClipboardPluginSettings(plugin);
        }
        else if (plugin.Id == "media")
        {
            BuildMediaPluginSettings(plugin);
        }
        else if (plugin.Id == "agent-status")
        {
            BuildAgentStatusPluginSettings();
        }

        ShowDetailPanel();
    }

    private void ShowDetailPanel()
    {
        var token = ++_detailTransitionToken;
        MainTabs.BeginAnimation(OpacityProperty, null);
        MainTabsTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PluginDetailPanel.BeginAnimation(OpacityProperty, null);
        DetailPanelTranslate.BeginAnimation(TranslateTransform.XProperty, null);

        PluginDetailPanel.Visibility = Visibility.Visible;
        PluginDetailPanel.Opacity = 0;
        DetailPanelTranslate.X = 32;

        MainTabs.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        MainTabsTranslate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-22, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(230))
        {
            BeginTime = TimeSpan.FromMilliseconds(80),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slideIn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(45),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        fadeIn.Completed += (_, _) =>
        {
            if (token == _detailTransitionToken)
                MainTabs.Visibility = Visibility.Collapsed;
        };
        PluginDetailPanel.BeginAnimation(OpacityProperty, fadeIn);
        DetailPanelTranslate.BeginAnimation(TranslateTransform.XProperty, slideIn);
    }

    private void BuildClipboardPluginSettings(IIslandPlugin plugin)
    {
        var cfg = plugin.Config as ClipboardPluginConfig;
        if (cfg == null) return;

        PluginSettingsContainer.Children.Add(CreateSliderRow(
            "最小字符", 5, 100, 1, cfg.MinFullDisplayChars,
            val => { cfg.MinFullDisplayChars = (int)val; SchedulePluginSettingsSave(cfg); }));

        PluginSettingsContainer.Children.Add(CreateSliderRow(
            "停留时间", 1, 15, 0.5, cfg.DisplayDurationMs / 1000.0,
            val => { cfg.DisplayDurationMs = (int)(val * 1000); SchedulePluginSettingsSave(cfg); },
            val => val.ToString("F1") + "s"));

        PluginSettingsContainer.Children.Add(CreateSliderRow(
            "滚动速度", 0.5, 5, 0.5, cfg.ScrollSpeed,
            val => { cfg.ScrollSpeed = val; SchedulePluginSettingsSave(cfg); },
            val => val.ToString("F1") + "px"));
    }

    private void BuildMediaPluginSettings(IIslandPlugin plugin)
    {
        var cfg = plugin.Config as MediaPluginConfig;
        if (cfg == null) return;

        PluginSettingsContainer.Children.Add(CreateToggleRow(
            "歌词显示",
            "有可用歌词时在悬停卡片中显示当前歌词行",
            cfg.ShowLyrics,
            val => { cfg.ShowLyrics = val; SchedulePluginSettingsSave(cfg); }));

        PluginSettingsContainer.Children.Add(CreateToggleRow(
            "暂停显示",
            "媒体暂停时仍然显示当前曲目状态",
            cfg.ShowWhenPaused,
            val => { cfg.ShowWhenPaused = val; SchedulePluginSettingsSave(cfg); }));

        PluginSettingsContainer.Children.Add(CreateSliderRow(
            "刷新间隔", 0.4, 5, 0.2, cfg.PollIntervalMs / 1000.0,
            val => { cfg.PollIntervalMs = (int)(val * 1000); SchedulePluginSettingsSave(cfg); },
            val => val.ToString("F1") + "s"));
    }

    private void BuildAgentStatusPluginSettings()
    {
        var inbox = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FluidBar",
            "agent-events",
            "inbox");

        var agentPlugin = _pluginManager.Plugins
            .OfType<AgentStatusPlugin>()
            .FirstOrDefault();

        PluginSettingsContainer.Children.Add(CreateInfoRow(
            "Hook Inbox",
            inbox));
        PluginSettingsContainer.Children.Add(CreateInfoRow(
            "支持事件",
            "SessionStart / PreToolUse / PostToolUse / Stop / Notification"));
        PluginSettingsContainer.Children.Add(CreateInfoRow(
            "事件格式",
            "tool/status/project/summary/toolName/branch/durationMs"));

        if (agentPlugin != null)
        {
            PluginSettingsContainer.Children.Add(new System.Windows.Controls.Separator
            {
                Margin = new Thickness(0, 8, 0, 8),
                Opacity = 0.3
            });

            PluginSettingsContainer.Children.Add(CreateToggleRow(
                "Hooks 守护",
                "定期检查 Claude Code hooks 配置，缺失时自动修复",
                _settings.AgentHooksGuardEnabled,
                val =>
                {
                    _settings.AgentHooksGuardEnabled = val;
                    _settings.Save();
                    agentPlugin.HooksGuardEnabled = val;
                    agentPlugin.StartGuard();
                }));

            PluginSettingsContainer.Children.Add(CreateSliderRow(
                "守护间隔", 5, 60, 5, _settings.AgentHooksGuardIntervalMs / 1000.0,
                val =>
                {
                    _settings.AgentHooksGuardIntervalMs = (int)(val * 1000);
                    _settings.Save();
                    agentPlugin.HooksGuardIntervalMs = (int)(val * 1000);
                    agentPlugin.StartGuard();
                },
                val => val.ToString("F0") + "s"));
        }
    }

    private UIElement CreateInfoRow(string label, string value)
    {
        var grid = new Grid { MinHeight = 46 };
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = new TextBlock
        {
            Text = label,
            Style = (Style)FindResource("SettingLabel")
        };
        Grid.SetColumn(labelText, 0);

        var valueText = new TextBlock
        {
            Text = value,
            Style = (Style)FindResource("ValueLabel"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueText, 1);

        grid.Children.Add(labelText);
        grid.Children.Add(valueText);
        return CreateInteractiveSettingRow(grid);
    }

    private UIElement CreateButtonRow(
        string label,
        string description,
        string buttonText,
        Func<Task> onClick)
    {
        var grid = new Grid { MinHeight = 54 };
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("ThemeFg"),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI")
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("ThemeFgTertiary"),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(textPanel, 0);

        var button = new System.Windows.Controls.Button
        {
            Content = buttonText,
            MinWidth = 76,
            Height = 30,
            Margin = new Thickness(12, 0, 0, 0)
        };
        button.Click += async (_, _) => await onClick();
        Grid.SetColumn(button, 1);

        grid.Children.Add(textPanel);
        grid.Children.Add(button);
        return CreateInteractiveSettingRow(grid);
    }

    private UIElement CreateTextRow(string label, string value, Action<string> onChanged)
    {
        var grid = new Grid { MinHeight = 46 };
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = new TextBlock
        {
            Text = label,
            Style = (Style)FindResource("SettingLabel")
        };
        Grid.SetColumn(labelText, 0);

        var textBox = new System.Windows.Controls.TextBox
        {
            Text = value,
            Margin = new Thickness(8, 0, 0, 0),
            MinHeight = 28,
            Padding = new Thickness(8, 4, 8, 4),
            Background = (System.Windows.Media.Brush)FindResource("ThemeHover"),
            Foreground = (System.Windows.Media.Brush)FindResource("ThemeFg"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("ThemeBorder"),
            BorderThickness = new Thickness(1),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI")
        };
        textBox.LostFocus += (_, _) => onChanged(textBox.Text);
        Grid.SetColumn(textBox, 1);

        grid.Children.Add(labelText);
        grid.Children.Add(textBox);
        return CreateInteractiveSettingRow(grid);
    }

    private UIElement CreateSliderRow(string label, double min, double max,
        double tick, double value, Action<double> onChanged,
        Func<double, string>? formatValue = null)
    {
        formatValue ??= val => val.ToString("F0");

        var grid = new Grid { MinHeight = 44 };
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(44) });

        var labelText = new TextBlock
        {
            Text = label,
            Style = (Style)FindResource("SettingLabel")
        };
        Grid.SetColumn(labelText, 0);

        var slider = new Slider
        {
            Minimum = min, Maximum = max,
            TickFrequency = tick, IsSnapToTickEnabled = true,
            Value = value, Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(slider, 1);

        var valueText = new TextBlock
        {
            Text = formatValue(value),
            Style = (Style)FindResource("ValueLabel")
        };
        Grid.SetColumn(valueText, 2);

        slider.ValueChanged += (_, e) =>
        {
            valueText.Text = formatValue(e.NewValue);
            onChanged(e.NewValue);
        };

        grid.Children.Add(labelText);
        grid.Children.Add(slider);
        grid.Children.Add(valueText);

        return CreateInteractiveSettingRow(grid);
    }

    private UIElement CreateToggleRow(string label, string description,
        bool value, Action<bool> onChanged)
    {
        var grid = new Grid { MinHeight = 48 };
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("ThemeFg"),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI")
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("ThemeFgTertiary"),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(textPanel, 0);

        var toggle = new WpfToggleButton
        {
            Width = 44,
            Height = 22,
            IsChecked = value,
            Style = (Style)FindResource("ToggleSwitchStyle"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        toggle.Checked += (_, _) => onChanged(true);
        toggle.Unchecked += (_, _) => onChanged(false);
        Grid.SetColumn(toggle, 1);

        grid.Children.Add(textPanel);
        grid.Children.Add(toggle);

        return CreateInteractiveSettingRow(grid);
    }

    private Border CreateInteractiveSettingRow(UIElement content)
    {
        // 用主题画刷（DynamicResource）响应日/夜切换
        var normalBackground = (System.Windows.Media.Brush)FindResource("ThemeHover");
        var hoverBackground = (System.Windows.Media.Brush)FindResource("ThemeSurface");
        var normalBorder = (System.Windows.Media.Brush)FindResource("ThemeBorder");
        var hoverBorder = (System.Windows.Media.Brush)FindResource("ThemeAccent");

        var row = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(12),
            Background = normalBackground,
            BorderBrush = normalBorder,
            BorderThickness = new Thickness(1),
            Child = content
        };

        row.MouseEnter += (_, _) =>
        {
            row.Background = hoverBackground;
            row.BorderBrush = hoverBorder;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = normalBackground;
            row.BorderBrush = normalBorder;
        };

        return row;
    }

    private static void AnimateTransform(
        Animatable target,
        DependencyProperty property,
        double value,
        int milliseconds)
    {
        target.BeginAnimation(property,
            new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void DetailEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (_detailPlugin is IIslandPlugin plugin)
        {
            var enabled = DetailEnabledToggle.IsChecked == true;
            _pluginManager.SetEnabled(plugin, enabled);
        }
        else if (_detailMonitor is ISystemMonitor monitor)
        {
            var enabled = DetailEnabledToggle.IsChecked == true;
            _monitorManager.SetEnabled(monitor, enabled);
        }
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowMainTabs();

        BackBtn.Visibility = Visibility.Collapsed;
        LogoIcon.Visibility = Visibility.Visible;
        HeaderTitle.Text = "FluidBar";
        PluginList.SelectedItem = null;
        MonitorList.SelectedItem = null;
        _detailPlugin = null;
        _detailMonitor = null;
    }

    private void ShowMainTabs()
    {
        var token = ++_detailTransitionToken;
        MainTabs.BeginAnimation(OpacityProperty, null);
        MainTabsTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PluginDetailPanel.BeginAnimation(OpacityProperty, null);
        DetailPanelTranslate.BeginAnimation(TranslateTransform.XProperty, null);

        MainTabs.Visibility = Visibility.Visible;
        MainTabs.Opacity = 0;
        MainTabsTranslate.X = -18;

        PluginDetailPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        DetailPanelTranslate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(24, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
        {
            BeginTime = TimeSpan.FromMilliseconds(70),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slideIn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
        {
            BeginTime = TimeSpan.FromMilliseconds(40),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        fadeIn.Completed += (_, _) =>
        {
            if (token == _detailTransitionToken)
                PluginDetailPanel.Visibility = Visibility.Collapsed;
        };
        MainTabs.BeginAnimation(OpacityProperty, fadeIn);
        MainTabsTranslate.BeginAnimation(TranslateTransform.XProperty, slideIn);
    }

    #endregion

    #region 功能列表（系统监控）

    private void LoadMonitorList()
    {
        MonitorList.ItemsSource = _monitorManager.Monitors;
    }

    private void MonitorList_SelectionChanged(object sender,
        SelectionChangedEventArgs e)
    {
        if (MonitorList.SelectedItem is ISystemMonitor monitor)
            ShowMonitorDetail(monitor);
    }

    private void ShowMonitorDetail(ISystemMonitor monitor)
    {
        _detailPlugin = null;
        _detailMonitor = monitor;

        BackBtn.Visibility = Visibility.Visible;
        LogoIcon.Visibility = Visibility.Collapsed;
        HeaderTitle.Text = monitor.Name;
        DetailEnabledLabel.Text = "启用功能";
        DetailSettingsHeader.Text = "功能设置";

        DetailIcon.Text = monitor.Icon;
        DetailName.Text = monitor.Name;
        DetailDesc.Text = monitor.Description;
        _isLoading = true;
        DetailEnabledToggle.IsChecked = monitor.Enabled;
        _isLoading = false;

        PluginSettingsContainer.Children.Clear();
        BuildMonitorFeatureSettings(monitor);
        ShowDetailPanel();
    }

    private void BuildMonitorFeatureSettings(ISystemMonitor monitor)
    {
        var feature = _settings.GetMonitorFeatureSettings(monitor.Id);

        PluginSettingsContainer.Children.Add(CreateToggleRow(
            "悬停卡片",
            "鼠标移入灵动岛时放大为更明显的卡片状态",
            feature.HoverCardEnabled,
            val => { feature.HoverCardEnabled = val; ScheduleSettingsSaveAndChanged(); }));

        PluginSettingsContainer.Children.Add(CreateSliderRow(
            "显示时长", 1, 8, 0.5, feature.DisplayDurationMs / 1000.0,
            val =>
            {
                feature.DisplayDurationMs = (int)(val * 1000);
                ScheduleSettingsSaveAndChanged();
            },
            val => val.ToString("F1") + "s"));

        PluginSettingsContainer.Children.Add(CreateToggleRow(
            "强调动画",
            "新状态到来时使用更明显的回弹和环绕微光",
            feature.EmphasizeTransitions,
            val => { feature.EmphasizeTransitions = val; ScheduleSettingsSaveAndChanged(); }));

        // Notification-specific: permission request
        if (monitor is NotificationMonitor notifications)
        {
            PluginSettingsContainer.Children.Add(CreateButtonRow(
                "通知权限",
                "请求 Windows 通知读取权限",
                "请求授权",
                async () =>
                {
                    var status = await notifications.RequestAccessAsync();
                    System.Windows.MessageBox.Show(
                        $"Windows 通知权限状态：{status}",
                        "FluidBar 通知",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }));
        }
    }

    private void MonitorToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (sender is WpfToggleButton toggle && toggle.Tag is string id)
        {
            var monitor = _monitorManager.Monitors.FirstOrDefault(m => m.Id == id);
            if (monitor != null)
                _monitorManager.SetEnabled(monitor, toggle.IsChecked == true);
        }
    }

    #endregion
}
