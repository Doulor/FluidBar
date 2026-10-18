using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FluidBar.Platform.Windows.Interop;

/// <summary>Helpers around <see cref="Marshal.GetLastWin32Error"/> with stable logging text.</summary>
internal static class Win32Error
{
    public const int ErrorSuccess = 0;
    public const int ErrorAccessDenied = 5;
    public const int ErrorInvalidHandle = 6;
    public const int ErrorNotEnoughMemory = 8;
    public const int ErrorInvalidParameter = 87;
    public const int ErrorInsufficientBuffer = 122;
    public const int ErrorCallNotImplemented = 120;
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    public static int LastError => Marshal.GetLastWin32Error();

    public static bool TryGetLastError(out int error)
    {
        error = Marshal.GetLastWin32Error();
        return error != ErrorSuccess;
    }

    public static string Format(int error)
    {
        var message = error == ErrorSuccess ? "The operation completed successfully." : Marshal.GetPInvokeErrorMessage(error);
        return $"Win32 error {error}: {message}";
    }

    public static InvalidOperationException CreateException(string operation, int error)
        => new($"{operation} failed. {Format(error)}");
}

/// <summary>
/// Owns a Win32 handle that must be closed with <c>CloseHandle</c> (events, wait handles,
/// notification registrations are released differently and have their own types below).
/// </summary>
internal sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeKernelHandle()
        : base(ownsHandle: true)
    {
    }

    public static SafeKernelHandle CreateEvent(bool manualReset, bool initialState, string? name = null)
    {
        var raw = NativeMethods.Kernel32.CreateEventW(IntPtr.Zero, manualReset, initialState, name);
        if (raw == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw Win32Error.CreateException($"CreateEvent('{name ?? "<unnamed>"}')", error);
        }

        return new SafeKernelHandle { handle = raw };
    }

    protected override bool ReleaseHandle() => NativeMethods.Kernel32.CloseHandle(handle);
}

/// <summary>Registry key handle (<c>RegCloseKey</c>).</summary>
internal sealed class SafeRegistryKey : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeRegistryKey()
        : base(ownsHandle: true)
    {
    }

    public static SafeRegistryKey FromRaw(IntPtr rawKey) => new() { handle = rawKey };

    public static SafeRegistryKey OpenCurrentUser(string subKey, uint access)
    {
        var result = NativeMethods.Advapi32.RegOpenKeyExW(NativeMethods.HKEY_CURRENT_USER, subKey, 0, access, out var raw);
        if (result != 0)
        {
            throw Win32Error.CreateException($"RegOpenKeyEx('{subKey}')", result);
        }

        return new SafeRegistryKey { handle = raw };
    }

    protected override bool ReleaseHandle() => NativeMethods.Advapi32.RegCloseKey(handle) == 0;
}

/// <summary>Tray icon handle with explicit ownership (shared <c>IDI_APPLICATION</c> icons are never destroyed).</summary>
internal sealed class SafeIconHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly bool _ownsHandle;

    internal SafeIconHandle()
        : base(ownsHandle: true)
    {
        _ownsHandle = false;
    }

    private SafeIconHandle(IntPtr handle, bool ownsHandle)
        : base(ownsHandle: true)
    {
        _ownsHandle = ownsHandle;
        SetHandle(handle);
    }

    public static SafeIconHandle Owned(IntPtr handle) => new(handle, ownsHandle: true);

    /// <summary>Wraps a borrowed/shared icon (for example <c>IDI_APPLICATION</c>); never destroys it.</summary>
    public static SafeIconHandle Borrowed(IntPtr handle) => new(handle, ownsHandle: false);

    protected override bool ReleaseHandle()
    {
        if (!_ownsHandle || handle == IntPtr.Zero)
        {
            return true;
        }

        return NativeMethods.User32.DestroyIcon(handle);
    }
}

/// <summary>Win32 menu handle (<c>DestroyMenu</c>).</summary>
internal sealed class SafeMenuHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeMenuHandle()
        : base(ownsHandle: true)
    {
    }

    public static SafeMenuHandle CreatePopup() => new() { handle = NativeMethods.User32.CreatePopupMenu() };

    protected override bool ReleaseHandle() => NativeMethods.User32.DestroyMenu(handle);
}

