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
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Settings;
using FluidBar.Marketplace.Signing;
using FluidBar.Plugins.Builtin;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Platform.Windows.Shell;
using FluidBar.Sdk.Versioning;
using UpdateState = FluidBar.Platform.Windows.Update.UpdateState;

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
    private readonly SettingsAwareLogger _logger;
    private LocalizationService? _localizer;
    private SettingsController? _settings;
    private DesignIslandDataSource? _data;
    private IslandHost? _host;
    private SkiaIslandHost? _skiaHost;
    private SkiaShellIntegration? _shell;
    private SkiaPlatformActions? _platformActions;
    private JsonUiSettingsStore? _settingsStore;
    private PluginRuntime? _pluginRuntime;
    private readonly PluginIslandEventAdapter _pluginEventAdapter = new();
    private StartupRegistrationSync? _startupRegistration;
    private AppUpdateCoordinator? _updates;
    private CrashGuard? _crashGuard;
    private UpdateState _lastPromptedUpdateState = UpdateState.Idle;

    public AppBootstrap(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        // Level follows the advanced setting (spec §8.2 diagnostics.logLevel) without a restart.
        _logger = new SettingsAwareLogger(() => _settings?.Current.Advanced.LogLevel);
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

            // "Follow the system" values (theme / reduced motion / accent) are probed once off the UI
            // thread; the cached snapshot is what the theme mapper reads on the UI thread.
            await RefreshSystemAppearanceAsync().ConfigureAwait(true);
            StartLogFileSink();
            EvaluateCrashGuard();
            await StartIslandHostAsync().ConfigureAwait(true);
            await StartStartupRegistrationAsync().ConfigureAwait(true);
            await StartShellIntegrationAsync().ConfigureAwait(true);
            await StartUpdateCoordinatorAsync().ConfigureAwait(true);
            await StartPluginRuntimeAsync().ConfigureAwait(true);

            if (string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_SMOKE"), "1", StringComparison.Ordinal))
            {
                _ = SmokeAsync();
            }

            if (string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_AUDIT"), "1", StringComparison.Ordinal))
            {
                _ = AuditAsync();
            }

            _logger.Information(LogCategories.Lifecycle, "app.avalonia-rendering",
                "Avalonia rendering backend resolved.",
                data: new Dictionary<string, object?>
                {
                    ["backend"] = AvaloniaRenderingPolicy.ShouldUseSoftwareRendering() ? "software" : "platform-default",
                    ["host"] = Environment.GetEnvironmentVariable("FLUIDBAR_HOST") ?? "(default skia-first)",
                    ["override"] = Environment.GetEnvironmentVariable(AvaloniaRenderingPolicy.OverrideVariable),
                });
            _logger.Information(LogCategories.Lifecycle, "app.started", "FluidBar shell started.");
        }
        catch (Exception ex)
        {
            _logger.Critical(LogCategories.Lifecycle, "app.start-failed", "FluidBar failed to start.", ex);
            Shutdown();
        }
    }

    /// <summary>Refreshes the cached Windows appearance preferences (theme / reduced motion / accent).</summary>
    private async Task RefreshSystemAppearanceAsync()
    {
        try
        {
            await Task.Run(static () => SystemAppearanceProbe.Refresh()).ConfigureAwait(true);
            _logger.Information(LogCategories.Settings, "settings.system-appearance",
                "Windows appearance preferences resolved.",
                data: new Dictionary<string, object?>
                {
                    ["lightTheme"] = SystemAppearanceProbe.IsLightTheme,
                    ["reducedMotion"] = SystemAppearanceProbe.PrefersReducedMotion,
                    ["accent"] = SystemAppearanceProbe.SystemAccentHex,
                });
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Settings, "settings.system-appearance-failed",
                "Reading the Windows appearance preferences failed; neutral defaults are used.", ex);
        }
    }

    /// <summary>Starts the JSON-lines file log sink in the background (spec §8.1/§9.3).</summary>
    private void StartLogFileSink()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluidBar", "logs");
        _ = Task.Run(async () =>
        {
            try
            {
                await _logger.StartFileSinkAsync(directory).ConfigureAwait(false);
                _logger.Information(LogCategories.Lifecycle, "app.log-sink-started",
                    "JSON-lines log sink started.",
                    data: new Dictionary<string, object?> { ["path"] = _logger.LogFilePath });
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategories.Lifecycle, "app.log-sink-failed",
                    "The JSON-lines log sink could not be started.", ex);
            }
        });
    }

    /// <summary>
    /// Applies the advanced "crash guard" setting: hooks unhandled exceptions, records crash markers
    /// and forces safe mode after two consecutive crashes (spec §8.4).
    /// </summary>
    private void EvaluateCrashGuard()
    {
        if (_settings is null)
        {
            return;
        }

        if (!_settings.Current.Advanced.CrashGuard)
        {
            _logger.Information(LogCategories.Lifecycle, "crash-guard.disabled",
                "Crash guard is disabled in the advanced settings.");
            return;
        }

        _crashGuard = new CrashGuard(logger: _logger);
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (!_crashGuard.TryEnterSafeMode())
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            Advanced = settings.Advanced with { SafeMode = true },
        });
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new InvalidOperationException("Unknown unhandled exception.");
        _crashGuard?.RecordUnhandledException(exception, "appdomain");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _crashGuard?.RecordUnhandledException(e.Exception, "task");
        e.SetObserved();
    }

    private void UninstallCrashHooks()
    {
        if (_crashGuard is null)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _crashGuard.MarkCleanExit();
        _crashGuard = null;
    }

    /// <summary>Reconciles the HKCU Run key with the "start with Windows" setting (self-heal on start).</summary>
    private async Task StartStartupRegistrationAsync()
    {
        if (_settings is null)
        {
            return;
        }

        try
        {
            _startupRegistration = new StartupRegistrationSync(_settings, _logger);
            _startupRegistration.Attach();
            await _startupRegistration.SyncAsync(selfHeal: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Settings, "settings.startup.init-failed",
                "The start-with-Windows registration could not be synchronised.", ex);
        }
    }

    /// <summary>Composes the update coordinator and schedules the startup + interval checks.</summary>
    private async Task StartUpdateCoordinatorAsync()
    {
        if (_settings is null)
        {
            return;
        }

        try
        {
            _updates = new AppUpdateCoordinator(_settings, _logger);
            _updates.Changed += OnUpdateChanged;
            await _updates.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Settings, "update.coordinator-failed",
                "The update coordinator could not be composed; update checks are unavailable.", ex);
            _updates = null;
        }
    }

    /// <summary>Prompts about an update state change through a tray balloon (when the tray is visible).</summary>
    private void OnUpdateChanged(object? sender, EventArgs e)
    {
        var snapshot = _updates?.Current;
        if (snapshot is null || snapshot.State == _lastPromptedUpdateState)
        {
            return;
        }

        _lastPromptedUpdateState = snapshot.State;
        if (snapshot.State is not (UpdateState.Available or UpdateState.ReadyToInstall or UpdateState.RestartRequired)
            || _localizer is null)
        {
            return;
        }

        var version = snapshot.AvailableRelease?.Version.ToString() ?? _updates!.CurrentVersion.ToString();
        var message = snapshot.State switch
        {
            UpdateState.Available => _localizer.Format(LocKeys.Settings.About.Update.Available,
                ("version", version), ("size", FormatBytes(snapshot.AvailableRelease?.Installer?.SizeBytes ?? 0))),
            UpdateState.ReadyToInstall => _localizer.Format(LocKeys.Settings.About.Update.Ready,
                ("version", version)),
            _ => _localizer.Get(LocKeys.Settings.About.Update.RestartRequired),
        };

        Dispatcher.UIThread.Post(() => _ = PromptUpdateAsync(message));
    }

    private async Task PromptUpdateAsync(string message)
    {
        if (_shell is null)
        {
            return;
        }

        try
        {
            await _shell.ShowBalloonAsync("FluidBar", message).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Settings, "update.prompt-failed",
                "The update prompt could not be shown.", ex);
        }
    }

    private static string FormatBytes(long bytes)
        => bytes >= 1024L * 1024L
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.0} MB")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / 1024.0:0} KB");

    /// <summary>Runs the explicit "restart and install" action and exits so the installer can replace files.</summary>
    private void RestartToInstall()
    {
        if (_updates is null)
        {
            return;
        }

        var result = _updates.Install();
        if (result.Launched)
        {
            _logger.Information(LogCategories.Lifecycle, "update.restart",
                "Verified installer launched; shutting down for the restart.",
                data: new Dictionary<string, object?> { ["installer"] = result.InstallerPath });
            Shutdown();
            return;
        }

        _logger.Warning(LogCategories.Lifecycle, "update.restart-refused",
            "The installer could not be launched.", null,
            new Dictionary<string, object?>
            {
                ["reason"] = result.FailureReason,
                ["installer"] = result.InstallerPath,
            });
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
            var actions = new SkiaPlatformActions(_settings, _logger, timeProvider: null,
                quickToggles: null, localizer: _localizer, weatherCity: _settings.Current.WeatherCity);
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

    /// <summary>
    /// Deterministic visual-audit run (developer evidence): renders every compact state, all six expanded
    /// pages and the revealed preview rail from the production Skia renderer and writes PNGs. Demo
    /// activities are published through the host's public publisher; no real input, no user app touched.
    /// </summary>
    private async Task AuditAsync()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            _settings?.Update(settings => settings with
            {
                AutoCollapseMs = 0,
                Language = "zh-Hans",
                Plugins = settings.Plugins with { Enabled = [] },
            });
            await Task.Delay(1400).ConfigureAwait(true);
            var directory = Environment.GetEnvironmentVariable("FLUIDBAR_AUDIT_DIR")
                ?? Path.Combine(Path.GetTempPath(), "fluidbar-evidence", "audit");
            Directory.CreateDirectory(directory);

            async Task ShotAsync(string name)
            {
                await Task.Delay(450).ConfigureAwait(true);
                _skiaHost?.RenderNow(forceFull: true);
                log.AppendLine("=== " + name + " ===");
                log.AppendLine(_skiaHost?.DescribeWindows() ?? "no skia host");
                if (_skiaHost?.SavePrimaryFramePng(Path.Combine(directory, name + ".png")) != true)
                {
                    log.AppendLine("capture-failed");
                }
            }

            var expandedOnly = string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_AUDIT_MODE"),
                "expanded", StringComparison.OrdinalIgnoreCase);
            if (expandedOnly)
            {
                // Expanded-page evidence runs against the normal plugin/data set so every page shows
                // real content (media, notifications, agent rows, widgets).
                _settings?.Update(settings => settings with
                {
                    AutoCollapseMs = 0,
                    Language = "zh-Hans",
                    Plugins = settings.Plugins with
                    {
                        Enabled = ["fluidbar-media", "fluidbar-clipboard", "fluidbar-notifications", "fluidbar-agent"],
                    },
                });
                await Task.Delay(1500).ConfigureAwait(true);
            }
            else
            {
                await ShotAsync("compact-idle");
            }

            var demos = new (string Name, ActivityKind Kind, ActivityPayload Payload)[]
            {
                ("compact-media", ActivityKind.Media, new ActivityPayload
                {
                    Title = "Midnight Drive", Subtitle = "Lumen Fields", Body = "Nocturne", Badge = "Lumen Fields",
                    SourceName = "Spotify", IsActive = true, ShowsAudioWave = true, ProgressPercent = 42,
                    PositionTicks = TimeSpan.FromSeconds(75).Ticks, EndTicks = TimeSpan.FromSeconds(214).Ticks,
                }),
                ("compact-notifications", ActivityKind.Notification, new ActivityPayload
                {
                    Title = "GitHub", Subtitle = "Doulor/bybit-ai-trading", Body = "Run failed: Export Live Data",
                    Badge = "2", SourceName = "GitHub",
                }),
                ("compact-clipboard", ActivityKind.Clipboard, new ActivityPayload
                {
                    Title = "已复制", Body = "https://example.com/fluidbar", SourceName = "Edge",
                }),
                ("compact-agent", ActivityKind.Agent, new ActivityPayload
                {
                    Title = "运行命令", Subtitle = "deepseek", Body = "工具执行中", Badge = "running",
                    SourceName = "deepseek", IsActive = true,
                }),
                ("compact-hud", ActivityKind.Volume, new ActivityPayload
                {
                    Title = "音量", Level = 62, IsActive = true,
                }),
            };

            var sequence = 1L;
            if (!expandedOnly)
            {
                foreach (var demo in demos)
                {
                    _skiaHost?.PublishActivity(demo.Kind, demo.Payload, sequence++, "audit");
                    await ShotAsync(demo.Name);
                    _skiaHost?.RetractActivity(demo.Kind, sequence++, "audit");
                    await Task.Delay(250).ConfigureAwait(true);
                }
            }

            SimulatePrimaryClick();
            await ShotAsync("expanded-home");
            var pages = new[] { IslandPage.Media, IslandPage.Notifications, IslandPage.Clipboard, IslandPage.Agent, IslandPage.Widgets };
            foreach (var page in pages)
            {
                SimulatePageRequested(page);
                await ShotAsync("expanded-" + page.ToString().ToLowerInvariant());
            }

            SimulatePageRequested(IslandPage.Home);
            await Task.Delay(300).ConfigureAwait(true);
            _skiaHost?.SimulateWheel(-140);
            await Task.Delay(200).ConfigureAwait(true);
            _skiaHost?.SimulateWheel(-140);
            await ShotAsync("rail-expanded");
        }
        catch (Exception ex)
        {
            log.AppendLine("audit-failed: " + ex);
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "fluidbar-audit.log"), log.ToString())
                .ConfigureAwait(true);
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
            await _shell.StartAsync(_settings!.Current.TrayIcon).ConfigureAwait(true);
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

        if (previous.TrayIcon != current.TrayIcon)
        {
            var visible = current.TrayIcon;
            Dispatcher.UIThread.Post(() => _ = ApplyTrayVisibilityAsync(visible));
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

    private async Task ApplyTrayVisibilityAsync(bool visible)
    {
        try
        {
            if (_shell is not null)
            {
                await _shell.SetTrayVisibleAsync(visible).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Lifecycle, "app.tray-toggle-failed",
                "The tray icon visibility could not be changed; restart FluidBar to retry.", ex);
        }
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
                // Safe mode disables every community plugin (spec §8.4); first-party ones still load.
                LoadCommunity = !_settings.Current.Advanced.SafeMode,
                Enablement = new SettingsPluginEnablementStore(_settings),
                MarketplaceIndexUrl = ResolveMarketplaceIndexUrl(),
                Trust = ResolveMarketplaceTrust(),
                HostUiLocales = [_localizer.Culture, "en-US"],
                BuiltinPackageDirectory = AppContext.BaseDirectory,
            };

            _pluginRuntime = await Task.Run(() => PluginRuntime.CreateAsync(options)).ConfigureAwait(true);
            var report = _pluginRuntime.Report;
            if (_settings.Current.Advanced.SafeMode)
            {
                _logger.Warning(LogCategories.Plugin, "plugin.runtime.safe-mode",
                    "Safe mode is on: community plugins are not loaded.");
            }

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

    /// <summary>
    /// Builds the first-party platform services from the existing island platform seam.
    /// </summary>
    /// <remarks>
    /// The notification listener is NOT created here: <see cref="SkiaPlatformActions"/> owns the single
    /// instance (the notification page seeds it) and this method hands the notifications plugin an
    /// arrivals-only view of that same instance. One listener, one read loop, one disposal — the plugin
    /// still never replays the existing toast backlog as a compact activity.
    /// </remarks>
    private BuiltinPlatformServices CreateBuiltinPlatformServices()
    {
        if (_platformActions is null)
        {
            return new BuiltinPlatformServices();
        }

        INotificationSource? notifications = null;
        try
        {
            notifications = _platformActions.PluginNotificationSource;
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Plugin, "plugin.notifications.unavailable",
                "The shared notification listener could not be exposed to plugins; the notifications built-in plugin stays disabled.", ex);
        }

        return new BuiltinPlatformServices
        {
            Media = _platformActions.MediaSource,
            Clipboard = _platformActions.ClipboardSource,
            ArtworkTempStore = _platformActions.ArtworkTempStore,
            AgentInbox = _platformActions.AgentInboxSource,
            AgentHookInstaller = _platformActions.AgentHookInstaller,
            Notifications = notifications,
        };
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
            _pluginRuntime?.Plugins,
            openPath: null,
            updates: _updates,
            frameStats: _skiaHost,
            restartToInstall: RestartToInstall);
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
        // The single notification listener is owned (and disposed exactly once) by _platformActions.

        _skiaHost?.Dispose();
        _skiaHost = null;

        // Update coordinator: stop the schedule and dispose the shared HttpClient.
        if (_updates is not null)
        {
            _updates.Changed -= OnUpdateChanged;
            try
            {
                _updates.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategories.Settings, "update.dispose-failed",
                    "The update coordinator did not dispose cleanly.", ex);
            }

            _updates = null;
        }

        _startupRegistration?.Dispose();
        _startupRegistration = null;
        UninstallCrashHooks();

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

        // Force the debounced settings write before the process exits (spec §8.1).
        if (_settingsStore is not null)
        {
            try
            {
                _settingsStore.FlushAsync().Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategories.Settings, "settings.flush-failed",
                    "Pending settings could not be flushed on exit.", ex);
            }
        }

        _settings?.Dispose();
        _settingsStore?.Dispose();
        _localizer?.Dispose();

        try
        {
            _logger.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Exit path: a log sink that cannot stop must not block shutdown.
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





