using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using FluidBar.Monitors;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace FluidBar;

public partial class App : Application
{
    private NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private EventBus? _bus;
    private PluginManager? _pluginManager;
    private SystemMonitorManager? _monitorManager;
    private ClipboardPlugin? _clipboardPlugin;
    private MediaPlugin? _mediaPlugin;
    private AgentStatusPlugin? _agentStatusPlugin;
    private FluidBarSettings? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = FluidBarSettings.Load();
        _bus = new EventBus();

        SetupTrayIcon();
        SetupThemeWatcher();

        // 创建主窗口
        _mainWindow = new MainWindow(_bus, _settings);
        _mainWindow.RequestOpenSettings += OpenSettings;
        _mainWindow.Show();

        // 初始化插件系统
        _pluginManager = new PluginManager(_bus, _settings);
        _clipboardPlugin = new ClipboardPlugin();
        _pluginManager.Register(_clipboardPlugin);
        _clipboardPlugin.AttachWindow(_mainWindow);
        _mediaPlugin = new MediaPlugin();
        _pluginManager.Register(_mediaPlugin);
        _agentStatusPlugin = new AgentStatusPlugin();
        _pluginManager.Register(_agentStatusPlugin);
        _pluginManager.StartAll();

        if (_clipboardPlugin.Config is ClipboardPluginConfig cfg)
        {
            _mainWindow.SetClipboardPluginSettings(
                (ClipboardPluginSettings)cfg.CreateSettingsPanel());
        }

        if (_mediaPlugin?.SessionProvider is IMediaSessionProvider mediaProvider)
        {
            _mainWindow.SetMediaSessionProvider(mediaProvider);
        }

        // 初始化系统监控
        _monitorManager = new SystemMonitorManager(_bus);
        _monitorManager.Register(new VolumeMonitor());
        _monitorManager.Register(new BatteryMonitor());
        _monitorManager.Register(new InputMethodMonitor());
        _monitorManager.Register(new LockKeyMonitor());
        _monitorManager.Register(new NetworkMonitor());
        _monitorManager.Register(new UsbMonitor());
        _monitorManager.Register(new BrightnessMonitor());
        _monitorManager.Register(new BluetoothMonitor());
        _monitorManager.Register(new ClockMonitor());
        _monitorManager.Register(new NotificationMonitor());

        // 延迟启动：确保 Window_Loaded 先完成（PositionWindow + ApplySettings）
        // 否则事件触发时窗口尚未就位，动画被 PositionWindow 覆盖
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => _monitorManager.StartAll()));

        // 启动时根据配置隐藏托盘图标
        if (_settings.HideTrayIcon && _trayIcon != null)
            _trayIcon.Visible = false;

        // 不再在启动时推送系统主题（避免弹出 "Dark"/"Light" 文字）
        // 系统主题变更由 SetupThemeWatcher 中的 UserPreferenceChanged 事件处理
    }

    #region 系统主题检测

    private void SetupThemeWatcher()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                // 系统主题可能已变化，通知灵动岛
                _bus?.Publish(new IslandEvent(
                    "system", "主题变更",
                    GetSystemTheme(), "info"));
            }
        };
    }

    /// <summary>
    /// 获取当前系统主题：Dark 或 Light
    /// </summary>
    public static string GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0 ? "Dark" : "Light";
        }
        catch { }
        return "Dark";
    }

    /// <summary>
    /// 获取系统主题对应的背景色
    /// </summary>
    public static string GetThemeBackgroundColor()
    {
        return GetSystemTheme() == "Dark" ? "#E8202022" : "#E0F0F0F5";
    }

    #endregion

    #region 托盘图标

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "FluidBar",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        var settingsItem = new ToolStripMenuItem("设置");
        settingsItem.Click += (_, _) => OpenSettings();
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitApp();

        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => OpenSettings();
    }

    private static Icon CreateTrayIcon()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var path = new GraphicsPath();
        int cr = 7;
        var rect = new Rectangle(3, 9, size - 6, 14);
        path.AddArc(rect.X, rect.Y, cr * 2, cr * 2, 180, 90);
        path.AddArc(rect.Right - cr * 2, rect.Y, cr * 2, cr * 2, 270, 90);
        path.AddArc(rect.Right - cr * 2, rect.Bottom - cr * 2, cr * 2, cr * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - cr * 2, cr * 2, cr * 2, 90, 90);
        path.CloseFigure();

        using var glow = new SolidBrush(Color.FromArgb(60, 10, 132, 255));
        g.FillEllipse(glow, 4, 4, 24, 24);

        using var brush = new SolidBrush(Color.FromArgb(245, 0, 0, 0));
        g.FillPath(brush, path);

        using var rimPen = new Pen(Color.FromArgb(65, 255, 255, 255), 1f);
        g.DrawPath(rimPen, path);

        using var accent = new SolidBrush(Color.FromArgb(235, 10, 132, 255));
        g.FillEllipse(accent, 11, 13, 4, 4);
        using var soft = new SolidBrush(Color.FromArgb(210, 48, 209, 88));
        g.FillEllipse(soft, 17, 13, 4, 4);

        return Icon.FromHandle(bmp.GetHicon());
    }

    #endregion

    private void OpenSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(
                _settings!, _pluginManager!, _monitorManager!, OnSettingsChanged);
            _settingsWindow.TrayIconVisibilityChanged += OnTrayIconVisibilityChanged;
            _settingsWindow.IsVisibleChanged += (_, _) =>
            {
                if (_settingsWindow.IsVisible)
                    _mainWindow?.OnSettingsPanelOpened();
                else
                    _mainWindow?.OnSettingsPanelClosed();
            };
        }

        // 重置透明度（淡出动画可能将其设为 0）
        _settingsWindow.BeginAnimation(UIElement.OpacityProperty, null);
        _settingsWindow.Opacity = 1;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsChanged()
    {
        try
        {
            _mainWindow?.ApplySettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FluidBar] ApplySettings failed: {ex}");
        }
    }

    private void OnTrayIconVisibilityChanged(bool hide)
    {
        if (_trayIcon != null)
            _trayIcon.Visible = !hide;
    }

    private void ExitApp()
    {
        _monitorManager?.Dispose();
        _pluginManager?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _settingsWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _monitorManager?.Dispose();
        _pluginManager?.Dispose();
        base.OnExit(e);
    }
}
