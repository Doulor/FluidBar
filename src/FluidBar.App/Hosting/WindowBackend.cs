using Avalonia.Controls;
using Avalonia.Platform;
using FluidBar.Platform.Windows.Hosting;
using CoreMonitorInfo = FluidBar.Core.Layout.MonitorInfo;
using CorePixelRect = FluidBar.Core.Layout.PixelRect;
using CoreShellGeometry = FluidBar.Core.Layout.ShellGeometry;

namespace FluidBar.App.Hosting;

/// <summary>
/// The App's only door to Win32 window behaviour (spec 5.2: no P/Invoke in App). The production
/// implementation delegates to <see cref="IslandWindowInterop"/>; tests use a recording fake.
/// </summary>
public interface IIslandWindowBackend : IDisposable
{
    /// <summary>Applies island ex-styles and returns whether a native HWND was available.</summary>
    bool Attach(nint hwnd);

    /// <summary>Installs the WM_NCHITTEST policy; returns a disposable that restores the window proc.</summary>
    IDisposable? RegisterHitTest(nint hwnd, Func<int, int, bool> isInteractiveInClientPixels);

    /// <summary>One physical placement (never called per frame).</summary>
    void Place(nint hwnd, CorePixelRect pixelRect);

    /// <summary>Settle-time window region update from the current shell geometry.</summary>
    void UpdateRegion(nint hwnd, CoreShellGeometry geometry, CoreMonitorInfo monitor);

    /// <summary>Whole-window click-through toggle (hidden states only).</summary>
    void SetClickThrough(nint hwnd, bool clickThrough);

    /// <summary>Makes the window topmost without activation.</summary>
    void SetTopmost(nint hwnd);

    /// <summary>Policy-checked topmost reassert (≤1 Hz, shell whitelist).</summary>
    bool ReassertTopmost(nint hwnd);
}

/// <summary>Production backend: thin adapter over the Platform.Windows interop surface.</summary>
public sealed class PlatformWindowBackend : IIslandWindowBackend
{
    private readonly IslandWindowInterop _interop;
    private readonly List<IDisposable> _registrations = [];

    /// <summary>Creates the backend.</summary>
    /// <param name="interop">Platform interop instance (owned by the host).</param>
    public PlatformWindowBackend(IslandWindowInterop interop)
    {
        _interop = interop ?? throw new ArgumentNullException(nameof(interop));
    }

    /// <inheritdoc />
    public bool Attach(nint hwnd)
    {
        if (hwnd == 0)
        {
            return false;
        }

        // Diagnostic escape hatch for composition debugging (no behaviour change in production).
        if (!string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_NO_INTEROP_STYLES"), "1", StringComparison.Ordinal))
        {
            _interop.ApplyIslandStyles(hwnd);
        }

        _interop.SetTopmost(hwnd);
        return true;
    }

    /// <inheritdoc />
    public IDisposable? RegisterHitTest(nint hwnd, Func<int, int, bool> isInteractiveInClientPixels)
    {
        if (hwnd == 0)
        {
            return null;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_NO_INTEROP_STYLES"), "1", StringComparison.Ordinal))
        {
            return null;
        }

        var registration = _interop.RegisterHitTest(hwnd, isInteractiveInClientPixels);
        _registrations.Add(registration);
        return registration;
    }

    /// <inheritdoc />
    public void Place(nint hwnd, CorePixelRect pixelRect)
    {
        if (hwnd == 0)
        {
            return;
        }

        _interop.PlacePhysical(hwnd, pixelRect.X, pixelRect.Y, pixelRect.Width, pixelRect.Height);
    }

    /// <inheritdoc />
    public void UpdateRegion(nint hwnd, CoreShellGeometry geometry, CoreMonitorInfo monitor)
    {
        if (hwnd == 0)
        {
            return;
        }

        _interop.UpdateWindowRegionOnSettle(hwnd, geometry, monitor);
    }

    /// <inheritdoc />
    public void SetClickThrough(nint hwnd, bool clickThrough)
    {
        if (hwnd != 0)
        {
            _interop.SetClickThrough(hwnd, clickThrough);
        }
    }

    /// <inheritdoc />
    public void SetTopmost(nint hwnd)
    {
        if (hwnd != 0)
        {
            _interop.SetTopmost(hwnd);
        }
    }

    /// <inheritdoc />
    public bool ReassertTopmost(nint hwnd) => hwnd != 0 && _interop.TryReassertTopmost(hwnd);

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
        _interop.Dispose();
    }
}

/// <summary>
/// Recording backend for headless tests: captures every call so the suite can assert "one placement
/// per anchor/display change, zero per animation frame" and click-through transitions.
/// </summary>
public sealed class RecordingWindowBackend : IIslandWindowBackend
{
    /// <summary>Recorded placements in call order.</summary>
    public List<CorePixelRect> Placements { get; } = [];

    /// <summary>Recorded click-through transitions.</summary>
    public List<bool> ClickThroughTransitions { get; } = [];

    /// <summary>Recorded settle-time region updates.</summary>
    public List<CoreShellGeometry> Regions { get; } = [];

    /// <summary>Number of topmost reassertions.</summary>
    public int TopmostReasserts { get; private set; }

    /// <summary>Whether <see cref="Attach"/> was called.</summary>
    public bool Attached { get; private set; }

    /// <inheritdoc />
    public bool Attach(nint hwnd)
    {
        Attached = true;
        return hwnd != 0;
    }

    /// <inheritdoc />
    public IDisposable? RegisterHitTest(nint hwnd, Func<int, int, bool> isInteractiveInClientPixels) => null;

    /// <inheritdoc />
    public void Place(nint hwnd, CorePixelRect pixelRect) => Placements.Add(pixelRect);

    /// <inheritdoc />
    public void UpdateRegion(nint hwnd, CoreShellGeometry geometry, CoreMonitorInfo monitor) => Regions.Add(geometry);

    /// <inheritdoc />
    public void SetClickThrough(nint hwnd, bool clickThrough)
    {
        if (ClickThroughTransitions.Count == 0 || ClickThroughTransitions[^1] != clickThrough)
        {
            ClickThroughTransitions.Add(clickThrough);
        }
    }

    /// <inheritdoc />
    public void SetTopmost(nint hwnd)
    {
    }

    /// <inheritdoc />
    public bool ReassertTopmost(nint hwnd)
    {
        TopmostReasserts++;
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>Maps Avalonia <see cref="Screen"/> data to Core monitor snapshots (DIP contract).</summary>
public static class ScreenMapper
{
    /// <summary>Converts one Avalonia screen to a Core monitor snapshot.</summary>
    public static CoreMonitorInfo ToCoreMonitor(Screen screen, int index, bool isPrimary)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var dpi = (uint)Math.Round(Math.Clamp(screen.Scaling, 0.5, 4.0) * 96);
        var bounds = screen.Bounds;
        var working = screen.WorkingArea;
        return new CoreMonitorInfo
        {
            Id = string.IsNullOrWhiteSpace(screen.DisplayName) ? $"display-{index}" : screen.DisplayName,
            Bounds = new CorePixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            WorkArea = new CorePixelRect(working.X, working.Y, working.Width, working.Height),
            Dpi = dpi,
            IsPrimary = isPrimary,
        };
    }

    /// <summary>Returns the primary screen of a window, falling back to the first screen.</summary>
    public static Screen? PrimaryScreen(Screens screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        return screens.Primary ?? screens.All.FirstOrDefault();
    }
}
