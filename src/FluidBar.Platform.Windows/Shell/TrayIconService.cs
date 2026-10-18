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
        var window = await _host.CreateWindowAsync("tray", PlatformWindowKind.MessageOnly,
            OnWindowMessage, cancellationToken).ConfigureAwait(false);

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

        switch (notification)
        {
            case NativeMethods.NIN_SELECT:
                Raise(LeftClicked, nameof(LeftClicked));
                break;
            case NativeMethods.NIN_KEYSELECT:
                ShowContextMenu(hwnd);
                break;
            case NativeMethods.WM_CONTEXTMENU:
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

    private void ShowContextMenu(IntPtr hwnd)
    {
        TrayMenuItem[] items;
        lock (_menuGate)
        {
            items = _menuItems.ToArray();
        }

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

        // Required for the menu to dismiss correctly: the owner window must be foreground.
        NativeMethods.User32.SetForegroundWindow(hwnd);
        var selected = (uint)NativeMethods.User32.TrackPopupMenuEx(menu.DangerousGetHandle(),
            NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY | NativeMethods.TPM_RIGHTBUTTON,
            cursor.X, cursor.Y, hwnd, IntPtr.Zero);
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
