using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using System.Diagnostics;
using FluidBar.App.Localization;
using FluidBar.App.Rendering;
using FluidBar.App.Settings;
using FluidBar.App.Views.Market;
using FluidBar.Island;
using FluidBar.Platform.Windows.Hosting;
using System.IO;
using SkiaSharp;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Settings window: 900×620 custom chrome, seven sections, transcribed from the interactive prototype
/// (docs/rewrite/prototype2): <c>.settings-window</c> (22 px radius, 1 px <c>--ui-line-strong</c> rim),
/// <c>.settings-titlebar</c> (50 px, 18/12 padding, 24 px mark, 13 px title, 9 px uppercase sub, 32 px close),
/// <c>.settings-nav</c> (208 px, 14/10 padding, 36 px items, kicker, counts, version block) and
/// <c>.settings-pane</c> (24/30 padding). Every change applies live through <see cref="SettingsController"/>.
/// </summary>
public sealed class SettingsWindow : Window, ILocalizable
{
    private static readonly (SettingsWindowSection Section, string LabelKey, SettingsIconKind Icon)[] SectionOrder =
    [
        (SettingsWindowSection.General, LocKeys.Settings.General.Title, SettingsIconKind.Settings),
        (SettingsWindowSection.Appearance, LocKeys.Settings.Appearance.Title, SettingsIconKind.Palette),
        (SettingsWindowSection.Island, LocKeys.Settings.Island.Title, SettingsIconKind.Island),
        (SettingsWindowSection.Plugins, LocKeys.Settings.Plugins.Title, SettingsIconKind.Puzzle),
        // Agent support moved into the plugins page: the navigation is seven items again (prototype).
        (SettingsWindowSection.Marketplace, LocKeys.Settings.Marketplace.Title, SettingsIconKind.Storefront),
        (SettingsWindowSection.Advanced, LocKeys.Settings.Advanced.Title, SettingsIconKind.Sliders),
        (SettingsWindowSection.About, LocKeys.Settings.About.NavLabel, SettingsIconKind.Info),
    ];

    private readonly SettingsController _settings;
    private readonly ILocalizer _localizer;
    private readonly IMarketplaceUiService _marketplace;
    private readonly IPluginUiService _plugins;
    private readonly Action<string?> _openPath;
    private readonly IUpdateUiService? _updates;
    private readonly IFrameStatsSource? _frameStats;
    private readonly Action? _restartToInstall;
    private readonly FluidBar.App.Hosting.Pages.PageRegistry? _pages;
    private readonly StackPanel _nav = new() { Spacing = SettingsMetrics.NavGap };
    private readonly ScrollViewer _content = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _subText = new();
    private readonly TextBlock _versionText = new();
    private readonly TextBlock _versionLine = new();
    private readonly List<Border> _navRows = [];
    private readonly List<SettingsIcon> _navIcons = [];
    private readonly List<TextBlock> _navLabels = [];
    private readonly Dictionary<SettingsWindowSection, Border> _navCounts = [];
    private readonly Border _shell;
    private SettingsWindowSection _section;
    private IslandPreview? _preview;
    private bool _closed;
    private readonly WindowRegionTracker _regionTracker = new();
    private Border? _bleed;
    private IReadOnlyList<string> _firstFrameSteps = Array.Empty<string>();
    private bool _firstFramePreparing;
    private string? _status;
    private SettingsPalette _palette;
    private bool _liquidGlass;

    /// <summary>True when DWM accepted the acrylic system backdrop for this window (fallback only).</summary>
    private bool _acrylicEnabled;

    /// <summary>Blurred-desktop pipeline for the liquid-glass panel (its own, never the island's).</summary>
    private SettingsGlassBackdrop? _backdrop;

    private WriteableBitmap? _glassTarget;
    private ImageBrush? _glassBrush;
    private bool _glassBrushNeedsApply;
    private (int X, int Y, int Width, int Height) _backdropRect;
    private (int X, int Y, int Width, int Height) _appliedRect;
    private bool _accentFallbackTried;

    /// <summary>Diagnostics: last physical-rect probe result (true when Win32 answered).</summary>
    private bool _physicalRectOk;

    /// <summary>Diagnostics: last raw Win32 client rect and origin.</summary>
    private string _physicalRectProbe = "none";

    /// <summary>Diagnostics: mean colour and checksum of the last composed backdrop.</summary>
    private string _glassFrameDiagnostics = "none";

    /// <summary>
    /// The theme the uploaded pixels were baked for. The tint lives inside the bitmap, so a frame composed for the
    /// other theme is the *other material*, not a stale value: it must be recomposed, never painted.
    /// </summary>
    private bool? _composedForLight;

    /// <summary>Re-entrancy guard: applying the fill may recompose, which uploads and re-applies the fill.</summary>
    private bool _composing;

    /// <summary>True while the window is being dragged: captures use the cheap GDI path.</summary>
    private bool _dragging;

    /// <summary>Fires once the window has been still long enough to take a correct (composed) capture.</summary>
    private DispatcherTimer? _settleTimer;
    private bool _centerFade;
    private bool _exitStarted;
    private bool _allowClose;
    private bool _layeredStyleAdded;
    private double _windowAlpha = 1;
    private AvaloniaFrameClock? _fadeClock;
    private ScaleTransform? _fadeScale;
    private TranslateTransform? _fadeTranslate;
    private TransformGroup? _fadeTransform;

    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(180);

    /// <summary>Creates the settings window.</summary>
    public SettingsWindow(
        SettingsController settings,
        ILocalizer localizer,
        SettingsWindowSection section,
        IMarketplaceUiService? marketplace = null,
        IPluginUiService? plugins = null,
        Action<string?>? openPath = null,
        IUpdateUiService? updates = null,
        IFrameStatsSource? frameStats = null,
        Action? restartToInstall = null,
        FluidBar.App.Hosting.Pages.PageRegistry? pages = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _updates = updates;
        _frameStats = frameStats;
        _restartToInstall = restartToInstall;
        _pages = pages;
        _marketplace = marketplace ?? CreateDefaultMarketplace(localizer);
        _plugins = plugins ?? new FluidBar.App.Views.Plugins.BuiltinPluginContributionUiService(settings, localizer);
        _openPath = openPath ?? (static path =>
        {
            if (!string.IsNullOrEmpty(path))
            {
                _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
        });
        _section = section;
        _liquidGlass = settings.Current.Material == FluidBar.App.Settings.IslandMaterial.LiquidGlass;
        _palette = SettingsPalette.For(ResolvePaletteTheme(settings.Current.Theme), _liquidGlass);

        // .settings-window { width: min(900px, 100vw - 48px); height: min(620px, 100vh - 48px) }
        Width = SettingsMetrics.WindowWidth;
        Height = SettingsMetrics.WindowHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.None;
        Title = localizer.Get(LocKeys.Settings.Title);
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        // Until the first frame is painted the platform fills the window with this fallback; Avalonia's
        // default is a light system colour, which is exactly the grey plate users see before the panel.
        TransparencyBackgroundFallback = new SolidColorBrush(_palette.Window);


        // The platform composites the window before Avalonia's first frame: this hook replaces the grey
        // default window-class background with the panel colour and clips the rounded region, both while
        // the window is still hidden inside ShowWindow (WM_SHOWWINDOW), i.e. before the first frame.
        Avalonia.Controls.Win32Properties.AddWndProcHookCallback(this, OnNativeMessage);

        _bleed = new Border { IsVisible = false };
        _shell = new Border
        {
            CornerRadius = new CornerRadius(SettingsMetrics.WindowRadius),
            BorderThickness = new Thickness(SettingsMetrics.WindowBorder),
            ClipToBounds = true,
            Child = BuildLayout(),
        };

        // The glass backdrop is a 1/6 bake stretched back up to the panel size. With the default bitmap
        // interpolation that upscale reads as grain/blockiness across the whole panel ("很多噪点"), so ask for the
        // high-quality resample explicitly.
        RenderOptions.SetBitmapInterpolationMode(_shell, BitmapInterpolationMode.HighQuality);
        ApplyChrome();
        var root = new Grid();
        root.Children.Add(_bleed);
        root.Children.Add(_shell);
        Content = root;

        _settings.Changed += OnSettingsChanged;
        Activated += (_, _) => ReassertAcrylicBackdrop();
        _localizer.Changed += OnLocalizerChanged;
        if (_updates is not null)
        {
            _updates.Changed += OnUpdateChanged;
        }

        RebuildContent();
        _ = RefreshNavCountsAsync();
    }

    /// <summary>
    /// Enables the centered fade used when the app opens settings. The island is not involved:
    /// the window appears at its normal centered position and fades/scale-settles in place.
    /// </summary>
    // Compatibility with the pre-center-fade bootstrap in the clean baseline worktree.
    public void PrepareEntrance(LayeredRect source, double scale) => EnableCenterFade();

    public void EnableCenterFade()
    {
        // Acceptance/screenshot runs open the window without the entrance animation: the fade starts at
        // opacity 0, so a capture taken before it settles sees nothing at all.
        if (string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_SETTINGS_NO_FADE"), "1",
                StringComparison.Ordinal))
        {
            Opacity = 1;
            return;
        }

        _centerFade = true;
        Opacity = 0;
        _fadeScale = new ScaleTransform(0.985, 0.985);
        _fadeTranslate = new TranslateTransform(0, 10);
        _fadeTransform = new TransformGroup { Children = { _fadeScale, _fadeTranslate } };
        _shell.RenderTransformOrigin = RelativePoint.Center;
        _shell.RenderTransform = _fadeTransform;
    }

