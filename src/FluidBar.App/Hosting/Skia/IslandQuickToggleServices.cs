using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Focus;
using FluidBar.Platform.Windows.Radios;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Composition seam for the three platform-backed home quick toggles (Wi-Fi, Bluetooth, Focus).
/// <see cref="SkiaPlatformActions"/> owns and disposes the services it is given; tests inject fakes so
/// no radio, focus session or other hardware is required.
/// </summary>
public sealed class IslandQuickToggleServices
{
    /// <summary>Creates the bundle from three already-constructed services.</summary>
    /// <param name="wifi">Wi-Fi radio toggle service.</param>
    /// <param name="bluetooth">Bluetooth radio toggle service.</param>
    /// <param name="focus">Focus/quiet-hours toggle service.</param>
    public IslandQuickToggleServices(IRadioToggleService wifi, IRadioToggleService bluetooth,
        IFocusAssistService focus)
    {
        Wifi = wifi ?? throw new ArgumentNullException(nameof(wifi));
        Bluetooth = bluetooth ?? throw new ArgumentNullException(nameof(bluetooth));
        Focus = focus ?? throw new ArgumentNullException(nameof(focus));
    }

    /// <summary>Wi-Fi radio toggle service.</summary>
    public IRadioToggleService Wifi { get; }

    /// <summary>Bluetooth radio toggle service.</summary>
    public IRadioToggleService Bluetooth { get; }

    /// <summary>Focus/quiet-hours toggle service.</summary>
    public IFocusAssistService Focus { get; }

    /// <summary>Builds the production services (Windows.Devices.Radios + FocusSessionManager).</summary>
    /// <param name="platformLogger">Platform logger shared with the other island services.</param>
    /// <param name="counters">Diagnostics counters shared with the other island services.</param>
    /// <param name="timeProvider">Injected clock (tests/diagnostics).</param>
    public static IslandQuickToggleServices CreateDefault(IPlatformLogger platformLogger,
        PlatformCounters counters, TimeProvider timeProvider) => new(
        new RadioToggleService(
            new WindowsRadioBackend(PlatformRadioKind.Wifi, platformLogger, counters),
            platformLogger, counters, timeProvider),
        new RadioToggleService(
            new WindowsRadioBackend(PlatformRadioKind.Bluetooth, platformLogger, counters),
            platformLogger, counters, timeProvider),
        new FocusAssistService(
            new WindowsFocusAssistBackend(platformLogger, counters),
            platformLogger, counters, timeProvider));
}
