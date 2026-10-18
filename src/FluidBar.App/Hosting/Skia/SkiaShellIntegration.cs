using System.Diagnostics;
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
    private readonly Action _settings;
    private readonly Action _market;
    private readonly Action _exit;
    private readonly HotkeySettings _bindings;
    private int _disposed;

    /// <summary>Creates the shell integration; call <see cref="StartAsync"/> once.</summary>
    public SkiaShellIntegration(
        Action toggle,
        Action settings,
        Action market,
        Action exit,
        HotkeySettings? bindings = null,
        TimeProvider? timeProvider = null)
    {
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _market = market ?? throw new ArgumentNullException(nameof(market));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));
        _bindings = bindings ?? new HotkeySettings();

        var logger = new PlatformLoggerAdapter();
        var counters = new PlatformCounters();
        _messageHost = new PlatformMessageHost(logger, counters);
        _tray = new TrayIconService(_messageHost, logger, counters);
        _hotkeys = new HotkeyService(_messageHost, logger, counters);
    }

    /// <summary>Starts the message host, tray icon and hotkeys.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _messageHost.StartAsync(cancellationToken).ConfigureAwait(false);

        _tray.MenuItemInvoked += OnTrayMenuItem;
        _tray.LeftClicked += OnTrayLeftClicked;
        _hotkeys.Pressed += OnHotkeyPressed;

        await _tray.StartAsync(new TrayIconOptions { ToolTip = "FluidBar 2.0", IconFilePath = null }, cancellationToken).ConfigureAwait(false);
        _tray.SetMenuItems(
        [
            new TrayMenuItem("toggle", "显示 / 隐藏灵动岛"),
            new TrayMenuItem("settings", "设置"),
            new TrayMenuItem("market", "插件市场"),
            TrayMenuItem.Separator,
            new TrayMenuItem("exit", "退出 FluidBar"),
        ]);
        await _tray.SetToolTipAsync("FluidBar 2.0", cancellationToken).ConfigureAwait(false);

        await _hotkeys.StartAsync(cancellationToken).ConfigureAwait(false);
        await RegisterAsync("toggle", _bindings.Toggle, cancellationToken).ConfigureAwait(false);
        await RegisterAsync("hide", _bindings.Hide, cancellationToken).ConfigureAwait(false);
        await RegisterAsync("settings", _bindings.Settings, cancellationToken).ConfigureAwait(false);
    }

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
                _toggle();
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
        try { await _tray.StopAsync().ConfigureAwait(false); } catch (Exception ex) { Trace.WriteLine("tray stop failed: " + ex); }
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