    private static IMarketplaceUiService CreateDefaultMarketplace(ILocalizer localizer)
        => new MarketplaceFallbackUiService(null, new DesignMarketplaceUiService(), localizer);

    /// <summary>Raised when a status line should be surfaced (tests/accessibility).</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Currently selected section.</summary>
    public SettingsWindowSection CurrentSection => _section;

    /// <summary>Status text last shown by a section.</summary>
    public string? Status => _status;

    /// <summary>Switches section (used by the marketplace shortcut).</summary>
    public void ShowSection(SettingsWindowSection section)
    {
        _section = section;
        UpdateNavSelection();
        RebuildContent();
    }

    /// <inheritdoc />
    public void ApplyLocalization(ILocalizer localizer)
    {
        RebuildNav();
        RebuildContent();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Windows animates a new window by painting its system background brush first, which is the grey
        // square users see before the rounded panel appears. The animation is pure decoration here.
        if (TryGetPlatformHandle()?.Handle is { } openedHandle && openedHandle != IntPtr.Zero)
        {
            _ = WindowCornerRegion.TryDisableTransitions(openedHandle, out _);
        }

        // The compositor of this stack ignores SetWindowRgn (it reports success, the surface still shows
        // the square corners), so the only shape it honours is DWM's own window rounding. Ask for it and,
        // when granted, let the window be the rounded shape: the shell fills it edge to edge (no painted
        // curve fighting DWM's) and the surface colour matches the panel, so no grey wedge can appear.
        var roundingHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        // A remote session can claim Transparent while the composited surface is opaque, so alpha is only
        // trusted on a local session; everywhere else the DWM-rounded opaque fallback takes over.
        // Experiment switch (FLUIDBAR_WINDOW_ALPHA=1): force the per-pixel-alpha path without the
        // self-check, and tell DWM not to draw its own corner/shadow band, so only the client alpha
        // shapes the window. Default behaviour is unchanged when the variable is absent.
        var forcedAlpha = string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_WINDOW_ALPHA"), "1",
            StringComparison.Ordinal);
        // Plan B is the default: this platform does not deliver client alpha and DWM's own rounded corner
        // drags a composited edge band around the window, so the default is a clean square panel (no
        // rounding, no edge band). The alpha path stays available through FLUIDBAR_WINDOW_ALPHA=1.
        var alphaUsable = forcedAlpha || _liquidGlass;
        ChromeMode = alphaUsable ? SettingsWindowChrome.Transparent : SettingsWindowChrome.Opaque;

        if (ChromeMode == SettingsWindowChrome.Transparent)
        {
            // Primary path: the surface carries per-pixel alpha, so the window background is fully
            // transparent and the only painted thing is the 22 DIP rounded panel.
            Background = Brushes.Transparent;
            // Liquid glass uses the original full-bleed square shell and lets DWM round the native
            // window (the same logic as the opaque chrome). Anything else paints a second radius inside
            // DWM's 8 DIP cut and leaves a square band around it.
            _shell.CornerRadius = new CornerRadius(_liquidGlass ? 0 : SettingsMetrics.WindowRadius);
            _shell.BorderThickness = new Thickness(_liquidGlass ? 0 : SettingsMetrics.WindowBorder);
            if (forcedAlpha && roundingHandle != IntPtr.Zero)
            {
                _ = WindowDwmPolicy.TryDisableRounding(roundingHandle, out _);
            }
            else if (_liquidGlass && roundingHandle != IntPtr.Zero)
            {
                // One native backdrop layer only: DWM acrylic supplies the real desktop blur and the
                // old full-bleed shell lets DWM own the rounded silhouette.
                _acrylicEnabled = false; // the material is drawn; DWMWA_SYSTEMBACKDROP_TYPE paints nothing here
                _ = WindowDwmPolicy.TryEnableRounding(roundingHandle, out _);
            }

        }
        else
        {
            var roundHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            var roundHandleIsUsable = roundHandle != IntPtr.Zero;

            // Plan B default: square panel-coloured window, no DWM rounding requested.
            DwmRoundingActive = false;
            Background = new SolidColorBrush(_palette.Window);
            _shell.CornerRadius = new CornerRadius(0);
            _shell.BorderThickness = new Thickness(0);

            // Default: let DWM round the window itself (8 DIP, the radius the system supports). The shell
            // stays square and fills the whole window rect, so DWM cuts the corner out of the panel and no
            // unpainted wedge (grey band) can appear between the corner and the content.
            if (roundHandleIsUsable)
            {
                DwmRoundingActive = WindowCornerRegion.TryEnableDwmRounding(roundHandle, out _);
                if (DwmRoundingActive)
                {
                    // The shell stays square and fills the whole window rect: DWM cuts the rounded corner
                    // out of the panel itself, so no unpainted wedge (and no grey band) can appear.
                    _ = WindowCornerRegion.TryRemoveDwmBorder(roundHandle, out _);
                    _shell.CornerRadius = new CornerRadius(0);
                    _shell.BorderThickness = new Thickness(0);
                }
            }
        }

