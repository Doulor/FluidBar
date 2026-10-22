using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Shell;

/// <summary>Options for the tray icon.</summary>
public sealed class TrayIconOptions
{
    /// <summary>Localized tooltip (max 127 chars; longer values are truncated).</summary>
    public required string ToolTip { get; init; }

    /// <summary>Optional .ico file. When omitted the generic application icon is used (shared, never destroyed).</summary>
    public string? IconFilePath { get; init; }
}

/// <summary>One tray context-menu entry. Use <see cref="Separator"/> for a separator line.</summary>
public sealed record TrayMenuItem(string Id, string Text, bool IsEnabled = true, bool IsChecked = false)
{
    /// <summary>Separator marker; <see cref="Id"/> is ignored.</summary>
    public static TrayMenuItem Separator { get; } = new("__separator__", string.Empty, false, false);

    public bool IsSeparator => ReferenceEquals(this, Separator) || Id == "__separator__";
}

/// <summary>Raised when a tray context-menu item is chosen. Raised on a thread-pool thread.</summary>
public sealed class TrayMenuItemInvokedEventArgs : EventArgs
{
    public TrayMenuItemInvokedEventArgs(string itemId) => ItemId = itemId;

    public string ItemId { get; }
}

/// <summary>
/// Tray icon service (<c>Shell_NotifyIcon</c>, version 4) with a context menu, tooltip updates,
/// balloon notifications and deterministic disposal (<c>NIM_DELETE</c> + icon/menu release).
/// </summary>
/// <remarks>
/// The tray context menu is a user-initiated modal loop on the platform host thread, so the menu
/// itself must stay tiny; long-running work belongs in the event handler (thread pool).
/// </remarks>
public sealed class TrayIconService : IAsyncDisposable
{
    private const uint TrayIconId = 1;
    private const uint WM_TRAYICON = NativeMethods.WM_APP + 100;
    private const int MaxToolTipLength = 127;
    private const int MaxInfoLength = 255;
    private const int MaxInfoTitleLength = 63;

    private readonly PlatformMessageHost _host;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly List<TrayMenuItem> _menuItems = new();
    private readonly object _menuGate = new();
    private PlatformWindow? _window;
    private SafeIconHandle? _icon;
    private string _toolTip = string.Empty;
    private int _disposed;