/// <summary>Power-setting notification registration (<c>UnregisterPowerSettingNotification</c>).</summary>
internal sealed class SafePowerNotificationHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafePowerNotificationHandle()
        : base(ownsHandle: true)
    {
    }

    private SafePowerNotificationHandle(IntPtr registration)
        : base(ownsHandle: true)
    {
        SetHandle(registration);
    }

    public static SafePowerNotificationHandle Register(IntPtr recipient, in Guid setting)
        => new(NativeMethods.User32.RegisterPowerSettingNotification(recipient, in setting, 0));

    protected override bool ReleaseHandle() => NativeMethods.User32.UnregisterPowerSettingNotification(handle);
}

/// <summary>Device notification registration (<c>UnregisterDeviceNotification</c>).</summary>
internal sealed class SafeDeviceNotificationHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeDeviceNotificationHandle()
        : base(ownsHandle: true)
    {
    }

    private SafeDeviceNotificationHandle(IntPtr registration)
        : base(ownsHandle: true)
    {
        SetHandle(registration);
    }

    public static SafeDeviceNotificationHandle Register(IntPtr recipient, in NativeMethods.DevBroadcastDeviceInterface devInterface)
        => new(NativeMethods.User32.RegisterDeviceNotificationW(recipient, in devInterface, 0));

    protected override bool ReleaseHandle() => NativeMethods.User32.UnregisterDeviceNotification(handle);
}

/// <summary>Low-level keyboard hook handle (<c>UnhookWindowsHookEx</c>).</summary>
internal sealed class SafeHookHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeHookHandle()
        : base(ownsHandle: true)
    {
    }

    public static SafeHookHandle Create(NativeMethods.LowLevelKeyboardProc proc, IntPtr module)
    {
        var raw = NativeMethods.User32.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, proc, module, 0);
        if (raw == IntPtr.Zero)
        {
            throw Win32Error.CreateException("SetWindowsHookEx(WH_KEYBOARD_LL)", Win32Error.LastError);
        }

        return new SafeHookHandle { handle = raw };
    }

    protected override bool ReleaseHandle() => NativeMethods.User32.UnhookWindowsHookEx(handle);
}

/// <summary>WinEvent hook handle (<c>UnhookWinEvent</c>).</summary>
internal sealed class SafeWinEventHookHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeWinEventHookHandle()
        : base(ownsHandle: true)
    {
    }

    public static SafeWinEventHookHandle Create(NativeMethods.WinEventProc proc, uint eventMin, uint eventMax)
    {
        var raw = NativeMethods.User32.SetWinEventHook(eventMin, eventMax, IntPtr.Zero, proc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        if (raw == IntPtr.Zero)
        {
            throw Win32Error.CreateException(
                $"SetWinEventHook(0x{eventMin:X4}, 0x{eventMax:X4})", Win32Error.LastError);
        }

        return new SafeWinEventHookHandle { handle = raw };
    }

    protected override bool ReleaseHandle() => NativeMethods.User32.UnhookWinEvent(handle);
}

/// <summary>Session notification registration (<c>WTSUnRegisterSessionNotification</c>).</summary>
internal sealed class SafeSessionNotificationHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeSessionNotificationHandle()
        : base(ownsHandle: true)
    {
    }

    private SafeSessionNotificationHandle(IntPtr registration)
        : base(ownsHandle: true)
    {
        SetHandle(registration);
    }

    public static SafeSessionNotificationHandle Register(IntPtr recipient)
    {
        var result = NativeMethods.WtsApi32.WTSRegisterSessionNotification(recipient, NativeMethods.NOTIFY_FOR_THIS_SESSION);
        if (!result)
        {
            throw Win32Error.CreateException("WTSRegisterSessionNotification", Win32Error.LastError);
        }

        return new SafeSessionNotificationHandle(recipient);
    }

    protected override bool ReleaseHandle() => NativeMethods.WtsApi32.WTSUnRegisterSessionNotification(handle);
}

/// <summary>WLAN API client handle (<c>WlanCloseHandle</c>).</summary>
internal sealed class SafeWlanHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeWlanHandle()
        : base(ownsHandle: true)
    {
    }

    public static SafeWlanHandle Open()
    {
        var result = NativeMethods.WlanApi.WlanOpenHandle(2, IntPtr.Zero, out _, out var handle);
        if (result != 0)
        {
            throw Win32Error.CreateException("WlanOpenHandle", (int)result);
        }

        return new SafeWlanHandle { handle = handle };
    }

    protected override bool ReleaseHandle() => NativeMethods.WlanApi.WlanCloseHandle(handle, IntPtr.Zero) == 0;
}

