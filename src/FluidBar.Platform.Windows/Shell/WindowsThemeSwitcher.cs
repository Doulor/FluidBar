using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Monitors;
using Microsoft.Win32;

namespace FluidBar.Platform.Windows.Shell;

/// <summary>Changes the Windows app and system colour modes without blocking the caller.</summary>
public interface ISystemThemeSwitcher
{
    /// <summary>Flips the observed Windows app mode and applies it to both colour preferences.</summary>
    Task ToggleAsync(CancellationToken cancellationToken = default);
}

/// <summary>Writes the current user's theme preferences and notifies desktop applications.</summary>
public sealed class WindowsThemeSwitcher : ISystemThemeSwitcher
{
    private readonly object _gate = new();
    private readonly Func<RegistryKey> _openKey;
    private readonly Action _notify;
    private readonly IPlatformLogger _logger;

    /// <summary>Creates a switcher for the signed-in Windows user.</summary>
    public WindowsThemeSwitcher(IPlatformLogger? logger = null)
    {
        _logger = logger ?? NullPlatformLogger.Instance;
        _openKey = () => Registry.CurrentUser.CreateSubKey(ThemeValueMapper.PersonalizeKey, writable: true);
        _notify = BroadcastChange;
    }

    // Tests use an isolated registry key and intercept the broadcast; personal preferences are untouched.
    internal WindowsThemeSwitcher(Func<RegistryKey> openKey, Action notify)
    {
        _openKey = openKey;
        _notify = notify;
        _logger = NullPlatformLogger.Instance;
    }

    /// <inheritdoc />
    public Task ToggleAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        // Read inside the lock, so rapid clicks each invert the previous completed write.
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var key = _openKey();
            var current = key.GetValue("AppsUseLightTheme") as int?
                ?? key.GetValue("SystemUsesLightTheme") as int? ?? 1;
            var next = current == 0 ? 1 : 0;
            key.SetValue("AppsUseLightTheme", next, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", next, RegistryValueKind.DWord);
            SystemAppearanceProbe.Refresh(_logger);
            _notify();
        }
    }, cancellationToken);

    private void BroadcastChange()
    {
        var section = Marshal.StringToHGlobalUni("ImmersiveColorSet");
        try
        {
            // WM_SETTINGCHANGE carries a string pointer: use a synchronous, bounded broadcast and
            // retain the buffer until it returns. All of this runs on the thread pool.
            var result = NativeMethods.User32.SendMessageTimeoutW(new IntPtr(0xFFFF),
                NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero, section,
                NativeMethods.SMTO_ABORTIFHUNG, 1000, out _);
            if (result == IntPtr.Zero)
            {
                _logger.LogWarning(PlatformLogCategory.Shell, "system-appearance.theme-notify-failed",
                    "Windows theme preferences were saved, but a desktop window did not acknowledge the change.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(section);
        }
    }
}
