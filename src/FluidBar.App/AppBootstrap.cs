using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using FluidBar.App.Diagnostics;
using FluidBar.App.Hosting;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Localization;
using FluidBar.App.Plugins;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Settings;
using FluidBar.Marketplace.Signing;
using FluidBar.Plugins.Builtin;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Sdk.Versioning;

namespace FluidBar.App;

/// <summary>
/// Composition root: loads settings and catalogs off the UI thread, creates the island host, and
/// wires the settings/marketplace windows. Kept free of business rules (the host only forwards).
/// <para>
/// Host selection: <c>FLUIDBAR_HOST=skia</c> forces the Win32 layered + Skia host, <c>avalonia</c>
/// forces the legacy Avalonia window host. By default the Skia host is tried first and the Avalonia
/// host is used as a logged fallback when the layered window cannot be created.
/// </para>
/// </summary>
internal sealed class AppBootstrap
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly TraceAppLogger _logger = new(LogLevel.Information);
    private LocalizationService? _localizer;
    private SettingsController? _settings;
    private DesignIslandDataSource? _data;
    private IslandHost? _host;
    private SkiaIslandHost? _skiaHost;
    private SkiaShellIntegration? _shell;
    private SkiaPlatformActions? _platformActions;
    private JsonUiSettingsStore? _settingsStore;
    private PluginRuntime? _pluginRuntime;
    private NotificationService? _pluginNotifications;
    private readonly PluginIslandEventAdapter _pluginEventAdapter = new();

    public AppBootstrap(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    public async Task StartAsync()
    {
        try
        {
            _settingsStore = new JsonUiSettingsStore(JsonUiSettingsStore.DefaultPath, _logger);
            var initial = await _settingsStore.LoadAsync().ConfigureAwait(true);
            _settings = new SettingsController(initial, _settingsStore, _logger);

            var pseudo = string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_PSEUDO"), "1", StringComparison.Ordinal);
            _localizer = LocalizationService.CreateDefault(
                initial.Language,
                showMissingKeys: false,
                enablePseudoLocale: pseudo);
            LocalizationRuntime.Current = _localizer;

            _data = new DesignIslandDataSource(_localizer);
            _lifetime.Exit += (_, _) => DisposeServices();
            await StartIslandHostAsync().ConfigureAwait(true);
            await StartShellIntegrationAsync().ConfigureAwait(true);
            await StartPluginRuntimeAsync().ConfigureAwait(true);

            if (string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_SMOKE"), "1", StringComparison.Ordinal))
            {
                _ = SmokeAsync();
            }

            _logger.Information(LogCategories.Lifecycle, "app.started", "FluidBar shell started.");
        }
        catch (Exception ex)
        {
            _logger.Critical(LogCategories.Lifecycle, "app.start-failed", "FluidBar failed to start.", ex);
            Shutdown();
        }
    }

    /// <summary>
    /// Composes the narrow page-control seam (volume/brightness/media/clipboard). Starting is
    /// best-effort and off the UI thread: a failed service leaves its controls reporting
    /// <c>control-unsupported</c> instead of pretending to work.
    /// </summary>
    private SkiaPlatformActions? TryCreatePlatformActions()
    {
        if (_settings is null)
        {
            return null;
        }

        try
        {
            var actions = new SkiaPlatformActions(_settings, _logger);
            actions.StartInBackground();
            return actions;
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "app.island-actions-failed",
                "The page-control platform seam could not be composed; controls will report unsupported.", ex);
            return null;
        }
    }

    /// <summary>
    /// Starts the island host. The Skia layered host is the default; the legacy Avalonia host is the
    /// fallback (and can be forced with <c>FLUIDBAR_HOST=avalonia</c>).
    /// </summary>
    private async Task StartIslandHostAsync()
    {
        var selection = Environment.GetEnvironmentVariable("FLUIDBAR_HOST");
        var forceAvalonia = string.Equals(selection, "avalonia", StringComparison.OrdinalIgnoreCase);
        var forceSkia = string.Equals(selection, "skia", StringComparison.OrdinalIgnoreCase);

        if (!forceAvalonia)
        {
            _platformActions = TryCreatePlatformActions();
            try
            {
                var host = new SkiaIslandHost(new SkiaIslandHostOptions
                {
                    Localizer = _localizer!,
                    Settings = _settings!,
                    Data = (IIslandDataSource?)_platformActions ?? _data!,
                    Logger = _logger,
                    PlatformActions = _platformActions,
                    TargetFrameRate = 120,
                });
                host.SettingsRequested += OpenSettings;
                host.MarketRequested += OpenMarket;
                await host.StartAsync().ConfigureAwait(true);
                _skiaHost = host;
                _logger.Information(LogCategories.Lifecycle, "app.host-selected", "Island host: skia (layered Win32).");
                return;
            }
            catch (Exception ex) when (!forceSkia)
            {
                _logger.Warning(LogCategories.Lifecycle, "app.skia-host-failed",
                    "The Skia layered host could not be created; falling back to the Avalonia island host.", ex);
                _skiaHost?.Dispose();
                _skiaHost = null;
                _platformActions?.DisposeInBackground();
                _platformActions = null;
            }
        }

        _host = new IslandHost(new IslandHostOptions
        {
            Localizer = _localizer!,
            Settings = _settings!,
            Data = _data!,
            Logger = _logger,
        });
        _host.SettingsRequested += OpenSettings;
        _host.MarketRequested += OpenMarket;
        await _host.StartAsync().ConfigureAwait(true);
        _logger.Information(LogCategories.Lifecycle, "app.host-selected", "Island host: avalonia (fallback).");
    }

    private async Task SmokeAsync()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            _settings?.Update(s => s with { AutoCollapseMs = 0, Language = "en-US" });
            await Task.Delay(1500).ConfigureAwait(true);
            await CaptureAsync("fluidbar-01-idle.png", log).ConfigureAwait(true);
            SimulatePrimaryClick();
            await Task.Delay(1800).ConfigureAwait(true);
            await CaptureAsync("fluidbar-02-expanded-home.png", log).ConfigureAwait(true);

            var pages = new[] { FluidBar.Core.Activities.IslandPage.Media, FluidBar.Core.Activities.IslandPage.Notifications,
                FluidBar.Core.Activities.IslandPage.Clipboard, FluidBar.Core.Activities.IslandPage.Agent,
                FluidBar.Core.Activities.IslandPage.Widgets };
            var index = 3;
            foreach (var page in pages)
            {
                SimulatePageRequested(page);
                await Task.Delay(700).ConfigureAwait(true);
                await CaptureAsync($"fluidbar-{index:00}-{page.ToString().ToLowerInvariant()}.png", log).ConfigureAwait(true);
                index++;
            }

            SimulatePageRequested(FluidBar.Core.Activities.IslandPage.Home);
            _settings?.Update(s => s with { Language = "zh-Hans" });
            await Task.Delay(900).ConfigureAwait(true);
            await CaptureAsync("fluidbar-08-zh-home.png", log).ConfigureAwait(true);
            _settings?.Update(s => s with { Language = "en-US" });
            await Task.Delay(500).ConfigureAwait(true);
            SimulateEscape();
            await Task.Delay(1200).ConfigureAwait(true);
            await CaptureAsync("fluidbar-09-collapsed.png", log).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            log.AppendLine("smoke-failed: " + ex);
        }
        finally
        {
            var path = Path.Combine(Path.GetTempPath(), "fluidbar-smoke.log");
            await File.WriteAllTextAsync(path, log.ToString()).ConfigureAwait(true);
            Shutdown();
        }
    }

    private void SimulatePrimaryClick()
    {
        _skiaHost?.SimulatePrimaryClick();
        _host?.SimulatePrimaryClick();
    }

    private void SimulatePageRequested(FluidBar.Core.Activities.IslandPage page)
    {
        _skiaHost?.SimulatePageRequested(page);
        _host?.SimulatePageRequested(page);
    }

    private void SimulateEscape()
    {
        _skiaHost?.SimulateEscape();
        _host?.SimulateEscape();
    }

    private async Task CaptureAsync(string fileName, System.Text.StringBuilder log)
    {
        log.AppendLine("=== " + fileName + " ===");
        log.AppendLine(_skiaHost?.DescribeWindows() ?? _host?.DescribeWindows() ?? "no host");
        var folder = Path.Combine(Path.GetTempPath(), "fluidbar-evidence");
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, fileName);

        if (_skiaHost is not null)
        {
            _skiaHost.RenderNow(forceFull: true);
            if (!_skiaHost.SavePrimaryFramePng(destination))
            {
                log.AppendLine("capture-failed: no skia surface");
            }

            await Task.CompletedTask;
            return;
        }

        var window = _host?.PrimaryWindow;
        if (window is null)
        {
            return;
        }

        try
        {
            var scale = window.RenderScaling;
            var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(window.ClientSize.Width * scale), (int)Math.Ceiling(window.ClientSize.Height * scale)),
                new Vector(96 * scale, 96 * scale));
            bitmap.Render(window);
            bitmap.Save(destination);
        }
        catch (Exception ex)
        {
            log.AppendLine("capture-failed: " + ex);
        }

        await Task.CompletedTask;
    }

    private async Task StartShellIntegrationAsync()
    {
        if (_skiaHost is null || string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_NO_TRAY"), "1", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _shell = new SkiaShellIntegration(
                toggle: () => Dispatcher.UIThread.Post(() => _skiaHost?.SimulateToggleHotkey()),
                settings: () => Dispatcher.UIThread.Post(OpenSettings),
                market: () => Dispatcher.UIThread.Post(OpenMarket),
                exit: () => Dispatcher.UIThread.Post(Shutdown),
                bindings: new HotkeySettings
                {
                    Toggle = _settings!.Current.HotkeyToggle,
                    Hide = _settings.Current.HotkeyHide,
                    Settings = _settings.Current.HotkeySettings,
                });
            await _shell.StartAsync().ConfigureAwait(true);
            _settings!.Changed += OnSettingsChanged;
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Lifecycle, "app.shell-integration-failed",
                "Tray/hotkey integration could not be started; the island still runs.", ex);
            _shell = null;
        }
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (_shell is null)
        {
            return;
        }

        if (string.Equals(previous.HotkeyToggle, current.HotkeyToggle, StringComparison.Ordinal)
            && string.Equals(previous.HotkeyHide, current.HotkeyHide, StringComparison.Ordinal)
            && string.Equals(previous.HotkeySettings, current.HotkeySettings, StringComparison.Ordinal))
        {
            return;
        }

        var bindings = new HotkeySettings
        {
            Toggle = current.HotkeyToggle,
            Hide = current.HotkeyHide,
            Settings = current.HotkeySettings,
        };
        Dispatcher.UIThread.Post(() => _ = ApplyHotkeysAsync(bindings));
    }

    private async Task ApplyHotkeysAsync(HotkeySettings bindings)
    {
        try
        {
            if (_shell is not null)
            {
                await _shell.ApplyBindingsAsync(bindings).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Lifecycle, "app.hotkeys-apply-failed",
                "The edited hotkey bindings could not be applied; restart FluidBar to retry.", ex);
        }
    }
    /// <summary>
    /// Composes the plugin platform after the island host exists (plugins publish into its event hub).
    /// Composition, discovery and loading run on the thread pool; a plugin failure never reaches the island.
    /// </summary>
    private async Task StartPluginRuntimeAsync()
    {
        if (_settings is null || _localizer is null)
        {
            return;
        }

        try
        {
            var services = CreateBuiltinPlatformServices();
            var options = new PluginRuntimeOptions
            {
                PluginRootDirectory = PluginRuntimePaths.ResolveRoot(),
                AppVersion = new SemanticVersion(2, 0, 0),
                Localizer = _localizer,
                Logger = new AppPluginHostLogger(_logger),
                InstallLogger = new AppPluginInstallLogger(_logger),
                Dispatcher = AvaloniaHostDispatcher.Instance,
                IslandPublisherFactory = manifest => new AppPluginIslandPublisher(
                    manifest.Id,
                    _pluginEventAdapter,
                    PublishPluginEvent),
                NotificationSinkFactory = manifest => new LoggingPluginNotificationSink(_logger, manifest.Id),
                BuiltinServices = services,
                Enablement = new SettingsPluginEnablementStore(_settings),
                MarketplaceIndexUrl = ResolveMarketplaceIndexUrl(),
                Trust = ResolveMarketplaceTrust(),
                HostUiLocales = [_localizer.Culture, "en-US"],
                BuiltinPackageDirectory = AppContext.BaseDirectory,
            };

            _pluginRuntime = await Task.Run(() => PluginRuntime.CreateAsync(options)).ConfigureAwait(true);
            var report = _pluginRuntime.Report;
            _logger.Information(LogCategories.Plugin, "plugin.runtime.started",
                $"Plugin runtime composed: {_pluginRuntime.Builtins.Count} built-in, " +
                $"{report.CommunityResults.Count(result => result.Success)} community active, " +
                $"{report.CommunityResults.Count(result => !result.Success)} community failed.");
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Plugin, "plugin.runtime.failed",
                "The plugin runtime could not be composed; the island keeps running without plugins.", ex);
            _pluginRuntime = null;
        }
    }

    /// <summary>Builds the first-party platform services from the existing island platform seam.</summary>
    private BuiltinPlatformServices CreateBuiltinPlatformServices()
    {
        if (_platformActions is null)
        {
            return new BuiltinPlatformServices();
        }

        var services = new BuiltinPlatformServices
        {
            Media = _platformActions.MediaSource,
            Clipboard = _platformActions.ClipboardSource,
            ArtworkTempStore = _platformActions.ArtworkTempStore,
        };

        try
        {
            _pluginNotifications = new NotificationService(_platformActions.PlatformLogger, _platformActions.Counters);
            services = new BuiltinPlatformServices
            {
                Media = _platformActions.MediaSource,
                Clipboard = _platformActions.ClipboardSource,
                ArtworkTempStore = _platformActions.ArtworkTempStore,
                Notifications = _pluginNotifications,
            };
            _ = StartNotificationListenerAsync(_pluginNotifications);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Plugin, "plugin.notifications.unavailable",
                "The notification listener could not be created; the notifications built-in plugin stays disabled.", ex);
        }

        return services;
    }

    private async Task StartNotificationListenerAsync(NotificationService service)
    {
        try
        {
            await service.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Plugin, "plugin.notifications.start-failed",
                "The notification listener did not start; the notifications built-in plugin receives no events.", ex);
        }
    }

    /// <summary>Routes one adapted plugin event into the active island host's Core event hub.</summary>
    private void PublishPluginEvent(FluidBar.Core.Events.IslandEvent islandEvent)
    {
        if (_skiaHost is not null)
        {
            _skiaHost.Publish(islandEvent);
        }
        else
        {
            _host?.Publish(islandEvent);
        }
    }

    private static Uri? ResolveMarketplaceIndexUrl()
    {
        var configured = Environment.GetEnvironmentVariable("FLUIDBAR_MARKET_URL");
        return !string.IsNullOrWhiteSpace(configured) && Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    /// <summary>
    /// Production trust keys are injected at build/release time; this dev hook reads them from the
    /// environment. Without a key the signed index cannot be verified, so the marketplace falls back to the
    /// clearly-labelled demo catalog instead of trusting an unsigned document.
    /// </summary>
    private MarketplaceTrustOptions ResolveMarketplaceTrust()
    {
        var publicKey = Environment.GetEnvironmentVariable("FLUIDBAR_MARKET_KEY");
        var keyId = Environment.GetEnvironmentVariable("FLUIDBAR_MARKET_KEY_ID");
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(keyId))
        {
            if (ResolveMarketplaceIndexUrl() is not null)
            {
                _logger.Warning(LogCategories.Plugin, "plugin.market.trust-missing",
                    "FLUIDBAR_MARKET_URL is set without FLUIDBAR_MARKET_KEY/FLUIDBAR_MARKET_KEY_ID; the signed index cannot be verified and the demo catalog is used.");
            }

            return MarketplaceTrustOptions.Empty;
        }

        return MarketplaceTrustOptions.ForMarketplaceKeys(new TrustedKey
        {
            KeyId = keyId,
            Role = SignatureRole.Marketplace,
            PublicKeySpkiBase64 = publicKey,
        });
    }

    private void OpenSettings() => ShowSettingsWindow(SettingsWindowSection.General);
    private void OpenMarket() => ShowSettingsWindow(SettingsWindowSection.Marketplace);

    private void ShowSettingsWindow(SettingsWindowSection section)
    {
        if (_settings is null || _localizer is null)
        {
            return;
        }

        // One window: a hotkey press while it is already open switches section and re-focuses it.
        if (_settingsWindow is { } existing)
        {
            existing.ShowSection(section);
            BringToFront(existing);
            return;
        }

        var window = new SettingsWindow(
            _settings,
            _localizer,
            section,
            _pluginRuntime?.Marketplace,
            _pluginRuntime?.Plugins);
        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;
        window.Show();
        BringToFront(window);
    }

    /// <summary>
    /// Raises the window above the foreground app. The island runs without a foreground window, so
    /// Windows refuses a plain Activate/SetForegroundWindow; a brief Topmost toggle is the reliable
    /// way to bring a background process's window to the front.
    /// </summary>
    private static void BringToFront(Window window)
    {
        window.Activate();
        window.Topmost = true;
        Dispatcher.UIThread.Post(() => window.Topmost = false, DispatcherPriority.Background);
    }

    private SettingsWindow? _settingsWindow;
    private bool _shutdownStarted;

    private void DisposeServices()
    {
        _pluginRuntime?.DisposeInBackground();
        _pluginRuntime = null;
        if (_pluginNotifications is not null)
        {
            _ = DisposeQuietlyAsync(_pluginNotifications);
            _pluginNotifications = null;
        }

        _skiaHost?.Dispose();
        _skiaHost = null;
        _settings?.Changed -= OnSettingsChanged;
        if (_shell is not null)
        {
            try
            {
                _shell.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategories.Lifecycle, "app.shell-dispose-failed",
                    "The tray/hotkey integration did not dispose cleanly.", ex);
            }

            _shell = null;
        }
        _host?.Dispose();
        _host = null;
        _platformActions?.DisposeInBackground();
        _platformActions = null;
        _data?.Dispose();
        _settings?.Dispose();
        _settingsStore?.Dispose();
        _localizer?.Dispose();
    }

    private static async Task DisposeQuietlyAsync(IAsyncDisposable disposable)
    {
        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // App exit path: the process is going away; a failed service stop must not crash shutdown.
        }
    }

    private void Shutdown()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        DisposeServices();
        _lifetime.Shutdown();
    }
}





