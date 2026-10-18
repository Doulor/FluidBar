using System.Runtime.InteropServices;

namespace FluidBar.Platform.Windows.Interop;

/// <summary>
/// Hand-written Win32 interop for the platform layer. No UI framework types are referenced.
/// All declarations are internal: the public surface is the service layer, and the architecture
/// tests forbid P/Invoke outside <c>FluidBar.Platform.Windows</c>.
/// </summary>
internal static partial class NativeMethods
{
    // ---------------------------------------------------------------------
    // Window messages / styles
    // ---------------------------------------------------------------------
    internal const uint WM_NULL = 0x0000;
    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_CLOSE = 0x0010;
    internal const uint WM_QUIT = 0x0012;
    internal const uint WM_SETTINGCHANGE = 0x001A;
    internal const uint WM_SETFOCUS = 0x0007;
    internal const uint WM_KILLFOCUS = 0x0008;
    internal const uint WM_GETDLGCODE = 0x0087;
    internal const uint WM_GETOBJECT = 0x003D;
    internal const uint WM_MOUSEACTIVATE = 0x0021;
    internal const uint WM_INPUTLANGCHANGE = 0x0051;
    internal const uint WM_DISPLAYCHANGE = 0x007E;
    internal const uint WM_NCHITTEST = 0x0084;
    internal const uint WM_POWERBROADCAST = 0x0218;
    internal const uint WM_DEVICECHANGE = 0x0219;
    internal const uint WM_WTSSESSION_CHANGE = 0x02B1;
    internal const uint WM_DPICHANGED = 0x02E0;
    internal const uint WM_CLIPBOARDUPDATE = 0x031D;
    internal const uint WM_HOTKEY = 0x0312;
    internal const uint WM_IME_CONTROL = 0x0283;
    internal const uint WM_APP = 0x8000;
    internal const uint WM_USER = 0x0400;
    internal const uint WM_CONTEXTMENU = 0x007B;
    internal const uint WM_LBUTTONUP = 0x0202;
    internal const uint WM_LBUTTONDBLCLK = 0x0203;
    internal const uint WM_RBUTTONUP = 0x0205;
    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_KEYUP = 0x0101;
    internal const uint WM_SYSKEYDOWN = 0x0104;
    internal const uint WM_SYSKEYUP = 0x0105;
    internal const uint WM_SETCURSOR = 0x0020;
    internal const uint WM_PAINT = 0x000F;
    internal const uint WM_ERASEBKGND = 0x0014;
    internal const uint WM_NCDESTROY = 0x0082;
    internal const uint WM_MOUSEMOVE = 0x0200;
    internal const uint WM_LBUTTONDOWN = 0x0201;
    internal const uint WM_MOUSEWHEEL = 0x020A;
    internal const uint WM_MOUSEHWHEEL = 0x020E;
    internal const uint WM_CAPTURECHANGED = 0x0215;
    internal const uint WM_MOUSELEAVE = 0x02A3;

    internal const int WS_POPUP = unchecked((int)0x80000000);
    internal const int WS_EX_TOPMOST = 0x00000008;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    internal const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    internal const int GWL_EXSTYLE = -20;
    internal const int GWL_STYLE = -16;
    internal const int GWLP_WNDPROC = -4;
    internal const int GWLP_USERDATA = -21;

    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNOACTIVATE = 4;

    internal const uint CS_DBLCLKS = 0x0008;

    /// <summary><c>UpdateLayeredWindow</c> flag: use the per-pixel alpha in <c>BLENDFUNCTION</c>.</summary>
    internal const uint ULW_ALPHA = 0x00000002;
    internal const byte AC_SRC_OVER = 0x00;
    internal const byte AC_SRC_ALPHA = 0x01;
    internal const uint BI_RGB = 0x00000000;
    internal const uint DIB_RGB_COLORS = 0x00000000;
    internal const uint SRCCOPY = 0x00CC0020;

