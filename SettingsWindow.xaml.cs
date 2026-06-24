using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfMessageBox = System.Windows.MessageBox;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using FluidBar.Monitors;
using MediaColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCursors = System.Windows.Input.Cursors;
using WpfThickness = System.Windows.Thickness;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

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

    // Position preview borders
    private Dictionary<string, Border>? _previewBorders;

    // Dynamic references for island page controls
    private Slider? _cornerRadiusSlider, _opacitySlider, _bgOpacitySlider;
    private Slider? _islandWidthSlider, _islandHeightSlider, _offsetXSlider, _offsetYSlider, _hideDelaySlider;
    private TextBlock? _cornerRadiusVal, _opacityVal, _bgOpacityVal;
    private TextBlock? _islandWidthVal, _islandHeightVal, _offsetXVal, _offsetYVal, _hideDelayVal;
    private WpfToggleButton? _alwaysVisibleToggle;
    private WpfComboBox? _displayStrategyCombo, _rimModeCombo;
    private WpfRadioButton? _posTopLeft, _posTop, _posTopRight, _posBottomLeft, _posBottom, _posBottomRight;

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
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); tick(); };
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
        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));

        BuildIslandPage();
        BuildMorePage();
        LoadPluginList();
        //LoadMonitorList();
        LoadValuesFromSettings();
        _isLoading = false;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void SettingsWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += (_, _) =>
        {
            FlushPendingSettingsWork();
            Hide();
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    #region Navigation

    private string _currentPage = "Island";

    private void Nav_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (sender is not WpfRadioButton rb || rb.IsChecked != true) return;
        var tag = rb.Tag?.ToString();
        if (tag == _currentPage) return;

        _currentPage = tag ?? "Island";
        HideAllPages();

        switch (tag)
        {
            case "Island":
                PageIsland.Visibility = Visibility.Visible;
                break;
            case "Features":
                PageFeatures.Visibility = Visibility.Visible;
                break;
            case "Plugins":
                PagePlugins.Visibility = Visibility.Visible;
                break;
            case "More":
                PageMore.Visibility = Visibility.Visible;
                break;
        }
    }

    private void HideAllPages()
    {
        PageIsland.Visibility = Visibility.Collapsed;
        PageFeatures.Visibility = Visibility.Collapsed;
        PagePlugins.Visibility = Visibility.Collapsed;
        PageMore.Visibility = Visibility.Collapsed;
        PageDetail.Visibility = Visibility.Collapsed;
        BackBtn.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Island Page (built programmatically)

    private void BuildIslandPage()
    {
        var panel = PageIslandContent;

        // -- Appearance card --
        panel.Children.Add(MakeCard("外观", new UIElement[]
        {
            MakeSliderRow("圆角", 18, 34, 1, 24, out _cornerRadiusSlider, out _cornerRadiusVal),
            MakeSliderRow("透明度", 0.3, 1.0, 0.05, 1.0, out _opacitySlider, out _opacityVal,
                v => ((int)(v * 100)) + "%"),
            MakeSliderRow("背景", 0.1, 1.0, 0.05, 1.0, out _bgOpacitySlider, out _bgOpacityVal,
                v => ((int)(v * 100)) + "%"),
        }));

        // -- Size card --
        panel.Children.Add(MakeCard("尺寸", new UIElement[]
        {
            MakeSliderRow("宽度", 280, 640, 10, 400, out _islandWidthSlider, out _islandWidthVal),
            MakeSliderRow("高度", 48, 96, 2, 72, out _islandHeightSlider, out _islandHeightVal),
        }));

        // -- Position card --
        panel.Children.Add(MakeCard("位置", new UIElement[]
        {
            MakePositionGrid(),
            MakeSliderRow("水平偏移", -400, 400, 10, 0, out _offsetXSlider, out _offsetXVal),
            MakeSliderRow("垂直偏移", -400, 400, 10, 0, out _offsetYSlider, out _offsetYVal),
        }));

        // -- Behavior card --
        panel.Children.Add(MakeCard("行为", new UIElement[]
        {
            MakeSliderRow("显示时长", 1, 10, 0.5, 3.0, out _hideDelaySlider, out _hideDelayVal,
                v => v.ToString("F1") + "s"),
            MakeToggleGridRow("始终显示", out _alwaysVisibleToggle),
            MakeComboGridRow("显示策略", out _displayStrategyCombo,
                new[] { ("只显示最新", "LatestOnly"), ("同时显示多个", "Multiple") }),
        }));

        // -- Rim card --
        panel.Children.Add(MakeCard("环绕微光", new UIElement[]
        {
            MakeComboGridRow("流转模式", out _rimModeCombo,
                new[] { ("始终流转", "Always"), ("新状态流转", "Event"), ("仅插件流转", "Plugin") }),
        }));
    }

    private UIElement MakeCard(string title, UIElement[] rows)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x8A, 0x90, 0xA0)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            Margin = new WpfThickness(0, 0, 0, 8)
        });
        foreach (var row in rows)
            stack.Children.Add(row);

        return new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new WpfThickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new WpfThickness(10, 8, 10, 8),
            Margin = new WpfThickness(0, 0, 0, 8),
            Child = stack
        };
    }

    private UIElement MakeSliderRow(string label, double min, double max, double tick, double value,
        out Slider slider, out TextBlock valText, Func<double, string>? fmt = null)
    {
        fmt ??= v => v.ToString("F0");
        var grid = new Grid { Margin = new WpfThickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xB0, 0xB8, 0xC8)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelBlock, 0);

        slider = new Slider
        {
            Minimum = min, Maximum = max,
            TickFrequency = tick, IsSnapToTickEnabled = true,
            Value = value, Margin = new WpfThickness(6, 0, 0, 0)
        };
        Grid.SetColumn(slider, 1);

        valText = new TextBlock
        {
            Text = fmt(value),
            FontSize = 10,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x5A, 0x60, 0x70)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(valText, 2);

        var sliderCtrl = slider;
        var valTextCtrl = valText;
        slider.ValueChanged += (_, e) =>
        {
            valTextCtrl.Text = fmt(e.NewValue);
            Setting_ValueChanged(sliderCtrl, e);
        };

        grid.Children.Add(labelBlock);
        grid.Children.Add(slider);
        grid.Children.Add(valText);
        return grid;
    }

    private UIElement MakeToggleGridRow(string label, out WpfToggleButton toggle)
    {
        var grid = new Grid { Margin = new WpfThickness(0, 0, 0, 6), Height = 26 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xB0, 0xB8, 0xC8)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelBlock, 0);

        toggle = new WpfToggleButton
        {
            Width = 40, Height = 22,
            Style = (Style)FindResource("Toggle"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var toggleCtrl = toggle;
        toggle.Checked += (_, _) => Setting_ToggleChanged(toggleCtrl, true);
        toggle.Unchecked += (_, _) => Setting_ToggleChanged(toggleCtrl, false);
        Grid.SetColumn(toggle, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(toggle);
        return grid;
    }

    private UIElement MakeComboGridRow(string label, out WpfComboBox combo,
        (string display, string tag)[] items)
    {
        var grid = new Grid { Margin = new WpfThickness(0, 0, 0, 6), Height = 28 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xB0, 0xB8, 0xC8)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelBlock, 0);

        combo = new WpfComboBox { Margin = new WpfThickness(6, 0, 0, 0) };
        foreach (var (display, tag) in items)
            combo.Items.Add(new ComboBoxItem { Content = display, Tag = tag });
        var comboCtrl = combo;
        combo.SelectionChanged += (_, e) => Setting_ComboChanged(comboCtrl, e);
        Grid.SetColumn(combo, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(combo);
        return grid;
    }

    private UIElement MakePositionGrid()
    {
        var grid = new Grid { Margin = new WpfThickness(0, 0, 0, 8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _previewBorders = new Dictionary<string, Border>();

        void AddPos(string name, string label, int row, int col, out WpfRadioButton radio)
        {
            var border = new Border
            {
                Width = 46, Height = 20,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(MediaColor.FromRgb(0x1A, 0x1D, 0x24)),
                BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new WpfThickness(1)
            };
            _previewBorders[name] = border;

            var text = new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(0x5A, 0x60, 0x70)),
                FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new WpfThickness(0, 3, 0, 0)
            };

            var stack = new StackPanel { Margin = new WpfThickness(3) };
            stack.Children.Add(border);
            stack.Children.Add(text);

            radio = new WpfRadioButton
            {
                Tag = name,
                Style = (Style)FindResource("PosRadio"),
                Content = stack
            };
            radio.Checked += PositionRadio_Changed;
            Grid.SetRow(radio, row);
            Grid.SetColumn(radio, col);
            grid.Children.Add(radio);
        }

        AddPos("TopLeft", "左上", 0, 0, out _posTopLeft);
        AddPos("Top", "顶部", 0, 1, out _posTop);
        AddPos("TopRight", "右上", 0, 2, out _posTopRight);
        AddPos("BottomLeft", "左下", 1, 0, out _posBottomLeft);
        AddPos("Bottom", "底部", 1, 1, out _posBottom);
        AddPos("BottomRight", "右下", 1, 2, out _posBottomRight);

        return grid;
    }

    #endregion

    #region More Page (built programmatically)

    private void BuildMorePage()
    {
        var panel = PageMoreContent;

        var startupToggle = MakeBuiltInToggle("开机自启动", "登录 Windows 时自动运行");
        startupToggle.toggle.Checked += (_, _) => StartupToggle_Changed(true);
        startupToggle.toggle.Unchecked += (_, _) => StartupToggle_Changed(false);
        panel.Children.Add(startupToggle.element);

        var hideTrayToggle = MakeBuiltInToggle("隐藏托盘图标", "Ctrl+Alt+Click 灵动岛打开设置");
        hideTrayToggle.toggle.Checked += (_, _) => HideTrayToggle_Changed(true);
        hideTrayToggle.toggle.Unchecked += (_, _) => HideTrayToggle_Changed(false);
        panel.Children.Add(hideTrayToggle.element);

        // Hold-to-hide key
        var keyGrid = new Grid { Margin = new WpfThickness(0, 0, 0, 6), Height = 40 };
        keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        var keyTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        keyTextStack.Children.Add(new TextBlock
        {
            Text = "按住隐藏", FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xB0, 0xB8, 0xC8)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI")
        });
        keyTextStack.Children.Add(new TextBlock
        {
            Text = "按住按键临时隐藏", FontSize = 10,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x5A, 0x60, 0x70)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI")
        });
        Grid.SetColumn(keyTextStack, 0);

        var holdToHideCombo = new WpfComboBox { Margin = new WpfThickness(6, 0, 0, 0) };
        holdToHideCombo.SelectionChanged += HoldToHideKeyCombo_Changed;
        Grid.SetColumn(holdToHideCombo, 1);

        keyGrid.Children.Add(keyTextStack);
        keyGrid.Children.Add(holdToHideCombo);
        panel.Children.Add(MakeMoreCard(keyGrid));

        // Version
        var verGrid = new Grid { Margin = new WpfThickness(0, 0, 0, 8), Height = 24 };
        verGrid.Children.Add(new TextBlock
        {
            Text = "版本", FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xB0, 0xB8, 0xC8)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var verVal = new TextBlock
        {
            Text = "2.0.0", FontSize = 10,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x5A, 0x60, 0x70)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        verGrid.Children.Add(verVal);
        panel.Children.Add(MakeMoreCard(verGrid));

        // Reset button
        var resetBtn = new System.Windows.Controls.Button
        {
            Content = "重置所有设置",
            Height = 28, Padding = new WpfThickness(12, 0, 12, 0),
            FontSize = 10, FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xEF, 0x44, 0x44)),
            Background = WpfBrushes.Transparent,
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x30, 0xEF, 0x44, 0x44)),
            BorderThickness = new WpfThickness(1),
            Cursor = WpfCursors.Hand,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        resetBtn.Click += ResetBtn_Click;
        panel.Children.Add(resetBtn);

        // Store references for LoadValues
        _holdToHideKeyCombo = holdToHideCombo;
        _startupToggleRef = startupToggle.toggle;
        _hideTrayToggleRef = hideTrayToggle.toggle;
    }

    private WpfComboBox? _holdToHideKeyCombo;
    private WpfToggleButton? _startupToggleRef, _hideTrayToggleRef;

    private (UIElement element, WpfToggleButton toggle) MakeBuiltInToggle(string title, string desc)
    {
        var grid = new Grid { Margin = new WpfThickness(0, 0, 0, 6), Height = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = title, FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xB0, 0xB8, 0xC8)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI")
        });
        textStack.Children.Add(new TextBlock
        {
            Text = desc, FontSize = 10,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x5A, 0x60, 0x70)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI")
        });
        Grid.SetColumn(textStack, 0);

        var toggle = new WpfToggleButton
        {
            Width = 40, Height = 22,
            Style = (Style)FindResource("Toggle"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(toggle, 1);

        grid.Children.Add(textStack);
        grid.Children.Add(toggle);
        return (MakeMoreCard(grid), toggle);
    }

    private UIElement MakeMoreCard(UIElement content)
    {
        return new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new WpfThickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new WpfThickness(10, 8, 10, 8),
            Margin = new WpfThickness(0, 0, 0, 8),
            Child = content
        };
    }

    #endregion

    #region Load values

    private void LoadValuesFromSettings()
    {
        _isLoading = true;
        CoerceLayoutSettings();

        if (_cornerRadiusSlider != null) _cornerRadiusSlider.Value = _settings.CornerRadius;
        if (_cornerRadiusVal != null) _cornerRadiusVal.Text = _settings.CornerRadius.ToString("F0");

        if (_opacitySlider != null) _opacitySlider.Value = _settings.Opacity;
        if (_opacityVal != null) _opacityVal.Text = ((int)(_settings.Opacity * 100)) + "%";

        if (_bgOpacitySlider != null) _bgOpacitySlider.Value = _settings.BackgroundOpacity;
        if (_bgOpacityVal != null) _bgOpacityVal.Text = ((int)(_settings.BackgroundOpacity * 100)) + "%";

        if (_islandWidthSlider != null) _islandWidthSlider.Value = _settings.ExpandedMaxWidth;
        if (_islandWidthVal != null) _islandWidthVal.Text = _settings.ExpandedMaxWidth.ToString("F0");

        if (_islandHeightSlider != null) _islandHeightSlider.Value = _settings.ExpandedHeight;
        if (_islandHeightVal != null) _islandHeightVal.Text = _settings.ExpandedHeight.ToString("F0");

        if (_offsetXSlider != null) _offsetXSlider.Value = _settings.OffsetX;
        if (_offsetXVal != null) _offsetXVal.Text = _settings.OffsetX.ToString("F0");

        if (_offsetYSlider != null) _offsetYSlider.Value = _settings.OffsetY;
        if (_offsetYVal != null) _offsetYVal.Text = _settings.OffsetY.ToString("F0");

        if (_hideDelaySlider != null) _hideDelaySlider.Value = _settings.AutoHideDelayMs / 1000.0;
        if (_hideDelayVal != null) _hideDelayVal.Text = (_settings.AutoHideDelayMs / 1000.0).ToString("F1") + "s";

        if (_alwaysVisibleToggle != null) _alwaysVisibleToggle.IsChecked = _settings.AlwaysVisible;

        SetDisplayStrategyCombo(_settings.DisplayStrategy);
        SetRimModeCombo(_settings.RimMode);
        SetPositionRadio(_settings.Position);
        UpdatePositionPreview(_settings.Position);

        if (_hideTrayToggleRef != null) _hideTrayToggleRef.IsChecked = _settings.HideTrayIcon;

        SetHoldToHideKeyCombo(_settings.HoldToHideKey);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            if (_startupToggleRef != null) _startupToggleRef.IsChecked = key?.GetValue(AppName) != null;
        }
        catch { if (_startupToggleRef != null) _startupToggleRef.IsChecked = false; }

        _isLoading = false;
    }

    private void CoerceLayoutSettings()
    {
        _settings.CollapsedWidth = Math.Max(_settings.CollapsedWidth, IslandPresentationFactory.MinimumCollapsedWidth);
        _settings.CollapsedHeight = Math.Max(_settings.CollapsedHeight, IslandPresentationFactory.MinimumCollapsedHeight);
        _settings.ExpandedMaxWidth = Math.Max(_settings.ExpandedMaxWidth, IslandPresentationFactory.MinimumExpandedWidth);
        _settings.ExpandedHeight = Math.Clamp(
            Math.Max(_settings.ExpandedHeight, IslandPresentationFactory.MinimumExpandedHeight),
            IslandPresentationFactory.MinimumExpandedHeight,
            IslandPresentationFactory.MaximumExpandedHeight);
        _settings.CornerRadius = Math.Max(18, _settings.CornerRadius);
    }

    private void SetPositionRadio(string position)
    {
        if (_posTop != null) _posTop.IsChecked = position == "Top";
        if (_posBottom != null) _posBottom.IsChecked = position == "Bottom";
        if (_posTopLeft != null) _posTopLeft.IsChecked = position == "TopLeft";
        if (_posTopRight != null) _posTopRight.IsChecked = position == "TopRight";
        if (_posBottomLeft != null) _posBottomLeft.IsChecked = position == "BottomLeft";
        if (_posBottomRight != null) _posBottomRight.IsChecked = position == "BottomRight";
    }

    private void UpdatePositionPreview(string position)
    {
        if (_previewBorders == null) return;
        var activeColor = MediaColor.FromRgb(0x4F, 0x6A, 0xFF);
        var inactiveColor = MediaColor.FromRgb(0x1A, 0x1D, 0x24);
        var activeBorder = MediaColor.FromArgb(0x50, 0x4F, 0x6A, 0xFF);
        var inactiveBorder = MediaColor.FromArgb(0x18, 0xFF, 0xFF, 0xFF);

        foreach (var kv in _previewBorders)
        {
            var isActive = kv.Key == position;
            kv.Value.Background = new SolidColorBrush(isActive ? activeColor : inactiveColor);
            kv.Value.BorderBrush = new SolidColorBrush(isActive ? activeBorder : inactiveBorder);
        }
    }

    #endregion

    #region Setting handlers

    private void Setting_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;

        if (sender == _cornerRadiusSlider)
        {
            _settings.CornerRadius = Math.Max(18, Math.Round(e.NewValue));
            if (_cornerRadiusVal != null) _cornerRadiusVal.Text = _settings.CornerRadius.ToString("F0");
        }
        else if (sender == _opacitySlider)
        {
            _settings.Opacity = Math.Round(e.NewValue, 2);
            if (_opacityVal != null) _opacityVal.Text = ((int)(_settings.Opacity * 100)) + "%";
        }
        else if (sender == _bgOpacitySlider)
        {
            _settings.BackgroundOpacity = Math.Round(e.NewValue, 2);
            if (_bgOpacityVal != null) _bgOpacityVal.Text = ((int)(_settings.BackgroundOpacity * 100)) + "%";
        }
        else if (sender == _islandWidthSlider)
        {
            _settings.ExpandedMaxWidth = Math.Max(IslandPresentationFactory.MinimumExpandedWidth, Math.Round(e.NewValue));
            _settings.CollapsedWidth = Math.Clamp(Math.Round(_settings.ExpandedMaxWidth * 0.34),
                IslandPresentationFactory.MinimumCollapsedWidth, 220);
            if (_islandWidthVal != null) _islandWidthVal.Text = _settings.ExpandedMaxWidth.ToString("F0");
        }
        else if (sender == _islandHeightSlider)
        {
            _settings.ExpandedHeight = Math.Clamp(Math.Round(e.NewValue),
                IslandPresentationFactory.MinimumExpandedHeight, IslandPresentationFactory.MaximumExpandedHeight);
            _settings.CollapsedHeight = Math.Max(IslandPresentationFactory.MinimumCollapsedHeight,
                Math.Round(_settings.ExpandedHeight * 0.53));
            if (_islandHeightVal != null) _islandHeightVal.Text = _settings.ExpandedHeight.ToString("F0");
        }
        else if (sender == _offsetXSlider)
        {
            _settings.OffsetX = Math.Round(e.NewValue);
            if (_offsetXVal != null) _offsetXVal.Text = _settings.OffsetX.ToString("F0");
        }
        else if (sender == _offsetYSlider)
        {
            _settings.OffsetY = Math.Round(e.NewValue);
            if (_offsetYVal != null) _offsetYVal.Text = _settings.OffsetY.ToString("F0");
        }
        else if (sender == _hideDelaySlider)
        {
            var seconds = Math.Round(e.NewValue, 1);
            _settings.AutoHideDelayMs = (int)(seconds * 1000);
            if (_hideDelayVal != null) _hideDelayVal.Text = seconds.ToString("F1") + "s";
        }

        ScheduleSettingsSaveAndChanged();
    }

    private void Setting_ToggleChanged(WpfToggleButton toggle, bool value)
    {
        if (_isLoading) return;

        if (toggle == _alwaysVisibleToggle)
        {
            _settings.AlwaysVisible = value;
            _settings.Save();
            _onSettingsChanged?.Invoke();
        }
    }

    private void Setting_ComboChanged(WpfComboBox combo, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        if (combo == _displayStrategyCombo && combo.SelectedItem is ComboBoxItem di)
        {
            var tag = di.Tag?.ToString();
            var next = tag == nameof(IslandDisplayStrategy.Multiple)
                ? IslandDisplayStrategy.Multiple
                : IslandDisplayStrategy.LatestOnly;
            if (_settings.DisplayStrategy != next)
            {
                _settings.DisplayStrategy = next;
                _settings.Save();
                ScheduleSettingsChanged();
            }
        }
        else if (combo == _rimModeCombo && combo.SelectedItem is ComboBoxItem ri && ri.Tag is string mode)
        {
            if (_settings.RimMode != mode)
            {
                _settings.RimMode = mode;
                _settings.Save();
                ScheduleSettingsChanged();
            }
        }
    }

    private void SetDisplayStrategyCombo(IslandDisplayStrategy strategy)
    {
        if (_displayStrategyCombo == null) return;
        var tag = strategy.ToString();
        foreach (ComboBoxItem item in _displayStrategyCombo.Items)
        {
            if (item.Tag?.ToString() == tag) { _displayStrategyCombo.SelectedItem = item; return; }
        }
        _displayStrategyCombo.SelectedIndex = 0;
    }

    private void SetRimModeCombo(string mode)
    {
        if (_rimModeCombo == null) return;
        foreach (ComboBoxItem item in _rimModeCombo.Items)
        {
            if (item.Tag?.ToString() == mode) { _rimModeCombo.SelectedItem = item; return; }
        }
        _rimModeCombo.SelectedIndex = 1;
    }

    private void SetHoldToHideKeyCombo(string key)
    {
        if (_holdToHideKeyCombo == null) return;
        _holdToHideKeyCombo.Items.Clear();
        foreach (var value in HoldToHideKeyPolicy.Values)
            _holdToHideKeyCombo.Items.Add(new ComboBoxItem { Content = HoldToHideKeyPolicy.DisplayName(value), Tag = value });

        var coerced = HoldToHideKeyPolicy.Coerce(key);
        foreach (ComboBoxItem item in _holdToHideKeyCombo.Items)
        {
            if (item.Tag?.ToString() == coerced) { _holdToHideKeyCombo.SelectedItem = item; return; }
        }
        _holdToHideKeyCombo.SelectedIndex = 1;
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

    private void HoldToHideKeyCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (_holdToHideKeyCombo?.SelectedItem is not ComboBoxItem item) return;
        var key = HoldToHideKeyPolicy.Coerce(item.Tag?.ToString());
        if (_settings.HoldToHideKey == key) return;
        _settings.HoldToHideKey = key;
        _settings.Save();
        ScheduleSettingsChanged();
    }

    private void StartupToggle_Changed(bool value)
    {
        if (_isLoading) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null) return;
            if (value) key.SetValue(AppName, Environment.ProcessPath ?? "");
            else key.DeleteValue(AppName, false);
        }
        catch { }
    }

    private void HideTrayToggle_Changed(bool hide)
    {
        if (_isLoading) return;
        if (hide)
        {
            var result = WpfMessageBox.Show(
                "隐藏托盘图标后，可通过 Ctrl+Alt+Click 灵动岛本体重新打开设置面板。\n\n确认隐藏？",
                "确认隐藏托盘图标",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                _isLoading = true;
                if (_hideTrayToggleRef != null) _hideTrayToggleRef.IsChecked = false;
                _isLoading = false;
                return;
            }
        }
        _settings.HideTrayIcon = hide;
        _settings.Save();
        TrayIconVisibilityChanged?.Invoke(hide);
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        _settings.Save();
        LoadValuesFromSettings();
        _onSettingsChanged?.Invoke();
    }

    #endregion

    #region Plugins & Features

    private void LoadPluginList()
    {
        PageFeatures.ItemsSource = _monitorManager.Monitors;
        PagePlugins.ItemsSource = _pluginManager.Plugins;
    }

    private void PluginList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PagePlugins.SelectedItem is IIslandPlugin plugin)
            ShowPluginDetail(plugin);
    }

    private void MonitorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageFeatures.SelectedItem is ISystemMonitor monitor)
            ShowMonitorDetail(monitor);
    }

    private void ShowPluginDetail(IIslandPlugin plugin)
    {
        _detailPlugin = plugin;
        _detailMonitor = null;

        HideAllPages();
        PageDetail.Visibility = Visibility.Visible;
        BackBtn.Visibility = Visibility.Visible;

        DetailIcon.Text = plugin.Icon;
        DetailName.Text = plugin.Name;
        DetailDesc.Text = plugin.Description;
        DetailEnabledLabel.Text = "启用插件";
        DetailSettingsHeader.Text = "插件设置";
        _isLoading = true;
        DetailEnabledToggle.IsChecked = plugin.Enabled;
        _isLoading = false;

        PluginSettingsContainer.Children.Clear();
        if (plugin.Id == "clipboard") BuildClipboardPluginSettings(plugin);
        else if (plugin.Id == "media") BuildMediaPluginSettings(plugin);
        else if (plugin.Id == "agent-status") BuildAgentStatusPluginSettings();
    }

    private void ShowMonitorDetail(ISystemMonitor monitor)
    {
        _detailPlugin = null;
        _detailMonitor = monitor;

        HideAllPages();
        PageDetail.Visibility = Visibility.Visible;
        BackBtn.Visibility = Visibility.Visible;

        DetailIcon.Text = monitor.Icon;
        DetailName.Text = monitor.Name;
        DetailDesc.Text = monitor.Description;
        DetailEnabledLabel.Text = "启用功能";
        DetailSettingsHeader.Text = "功能设置";
        _isLoading = true;
        DetailEnabledToggle.IsChecked = monitor.Enabled;
        _isLoading = false;

        PluginSettingsContainer.Children.Clear();
        BuildMonitorFeatureSettings(monitor);
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        HideAllPages();
        PageIsland.Visibility = Visibility.Visible;
        NavIsland.IsChecked = true;
        _currentPage = "Island";
        PageFeatures.SelectedItem = null;
        PagePlugins.SelectedItem = null;
        _detailPlugin = null;
        _detailMonitor = null;
    }

    private void DetailEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (_detailPlugin is IIslandPlugin plugin)
            _pluginManager.SetEnabled(plugin, DetailEnabledToggle.IsChecked == true);
        else if (_detailMonitor is ISystemMonitor monitor)
            _monitorManager.SetEnabled(monitor, DetailEnabledToggle.IsChecked == true);
    }

    #endregion

    #region Plugin/Feature settings builders

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
        PluginSettingsContainer.Children.Add(CreateToggleRow("歌词显示",
            "有可用歌词时在悬停卡片中显示当前歌词行", cfg.ShowLyrics,
            val => { cfg.ShowLyrics = val; SchedulePluginSettingsSave(cfg); }));
        PluginSettingsContainer.Children.Add(CreateToggleRow("暂停显示",
            "媒体暂停时仍然显示当前曲目状态", cfg.ShowWhenPaused,
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
            "FluidBar", "agent-events", "inbox");
        PluginSettingsContainer.Children.Add(CreateInfoRow("Hook Inbox", inbox));
        PluginSettingsContainer.Children.Add(CreateInfoRow("事件格式",
            "写入 JSON: tool/status/project/summary/branch/durationMs"));
    }

    private void BuildMonitorFeatureSettings(ISystemMonitor monitor)
    {
        var feature = _settings.GetMonitorFeatureSettings(monitor.Id);
        PluginSettingsContainer.Children.Add(CreateToggleRow("悬停卡片",
            "鼠标移入灵动岛时放大为更明显的卡片状态", feature.HoverCardEnabled,
            val => { feature.HoverCardEnabled = val; ScheduleSettingsSaveAndChanged(); }));
        PluginSettingsContainer.Children.Add(CreateSliderRow(
            "显示时长", 1, 8, 0.5, feature.DisplayDurationMs / 1000.0,
            val => { feature.DisplayDurationMs = (int)(val * 1000); ScheduleSettingsSaveAndChanged(); },
            val => val.ToString("F1") + "s"));
        PluginSettingsContainer.Children.Add(CreateToggleRow("强调动画",
            "新状态到来时使用更明显的回弹和环绕微光", feature.EmphasizeTransitions,
            val => { feature.EmphasizeTransitions = val; ScheduleSettingsSaveAndChanged(); }));

        if (monitor is NotificationMonitor notifications)
        {
            PluginSettingsContainer.Children.Add(CreateButtonRow("通知权限",
                "请求 Windows 通知读取权限", "请求授权",
                async () =>
                {
                    var status = await notifications.RequestAccessAsync();
                    WpfMessageBox.Show($"Windows 通知权限状态：{status}",
                        "FluidBar 通知", MessageBoxButton.OK, MessageBoxImage.Information);
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

    #region Shared detail-row builders

    private UIElement CreateInfoRow(string label, string value)
    {
        var grid = new Grid { Margin = new WpfThickness(0, 0, 0, 6), MinHeight = 36 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(MakeLabel(label, 0));
        var val = new TextBlock
        {
            Text = value, FontSize = 10,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x5A, 0x60, 0x70)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(val, 1);
        grid.Children.Add(val);
        return MakeDetailCard(grid);
    }

    private UIElement CreateSliderRow(string label, double min, double max,
        double tick, double value, Action<double> onChanged,
        Func<double, string>? fmt = null)
    {
        fmt ??= v => v.ToString("F0");
        var grid = new Grid { Margin = new WpfThickness(0, 0, 0, 6), MinHeight = 34 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.Children.Add(MakeLabel(label, 0));

        var slider = new Slider
        {
            Minimum = min, Maximum = max, TickFrequency = tick,
            IsSnapToTickEnabled = true, Value = value,
            Margin = new WpfThickness(6, 0, 0, 0)
        };
        Grid.SetColumn(slider, 1);

        var valText = new TextBlock
        {
            Text = fmt(value), FontSize = 10,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x5A, 0x60, 0x70)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(valText, 2);

        slider.ValueChanged += (_, e) => { valText.Text = fmt(e.NewValue); onChanged(e.NewValue); };
        grid.Children.Add(slider);
        grid.Children.Add(valText);
        return MakeDetailCard(grid);
    }

    private UIElement CreateToggleRow(string label, string desc, bool value, Action<bool> onChanged)
    {
        var grid = new Grid { Margin = new WpfThickness(0, 0, 0, 6), MinHeight = 38 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xEC, 0xF0, 0xF5)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI")
        });
        textStack.Children.Add(new TextBlock
        {
            Text = desc, FontSize = 10,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x5A, 0x60, 0x70)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(textStack, 0);

        var toggle = new WpfToggleButton
        {
            Width = 40, Height = 22, IsChecked = value,
            Style = (Style)FindResource("Toggle"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new WpfThickness(8, 0, 0, 0)
        };
        toggle.Checked += (_, _) => onChanged(true);
        toggle.Unchecked += (_, _) => onChanged(false);
        Grid.SetColumn(toggle, 1);

        grid.Children.Add(textStack);
        grid.Children.Add(toggle);
        return MakeDetailCard(grid);
    }

    private UIElement CreateButtonRow(string label, string desc, string buttonText, Func<Task> onClick)
    {
        var grid = new Grid { Margin = new WpfThickness(0, 0, 0, 6), MinHeight = 42 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xEC, 0xF0, 0xF5)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI")
        });
        textStack.Children.Add(new TextBlock
        {
            Text = desc, FontSize = 10,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x5A, 0x60, 0x70)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(textStack, 0);

        var btn = new System.Windows.Controls.Button
        {
            Content = buttonText, MinWidth = 64, Height = 26,
            FontSize = 10, Margin = new WpfThickness(8, 0, 0, 0)
        };
        btn.Click += async (_, _) => await onClick();
        Grid.SetColumn(btn, 1);

        grid.Children.Add(textStack);
        grid.Children.Add(btn);
        return MakeDetailCard(grid);
    }

    private UIElement MakeDetailCard(UIElement content)
    {
        return new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new WpfThickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new WpfThickness(10, 6, 10, 6),
            Margin = new WpfThickness(0, 0, 0, 6),
            Child = content
        };
    }

    private TextBlock MakeLabel(string text, int column)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xB0, 0xB8, 0xC8)),
            FontFamily = new WpfFontFamily("Segoe UI, Microsoft YaHei UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, column);
        return tb;
    }

    #endregion
}