        // Whatever the compositor did, clip the native window to the painted radius: a rectangular
        // window surface shows square wedges outside the rounded content as soon as alpha is
        // unavailable. Applied once per size/DPI change, never per frame. The first application happens
        // synchronously here so it lands on the earliest possible frame the platform lets us touch.
        ApplyPanelFill();
        RefreshGlassBackdrop(force: true);
        MaybeWriteSnapshot();
        PositionChanged += PositionChangedGlassHook;
        Closed += (_, _) =>
        {
            PositionChanged -= PositionChangedGlassHook;
            if (_settleTimer is not null)
            {
                _settleTimer.Stop();
                _settleTimer.Tick -= OnSettleTick;
                _settleTimer = null;
            }

            DisposeGlassBackdrop();
        };
        LayoutUpdated += (_, _) => ApplyCornerRegion("layout");
        SizeChanged += (_, _) => ApplyCornerRegion("size-changed");
        ApplyCornerRegion("opened");
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyCornerRegion("opened-post"),
            Avalonia.Threading.DispatcherPriority.Loaded);
        if (!forcedAlpha && !_liquidGlass)
        {
            // Acrylic is composited by DWM, so the alpha probe would misread the already-blurred
            // backdrop as an opaque surface and replace it with the square opaque fallback.
            Avalonia.Threading.Dispatcher.UIThread.Post(VerifyAlphaOrFallBack,
                Avalonia.Threading.DispatcherPriority.Background);
        }

        if (_centerFade)
        {
            ApplyWindowAlpha(0);
            Dispatcher.UIThread.Post(StartCenterFadeIn, DispatcherPriority.Loaded);
        }
    }

    private async void StartCenterFadeIn()
    {
        if (!_centerFade || _closed)
        {
            return;
        }

        // Let the first layout/composition pass finish while the window is still alpha 0. Starting the
        // opacity curve in the same dispatcher turn as the first paint made the opening frames hitch.
        await Task.Delay(70);
        if (_closed || _exitStarted)
        {
            return;
        }

        Opacity = 1;
        ApplyWindowAlpha(0);
        FadeOpacity(0, 1, FadeInDuration);
    }

    private void FadeOpacity(double from, double to, TimeSpan duration, Action? completed = null)
    {
        _fadeClock?.Stop();
        _fadeClock ??= new AvaloniaFrameClock(this);
        ApplyWindowAlpha(from);
        var stopwatch = Stopwatch.StartNew();
        _fadeClock.Start(_ =>
        {
            var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            ApplyWindowAlpha(from + ((to - from) * EaseOutCubic(progress)));
            ApplyFadeTransform(to >= from ? progress : 1 - progress);
            if (progress < 1)
            {
                return;
            }

            _fadeClock?.Stop();
            if (to >= from && _liquidGlass)
            {
                // Layered windows and the DWM acrylic backdrop don't mix: while WS_EX_LAYERED is
                // present DWM leaves the acrylic surface square. The fade only needs the layered
                // alpha; drop it once the panel is fully visible so DWM can round the acrylic.
                RemoveLayeredWindowStyle();
            }

            completed?.Invoke();
        });
    }

    private void ApplyFadeTransform(double progress)
    {
        if (_fadeScale is null || _fadeTranslate is null)
        {
            return;
        }

        var eased = EaseOutCubic(Math.Clamp(progress, 0, 1));
        _fadeScale.ScaleX = 0.985 + (0.015 * eased);
        _fadeScale.ScaleY = 0.985 + (0.015 * eased);
        _fadeTranslate.Y = 10 * (1 - eased);
    }

    private void ApplyWindowAlpha(double alpha)
        => ApplyWindowAlpha(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, alpha);

    private void ApplyWindowAlpha(IntPtr handle, double alpha)
    {
        _windowAlpha = Math.Clamp(alpha, 0, 1);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!_layeredStyleAdded)
        {
            var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            if ((style & WsExLayered) == 0)
            {
                SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExLayered));
                _layeredStyleAdded = true;
            }
        }

        _ = SetLayeredWindowAttributes(handle, 0, (byte)Math.Round(_windowAlpha * 255), LwaAlpha);
    }

    private void RemoveLayeredWindowStyle()
    {
        if (!_layeredStyleAdded)
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        if ((style & WsExLayered) != 0)
        {
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style & ~WsExLayered));
        }

        _layeredStyleAdded = false;
        _ = SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        // Removing WS_EX_LAYERED resets DWM's corner preference on this stack; re-assert it.
        _ = WindowDwmPolicy.TryEnableRounding(handle, out _);
        ApplyPanelFill();
    }

    private const int GwlExStyle = -20;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint point);
    private const long WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
        uint uFlags);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    /// <summary>Fades the centered settings window out before closing it.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_centerFade && !_allowClose && !_exitStarted)
        {
            e.Cancel = true;
            _exitStarted = true;
            _fadeClock?.Stop();
            FadeOpacity(_windowAlpha, 0, FadeOutDuration, () =>
            {
                _allowClose = true;
                Close();
            });
            return;
        }

        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _fadeClock?.Dispose();
        base.OnClosed(e);
    }

    private static double EaseOutCubic(double progress)
        => 1 - Math.Pow(1 - progress, 3);

    /// <summary>Ordered steps the first-frame preparation performed (empty until the window is shown).</summary>
    public IReadOnlyList<string> FirstFrameSteps => _firstFrameSteps;

    /// <summary>True once the window was prepared before its first composited frame.</summary>
    public bool FirstFramePrepared { get; private set; }

    /// <summary>Which chrome path the window ended up using (diagnostics and tests).</summary>
    public SettingsWindowChrome ChromeMode { get; private set; }

    /// <summary>Corner radius currently painted by the shell (DIP).</summary>
    public double ShellCornerRadiusDip => _shell.CornerRadius.TopLeft;

    /// <summary>Content radius used when DWM rounds the window itself (Windows 11 cuts at about 8 DIP).</summary>
    public const double DwmCornerRadiusDip = 8;

    /// <summary>True when the window corners are rendered by DWM (Windows 11).</summary>
    public bool DwmRoundingActive { get; private set; }

    /// <summary>Diagnostics: every window-region application (size, radius, <c>SetWindowRgn</c> result).</summary>
    public IReadOnlyList<string> RegionTrace => _regionTracker.Trace;

    /// <summary>The geometry the window region currently clips to (physical pixels), or null.</summary>
    public (int Width, int Height, int Radius)? AppliedRegion => _regionTracker.Applied;

    /// <summary>
    /// Native message hook that prepares the window for its first frame. <c>WM_SHOWWINDOW</c> is raised
    /// inside <c>ShowWindow</c> while the window is still hidden, which is the last managed-code moment
    /// before the compositor sees it; <c>WM_ERASEBKGND</c> is swallowed so the default grey erase never
    /// paints the rectangle.
    /// </summary>
    private IntPtr OnNativeMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const uint WmEraseBackground = 0x0014;
        const uint WmSize = 0x0005;
        const uint WmWindowPosChanged = 0x0047;
        const uint WmShowWindow = 0x0018;
        const uint WmDpiChanged = 0x02E0;
        if (msg == WmEraseBackground)
        {
            // Never let the system fill the surface with the class brush: Avalonia paints the panel.
            handled = true;
            return new IntPtr(1);
        }

        if ((msg == WmSize || msg == WmWindowPosChanged) && !_firstFramePreparing && hwnd != IntPtr.Zero)
        {
            // Avalonia applies its own size after creation and again on DPI change; the region must
            // follow every resize or it keeps clipping the old rectangle.
            ApplyCornerRegion("native-" + msg.ToString("X4"));
        }

        if ((msg == WmShowWindow || msg == WmDpiChanged) && !_firstFramePreparing && hwnd != IntPtr.Zero)
        {
            _firstFramePreparing = true;
            try
            {
                // Set the layered alpha before the first composited frame. Doing this only in
                // OnOpened allows one full-opacity frame to flash through.
                if (_centerFade && msg == WmShowWindow)
                {
                    ApplyWindowAlpha(hwnd, 0);
                }

                var radius = WindowCornerRegion.RadiusPx(SettingsMetrics.WindowRadius, RenderScaling);
                var argb = ((uint)_palette.Window.A << 24) | ((uint)_palette.Window.R << 16) |
                    ((uint)_palette.Window.G << 8) | _palette.Window.B;
                var preparation = WindowFirstFrame.Prepare(hwnd, radius, argb);
                _firstFrameSteps = preparation.Steps;
                FirstFramePrepared = preparation.Succeeded;
                if (!preparation.Succeeded)
                {
                    System.Diagnostics.Debug.WriteLine($"settings first-frame: {preparation.Error}");
                }

                ApplyCornerRegion("first-frame");
            }
            finally
            {
                _firstFramePreparing = false;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>Clips the native window to the rounded content shape (no-op until the handle exists).</summary>
    /// <param name="reason">Why the region is being re-applied (kept in <see cref="RegionTrace"/>).</param>
    private void ApplyCornerRegion(string reason)
    {
        if (_closed || TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            // A maximized window is its own rectangle; rounding it would cut the screen edge.
            _regionTracker.Clear(handle);
            return;
        }

        var scaling = RenderScaling <= 0 ? 1.0 : RenderScaling;
        var width = (int)Math.Ceiling(Bounds.Width * scaling);
        var height = (int)Math.Ceiling(Bounds.Height * scaling);
        var radius = WindowCornerRegion.RadiusPx(_shell.CornerRadius.TopLeft, scaling);
        var applied = _regionTracker.TryUpdate(handle, width, height, radius, out var error);
        if (!applied)
        {
            System.Diagnostics.Debug.WriteLine($"settings corner region skipped ({reason}): {error}");
        }
    }

    private static AppTheme ResolvePaletteTheme(AppTheme theme)
        => SystemSettingResolution.ResolveTheme(theme, SystemAppearanceSnapshot.FromWindows().LightTheme);

    private void OnUpdateChanged(object? sender, EventArgs e)
        => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_section == SettingsWindowSection.About)
            {
                RebuildContent();
            }
        });

    /// <summary>
    /// Verifies that the composited window really carries per-pixel alpha by sampling the screen just
    /// outside the painted corner: if that pixel differs from the desktop behind the window, the surface
    /// is opaque and the DWM-rounded fallback has to take over (remote sessions and some drivers report
    /// <c>Transparent</c> while composing an opaque surface).
    /// </summary>
    private void VerifyAlphaOrFallBack()
    {
        if (ChromeMode != SettingsWindowChrome.Transparent || _closed)
        {
            return;
        }

        try
        {
            var origin = Position;
            var scale = RenderScaling <= 0 ? 1.0 : RenderScaling;
            var inset = (int)Math.Round(3 * scale);
            var outside = (int)Math.Round(12 * scale);
            using var bitmap = new System.Drawing.Bitmap(1, 1);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(origin.X + inset, origin.Y + inset, 0, 0, new System.Drawing.Size(1, 1));
                var cornerPixel = bitmap.GetPixel(0, 0);
                graphics.CopyFromScreen(origin.X - outside, origin.Y - outside, 0, 0, new System.Drawing.Size(1, 1));
                var desktopPixel = bitmap.GetPixel(0, 0);
                var delta = Math.Abs(cornerPixel.R - desktopPixel.R) + Math.Abs(cornerPixel.G - desktopPixel.G)
                    + Math.Abs(cornerPixel.B - desktopPixel.B);
                if (delta > 12)
                {
                    // The corner is painted by an opaque surface: switch to the DWM-rounded fallback.
                    ChromeMode = SettingsWindowChrome.Opaque;
                    ApplyOpaqueFallback();
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException
            or NotSupportedException)
        {
            // A capture failure must not break the window; the transparent path stays as configured.
        }
    }

    /// <summary>Switches the window to the opaque, DWM-rounded chrome (no alpha required).</summary>
    private void ApplyOpaqueFallback()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        Background = new SolidColorBrush(_palette.Window);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (WindowCornerRegion.TryEnableDwmRounding(handle, out _))
        {
            DwmRoundingActive = true;
            _ = WindowCornerRegion.TryRemoveDwmBorder(handle, out _);
            _ = WindowCornerRegion.TryDisableNonClientRendering(handle, out _);
            _shell.CornerRadius = new CornerRadius(DwmCornerRadiusDip);
            _shell.BorderThickness = new Thickness(0);
        }
        else
        {
            _shell.CornerRadius = new CornerRadius(0);
        }

        ApplyWindowSurface();
        ApplyCornerRegion("alpha-fallback");
    }

    /// <summary>
    /// Keeps the window surface in sync with the current theme: in the opaque fallback the window
    /// background and the window-class brush must both be the panel colour, otherwise the platform clear
    /// colour (system grey) shows as a band between the rounded edge and the panel - visible in the light
    /// theme, almost invisible on the dark one.
    /// </summary>
    private void ApplyWindowSurface()
    {
        if (ChromeMode == SettingsWindowChrome.Transparent)
        {
            Background = Brushes.Transparent;
            return;
        }

        var brush = new SolidColorBrush(_palette.Window);
        Background = brush;
        if (TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero)
        {
            return;
        }

        var argb = ((uint)_palette.Window.A << 24) | ((uint)_palette.Window.R << 16) |
            ((uint)_palette.Window.G << 8) | _palette.Window.B;
        var radius = WindowCornerRegion.RadiusPx(_shell.CornerRadius.TopLeft, RenderScaling);
        _ = WindowFirstFrame.Prepare(handle, radius, argb);
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(from.R + ((to.R - from.R) * t)),
            (byte)Math.Round(from.G + ((to.G - from.G) * t)),
            (byte)Math.Round(from.B + ((to.B - from.B) * t)));
    }

    private void ApplyChrome()
    {
        // The material can change while the window is open (Appearance page). Reading it once in the
        // constructor left the chrome decision on a stale value, so switching to liquid glass in a live
        // window kept the opaque square panel (no glass) until the window was reopened.
        _liquidGlass = _settings.Current.Material == FluidBar.App.Settings.IslandMaterial.LiquidGlass;
        if (!_liquidGlass)
        {
            DisposeGlassBackdrop();
        }

        ApplyChromeCore();
        if (_liquidGlass && IsLoaded)
        {
            RefreshGlassBackdrop(force: true);
        }
    }

    private void ApplyChromeCore()
    {
        if (_bleed is not null)
        {
            // The layer must be the exact panel colour: it is only there to cover the platform surface
            // between DWM's corner cut and the painted arc.
            _bleed.Background = new SolidColorBrush(_palette.Window);
            _bleed.IsVisible = !_liquidGlass && ChromeMode == SettingsWindowChrome.Opaque;
        }

        ApplyPanelFill();
        _shell.BorderBrush = _palette.LineStrongBrush;
        _shell.BorderThickness = new Thickness(SettingsMetrics.WindowBorder);

        ApplyWindowSurface();
    }

    private Control BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("50,*") };
        root.Children.Add(BuildTitleBar());

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions(SettingsMetrics.NavWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",*") };
        body.Children.Add(BuildNav());
        body.Children.Add(BuildPane());
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return root;
    }

    private Control BuildTitleBar()
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
            Height = SettingsMetrics.TitleBarHeight,
            Margin = new Thickness(SettingsMetrics.TitleBarPadLeft, 0, SettingsMetrics.TitleBarPadRight, 0),
            // A Grid without a background is not hit-testable, so clicks on the empty part of the title
            // bar fell through to the rounded shell and BeginMoveDrag never ran (the window could not be
            // dragged by its title bar). Transparent keeps the pixels unchanged and makes it hit-testable.
            Background = Brushes.Transparent,
        };
        bar.PointerPressed += (_, e) =>
        {
            // The close button handles its own press; never start a move drag from it.
            if (!e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        // .sw-mark: 24×24, 8 px radius, #070709 with a 14% white rim.
        var mark = new Border
        {
            Width = SettingsMetrics.MarkSize,
            Height = SettingsMetrics.MarkSize,
            CornerRadius = new CornerRadius(SettingsMetrics.MarkRadius),
            Background = new SolidColorBrush(_palette.Mark),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new SettingsIcon
            {
                Kind = SettingsIconKind.Island,
                Brush = Brushes.White,
                Width = SettingsMetrics.MarkIcon,
                Height = SettingsMetrics.MarkIcon,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(mark, 0);
        bar.Children.Add(mark);

        // .sw-title: 13 px / 600 / 0.005em.
        _titleText.FontFamily = FluidBar.App.Theme.Fonts.Ui;
        _titleText.FontSize = SettingsMetrics.TitleSize;
        _titleText.FontWeight = FontWeight.SemiBold;
        _titleText.LetterSpacing = SettingsMetrics.TitleSize * SettingsMetrics.TitleTracking;
        _titleText.Foreground = _palette.ForegroundBrush;
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _titleText.Margin = new Thickness(SettingsMetrics.TitleBarGap, 0, 0, 0);
        Grid.SetColumn(_titleText, 1);
        bar.Children.Add(_titleText);

        // .sw-sub: 9 px uppercase, 0.09em tracking, --ui-fg-3.
        _subText.FontSize = SettingsMetrics.SubSize;
        _subText.LetterSpacing = SettingsMetrics.SubSize * SettingsMetrics.SubTracking;
        _subText.Foreground = _palette.TertiaryBrush;
        _subText.VerticalAlignment = VerticalAlignment.Center;
        _subText.Margin = new Thickness(SettingsMetrics.TitleBarGap, 0, 0, 0);
        Grid.SetColumn(_subText, 2);
        bar.Children.Add(_subText);

        // .sw-close: 32×32, margin-left auto, danger-red hover.
        var closeIcon = new SettingsIcon
        {
            Kind = SettingsIconKind.Close,
            Brush = _palette.SecondaryBrush,
            Width = SettingsMetrics.CloseIcon,
            Height = SettingsMetrics.CloseIcon,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var close = new Border
        {
            Width = SettingsMetrics.CloseSize,
            Height = SettingsMetrics.CloseSize,
            CornerRadius = new CornerRadius(SettingsMetrics.CloseRadius),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = closeIcon,
        };
        close.PointerEntered += (_, _) =>
        {
            close.Background = new SolidColorBrush(Color.FromArgb(209, 255, 69, 58));
            closeIcon.Brush = Brushes.White;
            closeIcon.InvalidateVisual();
        };
        close.PointerExited += (_, _) =>
        {
            close.Background = Brushes.Transparent;
            closeIcon.Brush = _palette.SecondaryBrush;
            closeIcon.InvalidateVisual();
        };
        close.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Close();
        };
        ToolTip.SetTip(close, _localizer.Get(LocKeys.Common.Close));
        Grid.SetColumn(close, 5);
        bar.Children.Add(close);

        // .sw-minimize: same 32×32 hit box as close, neutral hover instead of the danger red.
        var minimizeIcon = new SettingsIcon
        {
            Kind = SettingsIconKind.Minimize,
            Brush = _palette.SecondaryBrush,
            Width = SettingsMetrics.CloseIcon,
            Height = SettingsMetrics.CloseIcon,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var minimize = new Border
        {
            Width = SettingsMetrics.CloseSize,
            Height = SettingsMetrics.CloseSize,
            CornerRadius = new CornerRadius(SettingsMetrics.CloseRadius),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = minimizeIcon,
        };
        minimize.PointerEntered += (_, _) =>
        {
            minimize.Background = _palette.RaisedHoverBrush;
            minimizeIcon.Brush = _palette.ForegroundBrush;
            minimizeIcon.InvalidateVisual();
        };
        minimize.PointerExited += (_, _) =>
        {
            minimize.Background = Brushes.Transparent;
            minimizeIcon.Brush = _palette.SecondaryBrush;
            minimizeIcon.InvalidateVisual();
        };
        minimize.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            WindowState = WindowState.Minimized;
        };
        ToolTip.SetTip(minimize, _localizer.Get(LocKeys.Common.Minimize));
        Grid.SetColumn(minimize, 4);
        bar.Children.Add(minimize);

        return new Border
        {
            BorderBrush = _palette.LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.Transparent,
            Child = bar,
        };
    }

    private Control BuildNav()
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        // .nav-kicker { padding: 4px 10px 7px; 8.5px/600; 0.13em; uppercase }
        var kicker = new TextBlock
        {
            FontSize = SettingsMetrics.KickerSize,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = SettingsMetrics.KickerSize * SettingsMetrics.KickerTracking,
            Foreground = _palette.TertiaryBrush,
            Margin = new Thickness(SettingsMetrics.KickerPadX, SettingsMetrics.KickerPadTop,
                SettingsMetrics.KickerPadX, SettingsMetrics.KickerPadBottom),
        };
        Grid.SetRow(kicker, 0);
        grid.Children.Add(kicker);
        _kicker = kicker;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _nav,
        };
        SlimScrollBar(scroll);
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        // .nav-version { margin-top: auto; padding: 12px 10px 4px; 8.5px/1.5; border-top }
        var version = new StackPanel
        {
            Margin = new Thickness(SettingsMetrics.VersionPadX, SettingsMetrics.VersionPadTop,
                SettingsMetrics.VersionPadX, SettingsMetrics.VersionPadBottom),
        };
        _versionText.Text = "FluidBar 2.0.0";
        _versionText.FontSize = SettingsMetrics.VersionSize;
        _versionText.FontWeight = FontWeight.Medium;
        _versionText.LineHeight = SettingsMetrics.VersionSize * SettingsMetrics.VersionLineHeight;
        _versionText.Foreground = _palette.SecondaryBrush;
        version.Children.Add(_versionText);
        _versionLine.FontSize = SettingsMetrics.VersionSize;
        _versionLine.LineHeight = SettingsMetrics.VersionSize * SettingsMetrics.VersionLineHeight;
        _versionLine.Foreground = _palette.TertiaryBrush;
        _versionLine.TextWrapping = TextWrapping.Wrap;
        version.Children.Add(_versionLine);
        var footer = new Border
        {
            BorderBrush = _palette.LineBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = version,
        };
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        RebuildNav();
        return new Border
        {
            BorderBrush = _palette.LineBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(SettingsMetrics.NavPadX, SettingsMetrics.NavPadY,
                SettingsMetrics.NavPadX, SettingsMetrics.NavPadY),
            Child = grid,
        };
    }

    private TextBlock? _kicker;

    private Control BuildPane()
    {
        _content.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _content.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        SlimScrollBar(_content);
        _content.Padding = new Thickness(SettingsMetrics.PanePadX, SettingsMetrics.PanePadY,
            SettingsMetrics.PanePadX, SettingsMetrics.PanePadY);
        ApplyPaneScrollBarStyle();

        Grid.SetColumn(_content, 1);
        return _content;
    }

    /// <summary>
    /// .settings-pane scrollbar: 6 px wide, 4 px radius thumb at rgba(255,255,255,.16), transparent track.
    /// The Fluent default is wider and brighter, which is visible in the prototype comparison.
    /// </summary>
    private void ApplyPaneScrollBarStyle()
    {
        var thumb = new SolidColorBrush(Color.FromArgb(41, 255, 255, 255));
        var thumbHover = new SolidColorBrush(Color.FromArgb(66, 255, 255, 255));
        _content.AllowAutoHide = false;
        // Fluent's scrollbar paints its thumb from theme resources; provide the prototype values so the
        // pane scrollbar matches .settings-pane (thumb rgba(255,255,255,.16), transparent track).
        foreach (var key in new[]
                 {
                     "ScrollBarThumbFill", "ScrollBarThumbFillPointerOver", "ScrollBarThumbFillPressed",
                     "ScrollBarTrackFill", "ScrollBarBackground",
                 })
        {
            _content.Resources[key] = thumb;
        }

        _content.Styles.Add(new Style(x => x.OfType<ScrollBar>())
        {
            Setters =
            {
                new Setter(WidthProperty, SettingsMetrics.PaneScrollBarGutter),
                new Setter(MinWidthProperty, SettingsMetrics.PaneScrollBarGutter),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.ForegroundProperty, thumb),
            },
        });
        _content.Styles.Add(new Style(x => x.OfType<ScrollBar>().Template().OfType<Thumb>())
        {
            Setters =
            {
                new Setter(WidthProperty, SettingsMetrics.PaneScrollBarWidth),
                // The prototype's thumb is 6 px inside a wider overlay gutter.
                new Setter(MarginProperty, new Thickness((SettingsMetrics.PaneScrollBarGutter - SettingsMetrics.PaneScrollBarWidth) / 2, 0)),
                new Setter(CornerRadiusProperty, new CornerRadius(4)),
                new Setter(TemplatedControl.BackgroundProperty, thumb),
                // Windows renders the prototype's overlay scrollbar at half strength while idle
                // (measured in shot-11: thumb ≈ rgba(255,255,255,.08) over the pane fill).
                new Setter(OpacityProperty, 0.23),
            },
        });
        _content.Styles.Add(new Style(x => x.OfType<ScrollBar>().Template().OfType<Thumb>().Class(":pointerover"))
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, thumbHover) },
        });
        _content.Styles.Add(new Style(x => x.OfType<ScrollBar>().Template().OfType<Thumb>().Template().OfType<Border>())
        {
            Setters = { new Setter(Border.BackgroundProperty, thumb) },
        });
    }

    private void RebuildNav()
    {
        _nav.Children.Clear();
        _navRows.Clear();
        _navIcons.Clear();
        _navLabels.Clear();
        _navCounts.Clear();
        foreach (var (section, labelKey, icon) in SectionOrder)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var glyph = new SettingsIcon
            {
                Kind = icon,
                Brush = _palette.TertiaryBrush,
                Width = SettingsMetrics.NavIcon,
                Height = SettingsMetrics.NavIcon,
            };
            Grid.SetColumn(glyph, 0);
            row.Children.Add(glyph);
            _navIcons.Add(glyph);

            var label = new TextBlock
            {
                Text = _localizer.Get(labelKey),
                FontSize = SettingsMetrics.NavLabelSize,
                Foreground = _palette.SecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(SettingsMetrics.NavItemGap, 0, 0, 0),
            };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            _navLabels.Add(label);

            // .nav-count { margin-left: auto; min-width: 16; height: 16; padding: 0 4; radius 5; }
            var countHost = new Border
            {
                MinWidth = SettingsMetrics.NavCountHeight,
                Height = SettingsMetrics.NavCountHeight,
                Padding = new Thickness(SettingsMetrics.NavCountPadX, 0, SettingsMetrics.NavCountPadX, 0),
                CornerRadius = new CornerRadius(SettingsMetrics.NavCountRadius),
                Background = _palette.RaisedHoverBrush,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsVisible = false,
                Child = new TextBlock
                {
                    FontSize = SettingsMetrics.NavCountSize,
                    Foreground = _palette.SecondaryBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Grid.SetColumn(countHost, 2);
            row.Children.Add(countHost);
            _navCounts[section] = countHost;

            var accent = new Border
            {
                Width = 2,
                Height = 18,
                CornerRadius = new CornerRadius(1),
                Background = _palette.ActiveBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false,
                Margin = new Thickness(-SettingsMetrics.NavItemPadX + 2, 0, 0, 0),
            };
            _navAccents.Add(accent);
            var itemContent = new Grid();
            itemContent.Children.Add(row);
            itemContent.Children.Add(accent);

            var item = new Border
            {
                Height = SettingsMetrics.NavItemHeight,
                CornerRadius = new CornerRadius(SettingsMetrics.NavItemRadius),
                Padding = new Thickness(SettingsMetrics.NavItemPadX, 0, SettingsMetrics.NavItemPadX, 0),
                Background = Brushes.Transparent,
                Child = itemContent,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            var captured = section;
            item.PointerEntered += (_, _) =>
            {
                if (captured != _section)
                {
                    item.Background = _palette.RaisedBrush;
                }
            };
            item.PointerExited += (_, _) =>
            {
                if (captured != _section)
                {
                    item.Background = Brushes.Transparent;
                }
            };
            item.PointerPressed += (_, e) =>
            {
                ShowSection(captured);
                e.Handled = true;
            };
            _navRows.Add(item);
            _nav.Children.Add(item);
        }

        if (_kicker is not null)
        {
            _kicker.Text = _localizer.Get(LocKeys.Settings.NavKicker).ToUpperInvariant();
        }

        _titleText.Text = _localizer.Get(LocKeys.Settings.Title);
        _subText.Text = "FLUIDBAR 2.0";
        _versionLine.Text = _localizer.Get(LocKeys.Settings.VersionLine);
        UpdateNavSelection();
    }

    private readonly List<Border> _navAccents = [];

    /// <summary>
    /// Slim overlay scrollbar: 6 DIP wide with a semi-transparent thumb. Applied to every pane so the
    /// scrollbar reads as a hint instead of a chrome element.
    /// </summary>
    /// <param name="scroll">Scroll viewer to restyle.</param>
    private static void SlimScrollBar(ScrollViewer scroll)
    {
        scroll.Styles.Add(new Style(x => x.OfType<ScrollBar>())
        {
            Setters =
            {
                new Setter(WidthProperty, 6.0),
                new Setter(OpacityProperty, 0.55),
                new Setter(MinWidthProperty, 6.0),
            },
        });
    }

    private void UpdateNavSelection()
    {
        for (var i = 0; i < _navRows.Count; i++)
        {
            var section = SectionOrder[i].Section;
            var selected = section == _section;
            _navRows[i].Background = selected ? _palette.RaisedBrush : Brushes.Transparent;
            _navLabels[i].Foreground = selected ? _palette.ForegroundBrush : _palette.SecondaryBrush;
            _navLabels[i].FontWeight = selected ? FontWeight.Medium : FontWeight.Normal;
            _navAccents[i].IsVisible = selected;
            _navIcons[i].Brush = selected ? _palette.ForegroundBrush : _palette.TertiaryBrush;
            _navIcons[i].InvalidateVisual();
        }
    }

    private async Task RefreshNavCountsAsync()
    {
        try
        {
            var plugins = await _plugins.ListAsync(CancellationToken.None).ConfigureAwait(true);
            SetNavCount(SettingsWindowSection.Plugins, plugins.Count);
        }
        catch (Exception)
        {
            // A count badge is decorative; never let it break the window.
        }

        try
        {
            var entries = await _marketplace.BrowseAsync(new MarketplaceQuery(null, "all", "stable"), CancellationToken.None)
                .ConfigureAwait(true);
            SetNavCount(SettingsWindowSection.Marketplace, entries.Count);
        }
        catch (Exception)
        {
            // Same as above.
        }
    }

    private void SetNavCount(SettingsWindowSection section, int count)
    {
        if (!_navCounts.TryGetValue(section, out var host) || count <= 0)
        {
            return;
        }

        if (host.Child is TextBlock text)
        {
            text.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        host.IsVisible = true;
    }

    private void RebuildContent()
    {
        var context = new SettingsWindowContext
        {
            Settings = _settings,
            Localizer = _localizer,
            Marketplace = _marketplace,
            Plugins = _plugins,
            Palette = _palette,
            Owner = () => this,
            OpenPath = _openPath,
            ShowStatus = ShowStatus,
            Updates = _updates,
            FrameStats = _frameStats,
            RestartToInstall = _restartToInstall,
            Pages = _pages,
        };
        var content = SettingsSectionBuilder.Build(_section, context);
        if (_section == SettingsWindowSection.Appearance)
        {
            _preview = FindPreview(content);
        }

        _content.Content = content;
        UpdateNavSelection();
    }

    private static IslandPreview? FindPreview(Control root)
    {
        if (root is IslandPreview preview)
        {
            return preview;
        }

        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Control control)
                {
                    var found = FindPreview(control);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }
        }

        if (root is Decorator decorator && decorator.Child is Control decorated)
        {
            return FindPreview(decorated);
        }

        if (root is ContentControl contentControl && contentControl.Content is Control content)
        {
            return FindPreview(content);
        }

        return null;
    }

    private void ShowStatus(string text)
    {
        _status = text;
        StatusChanged?.Invoke(text);
    }

    private void OnLocalizerChanged(object? sender, EventArgs e)
    {
        RebuildNav();
        RebuildContent();
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        var themeChanged = ResolvePaletteTheme(previous.Theme) != ResolvePaletteTheme(current.Theme);
        if (themeChanged)
        {
            _palette = SettingsPalette.For(ResolvePaletteTheme(current.Theme), _liquidGlass);
            ApplyChrome();
            RebuildTitleBarBrushes();
            RebuildNav();
            RebuildContent();
            return;
        }

        if (previous.Material != current.Material)
        {
            // The palette is glass-aware (brighter dark-theme text over the blurred desktop).
            _liquidGlass = current.Material == FluidBar.App.Settings.IslandMaterial.LiquidGlass;
            _palette = SettingsPalette.For(ResolvePaletteTheme(current.Theme), _liquidGlass);
            ApplyChrome();
            RebuildTitleBarBrushes();
            return;
        }

        if (_preview is not null && _section == SettingsWindowSection.Appearance)
        {
            _preview.Update(current, _localizer);
        }

        // Diagnostics/update panes show live values: rebuild so the toggle has an immediate effect.
        if (_section == SettingsWindowSection.Advanced && previous.Advanced.FrameStats != current.Advanced.FrameStats)
        {
            RebuildContent();
        }
    }

    private void RebuildTitleBarBrushes()
    {
        _titleText.Foreground = _palette.ForegroundBrush;
        _subText.Foreground = _palette.TertiaryBrush;
        _versionText.Foreground = _palette.SecondaryBrush;
        _versionLine.Foreground = _palette.TertiaryBrush;
        if (_kicker is not null)
        {
            _kicker.Foreground = _palette.TertiaryBrush;
        }
    }
    /// <summary>Tint alpha over a DWM backdrop (fallback path, no self-drawn blur).</summary>
    private const double PanelGlassTopAlpha = 0.52;

    /// <summary>Tint alpha over a DWM backdrop, bottom edge.</summary>
    private const double PanelGlassBottomAlpha = 0.66;

    /// <summary>Tint alpha over the self-drawn blurred desktop, top edge. Lower = clearer glass.</summary>
    private const double PanelGlassOverImageTopAlpha = 0.34;

    /// <summary>Tint alpha over the self-drawn blurred desktop, bottom edge.</summary>
    private const double PanelGlassOverImageBottomAlpha = 0.48;

    /// <summary>Tint alpha over the blurred desktop in the dark theme, top edge (denser than the light theme).</summary>
    private const double PanelGlassDarkTopAlpha = 0.62;

    /// <summary>Tint alpha over the blurred desktop in the dark theme, bottom edge.</summary>
    private const double PanelGlassDarkBottomAlpha = 0.76;

    // Light glass has to stay *light*: a bright desktop needs only a touch more tint, while a dark desktop has to
    // be pushed back up or the panel turns mid-grey and every grey label disappears into it.
    /// <summary>Light theme: extra tint on a bright backdrop.</summary>
    private const double PanelGlassLightScrimMaxAlpha = 0.10;

    /// <summary>Light theme: backdrop luminance above which the tint densifies.</summary>
    private const double PanelGlassLightScrimThreshold = 0.70;

    /// <summary>Light theme: extra tint on a dark backdrop (keeps the panel white instead of grey).</summary>
    private const double PanelGlassLightLiftMaxAlpha = 0.45;

    /// <summary>Light theme: backdrop luminance below which the lift starts.</summary>
    private const double PanelGlassLightLiftThreshold = 0.55;

    // Dark glass is the mirror image: dense tint, denser on a bright backdrop, no lift.
    /// <summary>Dark theme: extra tint on a bright backdrop.</summary>
    private const double PanelGlassDarkScrimMaxAlpha = 0.30;

    /// <summary>Dark theme: backdrop luminance above which the tint densifies.</summary>
    private const double PanelGlassDarkScrimThreshold = 0.42;

    /// <summary>Dark theme: no lift (a dark panel must never be lightened).</summary>
    private const double PanelGlassDarkLiftMaxAlpha = 0.0;

    /// <summary>Dark theme: lift threshold (unused while the lift is zero).</summary>
    private const double PanelGlassDarkLiftThreshold = 0.55;

    /// <summary>
    /// One theme's glass material: tint density plus its contrast scrim.
    /// <para>
    /// The two themes are separate materials, not one material with two numbers. Sharing any of these four values
    /// leaves exactly one theme looking right (user report 2026-10-24: "深色和浅色只有一个会好看，绝对是你没把
    /// 他们两个分开"), and because the tint is baked into the backdrop bitmap, a frame composed for one theme must
    /// never be painted while the other is active.
    /// </para>
    /// </summary>
    /// <param name="TopAlpha">Tint alpha at the top edge (0..1).</param>
    /// <param name="BottomAlpha">Tint alpha at the bottom edge (0..1).</param>
    /// <param name="ScrimThreshold">Backdrop luminance above which the scrim densifies the tint.</param>
    /// <param name="ScrimMaxAlpha">Extra tint at full backdrop brightness.</param>
    internal readonly record struct GlassMaterial(
        double TopAlpha,
        double BottomAlpha,
        double ScrimThreshold,
        double ScrimMaxAlpha,
        double LiftThreshold,
        double LiftMaxAlpha);

    /// <summary>Material for a theme (the light values are exactly the ones that were signed off).</summary>
    /// <param name="isLight">True for the light theme.</param>
    internal static GlassMaterial GlassMaterialFor(bool isLight) => isLight
        ? new GlassMaterial(PanelGlassOverImageTopAlpha, PanelGlassOverImageBottomAlpha,
            PanelGlassLightScrimThreshold, PanelGlassLightScrimMaxAlpha,
            PanelGlassLightLiftThreshold, PanelGlassLightLiftMaxAlpha)
        : new GlassMaterial(PanelGlassDarkTopAlpha, PanelGlassDarkBottomAlpha,
            PanelGlassDarkScrimThreshold, PanelGlassDarkScrimMaxAlpha,
            PanelGlassDarkLiftThreshold, PanelGlassDarkLiftMaxAlpha);

    /// <summary>
    /// Paints the panel behind the content. Liquid glass uses the blurred desktop as the material (baked with
    /// its tint, see <see cref="OnGlassFrameReady"/>); until the first frame lands, a DWM backdrop plus a
    /// denser tint; every other material stays opaque so text contrast never depends on the wallpaper.
    /// </summary>
    private void ApplyPanelFill()
    {
        if (_shell is null)
        {
            return;
        }

        var window = _palette.Window;
        var translucent = _liquidGlass && ChromeMode == SettingsWindowChrome.Transparent;

        if (!translucent)
        {
            _shell.Background = new SolidColorBrush(Color.FromArgb(255, window.R, window.G, window.B));
            _shell.BorderBrush = _palette.LineStrongBrush;
            return;
        }

        if (!_composing && _glassBrush is not null && _composedForLight != _palette.IsLight)
        {
            // Pixels from the other theme: recompose before painting them, otherwise the theme just switched to
            // inherits the other theme's tint and only one of the two ever looks right.
            _ = ComposeGlass();
            if (_composedForLight != _palette.IsLight)
            {
                _glassBrush = null;
            }
        }

        if (_glassBrush is not null)
        {
            _shell.Background = _glassBrush;
        }
        else
        {
            // Only reachable when the desktop refused a capture: a flat tint is honest and, unlike the old
            // acrylic-accent attempt, deterministic — that overlay applied or not depending on timing, which
            // changed how dark the panel looked between launches.
            _shell.Background = TintGradient(window, PanelGlassTopAlpha, PanelGlassBottomAlpha);
        }

        // A lit rim reads as thickness: glass needs an edge highlight, otherwise the panel is a flat sheet.
        _shell.BorderBrush = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new Avalonia.Media.GradientStop(Color.FromArgb(64, 255, 255, 255), 0),
                new Avalonia.Media.GradientStop(Color.FromArgb(18, 255, 255, 255), 0.55),
                new Avalonia.Media.GradientStop(Color.FromArgb(38, 255, 255, 255), 1),
            },
        };
        _shell.BorderThickness = new Thickness(1);
    }

    /// <summary>Vertical tint gradient used for the glass slab.</summary>
    private static LinearGradientBrush TintGradient(Color window, double topAlpha, double bottomAlpha)
        => new()
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new Avalonia.Media.GradientStop(
                    Color.FromArgb((byte)Math.Round(255 * topAlpha), window.R, window.G, window.B), 0),
                new Avalonia.Media.GradientStop(
                    Color.FromArgb((byte)Math.Round(255 * bottomAlpha), window.R, window.G, window.B), 1),
            },
        };

    /// <summary>
    /// Captures the desktop behind this window and paints it as the panel material: capture → 1/6 downsample →
    /// gaussian blur → saturation lift → tint bake, the island's own glass recipe. It has to be drawn — a
    /// borderless Win32 window never gets a real DWM backdrop (<c>DWMWA_SYSTEMBACKDROP_TYPE</c> returns S_OK
    /// while DWM paints nothing), which is why the panel used to be a flat slab.
    /// </summary>
    /// <param name="force">Capture immediately instead of waiting for the drag throttle.</param>
    private void RefreshGlassBackdrop(bool force)
    {
        if (!_liquidGlass || ChromeMode != SettingsWindowChrome.Transparent || _closed)
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (_backdrop is null)
        {
            // The capture has to return what is *behind* the window, never the window itself.
            _ = ScreenBackdropCapture.TryExcludeFromCapture(handle);
            _backdrop = new SettingsGlassBackdrop();
            _backdrop.RegionReady += OnGlassRegionReady;
        }

        var origin = Position; // physical pixels, unlike Bounds (DIP)
        var scale = RenderScaling <= 0 ? 1.0 : RenderScaling;
        _backdropRect = (origin.X, origin.Y,
            (int)Math.Round(Bounds.Width * scale), (int)Math.Round(Bounds.Height * scale));

        UpdateBackdropRect();

        if (!_backdrop.HasRegion)
        {
            // First material: capture synchronously (GDI, ~2 ms) so the first visible frame is already glass.
            // An async first capture leaves a flat tint on screen for as long as the capture takes, which made
            // one launch look right and the next look wrong.
            _backdrop.TryCaptureNow(_backdropRect, allowComposedCapture: false);
        }

        _ = _backdrop.RequestRegion(_backdropRect, allowComposedCapture: !_dragging, force);
        ComposeGlass();
    }

    /// <summary>
    /// Recomputes the physical window rectangle used for the capture and for the offset sampling.
    /// <para>
    /// Taken from Win32 rather than from Avalonia: on this machine <c>Bounds</c> (900×620) times
    /// <c>RenderScaling</c> (1.5) and <c>Position</c> (605,335) described a 1350×930 window at (605,335) for a
    /// real 900×620 window at (403,223). The backdrop was therefore captured from the wrong place at the wrong
    /// zoom, so the material looked right in one launch and wrong in the next depending on what happened to sit
    /// under that rectangle (user report 2026-10-24: "第一次窗口的样子非常好看，第二次就不好看了").
    /// </para>
    /// </summary>
    private void UpdateBackdropRect()
    {
        if (TryGetPhysicalWindowRect(out var physical))
        {
            _backdropRect = physical;
            return;
        }

        var origin = Position;
        var scale = RenderScaling <= 0 ? 1.0 : RenderScaling;
        _backdropRect = (origin.X, origin.Y,
            (int)Math.Round(Bounds.Width * scale), (int)Math.Round(Bounds.Height * scale));
    }

    /// <summary>Client rectangle of this window in physical screen pixels (Win32 truth, no DIP conversion).</summary>
    /// <param name="rect">Physical bounds when the handle is available.</param>
    /// <returns>False when the window has no platform handle yet.</returns>
    private bool TryGetPhysicalWindowRect(out (int X, int Y, int Width, int Height) rect)
    {
        rect = default;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var clientOrigin = default(NativePoint);
        if (!GetClientRect(handle, out var client))
        {
            _physicalRectProbe = "GetClientRect failed err=" + Marshal.GetLastWin32Error();
            return false;
        }

        if (!ClientToScreen(handle, ref clientOrigin))
        {
            _physicalRectProbe = "ClientToScreen failed err=" + Marshal.GetLastWin32Error();
            return false;
        }

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        rect = (clientOrigin.X, clientOrigin.Y, width, height);
        _physicalRectOk = true;
        _physicalRectProbe = "client=" + width + "x" + height + " origin=" + clientOrigin.X + ","
            + clientOrigin.Y + " native=" + client.Left + "," + client.Top + "," + client.Right + ","
            + client.Bottom;
        return true;
    }

    /// <summary>
    /// Samples the cached region for the current window position and uploads it. No screen read: this is the
    /// zero-latency path, so a move is drawn in the same frame as the pointer.
    /// </summary>
    /// <returns>False when the window has moved outside the cached region.</returns>
    private bool ComposeGlass()
    {
        if (_backdrop is not { } backdrop)
        {
            return false;
        }

        _composing = true;
        try
        {
            return ComposeGlassFrame(backdrop);
        }
        finally
        {
            _composing = false;
        }
    }

    private bool ComposeGlassFrame(SettingsGlassBackdrop backdrop)
    {
        var window = _palette.Window;
        var material = GlassMaterialFor(_palette.IsLight);
        var started = Stopwatch.GetTimestamp();
        if (!backdrop.TryCompose(_backdropRect, new SKColor(window.R, window.G, window.B),
                material.TopAlpha, material.BottomAlpha, material.ScrimThreshold,
                material.ScrimMaxAlpha, material.LiftThreshold, material.LiftMaxAlpha, out var frame))
        {
            _glassBrushNeedsApply = true; // the next frame may be a different size after a region change
            return false;
        }

        UploadGlassFrame(frame);

        long checksum = 0;
        double sumR = 0, sumG = 0, sumB = 0;
        var sampled = 0;
        for (var i = 0; i + 3 < frame.Pixels.Length; i += 28)
        {
            checksum += frame.Pixels[i] + (frame.Pixels[i + 1] * 3L) + (frame.Pixels[i + 2] * 7L);
            sumB += frame.Pixels[i];
            sumG += frame.Pixels[i + 1];
            sumR += frame.Pixels[i + 2];
            sampled++;
        }

        _glassFrameDiagnostics = sampled == 0
            ? "none"
            : "mean=" + (sumR / sampled).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "/"
              + (sumG / sampled).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "/"
              + (sumB / sampled).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
              + " sum=" + checksum;

        if (string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_GLASS_TRACE"), "1", StringComparison.Ordinal))
        {
            try
            {
                var ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fluidbar-compose.log"),
                    "compose=" + ms.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                    + "ms " + frame.Width + "x" + frame.Height + "\n");
            }
            catch
            {
                // diagnostics only
            }
        }

        return true;
    }

    private void OnGlassRegionReady()
        => Dispatcher.UIThread.Post(() => ComposeGlass(), DispatcherPriority.Render);

    private void UploadGlassFrame(GlassFrame frame)
    {
        if (_closed)
        {
            return;
        }

        if (_glassTarget is null
            || _glassTarget.PixelSize.Width != frame.Width
            || _glassTarget.PixelSize.Height != frame.Height)
        {
            _glassTarget?.Dispose();
            _glassTarget = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            _glassBrush = new ImageBrush(_glassTarget) { Stretch = Stretch.Fill };
            _glassBrushNeedsApply = true;
        }

        using (var locked = _glassTarget.Lock())
        {
            var destination = locked.Address;
            var destinationRowBytes = locked.RowBytes;
            var rowBytes = Math.Min(destinationRowBytes, frame.RowBytes);
            for (var y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(frame.Pixels, y * frame.RowBytes, IntPtr.Add(destination, y * destinationRowBytes),
                    rowBytes);
            }
        }

        // Stamp before the brush is applied: ApplyPanelFill repaints at once and has to know these pixels match
        // the active theme.
        _composedForLight = _palette.IsLight;

        if (_glassBrushNeedsApply)
        {
            _glassBrushNeedsApply = false;
            ApplyPanelFill();
        }
        else
        {
            // Same bitmap, new pixels: no brush churn and no layout pass, just one repaint. This is what keeps
            // a drag at frame rate — the previous version re-encoded the backdrop to PNG and rebuilt the brush
            // on every position event.
            _shell.InvalidateVisual();
        }
    }

    private void PositionChangedGlassHook(object? sender, PixelPointEventArgs e)
    {
        _dragging = true;
        UpdateBackdropRect();
        if (!ComposeGlass())
        {
            // Out of the cached region: ask for a new one (async) and keep the last frame meanwhile.
            _ = _backdrop?.RequestRegion(_backdropRect, allowComposedCapture: false);
        }


        // Restart the settle countdown on every move; a stopped window gets one accurate (composed) capture.
        if (_settleTimer is null)
        {
            _settleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            _settleTimer.Tick += OnSettleTick;
        }

        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private void OnSettleTick(object? sender, EventArgs e)
    {
        _settleTimer?.Stop();
        _dragging = false;
        RefreshGlassBackdrop(force: true);
    }

    private void DisposeGlassBackdrop()
    {
        if (_backdrop is not null)
        {
            _backdrop.RegionReady -= OnGlassRegionReady;
            _backdrop.Dispose();
            _backdrop = null;
        }

        _glassBrush = null;
        _glassTarget?.Dispose();
        _glassTarget = null;
        _appliedRect = default;
    }

    /// <summary>
    /// Cheap idempotent re-assert of the fallback DWM backdrop. DWM drops the system backdrop after some
    /// style/focus transitions (layered toggle, window re-show, theme change); the drawn material does not care,
    /// so this only matters while no capture has landed yet.
    /// </summary>
    private void ReassertAcrylicBackdrop()
    {
        if (!_liquidGlass || ChromeMode != SettingsWindowChrome.Transparent || _glassBrush is not null)
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _ = WindowDwmPolicy.TryEnableAcrylicBackdrop(handle, out _);
        _ = WindowDwmPolicy.TryEnableRounding(handle, out _);
    }

    /// <summary>
    /// Writes a PNG of the composed window (visual tree → RenderTargetBitmap) so the liquid-glass material can
    /// be inspected without a screenshot of the desktop: the render includes the drawn blurred backdrop.
    /// </summary>
    private void MaybeWriteSnapshot()
    {
        var path = Environment.GetEnvironmentVariable("FLUIDBAR_SETTINGS_SNAPSHOT");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        DispatcherTimer.RunOnce(() =>
        {
            try
            {
                var size = new PixelSize(
                    Math.Max(1, (int)Math.Round(Bounds.Width)),
                    Math.Max(1, (int)Math.Round(Bounds.Height)));
                using var target = new RenderTargetBitmap(size, new Vector(96, 96));
                target.Render(this);
                target.Save(path + ".png");

                System.IO.File.AppendAllText(path + ".log",
                    DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)
                    + " snapshot drawn=" + (_glassBrush is not null)
                    + " captures=" + (_backdrop?.CaptureCount ?? 0)
                    + " failures=" + (_backdrop?.CaptureFailures ?? 0)
                    + " captureMs=" + (_backdrop?.LastCaptureMs ?? 0).ToString("0.0",
                        System.Globalization.CultureInfo.InvariantCulture)
                    + " bakeMs=" + (_backdrop?.LastBakeMs ?? 0).ToString("0.0",
                        System.Globalization.CultureInfo.InvariantCulture)
                    + " rect=" + _backdropRect
                    + " bounds=" + Bounds.Width.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                    + "x" + Bounds.Height.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                    + " scaling=" + RenderScaling.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    + " position=" + Position.X + "," + Position.Y
                    + " physicalOk=" + _physicalRectOk
                    + " probe=" + _physicalRectProbe
                    + " frame=" + _glassFrameDiagnostics
                    + " materialFor="
                    + (_composedForLight is null ? "none" : (_composedForLight.Value ? "light" : "dark"))
                    + " theme=" + (_palette.IsLight ? "light" : "dark")
                    + " size=" + size.Width + "x" + size.Height + "\n");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("[fluidbar settings] snapshot failed: " + ex.Message);
            }
        }, TimeSpan.FromMilliseconds(1500));
    }

}

/// <summary>Which chrome path the settings window uses for its rounded shape.</summary>
public enum SettingsWindowChrome
{
    /// <summary>Per-pixel alpha: transparent window background and a painted 22 DIP rounded panel.</summary>
    Transparent,

    /// <summary>No alpha: DWM rounds the window itself (or a square opaque panel as the last resort).</summary>
    Opaque,
}
