using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    private int _settingsApplyToken;
    private const string StartupRegistryKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "FluidBar";

    // 位置预览 Border 引用
    private Dictionary<string, Border>? _previewBorders;

    public event Action<bool>? TrayIconVisibilityChanged;

    public SettingsWindow(FluidBarSettings settings, PluginManager pluginManager,
        SystemMonitorManager monitorManager, Action onSettingsChanged)
    {
        _isLoading = true;
        _settings = settings;
        _pluginManager = pluginManager;
        _monitorManager = monitorManager;
        _onSettingsChanged = onSettingsChanged;
        InitializeComponent();
    }

    private void ScheduleSettingsChanged()
    {
        var token = ++_settingsApplyToken;
        Dispatcher.BeginInvoke((Action)(() =>
        {
            if (token == _settingsApplyToken)
                _onSettingsChanged?.Invoke();
        }), DispatcherPriority.Background);
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
        _isLoading = false;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(
            0, TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fadeOut);
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

        var activeColor = MediaColor.FromRgb(10, 132, 255);
        var inactiveColor = MediaColor.FromRgb(44, 44, 46);
        var activeBorder = MediaColor.FromArgb(80, 10, 132, 255);
        var inactiveBorder = MediaColor.FromArgb(25, 255, 255, 255);

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

        _settings.Save();
        _onSettingsChanged?.Invoke();
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
        DetailEnabledToggle.IsChecked = plugin.Enabled;

        PluginSettingsContainer.Children.Clear();

        if (plugin.Id == "clipboard")
        {
            BuildClipboardPluginSettings(plugin);
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
            val => { cfg.MinFullDisplayChars = (int)val; cfg.Save(); }));

        PluginSettingsContainer.Children.Add(CreateSliderRow(
            "停留时间", 1, 15, 0.5, cfg.DisplayDurationMs / 1000.0,
            val => { cfg.DisplayDurationMs = (int)(val * 1000); cfg.Save(); },
            val => val.ToString("F1") + "s"));

        PluginSettingsContainer.Children.Add(CreateSliderRow(
            "滚动速度", 0.5, 5, 0.5, cfg.ScrollSpeed,
            val => { cfg.ScrollSpeed = val; cfg.Save(); },
            val => val.ToString("F1") + "px"));
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
            Foreground = new SolidColorBrush(MediaColor.FromRgb(216, 216, 220)),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI")
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(122, 122, 130)),
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
        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var transform = new TransformGroup();
        transform.Children.Add(scale);
        transform.Children.Add(translate);

        var row = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(MediaColor.FromArgb(12, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(16, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = transform,
            Child = content
        };

        row.MouseEnter += (_, _) =>
        {
            row.Background = new SolidColorBrush(MediaColor.FromArgb(24, 255, 255, 255));
            AnimateTransform(scale, ScaleTransform.ScaleXProperty, 1.01, 140);
            AnimateTransform(scale, ScaleTransform.ScaleYProperty, 1.01, 140);
            AnimateTransform(translate, TranslateTransform.XProperty, 2, 140);
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = new SolidColorBrush(MediaColor.FromArgb(12, 255, 255, 255));
            AnimateTransform(scale, ScaleTransform.ScaleXProperty, 1, 160);
            AnimateTransform(scale, ScaleTransform.ScaleYProperty, 1, 160);
            AnimateTransform(translate, TranslateTransform.XProperty, 0, 160);
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
        DetailEnabledToggle.IsChecked = monitor.Enabled;

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
            val => { feature.HoverCardEnabled = val; _settings.Save(); }));

        PluginSettingsContainer.Children.Add(CreateSliderRow(
            "显示时长", 1, 8, 0.5, feature.DisplayDurationMs / 1000.0,
            val =>
            {
                feature.DisplayDurationMs = (int)(val * 1000);
                _settings.Save();
            },
            val => val.ToString("F1") + "s"));

        PluginSettingsContainer.Children.Add(CreateToggleRow(
            "强调动画",
            "新状态到来时使用更明显的回弹和环绕微光",
            feature.EmphasizeTransitions,
            val => { feature.EmphasizeTransitions = val; _settings.Save(); }));
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
