using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        CornerRadiusSlider.Value = _settings.CornerRadius;
        CornerRadiusValue.Text = _settings.CornerRadius.ToString("F0");

        OpacitySlider.Value = _settings.Opacity;
        OpacityValue.Text = ((int)(_settings.Opacity * 100)) + "%";

        CollapsedWidthSlider.Value = _settings.CollapsedWidth;
        CollapsedWidthValue.Text = _settings.CollapsedWidth.ToString("F0");

        ExpandedWidthSlider.Value = _settings.ExpandedMaxWidth;
        ExpandedWidthValue.Text = _settings.ExpandedMaxWidth.ToString("F0");

        OffsetXSlider.Value = _settings.OffsetX;
        OffsetXValue.Text = _settings.OffsetX.ToString("F0");

        OffsetYSlider.Value = _settings.OffsetY;
        OffsetYValue.Text = _settings.OffsetY.ToString("F0");

        HideDelaySlider.Value = _settings.AutoHideDelayMs / 1000.0;
        HideDelayValue.Text =
            (_settings.AutoHideDelayMs / 1000.0).ToString("F1") + "s";

        AlwaysVisibleToggle.IsChecked = _settings.AlwaysVisible;
        HideTrayToggle.IsChecked = _settings.HideTrayIcon;

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
            _settings.CornerRadius = Math.Round(e.NewValue);
            CornerRadiusValue.Text = _settings.CornerRadius.ToString("F0");
        }
        else if (sender == OpacitySlider)
        {
            _settings.Opacity = Math.Round(e.NewValue, 2);
            OpacityValue.Text = ((int)(_settings.Opacity * 100)) + "%";
        }
        else if (sender == CollapsedWidthSlider)
        {
            _settings.CollapsedWidth = Math.Round(e.NewValue);
            CollapsedWidthValue.Text = _settings.CollapsedWidth.ToString("F0");
        }
        else if (sender == ExpandedWidthSlider)
        {
            _settings.ExpandedMaxWidth = Math.Round(e.NewValue);
            ExpandedWidthValue.Text = _settings.ExpandedMaxWidth.ToString("F0");
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
        MainTabs.Visibility = Visibility.Collapsed;
        PluginDetailPanel.Visibility = Visibility.Visible;

        BackBtn.Visibility = Visibility.Visible;
        LogoIcon.Visibility = Visibility.Collapsed;
        HeaderTitle.Text = plugin.Name;

        DetailIcon.Text = plugin.Icon;
        DetailName.Text = plugin.Name;
        DetailDesc.Text = plugin.Description;
        DetailEnabledToggle.IsChecked = plugin.Enabled;

        PluginSettingsContainer.Children.Clear();

        if (plugin.Id == "clipboard")
        {
            BuildClipboardPluginSettings(plugin);
        }
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

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
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

        return grid;
    }

    private void DetailEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (PluginList.SelectedItem is IIslandPlugin plugin)
        {
            var enabled = DetailEnabledToggle.IsChecked == true;
            _pluginManager.SetEnabled(plugin, enabled);
        }
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        PluginDetailPanel.Visibility = Visibility.Collapsed;
        MainTabs.Visibility = Visibility.Visible;

        BackBtn.Visibility = Visibility.Collapsed;
        LogoIcon.Visibility = Visibility.Visible;
        HeaderTitle.Text = "FluidBar";
        PluginList.SelectedItem = null;
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
        // Toggle 直接控制，不需要进入详情
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
