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

        // 全局异常捕获（诊断崩溃根因）
        DispatcherUnhandledException += (_, args) =>
            WriteCrashLog("Dispatcher", args.Exception);
        System.AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog("AppDomain", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            WriteCrashLog("TaskScheduler", args.Exception);

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
        _agentStatusPlugin.HooksGuardEnabled = _settings.AgentHooksGuardEnabled;
        _agentStatusPlugin.HooksGuardIntervalMs = _settings.AgentHooksGuardIntervalMs;
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

        // 启动时自动检查更新（静默，检测到新版本以灵动岛提示一次）
        if (_settings.AutoUpdateCheck)
        {
            System.Windows.Threading.DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromSeconds(4) };
            updateTimer.Tick += async (_, _) =>
            {
                updateTimer.Stop();
                await CheckUpdateOnStartupAsync();
            };
            updateTimer.Start();
        }

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

    private static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FluidBar_Crash.log");
            var content = $"[{System.DateTime.Now:HH:mm:ss.fff}] {source}\r\n{ex}\r\n\r\n";
            System.IO.File.AppendAllText(path, content);
        }
        catch { }
    }

    /// <summary>
    /// 启动时静默检查更新，发现新版本自动下载安装包并提示安装。
    /// </summary>
    internal static string? PendingUpdateInstallerPath;

    private async Task CheckUpdateOnStartupAsync()
    {
        try
        {
            var current = GetCurrentVersion();
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FluidBar-Settings");
            var resp = await http.GetStringAsync("https://api.github.com/repos/Doulor/FluidBar/releases/latest");
            using var json = System.Text.Json.JsonDocument.Parse(resp);
            if (!json.RootElement.TryGetProperty("tag_name", out var tag)) return;
            var latestStr = tag.GetString()?.TrimStart('v') ?? "";
            if (!System.Version.TryParse(latestStr, out var latest) ||
                !System.Version.TryParse(current, out var cur)) return;
            if (latest <= cur) return;

            // 扫描 assets 找安装包
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

            if (string.IsNullOrEmpty(assetUrl))
            {
                // 没有安装包，提示手动下载
                var htmlUrl = json.RootElement.TryGetProperty("html_url", out var u)
                    ? u.GetString() ?? "https://github.com/Doulor/FluidBar/releases"
                    : "https://github.com/Doulor/FluidBar/releases";
                _bus?.Publish(new IslandEvent(
                    "update",
                    $"发现新版本 v{latestStr}",
                    "点击前往 GitHub 下载",
                    "info"));
                return;
            }

            // 提示正在下载
            _bus?.Publish(new IslandEvent(
                "update",
                $"发现新版本 v{latestStr}",
                "正在后台下载安装包…",
                "info"));

            // 后台下载安装包（带进度）
            var fileName = string.IsNullOrEmpty(assetName) ? "FluidBar-update.exe" : assetName;
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);
            using var dlHttp = new System.Net.Http.HttpClient();
            dlHttp.Timeout = TimeSpan.FromMinutes(10);
            dlHttp.DefaultRequestHeaders.UserAgent.ParseAdd("FluidBar-Settings");
            using var dlResp = await dlHttp.GetAsync(assetUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            dlResp.EnsureSuccessStatusCode();
            var totalBytes = dlResp.Content.Headers.ContentLength ?? 0;
            await using var contentStream = await dlResp.Content.ReadAsStreamAsync();
            await using var fs = System.IO.File.Create(tempPath);
            var buffer = new byte[81920];
            long downloaded = 0;
            int bytesRead;
            var lastPublishTick = System.Environment.TickCount64;
            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloaded += bytesRead;
                var now = System.Environment.TickCount64;
                if (now - lastPublishTick >= 500 && totalBytes > 0)
                {
                    lastPublishTick = now;
                    var percent = (int)Math.Clamp(downloaded * 100 / totalBytes, 0, 100);
                    _bus?.Publish(new IslandEvent(
                        "update",
                        $"下载新版本 v{latestStr}",
                        $"{percent}%",
                        "info",
                        new IslandEventPayload(Kind: IslandEventKind.Progress, ProgressPercent: percent)));
                }
            }
            await fs.FlushAsync();

            PendingUpdateInstallerPath = tempPath;

            // 下载完成，提示点击安装
            _bus?.Publish(new IslandEvent(
                "update",
                $"新版本 v{latestStr} 已就绪",
                "点击安装并重启",
                "info"));
        }
        catch
        {
            // 静默失败：启动时检查不弹错误提示
        }
    }

    private static string GetCurrentVersion()
    {
        try
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "2.0.0";
        }
        catch { return "2.0.0"; }
    }
}
