using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Shell;

/// <summary>
/// Snapshot of the Windows appearance preferences the App needs to honour "follow the system"
/// settings (theme / reduced motion / system accent). Reading is done once off the UI thread
/// (<see cref="Refresh"/>); the getters are pure cached reads so they are safe on the UI thread.
/// </summary>
/// <remarks>
/// Live <c>WM_SETTINGCHANGE</c> re-evaluation is not wired yet; the App calls <see cref="Refresh"/>
/// during composition and whenever the user changes one of the follow-system settings, which keeps the
/// resolved value truthful instead of silently assuming "dark, motion on".
/// </remarks>
public static class SystemAppearanceProbe
{
    private const int SpiGetClientAreaAnimation = 0x1042;
    private const int ColorHighlight = 13;
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly object Gate = new();
    private static int _loaded;
    private static bool _lightTheme;
    private static bool _reducedMotion;
    private static string? _accentHex;

    /// <summary>Whether the process-wide snapshot has been read at least once.</summary>
    public static bool IsLoaded => Volatile.Read(ref _loaded) != 0;

    /// <summary>True when the Windows app theme is the light ("Porcelain") variant.</summary>
    public static bool IsLightTheme => Volatile.Read(ref _loaded) != 0 && _lightTheme;

    /// <summary>True when Windows reports reduced motion (client-area animations disabled).</summary>
    public static bool PrefersReducedMotion => Volatile.Read(ref _loaded) != 0 && _reducedMotion;

    /// <summary>System accent colour as <c>#RRGGBB</c>, or null when it could not be read.</summary>
    public static string? SystemAccentHex => Volatile.Read(ref _loaded) != 0 ? _accentHex : null;

    /// <summary>Re-reads the Windows appearance preferences. Call from a background thread.</summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void Refresh(IPlatformLogger? logger = null)
    {
        var light = ReadLightTheme(logger);
        var reduced = ReadReducedMotion(logger);
        var accent = ReadSystemAccent();

        lock (Gate)
        {
            _lightTheme = light;
            _reducedMotion = reduced;
            _accentHex = accent;
            Volatile.Write(ref _loaded, 1);
        }
    }

    private static bool ReadLightTheme(IPlatformLogger? logger)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            logger?.LogWarning(PlatformLogCategory.Shell, "system-appearance.theme-read-failed",
                "The Windows app theme preference could not be read; assuming dark.");
            return false;
        }
    }

    private static bool ReadReducedMotion(IPlatformLogger? logger)
    {
        try
        {
            if (SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var enabled, 0))
            {
                return !enabled;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(PlatformLogCategory.Shell, "system-appearance.motion-read-failed",
                "The Windows animation preference could not be read; assuming animations are enabled.", ex);
        }

        return false;
    }

    private static string? ReadSystemAccent()
    {
        try
        {
            var colorRef = GetSysColor(ColorHighlight);
            // COLORREF is 0x00BBGGRR.
            var red = (byte)(colorRef & 0xFF);
            var green = (byte)((colorRef >> 8) & 0xFF);
            var blue = (byte)((colorRef >> 16) & 0xFF);
            return string.Create(CultureInfo.InvariantCulture, $"#{red:X2}{green:X2}{blue:X2}");
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int index);
}
