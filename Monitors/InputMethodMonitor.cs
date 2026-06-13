using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace FluidBar.Monitors;

/// <summary>
/// 输入法监控 - 检测同一窗口内的中/英输入法切换
/// 不会因切换窗口而误触发
/// </summary>
public sealed class InputMethodMonitor : ISystemMonitor
{
    public string Id => "inputmethod";
    public string Name => "输入法指示";
    public string Description => "切换输入法中英文模式时显示当前状态";
    public string Icon => "\uE765";
    public bool Enabled { get; set; } = true;
    public event Action<IslandEvent>? EventTriggered;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_IME_CONTROL = 0x0283;
    private const uint IMC_GETCONVERSIONMODE = 0x0001;
    private const uint IMC_GETOPENSTATUS = 0x0005;
    private const int IME_CMODE_NATIVE = 0x0001;

    private DispatcherTimer? _timer;
    private bool _isRunning;

    // 状态追踪
    private IntPtr _lastHwnd = IntPtr.Zero;       // 上次检测的窗口句柄
    private bool _lastIsChinese;                    // 上次窗口的 IME 状态
    private int _windowStableCount;                 // 窗口稳定计数
    private bool _initialized;                      // 是否已完成初始化
    private bool _lastFiredIsChinese;               // 上次触发事件时的状态（防重复触发）
    private DateTime _lastFireTime = DateTime.MinValue;

    private const int WindowStableThreshold = 1;    // 窗口切换后只需 1 轮确认
    private const int PollIntervalMs = 150;          // 轮询间隔 150ms
    private const int DebounceMs = 200;              // 防抖间隔 200ms

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _initialized = false;
        _lastHwnd = IntPtr.Zero;
        _windowStableCount = 0;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
        };
        _timer.Tick += (_, _) => CheckInputMethod();
        _timer.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        _timer?.Stop();
        _timer = null;
    }

    private void CheckInputMethod()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            // 获取当前窗口的 IME 状态
            var isChinese = GetImeChineseMode(hwnd);

            // 窗口切换了：静默更新状态，不触发事件
            if (hwnd != _lastHwnd)
            {
                _lastHwnd = hwnd;
                _lastIsChinese = isChinese;
                _lastFiredIsChinese = isChinese;
                _windowStableCount = 0;
                _initialized = true;
                return;
            }

            // 窗口刚稳定，等待 WindowStableThreshold 轮
            if (_windowStableCount < WindowStableThreshold)
            {
                _windowStableCount++;
                _lastIsChinese = isChinese;
                _lastFiredIsChinese = isChinese;
                if (!_initialized)
                {
                    _initialized = true;
                }
                return;
            }

            // 窗口已稳定，检测 IME 状态变化
            if (!_initialized)
            {
                _lastIsChinese = isChinese;
                _lastFiredIsChinese = isChinese;
                _initialized = true;
                return;
            }

            // 状态确实改变了 + 与上次触发时不同 + 满足防抖间隔
            if (isChinese != _lastIsChinese && isChinese != _lastFiredIsChinese)
            {
                var now = DateTime.Now;
                if ((now - _lastFireTime).TotalMilliseconds >= DebounceMs)
                {
                    _lastFireTime = now;
                    _lastFiredIsChinese = isChinese;
                    var label = isChinese ? "中" : "英";
                    EventTriggered?.Invoke(
                        new IslandEvent(Id, "输入法", label, "inputmethod"));
                }
            }

            _lastIsChinese = isChinese;
        }
        catch { }
    }

    /// <summary>
    /// 判断指定窗口是否处于中文输入模式
    /// </summary>
    private static bool GetImeChineseMode(IntPtr hwnd)
    {
        var threadId = GetWindowThreadProcessId(hwnd, out _);
        var hkl = GetKeyboardLayout(threadId);
        var langId = (ushort)((long)hkl & 0xFFFF);

        // 非中文键盘布局 → 英文模式
        var isChineseLayout = langId is 0x0804 or 0x0404 or 0x0C04 or 0x1004 or 0x1404;
        if (!isChineseLayout)
            return false;

        // 中文布局内，检查 IME 转换模式
        var imeWnd = ImmGetDefaultIMEWnd(hwnd);
        if (imeWnd == IntPtr.Zero)
            return false;

        var openStatus = SendMessage(imeWnd, WM_IME_CONTROL,
            (IntPtr)IMC_GETOPENSTATUS, IntPtr.Zero);
        if (openStatus == IntPtr.Zero)
            return false;

        var convMode = SendMessage(imeWnd, WM_IME_CONTROL,
            (IntPtr)IMC_GETCONVERSIONMODE, IntPtr.Zero);
        return (convMode.ToInt32() & IME_CMODE_NATIVE) != 0;
    }

    public void Dispose() => Stop();
}