    internal const uint TME_LEAVE = 0x00000002;
    internal const int MK_LBUTTON = 0x0001;
    internal const int MK_RBUTTON = 0x0002;
    internal const int MK_SHIFT = 0x0004;
    internal const int MK_CONTROL = 0x0008;
    internal const int MK_MBUTTON = 0x0010;
    internal const int MK_XBUTTON1 = 0x0020;
    internal const int MK_XBUTTON2 = 0x0040;
    internal const int VK_LBUTTON = 0x01;
    internal const int VK_RBUTTON = 0x02;
    internal const int VK_MBUTTON = 0x04;
    internal const int VK_XBUTTON1 = 0x05;
    internal const int VK_XBUTTON2 = 0x06;
    internal const int VK_SHIFT = 0x10;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_MENU = 0x12;
    internal const int VK_TAB = 0x09;
    internal const int VK_RETURN = 0x0D;
    internal const int VK_ESCAPE = 0x1B;
    internal const int VK_SPACE = 0x20;
    internal const int VK_END = 0x23;
    internal const int VK_HOME = 0x24;
    internal const int VK_LEFT = 0x25;
    internal const int VK_UP = 0x26;
    internal const int VK_RIGHT = 0x27;
    internal const int VK_DOWN = 0x28;

    internal const int DLGC_WANTARROWS = 0x0001;
    internal const int DLGC_WANTTAB = 0x0002;
    internal const int DLGC_WANTALLKEYS = 0x0004;

    internal static readonly IntPtr IDC_ARROW = new(32512);
    internal static readonly IntPtr IDC_IBEAM = new(32513);
    internal static readonly IntPtr IDC_HAND = new(32649);
    internal static readonly IntPtr IDC_SIZEALL = new(32646);
    internal static readonly IntPtr IDC_SIZEWE = new(32644);
    internal static readonly IntPtr IDC_SIZENS = new(32645);

    internal const int HTNOWHERE = 0;
    internal const int HTCLIENT = 1;
    internal const int HTTRANSPARENT = -1;
    internal const int MA_NOACTIVATE = 3;

    internal static readonly IntPtr HWND_TOP = new(0);

    internal static readonly IntPtr HWND_MESSAGE = new(-3);
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOOWNERZORDER = 0x0200;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_HIDEWINDOW = 0x0080;

    internal const uint MONITOR_DEFAULTTONULL = 0x00000000;
    internal const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    internal const int MDT_EFFECTIVE_DPI = 0;
    internal const int MDT_ANGULAR_DPI = 1;
    internal const int MDT_RAW_DPI = 2;

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const int WH_KEYBOARD_LL = 13;
    internal const int HC_ACTION = 0;

    internal const int VK_CAPITAL = 0x14;
    internal const int VK_NUMLOCK = 0x90;
    internal const int VK_SCROLL = 0x91;

    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_OBJECT_IME_SHOW = 0x8027;
    internal const uint EVENT_OBJECT_IME_HIDE = 0x8028;
    internal const uint EVENT_OBJECT_IME_CHANGE = 0x8029;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    internal const uint SMTO_BLOCK = 0x0001;
    internal const uint SMTO_ABORTIFHUNG = 0x0002;

    internal const uint IMC_GETCONVERSIONMODE = 0x0001;
    internal const uint IMC_GETOPENSTATUS = 0x0005;
    internal const int IME_CMODE_NATIVE = 0x0001;

    internal const uint CF_TEXT = 1;
    internal const uint CF_BITMAP = 2;
    internal const uint CF_DIB = 8;
    internal const uint CF_UNICODETEXT = 13;
    internal const uint CF_HDROP = 15;

    internal const uint GMEM_MOVEABLE = 0x0002;

    internal const int ERROR_CLASS_ALREADY_EXISTS = 1410;