    public TrayIconService(PlatformMessageHost host, IPlatformLogger logger, PlatformCounters counters)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
    }

    /// <summary>Raised when a context-menu item is chosen.</summary>
    public event EventHandler<TrayMenuItemInvokedEventArgs>? MenuItemInvoked;

    /// <summary>Raised on a left click of the tray icon.</summary>
    public event EventHandler? LeftClicked;

    /// <summary>Raised on a double click of the tray icon.</summary>
    public event EventHandler? DoubleClicked;

    /// <summary>Raised when the user clicks a balloon notification.</summary>
    public event EventHandler? BalloonClicked;

    /// <summary>Whether the icon has been added to the notification area.</summary>
    public bool IsVisible => _window is not null && _icon is not null;

    /// <summary>Adds the tray icon. Idempotent.</summary>
    public async Task StartAsync(TrayIconOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_window is not null)
        {
            return;
        }

        _toolTip = Truncate(options.ToolTip, MaxToolTipLength);
        var window = await _host.CreateWindowAsync("tray", PlatformWindowKind.HiddenTopLevel,
            OnWindowMessage, cancellationToken).ConfigureAwait(false);

        // Give the tray owner a real (1x1 off-screen) visible window: TrackPopupMenu cannot
        // foreground a message-only/zero-size window, which makes right-click appear dead.
        NativeMethods.User32.SetWindowPos(window.Handle, IntPtr.Zero, -32000, -32000, 1, 1,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        try
        {
            _icon = await _host.InvokeAsync(() => LoadIcon(options.IconFilePath), cancellationToken)
                .ConfigureAwait(false);
            await _host.InvokeAsync(() => AddIcon(window.Handle), cancellationToken).ConfigureAwait(false);
            _window = window;
            _logger.LogInfo(PlatformLogCategory.Shell, "tray.started", "Tray icon added.",
                new Dictionary<string, object?> { ["tooltip"] = _toolTip });
        }
        catch (Exception ex)
        {
            _counters.IncrementTrayOperationFailures();
            _logger.LogError(PlatformLogCategory.Shell, "tray.start-failed", "Failed to add the tray icon.", ex);
            _icon?.Dispose();
            _icon = null;
            await window.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private SafeIconHandle LoadIcon(string? iconFilePath)
    {
        if (!string.IsNullOrWhiteSpace(iconFilePath))
        {
            var loaded = NativeMethods.User32.LoadImageW(IntPtr.Zero, iconFilePath, NativeMethods.IMAGE_ICON, 0, 0,
                NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_DEFAULTSIZE);
            if (loaded != IntPtr.Zero)
            {
                return SafeIconHandle.Owned(loaded);
            }

            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Shell, "tray.icon-load-failed",
                $"Could not load tray icon from '{iconFilePath}'; using the generic application icon. "
                + Win32Error.Format(Win32Error.LastError));
        }

        var shared = NativeMethods.User32.LoadIconW(IntPtr.Zero, NativeMethods.IDI_APPLICATION);
        return shared == IntPtr.Zero
            ? throw new InvalidOperationException("Could not load the default application icon for the tray.")
            : SafeIconHandle.Borrowed(shared);
    }

    private bool AddIcon(IntPtr hwnd)
    {
        var data = CreateIconData(hwnd);
        data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
        data.uCallbackMessage = WM_TRAYICON;
        data.hIcon = _icon!.DangerousGetHandle();
        data.szTip = _toolTip;

        _counters.IncrementTrayOperations();
        if (!ShellNotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            throw new InvalidOperationException(
                "Shell_NotifyIcon(NIM_ADD) failed. " + Win32Error.Format(Win32Error.LastError));
        }

        var version = CreateIconData(hwnd);
        version.uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4;
        if (!ShellNotifyIcon(NativeMethods.NIM_SETVERSION, ref version))
        {
            // Non-fatal: legacy callback layout still works.
            _logger.LogWarning(PlatformLogCategory.Shell, "tray.set-version-failed",
                "Shell_NotifyIcon(NIM_SETVERSION) failed. " + Win32Error.Format(Win32Error.LastError));
        }

        return true;
    }

    private static NativeMethods.NOTIFYICONDATAW CreateIconData(IntPtr hwnd) => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
        hWnd = hwnd,
        uID = TrayIconId,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private static bool ShellNotifyIcon(uint message, ref NativeMethods.NOTIFYICONDATAW data)
        => Shell32Interop.Shell_NotifyIconW(message, ref data);

    private IntPtr? OnWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message != WM_TRAYICON)
        {
            return null;
        }

        var lParamValue = lParam.ToInt32();
        var notification = (uint)(lParamValue & 0xFFFF);
        var iconId = (uint)(lParamValue >> 16);
        if (iconId != 0 && iconId != TrayIconId)
        {
            return IntPtr.Zero;
        }

        // Two callback layouts have to be understood: version 4 (NIN_SELECT / WM_CONTEXTMENU, icon id in
        // the high word) and the legacy one (the raw mouse message, icon id in wParam). Only the version-4
        // codes were handled, so on a shell that refused NIM_SETVERSION — or answered with the legacy
        // layout — right click produced nothing at all ("托盘的右键菜单没有了", user report 2026-10-22).
        switch (notification)
        {
            case NativeMethods.NIN_SELECT:
            case NativeMethods.WM_LBUTTONUP:
                Raise(LeftClicked, nameof(LeftClicked));
                break;
            case NativeMethods.NIN_KEYSELECT:
            case NativeMethods.WM_CONTEXTMENU:
            case NativeMethods.WM_RBUTTONUP:
            case NativeMethods.WM_RBUTTONDBLCLK:
                ShowContextMenu(hwnd);
                break;
            case NativeMethods.WM_LBUTTONDBLCLK:
                Raise(DoubleClicked, nameof(DoubleClicked));
                break;
            case NativeMethods.NIN_BALLOONUSERCLICK:
                Raise(BalloonClicked, nameof(BalloonClicked));
                break;
            default:
                // Other notification-area messages are not user actions.
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>Set FLUIDBAR_TRAY_DIAG=1 to trace tray menu handling to %TEMP%\fluidbar-tray.log.</summary>
    private static readonly bool TrayDiag =
        Environment.GetEnvironmentVariable("FLUIDBAR_TRAY_DIAG") == "1";

    private static void Diag(string message)
    {
        if (!TrayDiag)
        {
            return;
        }

        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fluidbar-tray.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (System.IO.IOException)
        {
            // Diagnostics must never break the tray.
        }
    }

    /// <summary>
    /// Picks the window the menu should belong to: the tray owner is a 1x1 off-screen window, and a menu
    /// owned by it is created but never painted (measured 2026-10-22: TrackPopupMenuEx kept its modal loop
    /// running for seconds while nothing appeared on screen). A real visible window of this process renders
    /// the menu normally.
    /// </summary>
    private IntPtr FindMenuOwner(IntPtr trayWindow)
    {
        var owner = IntPtr.Zero;
        var bestArea = 0;
        NativeMethods.User32.EnumWindows((handle, _) =>
        {
            NativeMethods.User32.GetWindowThreadProcessId(handle, out var ownerPid);
            if (ownerPid != (uint)Environment.ProcessId || handle == trayWindow
                || !NativeMethods.User32.IsWindowVisible(handle))
            {
                return true;
            }

            if (!NativeMethods.User32.GetWindowRect(handle, out var rect))
            {
                return true;
            }

            var area = (rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (area > bestArea)
            {
                bestArea = area;
                owner = handle;
            }

            return true;
        }, IntPtr.Zero);

        return owner != IntPtr.Zero ? owner : trayWindow;
    }

    private void ShowContextMenu(IntPtr hwnd)
    {
        TrayMenuItem[] items;
        lock (_menuGate)
        {
            items = _menuItems.ToArray();
        }

        Diag($"show items={items.Length} hwnd=0x{hwnd:X} fg=0x{NativeMethods.User32.GetForegroundWindow():X}");
        if (items.Length == 0)
        {
            return;
        }

        using var menu = SafeMenuHandle.CreatePopup();
        if (menu.IsInvalid)
        {
            _counters.IncrementTrayOperationFailures();
            _logger.LogWarning(PlatformLogCategory.Shell, "tray.menu-create-failed",
                "CreatePopupMenu failed. " + Win32Error.Format(Win32Error.LastError));
            return;
        }

        var commandMap = new Dictionary<uint, string>();
        var command = 1u;
        var hasDefault = false;
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                NativeMethods.User32.AppendMenuW(menu.DangerousGetHandle(), NativeMethods.MF_SEPARATOR,
                    UIntPtr.Zero, null);
                continue;
            }

            var flags = NativeMethods.MF_STRING;
            if (!item.IsEnabled)
            {
                flags |= NativeMethods.MF_GRAYED;
            }

            if (item.IsChecked)
            {
                flags |= NativeMethods.MF_CHECKED;
            }

            if (!hasDefault)
            {
                flags |= NativeMethods.MF_DEFAULT;
                hasDefault = true;
            }

            NativeMethods.User32.AppendMenuW(menu.DangerousGetHandle(), flags, (UIntPtr)command, item.Text);
            commandMap[command] = item.Id;
            command++;
        }

        if (!NativeMethods.User32.GetCursorPos(out var cursor))
        {
            cursor = default;
        }

        // Required for the menu to appear AND to dismiss correctly: the owner window must be foreground.
        // The window is a 1x1 off-screen top-level, so activation is attempted twice (SetForegroundWindow
        // can be refused while another app owns the foreground).
        // A background process is not allowed to take the foreground; SetForegroundWindow simply returns
        // false and the menu is then owned by a non-foreground window — it opens BEHIND the active window
        // (invisible) and does not dismiss on an outside click. Measured 2026-10-22: setfg=False, and the
        // menu only closed when WM_CANCELMODE was posted, i.e. it really was open, just not on top.
        // Attach to the foreground thread for the duration of the call, then pin the owner topmost.
        var menuOwner = hwnd;
        var foregroundWindow = NativeMethods.User32.GetForegroundWindow();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : NativeMethods.User32.GetWindowThreadProcessId(foregroundWindow, out _);
        var currentThread = NativeMethods.Kernel32.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread
            && NativeMethods.User32.AttachThreadInput(currentThread, foregroundThread, true);
        var foreground = NativeMethods.User32.SetForegroundWindow(menuOwner);
        if (attached)
        {
            NativeMethods.User32.AttachThreadInput(currentThread, foregroundThread, false);
        }

        NativeMethods.User32.SetWindowPos(menuOwner, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        Diag($"menu at {cursor.X},{cursor.Y} owner=0x{menuOwner:X} setfg={foreground} fg=0x{foregroundWindow:X} attached={attached}");
        var selected = (uint)NativeMethods.User32.TrackPopupMenuEx(menu.DangerousGetHandle(),
            NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY | NativeMethods.TPM_RIGHTBUTTON,
            cursor.X, cursor.Y, menuOwner, IntPtr.Zero);
        NativeMethods.User32.SetWindowPos(menuOwner, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        Diag($"menu done selected={selected} err={Win32Error.LastError}");
        NativeMethods.User32.PostMessageW(hwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

        if (selected != 0 && commandMap.TryGetValue(selected, out var id))
        {
            _counters.IncrementTrayOperations();
            RaiseMenuItem(id);
        }
    }

    /// <summary>Replaces the context-menu items (localized text supplied by the caller).</summary>
    public void SetMenuItems(IReadOnlyList<TrayMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_menuGate)
        {
            _menuItems.Clear();
            _menuItems.AddRange(items);
        }
    }

    /// <summary>Updates the tooltip. Fire-and-forget on the host thread; failures are logged.</summary>
    public Task SetToolTipAsync(string toolTip, CancellationToken cancellationToken = default)
    {
        var text = Truncate(toolTip ?? string.Empty, MaxToolTipLength);
        _toolTip = text;
        var window = _window;
        if (window is null)
        {
            return Task.CompletedTask;
        }

        return _host.InvokeAsync(() =>
        {
            var data = CreateIconData(window.Handle);
            data.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
            data.szTip = text;
            _counters.IncrementTrayOperations();
            if (!ShellNotifyIcon(NativeMethods.NIM_MODIFY, ref data))
            {
                _counters.IncrementTrayOperationFailures();
                _logger.LogWarning(PlatformLogCategory.Shell, "tray.tooltip-failed",
                    "Shell_NotifyIcon(NIM_MODIFY) failed. " + Win32Error.Format(Win32Error.LastError));
            }

            return true;
        }, cancellationToken);
    }

    /// <summary>Shows a balloon/info notification from the tray icon.</summary>
    public Task ShowBalloonAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var window = _window;
        if (window is null)
        {
            return Task.CompletedTask;
        }

        return _host.InvokeAsync(() =>
        {
            var data = CreateIconData(window.Handle);
            data.uFlags = NativeMethods.NIF_INFO;
            data.szInfoTitle = Truncate(title ?? string.Empty, MaxInfoTitleLength);
            data.szInfo = Truncate(message ?? string.Empty, MaxInfoLength);
            data.dwInfoFlags = NativeMethods.NIIF_INFO;
            data.uTimeoutOrVersion = 5000;
            _counters.IncrementTrayOperations();
            if (!ShellNotifyIcon(NativeMethods.NIM_MODIFY, ref data))
            {
                _counters.IncrementTrayOperationFailures();
                _logger.LogWarning(PlatformLogCategory.Shell, "tray.balloon-failed",
                    "Shell_NotifyIcon(NIM_MODIFY/NIF_INFO) failed. " + Win32Error.Format(Win32Error.LastError));
            }

            return true;
        }, cancellationToken);
    }

    private void Raise(EventHandler? handler, string name)
    {
        if (handler is null)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(static state =>
        {
            var (self, h, eventName) = ((TrayIconService, EventHandler?, string))state!;
            try
            {
                h?.Invoke(self, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                self._counters.IncrementTrayOperationFailures();
                self._logger.LogError(PlatformLogCategory.Shell, "tray.handler-failed",
                    $"A tray '{eventName}' subscriber threw.", ex);
            }
        }, (this, handler, name));
    }

    private void RaiseMenuItem(string itemId)
    {
        var handler = MenuItemInvoked;
        if (handler is null)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(static state =>
        {
            var (self, h, id) = ((TrayIconService, EventHandler<TrayMenuItemInvokedEventArgs>, string))state!;
            try
            {
                h(self, new TrayMenuItemInvokedEventArgs(id));
            }
            catch (Exception ex)
            {
                self._counters.IncrementTrayOperationFailures();
                self._logger.LogError(PlatformLogCategory.Shell, "tray.handler-failed",
                    $"A tray menu subscriber threw for '{id}'.", ex);
            }
        }, (this, handler, itemId));
    }

    /// <summary>Removes the icon and releases native resources. Idempotent.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var window = _window;
        if (window is null)
        {
            return;
        }

        _window = null;
        try
        {
            await _host.InvokeAsync(() =>
            {
                var data = CreateIconData(window.Handle);
                _counters.IncrementTrayOperations();
                if (!ShellNotifyIcon(NativeMethods.NIM_DELETE, ref data))
                {
                    _counters.IncrementTrayOperationFailures();
                    _logger.LogWarning(PlatformLogCategory.Shell, "tray.delete-failed",
                        "Shell_NotifyIcon(NIM_DELETE) failed. " + Win32Error.Format(Win32Error.LastError));
                }

                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
            _icon?.Dispose();
            _icon = null;
            _logger.LogInfo(PlatformLogCategory.Shell, "tray.stopped", "Tray icon removed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>Thin <c>shell32!Shell_NotifyIconW</c> wrapper kept next to the tray service.</summary>
internal static class Shell32Interop
{
    private const string Lib = "shell32.dll";

    [System.Runtime.InteropServices.DllImport(Lib, SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool Shell_NotifyIconW(uint dwMessage, ref NativeMethods.NOTIFYICONDATAW lpData);
}


