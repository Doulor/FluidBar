using System.Diagnostics;
using FluidBar.App.Localization;
using FluidBar.Core.Settings;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Shell;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Shell integration for the layered/Skia island path: one message host, a tray icon and the
/// configured global hotkeys. It carries no Avalonia types and only forwards intents; the island
/// host owns the state machine. Disposal is idempotent.
/// </summary>
public sealed class SkiaShellIntegration : IAsyncDisposable
{
    private readonly PlatformMessageHost _messageHost;
    private readonly TrayIconService _tray;
    private readonly HotkeyService _hotkeys;
    private readonly Action _toggle;
    private readonly Action _hide;
    private readonly Action _settings;
    private readonly Action _market;
    private readonly Action _exit;
    private readonly ILocalizer? _localizer;
    private HotkeySettings _bindings;
    private bool _trayStarted;
    private int _disposed;

    /// <summary>Creates the shell integration; call <see cref="StartAsync"/> once.</summary>
    public SkiaShellIntegration(
        Action toggle,
        Action hide,
        Action settings,
        Action market,
        Action exit,
        HotkeySettings? bindings = null,
        ILocalizer? localizer = null,
        TimeProvider? timeProvider = null)
    {
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
        _hide = hide ?? throw new ArgumentNullException(nameof(hide));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _market = market ?? throw new ArgumentNullException(nameof(market));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));
        _localizer = localizer;
        _bindings = bindings ?? new HotkeySettings();

        var logger = new PlatformLoggerAdapter();
        var counters = new PlatformCounters();
        _messageHost = new PlatformMessageHost(logger, counters);
        _tray = new TrayIconService(_messageHost, logger, counters);
        _hotkeys = new HotkeyService(_messageHost, logger, counters);
    }

    /// <summary>Whether the tray icon is currently registered.</summary>
    public bool TrayVisible => _trayStarted;

    /// <summary>Starts the message host, tray icon (when enabled) and hotkeys.</summary>
    /// <param name="trayVisible">Initial tray-icon state from the general settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartAsync(bool trayVisible = true, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _messageHost.StartAsync(cancellationToken).ConfigureAwait(false);

        _tray.MenuItemInvoked += OnTrayMenuItem;
        _tray.LeftClicked += OnTrayLeftClicked;
        _hotkeys.Pressed += OnHotkeyPressed;

        if (trayVisible)
        {
            await StartTrayAsync(cancellationToken).ConfigureAwait(false);
        }

        await _hotkeys.StartAsync(cancellationToken).ConfigureAwait(false);
        await RegisterAsync("toggle", _bindings.Toggle, cancellationToken).ConfigureAwait(false);
        await RegisterAsync("hide", _bindings.Hide, cancellationToken).ConfigureAwait(false);
        await RegisterAsync("settings", _bindings.Settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies the general "tray icon" setting live (idempotent).</summary>
    /// <param name="visible">Whether the tray icon should be registered.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SetTrayVisibleAsync(bool visible, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (visible == _trayStarted)
        {
            return;
        }

        if (visible)
        {
            await StartTrayAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _tray.StopAsync(cancellationToken).ConfigureAwait(false);
        _trayStarted = false;
    }

    /// <summary>Shows a tray balloon when the icon is visible (update prompts); no-op otherwise.</summary>
    /// <param name="title">Balloon title.</param>
    /// <param name="message">Balloon message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ShowBalloonAsync(string title, string message, CancellationToken cancellationToken = default)
        => _trayStarted
            ? _tray.ShowBalloonAsync(title, message, cancellationToken)
            : Task.CompletedTask;

    private async Task StartTrayAsync(CancellationToken cancellationToken)
    {
        var tooltip = _localizer?.Format(LocKeys.Tray.Tooltip, ("status", "FluidBar 2.0")) ?? "FluidBar 2.0";
        await _tray.StartAsync(new TrayIconOptions
        {
            ToolTip = tooltip,
            IconFilePath = TryExtractTrayIcon(),
        }, cancellationToken).ConfigureAwait(false);
        _tray.SetMenuItems(
        [
            new TrayMenuItem("toggle", L(LocKeys.Tray.Open, "Open FluidBar")),
            new TrayMenuItem("hide", L(LocKeys.Page.Home.HideIsland, "Hide island")),
            new TrayMenuItem("settings", L(LocKeys.Tray.Settings, "Settings…")),
            new TrayMenuItem("market", L(LocKeys.Tray.Market, "Plugin market…")),
            TrayMenuItem.Separator,
            new TrayMenuItem("exit", L(LocKeys.Tray.Exit, "Exit FluidBar")),
        ]);
        await _tray.SetToolTipAsync(tooltip, cancellationToken).ConfigureAwait(false);
        _trayStarted = true;
    }

    /// <summary>Re-registers all global hotkeys after the user edits them in settings.</summary>
    public async Task ApplyBindingsAsync(HotkeySettings bindings, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _bindings = bindings;
        await _hotkeys.UnregisterAllAsync(cancellationToken).ConfigureAwait(false);
        await RegisterAsync("toggle", _bindings.Toggle, cancellationToken).ConfigureAwait(false);
        await RegisterAsync("hide", _bindings.Hide, cancellationToken).ConfigureAwait(false);
        await RegisterAsync("settings", _bindings.Settings, cancellationToken).ConfigureAwait(false);
    }
    private string L(string key, string fallback) => _localizer?.Get(key) ?? fallback;

    private async Task RegisterAsync(string name, string gesture, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return;
        }

        // Global hotkeys are a shared namespace: another app may already own the default.
        // Fall back to a Ctrl+Alt+Shift variant so the shell always keeps a working binding.
        var candidates = name switch
        {
            "hide" => new[] { gesture, "Ctrl+Alt+Shift+H" },
            "settings" => new[] { gesture, "Ctrl+Alt+Shift+S" },
            "toggle" => new[] { gesture, "Ctrl+Alt+Shift+D" },
            _ => new[] { gesture },
        };

        foreach (var candidate in candidates)
        {
            var result = await _hotkeys.RegisterAsync(name, candidate, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                return;
            }

            Trace.WriteLine($"FluidBar hotkey '{name}' ({candidate}) not registered: {result.Failure}.");
        }
    }

    /// <summary>Extracts the exe's associated icon to a temp .ico so the tray uses the FluidBar mark.</summary>
    private static string? TryExtractTrayIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                return null;
            }

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (icon is null)
            {
                return null;
            }

            var path = Path.Combine(Path.GetTempPath(), "fluidbar-tray.ico");
            using var stream = File.Create(path);
            icon.Save(stream);
            return path;
        }
        catch (Exception ex)
        {
            Trace.WriteLine("tray icon extraction failed: " + ex.Message);
            return null;
        }
    }
    private void OnTrayMenuItem(object? sender, TrayMenuItemInvokedEventArgs e)
    {
        switch (e.ItemId)
        {
            case "toggle":
                _toggle();
                break;
            case "settings":
                _settings();
                break;
            case "market":
                _market();
                break;
            case "exit":
                _exit();
                break;
        }
    }

    private void OnTrayLeftClicked(object? sender, EventArgs e) => _toggle();

    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        switch (e.RegistrationName)
        {
            case "toggle":
                _toggle();
                break;
            case "hide":
                _hide();
                break;
            case "settings":
                _settings();
                break;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _tray.MenuItemInvoked -= OnTrayMenuItem;
        _tray.LeftClicked -= OnTrayLeftClicked;
        _hotkeys.Pressed -= OnHotkeyPressed;

        try { await _hotkeys.StopAsync().ConfigureAwait(false); } catch (Exception ex) { Trace.WriteLine("hotkeys stop failed: " + ex); }
        try
        {
            if (_trayStarted)
            {
                await _tray.StopAsync().ConfigureAwait(false);
                _trayStarted = false;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine("tray stop failed: " + ex);
        }
        try { await _hotkeys.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { Trace.WriteLine("hotkeys dispose failed: " + ex); }
        try { await _tray.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { Trace.WriteLine("tray dispose failed: " + ex); }
        try { await _messageHost.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { Trace.WriteLine("message host dispose failed: " + ex); }
    }

    private sealed class PlatformLoggerAdapter : IPlatformLogger
    {
        public bool IsEnabled(PlatformLogLevel level) => level >= PlatformLogLevel.Warning;

        public void Log(in PlatformLogEntry entry)
            => Trace.WriteLine($"[{entry.Level}] {entry.Category}/{entry.EventId}: {entry.Message}");
    }
}










