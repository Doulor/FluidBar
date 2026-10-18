using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Monitors;

/// <summary>Light/dark/high-contrast classification.</summary>
public enum SystemThemeKind
{
    Unknown,
    Light,
    Dark,
    HighContrast,
}

/// <summary>Immutable system theme snapshot.</summary>
/// <param name="AppTheme">App-mode theme (<c>AppsUseLightTheme</c>).</param>
/// <param name="SystemTheme">System-mode theme (<c>SystemUsesLightTheme</c>).</param>
/// <param name="IsHighContrast">Whether high contrast is active.</param>
/// <param name="AccentColorArgb">DWM accent colour as 0xAARRGGBB, or 0 when unknown.</param>
public sealed record SystemThemeSnapshot(
    SystemThemeKind AppTheme,
    SystemThemeKind SystemTheme,
    bool IsHighContrast,
    uint AccentColorArgb);

/// <summary>Pure theme mapping (registry values are ABGR; the snapshot exposes ARGB).</summary>
public static class ThemeValueMapper
{
    /// <summary>Registry key holding the app/system light flags.</summary>
    public const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Registry key holding the DWM accent colour.</summary>
    public const string DwmKey = @"Software\Microsoft\Windows\DWM";

    /// <summary>Maps a <c>UseLightTheme</c> registry flag (1 = light, 0 = dark).</summary>
    public static SystemThemeKind FromLightFlag(int? value) => value switch
    {
        1 => SystemThemeKind.Light,
        0 => SystemThemeKind.Dark,
        _ => SystemThemeKind.Unknown,
    };

    /// <summary>Builds a snapshot from raw registry/SPI values.</summary>
    public static SystemThemeSnapshot Create(int? appsUseLightTheme, int? systemUsesLightTheme, bool highContrast,
        uint? accentColorAbgr)
    {
        var accent = accentColorAbgr is null ? 0u : UnpackAbgrToArgb(accentColorAbgr.Value);
        if (highContrast)
        {
            return new SystemThemeSnapshot(SystemThemeKind.HighContrast, SystemThemeKind.HighContrast, true, accent);
        }

        return new SystemThemeSnapshot(
            FromLightFlag(appsUseLightTheme),
            FromLightFlag(systemUsesLightTheme),
            false,
            accent);
    }

    /// <summary>Splits a DWM <c>AccentColor</c> value (0xAABBGGRR) into components.</summary>
    public static (byte A, byte R, byte G, byte B) UnpackAbgr(uint abgr) => (
        (byte)((abgr >> 24) & 0xFF),
        (byte)(abgr & 0xFF),
        (byte)((abgr >> 8) & 0xFF),
        (byte)((abgr >> 16) & 0xFF));

    /// <summary>Converts a DWM ABGR value to 0xAARRGGBB.</summary>
    public static uint UnpackAbgrToArgb(uint abgr)
    {
        var (a, r, g, b) = UnpackAbgr(abgr);
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }
}

/// <summary>Small synchronous registry reader; always called from the thread pool.</summary>
internal static class RegistryValueReader
{
    public static int? GetDword(string subKeyPath, string valueName)
    {
        var result = NativeMethods.Advapi32.RegOpenKeyExW(NativeMethods.HKEY_CURRENT_USER, subKeyPath, 0,
            NativeMethods.KEY_QUERY_VALUE, out var rawKey);
        if (result != 0)
        {
            return null;
        }

        using var key = SafeRegistryKey.FromRaw(rawKey);
        uint type = 0;
        uint size = 4;
        var buffer = new byte[4];
        result = NativeMethods.Advapi32.RegQueryValueExW(key.DangerousGetHandle(), valueName, IntPtr.Zero,
            out type, buffer, ref size);
        if (result != 0 || type != NativeMethods.REG_DWORD || size < 4)
        {
            return null;
        }

        return BitConverter.ToInt32(buffer, 0);
    }
}

/// <summary>
/// Event-driven system theme monitor: registry notification (<c>RegNotifyChangeKeyValue</c>) on the
/// Personalize/DWM keys plus <c>WM_SETTINGCHANGE</c>. Publishes change-only snapshots; no timer.
/// </summary>
public sealed class SystemThemeMonitor : PlatformMonitor<SystemThemeSnapshot>
{
    private readonly PlatformMessageHost _host;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _debounceGate = new();
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly TimeProvider _timeProvider;
    private PlatformWindow? _window;
    private RegistryChangeWatcher? _personalizeWatcher;
    private RegistryChangeWatcher? _dwmWatcher;
    private ITimer? _debounceTimer;