    /// <summary><c>CreateWaitableTimerEx</c> flag (Win10 1803+): sub-millisecond timer resolution.</summary>
    internal const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    internal const uint TIMER_ALL_ACCESS = 0x1F0003;
    internal const uint INFINITE = 0xFFFFFFFF;
    internal const uint WAIT_OBJECT_0 = 0;

    internal const uint IMAGE_ICON = 1;
    internal const uint LR_LOADFROMFILE = 0x0010;
    internal const uint LR_DEFAULTSIZE = 0x0040;
    internal const uint LR_SHARED = 0x8000;
    internal static readonly IntPtr IDI_APPLICATION = new(32512);

    internal const uint NIM_ADD = 0x00000000;
    internal const uint NIM_MODIFY = 0x00000001;
    internal const uint NIM_DELETE = 0x00000002;
    internal const uint NIM_SETVERSION = 0x00000004;
    internal const uint NIF_MESSAGE = 0x00000001;
    internal const uint NIF_ICON = 0x00000002;
    internal const uint NIF_TIP = 0x00000004;
    internal const uint NIF_STATE = 0x00000008;
    internal const uint NIF_INFO = 0x00000010;
    internal const uint NIF_SHOWTIP = 0x00000080;
    internal const uint NIS_HIDDEN = 0x00000001;
    internal const uint NIIF_INFO = 0x00000001;
    internal const uint NIIF_USER = 0x00000004;
    internal const uint NIN_SELECT = WM_USER;
    internal const uint NIN_KEYSELECT = WM_USER + 1;
    internal const uint NIN_BALLOONUSERCLICK = WM_USER + 5;
    internal const uint NOTIFYICON_VERSION_4 = 4;

    internal const uint MF_STRING = 0x00000000;
    internal const uint MF_SEPARATOR = 0x00000800;
    internal const uint MF_GRAYED = 0x00000001;
    internal const uint MF_DISABLED = 0x00000002;
    internal const uint MF_CHECKED = 0x00000008;
    internal const uint MF_DEFAULT = 0x00001000;
    internal const uint TPM_LEFTALIGN = 0x0000;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_NONOTIFY = 0x0080;
    internal const uint TPM_RETURNCMD = 0x0100;

    internal const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    internal const uint DBT_DEVICEARRIVAL = 0x8000;
    internal const uint DBT_DEVICEREMOVECOMPLETE = 0x8004;
    internal const uint DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;
    internal const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

    internal const uint PBT_APMPOWERSTATUSCHANGE = 0x000A;
    internal const uint PBT_APMSUSPEND = 0x0004;
    internal const uint PBT_APMRESUMEAUTOMATIC = 0x0012;
    internal const uint PBT_APMRESUMESUSPEND = 0x0007;
    internal const uint PBT_POWERSETTINGCHANGE = 0x8013;

    internal const uint SPI_GETHIGHCONTRAST = 0x0042;
    internal const uint HIGHCONTRASTON = 0x00000001;
    internal static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    internal const uint NOTIFY_FOR_THIS_SESSION = 0;
    internal const uint WTS_SESSION_LOCK = 0x7;
    internal const uint WTS_SESSION_UNLOCK = 0x8;
    internal const uint WTS_SESSION_LOGOFF = 0x6;

    internal const int QUNS_NOT_PRESENT = 1;
    internal const int QUNS_BUSY = 2;
    internal const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    internal const int QUNS_PRESENTATION_MODE = 4;
    internal const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
    internal const int QUNS_QUIET_TIME = 6;
    internal const int QUNS_APP = 7;

