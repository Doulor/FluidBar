using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FluidBar;

/// <summary>
/// 剪贴板监听插件 - 监听复制操作并触发灵动岛显示
/// </summary>
public sealed class ClipboardPlugin : IIslandPlugin
{
    public string Id => "clipboard";
    public string Name => "剪贴板";
    public string Description => "复制文本时在灵动岛上显示内容，支持长文本滚动";
    public string Icon => "\uE16F"; // Segoe MDL2 Paste
    public bool Enabled { get; set; } = true;
    public IPluginConfig? Config => _config;
    public event Action<IslandEvent>? EventTriggered;

    private ClipboardPluginConfig? _config;
    private ClipboardPluginSettings _settings;
    private HwndSource? _hwndSource;
    private Window? _window;
    private string _lastText = string.Empty;
    private bool _isRunning;

    private const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    public ClipboardPlugin()
    {
        _settings = ClipboardPluginSettings.Load();
    }

    public void Initialize()
    {
        _config = new ClipboardPluginConfig(_settings);
    }

    public void Start(Window window)
    {
        if (_isRunning) return;
        _window = window;
        var helper = new WindowInteropHelper(window);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);
        AddClipboardFormatListener(helper.Handle);
        _isRunning = true;
    }

    public void Start()
    {
        if (_window != null) Start(_window);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            RemoveClipboardFormatListener(_hwndSource.Handle);
            _hwndSource = null;
        }
        _isRunning = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            TryReadClipboard();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void TryReadClipboard()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text) && text != _lastText)
                {
                    _lastText = text;
                    EventTriggered?.Invoke(new IslandEvent(
                        Source: Id,
                        Title: "已复制",
                        Content: text,
                        IconKind: "clipboard"));
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _config?.Save();
    }
}

/// <summary>
/// 剪贴板插件配置面板数据
/// </summary>
public sealed class ClipboardPluginConfig : IPluginConfig
{
    public string Title => "剪贴板设置";

    private readonly ClipboardPluginSettings _settings;

    public ClipboardPluginConfig(ClipboardPluginSettings settings)
    {
        _settings = settings;
    }

    public int MinFullDisplayChars
    {
        get => _settings.MinFullDisplayChars;
        set => _settings.MinFullDisplayChars = value;
    }

    public int DisplayDurationMs
    {
        get => _settings.DisplayDurationMs;
        set => _settings.DisplayDurationMs = value;
    }

    public double ScrollSpeed
    {
        get => _settings.ScrollSpeed;
        set => _settings.ScrollSpeed = value;
    }

    public object CreateSettingsPanel() => _settings;

    public void Save() => _settings.Save();

    public void Load() { }

    public void ResetToDefaults()
    {
        _settings.ResetToDefaults();
    }
}
