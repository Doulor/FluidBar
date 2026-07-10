using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FluidBar;

/// <summary>
/// 亚克力背景辅助窗口。
/// 主窗口是分层窗口（AllowsTransparency="True"），系统亚克力模糊无法在
/// 分层窗口上裁出胶囊形状（会糊满整个矩形窗口）；因此用这个独立的非分层
/// 无边框窗口承载 SetWindowCompositionAttribute 亚克力，并用 SetWindowRgn
/// 裁出与胶囊一致的圆角矩形，由主窗口每帧同步其屏幕位置与尺寸。
/// 窗口鼠标穿透、不可激活、不出现在 Alt-Tab。
/// </summary>
public sealed class AcrylicBackdropWindow : Window
{
    private IntPtr _hwnd;
    private bool _acrylicApplied;
    private bool _shown;
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private int _lastW = -1;
    private int _lastH = -1;
    private int _lastRadius = -1;

    /// <summary>亚克力 API 需要 Win10 1803（17134）及以上。</summary>
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134);

    /// <summary>SetWindowCompositionAttribute 是否成功生效。</summary>
    public bool AcrylicApplied => _acrylicApplied;

    public AcrylicBackdropWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Width = 10;
        Height = 10;
        Left = -32000;
        Top = -32000;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE,
            ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
        // 初始位置在屏外（构造函数中 Left/Top = -32000），首次 UpdateBounds 时移入
    }

    /// <summary>
    /// 应用亚克力着色。opacity 控制着色层浓度（0 = 纯模糊，1 = 纯色遮盖）。
    /// </summary>
    public void ApplyTint(System.Windows.Media.Color color, double opacity)
    {
        if (_hwnd == IntPtr.Zero)
            return;

        // GradientColor 为 ABGR；alpha 为 0 时系统会整体禁用亚克力，最低保留 1
        var alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255), 1, 255);
        var gradient = (uint)alpha << 24 | (uint)color.B << 16 | (uint)color.G << 8 | color.R;

        var accent = new AccentPolicy
        {
            // ACCENT_ENABLE_ACRYLICBLURBEHIND (4) 在部分 Win10/11 版本上有 DWM 拖动
            // 卡顿/冻结的已知问题；BLURBEHIND (3) 是更稳定的毛玻璃模糊，观感接近
            // 且不会卡死。
            AccentState = ACCENT_ENABLE_BLURBEHIND,
            AccentFlags = 2,
            GradientColor = gradient,
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size,
            };
            _acrylicApplied = SetWindowCompositionAttribute(_hwnd, ref data) != 0;
        }
        catch
        {
            _acrylicApplied = false;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// 同步位置/尺寸/圆角（全部为物理像素），并保持 Z 序紧贴 ownerAbove 之下。
    /// 未变化时不产生任何系统调用。
    /// </summary>
    public void UpdateBounds(IntPtr ownerAbove, int x, int y, int w, int h, int cornerRadius)
    {
        if (_hwnd == IntPtr.Zero || !_acrylicApplied || w <= 0 || h <= 0)
            return;

        var sizeChanged = w != _lastW || h != _lastH || cornerRadius != _lastRadius;
        if (sizeChanged)
        {
            // 系统接管 region 句柄的生命周期，无需 DeleteObject
            var rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, cornerRadius * 2, cornerRadius * 2);
            SetWindowRgn(_hwnd, rgn, true);
        }

        var moved = x != _lastX || y != _lastY;
        if (moved || sizeChanged || !_shown)
        {
            SetWindowPos(_hwnd, ownerAbove, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            _shown = true;
        }

        _lastX = x;
        _lastY = y;
        _lastW = w;
        _lastH = h;
        _lastRadius = cornerRadius;
    }

    public void HideBackdrop()
    {
        if (!_shown || _hwnd == IntPtr.Zero)
            return;
        _shown = false;
        ShowWindow(_hwnd, SW_HIDE);
    }

    #region Win32

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int SW_HIDE = 0;

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2,
        int cx, int cy);

    #endregion
}
