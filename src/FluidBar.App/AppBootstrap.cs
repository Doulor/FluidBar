using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using FluidBar.App.Diagnostics;
using FluidBar.App.Hosting;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Settings;

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
    private JsonUiSettingsStore? _settingsStore;

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
            try
            {
                var host = new SkiaIslandHost(new SkiaIslandHostOptions
                {
                    Localizer = _localizer!,
                    Settings = _settings!,
                    Data = _data!,
                    Logger = _logger,
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
                bindings: new HotkeySettings());
            await _shell.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Lifecycle, "app.shell-integration-failed",
                "Tray/hotkey integration could not be started; the island still runs.", ex);
            _shell = null;
        }
    }
    private void OpenSettings() => ShowSettingsWindow(SettingsWindowSection.General);

    private void OpenMarket() => ShowSettingsWindow(SettingsWindowSection.Marketplace);

    private void ShowSettingsWindow(SettingsWindowSection section)
    {
        if (_settings is null || _localizer is null)
        {
            return;
        }

        var window = new SettingsWindow(_settings, _localizer, section);
        window.Show();
    }

    private bool _shutdownStarted;

    private void DisposeServices()
    {
        _skiaHost?.Dispose();
        _skiaHost = null;
        _host?.Dispose();
        _host = null;
        _data?.Dispose();
        _settings?.Dispose();
        _settingsStore?.Dispose();
        _localizer?.Dispose();
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