    public SystemThemeMonitor(PlatformMessageHost host, IPlatformLogger logger, PlatformCounters counters,
        TimeProvider? timeProvider = null)
        : base("theme", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        _window = await _host.CreateWindowAsync("theme", PlatformWindowKind.HiddenTopLevel,
            OnWindowMessage, cancellationToken).ConfigureAwait(false);

        _personalizeWatcher = new RegistryChangeWatcher(ThemeValueMapper.PersonalizeKey, watchSubTree: false,
            ScheduleRefresh, _logger, _counters);
        _dwmWatcher = new RegistryChangeWatcher(ThemeValueMapper.DwmKey, watchSubTree: false,
            ScheduleRefresh, _logger, _counters);

        try
        {
            _personalizeWatcher.Start();
            _dwmWatcher.Start();
        }
        catch (Exception ex)
        {
            // Watchers are an optimization: the WM_SETTINGCHANGE path still keeps the theme fresh.
            _logger.LogWarning(PlatformLogCategory.Monitor, "theme.watcher-failed",
                "Theme registry watcher could not be armed; relying on WM_SETTINGCHANGE.", ex);
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private IntPtr? OnWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == NativeMethods.WM_SETTINGCHANGE)
        {
            ScheduleRefresh();
            return IntPtr.Zero;
        }

        return null;
    }

    private void ScheduleRefresh()
    {
        lock (_debounceGate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = _timeProvider.CreateTimer(static state =>
            {
                var self = (SystemThemeMonitor)state!;
                _ = self.RefreshSafeAsync();
            }, this, TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RefreshSafeAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _counters.IncrementMonitorCallbackFailures();
            _logger.LogError(PlatformLogCategory.Monitor, "theme.refresh-failed", "Theme refresh failed.", ex);
        }
    }

    /// <summary>Re-reads theme values on the thread pool and publishes change-only.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var snapshot = await Task.Run(ReadSnapshot, cancellationToken).ConfigureAwait(false);
            Publish(snapshot);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private SystemThemeSnapshot ReadSnapshot()
    {
        var appsUseLightTheme = RegistryValueReader.GetDword(ThemeValueMapper.PersonalizeKey, "AppsUseLightTheme");
        var systemUsesLightTheme = RegistryValueReader.GetDword(ThemeValueMapper.PersonalizeKey, "SystemUsesLightTheme");

        var accentRaw = RegistryValueReader.GetDword(ThemeValueMapper.DwmKey, "AccentColor");
        uint? accent = accentRaw is null ? null : unchecked((uint)accentRaw.Value);
        if (accent is null)
        {
            var colorization = RegistryValueReader.GetDword(ThemeValueMapper.DwmKey, "ColorizationColor");
            if (colorization is not null)
            {
                // ColorizationColor is 0xAARRGGBB; normalise to the ABGR input expected by the mapper.
                var argb = unchecked((uint)colorization.Value);
                accent = ((argb & 0x000000FF) << 16) | (argb & 0x0000FF00) | ((argb & 0x00FF0000) >> 16) | (argb & 0xFF000000);
            }
        }

        var (highContrast, highContrastKnown) = ReadHighContrast();
        if (!highContrastKnown)
        {
            _logger.LogDebug(PlatformLogCategory.Monitor, "theme.high-contrast-unknown",
                "SPI_GETHIGHCONTRAST failed; assuming high contrast is off.");
        }

        return ThemeValueMapper.Create(appsUseLightTheme, systemUsesLightTheme, highContrast, accent);
    }

    private static (bool Enabled, bool Known) ReadHighContrast()
    {
        var info = new NativeMethods.HIGHCONTRASTW
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.HIGHCONTRASTW>(),
        };
        if (!NativeMethods.User32.SystemParametersInfoW(NativeMethods.SPI_GETHIGHCONTRAST,
                info.cbSize, ref info, 0))
        {
            return (false, false);
        }

        return ((info.dwFlags & NativeMethods.HIGHCONTRASTON) != 0, true);
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        lock (_debounceGate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        _personalizeWatcher?.Dispose();
        _personalizeWatcher = null;
        _dwmWatcher?.Dispose();
        _dwmWatcher = null;

        var window = _window;
        _window = null;
        if (window is not null)
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }
}
