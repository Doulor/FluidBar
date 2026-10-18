using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Island;
using FluidBar.App.Island.Expanded;
using FluidBar.App.Theme;
using FluidBar.Core.Layout;
using CoreMonitorInfo = FluidBar.Core.Layout.MonitorInfo;
using CorePixelRect = FluidBar.Core.Layout.PixelRect;

namespace FluidBar.App.Hosting;

/// <summary>
/// Fixed-size transparent, borderless, non-activating, always-on-top host window for one monitor
/// (spec 5.6, spike §1–4). The window is created once at its mode size (488×332 float/flush,
/// 488×392 dock) and is never moved or resized during animation: everything morphs inside
/// <see cref="IslandShell"/>. Placement happens once per monitor/anchor/mode change.
/// </summary>
public sealed class IslandWindow : Window, IDisposable
{
    private readonly IIslandWindowBackend _backend;
    private readonly IslandShell _shell;
    private readonly ExpandedPanel _panel;
    private IDisposable? _hitTestRegistration;
    private bool _allowClose;
    private bool _disposed;

    /// <summary>Creates the host window.</summary>
    /// <param name="backend">Window interop backend (fake in tests).</param>
    public IslandWindow(IIslandWindowBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _shell = new IslandShell();
        _panel = new ExpandedPanel();
        _shell.SetExpandedLayer(_panel);

        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = new SolidColorBrush(Colors.Transparent);
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        // If the compositor can ever not deliver per-pixel alpha, fail transparent rather than white.
        TransparencyBackgroundFallback = new SolidColorBrush(Colors.Transparent);
        Width = UiMetrics.WindowWidth;
        Height = UiMetrics.WindowHeight;
        Content = string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_BARE_CONTENT"), "1", StringComparison.Ordinal)
            ? new Canvas()
            : _shell;
    }

    /// <summary>The single island visual (shell + content layers).</summary>
    public IslandShell Shell => _shell;

    /// <summary>The expanded rail/page panel cached inside the shell.</summary>
    public ExpandedPanel Panel => _panel;

    /// <summary>Native window handle (0 when the platform has no HWND, e.g. headless tests).</summary>
    public nint Hwnd { get; private set; }

    /// <summary>Whether native styles/hit-test were attached.</summary>
    public bool IsNativeAttached { get; private set; }

    /// <summary>Monitor this window is placed on.</summary>
    public CoreMonitorInfo? Monitor { get; set; }

    /// <summary>Dock window height: 24 + 52 strip + 8 gap + 284 panel + 24 = 392 DIP.</summary>
    public const double DockWindowHeight = (2 * UiMetrics.WindowPadding) + UiMetrics.DockStripHeight
        + LayoutTokens.DockExpandedGap + UiMetrics.ExpandedHeight;

    /// <summary>Ex-mode switch for dock (window is taller: strip + gap + uniform panel).</summary>
    public void SetDockMode(bool dock)
    {
        var target = dock ? DockWindowHeight : UiMetrics.WindowHeight;
        if (Math.Abs(Height - target) < 0.5)
        {
            return;
        }

        Height = target;
    }

    /// <summary>Attaches native styles and the hit-test policy once the HWND exists.</summary>
    /// <returns>True when a native HWND was obtained.</returns>
    public bool AttachNative()
    {
        if (IsNativeAttached)
        {
            return true;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? 0;
        Hwnd = handle;
        IsNativeAttached = _backend.Attach(handle);
        if (IsNativeAttached)
        {
            _hitTestRegistration = _backend.RegisterHitTest(handle, HitTestClientPixels);
        }

        return IsNativeAttached;
    }

    /// <summary>
    /// Places the window so the expanded shell envelope lands at its anchored work-area position.
    /// This is the only <c>SetWindowPos</c> path (spec D7/F-02); never called per animation frame.
    /// </summary>
    /// <param name="monitor">Target monitor snapshot.</param>
    /// <param name="mode">Presentation mode.</param>
    /// <param name="anchor">Anchor within the work area.</param>
    public void PlaceOn(CoreMonitorInfo monitor, IslandMode mode, IslandAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        Monitor = monitor;
        var request = new IslandLayoutRequest
        {
            Form = ShellForm.Expanded,
            Mode = mode,
            Anchor = anchor,
            OffsetY = double.NaN,
        };
        var layout = IslandLayout.Calculate(request, monitor);
        var shellRect = layout.Shell;
        var scale = monitor.Scale;
        var pixelX = (int)Math.Round(monitor.WorkArea.X + ((shellRect.X - UiMetrics.WindowPadding) * scale), MidpointRounding.AwayFromZero);
        var pixelY = (int)Math.Round(monitor.WorkArea.Y + ((shellRect.Y - UiMetrics.WindowPadding) * scale), MidpointRounding.AwayFromZero);
        var pixelWidth = (int)Math.Ceiling(Width * scale);
        var pixelHeight = (int)Math.Ceiling(Height * scale);
        Position = new PixelPoint(pixelX, pixelY);
        _backend.Place(Hwnd, new CorePixelRect(pixelX, pixelY, pixelWidth, pixelHeight));
    }

    /// <summary>Applies one motion frame to the shell (render-only; no layout, no window calls).</summary>
    public void ApplyFrame(in ShellFrame frame) => _shell.ApplyFrame(frame);

    /// <summary>Updates the window region from the settled shell geometry (once per settle).</summary>
    public void UpdateRegionOnSettle()
    {
        if (!IsNativeAttached || Monitor is null || _shell.Frame.IsHidden)
        {
            return;
        }

        _backend.UpdateRegion(Hwnd, _shell.CreateShellGeometry(), Monitor);
    }

    /// <summary>Toggles whole-window click-through (hidden state).</summary>
    public void ApplyClickThrough(bool clickThrough)
    {
        if (IsNativeAttached)
        {
            _backend.SetClickThrough(Hwnd, clickThrough);
        }
    }

    /// <summary>Low-frequency topmost reassert (called ≤1/s while visible).</summary>
    public void ReassertTopmost()
    {
        if (IsNativeAttached)
        {
            _backend.ReassertTopmost(Hwnd);
        }
    }

    /// <summary>Hit-tests a client-pixel point using the same geometry that draws the shell.</summary>
    public bool HitTestClientPixels(int pixelX, int pixelY)
    {
        var scale = Monitor?.Scale ?? 1.0;
        return HitTestDip(pixelX / scale, pixelY / scale);
    }

    /// <summary>Hit-tests a client-DIP point (exposed for the click-through matrix tests).</summary>
    public bool HitTestDip(double dipX, double dipY)
        => _shell.IsInteractive && _shell.ContainsPoint(new Point(dipX, dipY));

    /// <summary>Allows the next close to destroy the window (application shutdown).</summary>
    public void AllowClose() => _allowClose = true;

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            // Tray-app semantics: closing the island hides it instead of shutting down (spec 5.6).
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hitTestRegistration?.Dispose();
        _hitTestRegistration = null;
        _allowClose = true;
        _backend.Dispose();
        Close();
    }
}