    internal const int S_OK = 0;
    internal const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);
    internal const uint CLSCTX_ALL = 0x17;
    internal const uint COINIT_APARTMENTTHREADED = 0x2;
    internal const uint COINIT_MULTITHREADED = 0x0;

    internal static readonly IntPtr HKEY_CURRENT_USER = new(unchecked((long)0x80000001));
    internal const uint KEY_QUERY_VALUE = 0x0001;
    internal const uint KEY_SET_VALUE = 0x0002;
    internal const uint KEY_CREATE_SUB_KEY = 0x0004;
    internal const uint KEY_NOTIFY = 0x0010;
    internal const uint KEY_READ = 0x20019;
    internal const uint KEY_WRITE = 0x20006;
    internal const uint REG_OPTION_NON_VOLATILE = 0x00000000;
    internal const uint REG_NOTIFY_CHANGE_NAME = 0x00000001;
    internal const uint REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;
    internal const uint REG_SZ = 1;
    internal const uint REG_EXPAND_SZ = 2;
    internal const uint REG_DWORD = 4;
    internal const uint REG_QWORD = 11;

    internal const int WLAN_CONNECTION_MODE_PROFILE = 0;
    internal const int WLAN_INTF_OPCODE_CURRENT_CONNECTION = 7;

    // ---------------------------------------------------------------------
    // Structs
    // ---------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int CX;
        public int CY;

        public SIZE(int cx, int cy)
        {
            CX = cx;
            CY = cy;
        }
    }

    /// <summary><c>BLENDFUNCTION</c> for <c>UpdateLayeredWindow(ULW_ALPHA)</c>; <c>Pack = 1</c> is required.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    /// <summary>
    /// <c>UPDATELAYEREDWINDOWINFO</c> for <c>UpdateLayeredWindowIndirect</c>. Pointer fields point at
    /// <see cref="POINT"/>/<see cref="SIZE"/>/<see cref="RECT"/>/<see cref="BLENDFUNCTION"/> values; the
    /// host builds them on the stack so the per-frame path allocates nothing.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct UPDATELAYEREDWINDOWINFO
    {
        public uint cbSize;
        public IntPtr hdcDst;
        public IntPtr pptDst;
        public IntPtr psize;
        public IntPtr hdcSrc;
        public IntPtr pptSrc;
        public uint crKey;
        public IntPtr pblend;
        public uint dwFlags;
        public IntPtr prcDirty;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIGHCONTRASTW
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        // Data follows; read manually.
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DevBroadcastDeviceInterface
    {
        public uint cbSize;
        public uint dbcc_devicetype;
        public uint dbcc_reserved;
        public Guid dbcc_classguid;
        // dbcc_name follows as a null-terminated WCHAR string.
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strInterfaceDescription;
        public uint isState;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WLAN_INTERFACE_INFO_LIST_HEADER
    {
        public uint dwNumberOfItems;
        public uint dwIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DOT11_SSID
    {
        public uint uSSIDLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ucSSID;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WLAN_ASSOCIATION_ATTRIBUTES
    {
        public DOT11_SSID dot11Ssid;
        public uint dot11BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] dot11Bssid;
        public uint dot11PhyType;
        public uint uDot11PhyIndex;
        public uint wlanSignalQuality;
        public uint ulRxRate;
        public uint ulTxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WLAN_SECURITY_ATTRIBUTES
    {
        public int bSecurityEnabled;
        public int bOneXEnabled;
        public uint dot11AuthAlgorithm;
        public uint dot11CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WLAN_CONNECTION_ATTRIBUTES
    {
        public uint isState;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strProfileName;
        public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
        public WLAN_SECURITY_ATTRIBUTES wlanSecurityAttributes;
    }

    // ---------------------------------------------------------------------
    // Delegates
    // ---------------------------------------------------------------------
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Signature for a window-procedure subclass hook (used by <c>IslandWindowInterop</c>).</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr WndProcHookDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WaitOrTimerProc(IntPtr context, bool timedOut);

    // ---------------------------------------------------------------------
    // user32.dll
    // ---------------------------------------------------------------------
    internal static class User32
    {
        private const string Lib = "user32.dll";

        [DllImport(Lib, SetLastError = true)]
        internal static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool UnregisterClassW([MarshalAs(UnmanagedType.LPWStr)] string lpClassName, IntPtr hInstance);

        [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateWindowExW(uint dwExStyle, [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
            [MarshalAs(UnmanagedType.LPWStr)] string? lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport(Lib)]
        internal static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport(Lib, CharSet = CharSet.Unicode)]
        internal static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport(Lib)]
        internal static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport(Lib)]
        internal static extern IntPtr DispatchMessageW(ref MSG lpMsg);

        [DllImport(Lib)]
        internal static extern void PostQuitMessage(int nExitCode);

        [DllImport(Lib)]
        internal static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax,
            uint wRemoveMsg);

        [DllImport(Lib)]
        internal static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport(Lib, CharSet = CharSet.Unicode)]
        internal static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport(Lib)]
        internal static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport(Lib)]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport(Lib)]
        internal static extern IntPtr GetFocus();

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport(Lib)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport(Lib)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport(Lib, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetClassNameW(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

        [DllImport(Lib)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport(Lib)]
        internal static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport(Lib)]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport(Lib)]
        internal static extern short GetKeyState(int nVirtKey);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport(Lib)]
        internal static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport(Lib)]
        internal static extern IntPtr WindowFromPoint(POINT point);

        [DllImport(Lib)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport(Lib)]
        internal static extern bool ReleaseCapture();

        [DllImport(Lib)]
        internal static extern IntPtr GetCapture();

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

        [DllImport(Lib)]
        internal static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

        [DllImport(Lib)]
        internal static extern IntPtr SetCursor(IntPtr hCursor);

        [DllImport(Lib)]
        internal static extern bool ValidateRect(IntPtr hWnd, IntPtr lpRect);

        [DllImport(Lib)]
        internal static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

        [DllImport(Lib)]
        internal static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool UpdateLayeredWindowIndirect(IntPtr hWnd, ref UPDATELAYEREDWINDOWINFO pULWInfo);

        [DllImport(Lib)]
        internal static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport(Lib)]
        internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport(Lib, EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport(Lib, EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        internal static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
            => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

        [DllImport(Lib, EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport(Lib, EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        internal static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
            => IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

        [DllImport(Lib)]
        internal static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam,
            IntPtr lParam);

        [DllImport(Lib)]
        internal static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport(Lib, SetLastError = true)]
        internal static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
            uint uFlags);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod,
            uint dwThreadId);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport(Lib)]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport(Lib)]
        internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport(Lib)]
        internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool CloseClipboard();

        [DllImport(Lib)]
        internal static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport(Lib)]
        internal static extern uint EnumClipboardFormats(uint format);

        [DllImport(Lib, SetLastError = true)]
        internal static extern uint GetClipboardSequenceNumber();

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool EmptyClipboard();

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport(Lib, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint RegisterClipboardFormatW([MarshalAs(UnmanagedType.LPWStr)] string lpszFormat);

        [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr LoadImageW(IntPtr hInst, [MarshalAs(UnmanagedType.LPWStr)] string name,
            uint type, int cx, int cy, uint fuLoad);

        [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr CreatePopupMenu();

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem,
            [MarshalAs(UnmanagedType.LPWStr)] string? lpNewItem);

        [DllImport(Lib, SetLastError = true)]
        internal static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

        [DllImport(Lib)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport(Lib)]
        internal static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, out bool pvParam, uint fWinIni);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, ref HIGHCONTRASTW pvParam, uint fWinIni);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, in Guid powerSettingGuid,
            uint flags);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr RegisterDeviceNotificationW(IntPtr hRecipient,
            in DevBroadcastDeviceInterface notificationFilter, uint flags);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool UnregisterDeviceNotification(IntPtr handle);

        [DllImport(Lib)]
        internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport(Lib)]
        internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport(Lib)]
        internal static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

        [DllImport(Lib, CharSet = CharSet.Unicode)]
        internal static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

        [DllImport(Lib)]
        internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum,
            IntPtr dwData);

        [DllImport(Lib, SetLastError = true)]
        internal static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport(Lib)]
        internal static extern int GetSystemMetrics(int nIndex);

        [DllImport(Lib)]
        internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        [DllImport(Lib)]
        internal static extern IntPtr GetThreadDesktop(uint dwThreadId);
    }

    // ---------------------------------------------------------------------
    // gdi32.dll
    // ---------------------------------------------------------------------
    internal static class Gdi32
    {
        private const string Lib = "gdi32.dll";

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int width, int height);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool DeleteObject(IntPtr hObject);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool DeleteDC(IntPtr hdc);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
            out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport(Lib)]
        internal static extern int GdiFlush();

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
            IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr CreateSolidBrush(uint color);

        [DllImport(Lib, SetLastError = true)]
        internal static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);
    }

    // ---------------------------------------------------------------------
    // shell32.dll
    // ---------------------------------------------------------------------
    internal static class Shell32
    {
        private const string Lib = "shell32.dll";

        [DllImport(Lib)]
        internal static extern int SHQueryUserNotificationState(out int pquns);

        [DllImport(Lib, CharSet = CharSet.Unicode)]
        internal static extern int SHQueryRecycleBinW([MarshalAs(UnmanagedType.LPWStr)] string? pszRootPath,
            IntPtr pSHQueryRBInfo);

        [DllImport(Lib, CharSet = CharSet.Unicode)]
        internal static extern uint DragQueryFileW(IntPtr hDrop, uint fileIndex,
            [Out] char[]? fileName, uint cch);
    }

    // ---------------------------------------------------------------------
    // kernel32.dll
    // ---------------------------------------------------------------------
    internal static class Kernel32
    {
        private const string Lib = "kernel32.dll";

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        [DllImport(Lib, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);

        [DllImport(Lib)]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport(Lib)]
        internal static extern uint GetCurrentProcessId();

        [DllImport(Lib)]
        internal static extern uint GetCurrentThreadId();

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState,
            [MarshalAs(UnmanagedType.LPWStr)] string? lpName);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool ResetEvent(IntPtr hEvent);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateWaitableTimerExW(IntPtr lpTimerAttributes, string? lpTimerName,
            uint dwFlags, uint dwDesiredAccess);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool SetWaitableTimer(IntPtr hTimer, ref long lpDueTime, int lPeriod,
            IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, bool fResume);

        [DllImport(Lib, SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport(Lib, SetLastError = true)]
        internal static extern IntPtr GlobalSize(IntPtr hMem);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool RegisterWaitForSingleObject(out IntPtr phNewWaitObject, IntPtr hObject,
            WaitOrTimerProc callback, IntPtr context, uint dwMilliseconds, uint dwFlags);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool UnregisterWaitEx(IntPtr waitHandle, IntPtr completionEvent);

        [DllImport(Lib)]
        internal static extern void GetSystemTimeAsFileTime(out long lpSystemTimeAsFileTime);
    }

    // ---------------------------------------------------------------------
    // advapi32.dll — registry watcher support
    // ---------------------------------------------------------------------
    internal static class Advapi32
    {
        private const string Lib = "advapi32.dll";

        [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int RegOpenKeyExW(IntPtr hKey, [MarshalAs(UnmanagedType.LPWStr)] string lpSubKey,
            uint ulOptions, uint samDesired, out IntPtr phkResult);

        [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int RegCreateKeyExW(IntPtr hKey, [MarshalAs(UnmanagedType.LPWStr)] string lpSubKey,
            uint reserved, [MarshalAs(UnmanagedType.LPWStr)] string? lpClass, uint dwOptions, uint samDesired,
            IntPtr lpSecurityAttributes, out IntPtr phkResult, out uint lpdwDisposition);

        [DllImport(Lib)]
        internal static extern int RegCloseKey(IntPtr hKey);

        [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int RegQueryValueExW(IntPtr hKey, [MarshalAs(UnmanagedType.LPWStr)] string lpValueName,
            IntPtr lpReserved, out uint lpType, byte[]? lpData, ref uint lpcbData);

        [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int RegSetValueExW(IntPtr hKey, [MarshalAs(UnmanagedType.LPWStr)] string lpValueName,
            uint reserved, uint dwType, byte[] lpData, uint cbData);

        [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int RegDeleteValueW(IntPtr hKey, [MarshalAs(UnmanagedType.LPWStr)] string lpValueName);

        [DllImport(Lib, SetLastError = true)]
        internal static extern int RegNotifyChangeKeyValue(IntPtr hKey, bool bWatchSubtree, uint dwNotifyFilter,
            IntPtr hEvent, bool fAsynchronous);
    }

    // ---------------------------------------------------------------------
    // shcore.dll
    // ---------------------------------------------------------------------
    internal static class Shcore
    {
        private const string Lib = "shcore.dll";

        [DllImport(Lib)]
        internal static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    }

    // ---------------------------------------------------------------------
    // dwmapi.dll
    // ---------------------------------------------------------------------
    internal static class DwmApi
    {
        private const string Lib = "dwmapi.dll";

        /// <summary>Blocks until the next composition frame is presented (the layered-window vsync clock).</summary>
        [DllImport(Lib)]
        internal static extern int DwmFlush();

        [DllImport(Lib)]
        internal static extern int DwmGetColorizationColor(out uint pcrColorization, [MarshalAs(UnmanagedType.Bool)] out bool pfOpaqueBlend);

        [DllImport(Lib)]
        internal static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool pfEnabled);
    }

    // ---------------------------------------------------------------------
    // imm32.dll
    // ---------------------------------------------------------------------
    internal static class Imm32
    {
        private const string Lib = "imm32.dll";

        [DllImport(Lib)]
        internal static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

        [DllImport(Lib)]
        internal static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport(Lib)]
        internal static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport(Lib)]
        internal static extern bool ImmGetConversionStatus(IntPtr hIMC, out int lpfdwConversion, out int lpfdwSentence);
    }

    // ---------------------------------------------------------------------
    // dxva2.dll — monitor brightness (DDC/CI)
    // ---------------------------------------------------------------------
    internal static class Dxva2
    {
        private const string Lib = "dxva2.dll";

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize,
            [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize,
            [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint pdwMinimumBrightness,
            out uint pdwCurrentBrightness, out uint pdwMaximumBrightness);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);
    }

    // ---------------------------------------------------------------------
    // wtsapi32.dll
    // ---------------------------------------------------------------------
    internal static class WtsApi32
    {
        private const string Lib = "wtsapi32.dll";

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool WTSRegisterSessionNotification(IntPtr hWnd, uint dwFlags);

        [DllImport(Lib, SetLastError = true)]
        internal static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);
    }

    // ---------------------------------------------------------------------
    // wlanapi.dll
    // ---------------------------------------------------------------------
    internal static class WlanApi
    {
        private const string Lib = "wlanapi.dll";

        [DllImport(Lib, SetLastError = true)]
        internal static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion,
            out IntPtr phClientHandle);

        [DllImport(Lib, SetLastError = true)]
        internal static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport(Lib, SetLastError = true)]
        internal static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

        [DllImport(Lib, SetLastError = true)]
        internal static extern uint WlanQueryInterface(IntPtr hClientHandle, in Guid pInterfaceGuid,
            int OpCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData, out int pWlanOpcodeValueType);

        [DllImport(Lib)]
        internal static extern void WlanFreeMemory(IntPtr pMemory);
    }

    // ---------------------------------------------------------------------
    // ole32.dll
    // ---------------------------------------------------------------------
    internal static class Ole32
    {
        private const string Lib = "ole32.dll";

        [DllImport(Lib)]
        internal static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport(Lib)]
        internal static extern void CoUninitialize();

        [DllImport(Lib)]
        internal static extern void CoTaskMemFree(IntPtr pv);

        [DllImport(Lib)]
        internal static extern int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid,
            out IntPtr ppv);
    }
}
