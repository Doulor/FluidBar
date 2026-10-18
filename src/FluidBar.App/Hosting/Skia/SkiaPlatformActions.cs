using FluidBar.App.Settings;
using FluidBar.Core.Diagnostics;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.CoreIntegration;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Monitors;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Production <see cref="IIslandPlatformActions"/> for the Skia island: the only App-side place that
/// turns a semantic page-control action into a Platform.Windows service call or an App-owned
/// deferred event.
/// <para>
/// Composition is narrow on purpose: it owns one message host plus the volume, brightness, GSMTC
/// media and clipboard services needed by the controls — not the full platform graph (display,
/// battery, notifications, tray/hotkeys stay with their existing owners). Every service start is
/// best-effort: a failed service leaves its controls reporting <c>control-unsupported</c> with the
/// exact missing service instead of pretending to work.
/// </para>
/// <para>
/// Nothing here blocks the UI thread: service calls are async and fire-and-forget with logged
/// failures, and <see cref="Invoke"/> only reads a snapshot or dispatches a task.
/// </para>
/// </summary>
public sealed class SkiaPlatformActions : IIslandPlatformActions, IAsyncDisposable
{
    private readonly SettingsController _settings;
    private readonly IAppLogger _logger;
    private readonly IPlatformLogger _platformLogger;
    private readonly PlatformCounters _counters;
    private readonly PlatformMessageHost _messageHost;
    private readonly VolumeMonitor _volume;
    private readonly BrightnessMonitor _brightness;
    private readonly GsmtcMediaSessionService _media;
    private readonly ClipboardService _clipboard;
    private readonly ArtworkCache _artworkCache;
    private readonly ArtworkTempStore _artworkTempStore;
    private int _started;
    private int _disposed;

    /// <summary>Creates the adapter; call <see cref="StartAsync"/> or <see cref="StartInBackground"/> once.</summary>
    /// <param name="settings">Live App settings (theme quick toggle + persistence).</param>
    /// <param name="logger">App logger for control/service failures.</param>
    /// <param name="timeProvider">Injected clock (tests/diagnostics).</param>
    public SkiaPlatformActions(SettingsController settings, IAppLogger? logger = null, TimeProvider? timeProvider = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullAppLogger.Instance;
        _platformLogger = new AppLoggerPlatformAdapter(_logger);
        _counters = new PlatformCounters();
        var time = timeProvider ?? TimeProvider.System;

        _messageHost = new PlatformMessageHost(_platformLogger, _counters);
        _volume = new VolumeMonitor(_platformLogger, _counters, time);
        _brightness = new BrightnessMonitor(_platformLogger, _counters, time);
        _artworkCache = new ArtworkCache();
        _artworkTempStore = new ArtworkTempStore(
            new ArtworkTempStoreOptions { DirectoryPath = Path.Combine(Path.GetTempPath(), "FluidBar", "art") },
            _platformLogger,
            time);
        _media = new GsmtcMediaSessionService(_platformLogger, _counters, _artworkCache, time);
        _clipboard = new ClipboardService(_messageHost, _platformLogger, _counters, null, time);
    }

    /// <summary>GSMTC media source exposed to first-party plugins (same instance the island controls use).</summary>
    public IMediaSessionSource MediaSource => _media;

    /// <summary>Clipboard source exposed to first-party plugins.</summary>
    public IClipboardSource ClipboardSource => _clipboard;

    /// <summary>Bounded artwork materializer exposed to the media plugin (<c>AlbumArtPath</c>).</summary>
    public ArtworkTempStore ArtworkTempStore => _artworkTempStore;

    /// <summary>Dedicated platform message host (shared by the plugin notification listener).</summary>
    public PlatformMessageHost MessageHost => _messageHost;

    /// <summary>Diagnostics counters shared by the platform services.</summary>
    public PlatformCounters Counters => _counters;

    /// <summary>Platform logger used by the App-composed services.</summary>
    public IPlatformLogger PlatformLogger => _platformLogger;

    /// <summary>
    /// Raised for actions that belong to a later App owner (notification activation/dismiss, agent
    /// activation, task toggle, calendar navigation, clipboard pin). Subscribers must be fast; the
    /// controller already reported the action as deferred.
    /// </summary>
    public event Action<IslandPlatformAction>? DeferredActionRequested;

    /// <summary>Whether the best-effort start has been kicked off.</summary>
    public bool IsStarted => Volatile.Read(ref _started) != 0;

    /// <summary>Starts the platform services on the thread pool (never blocks the UI thread).</summary>
    public void StartInBackground() => _ = StartSafelyAsync();

    /// <summary>Starts the message host and each control service; individual failures are logged, not thrown.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        await StartOneAsync("message-host", () => _messageHost.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("volume", () => _volume.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("brightness", () => _brightness.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("media", () => _media.StartAsync(cancellationToken)).ConfigureAwait(false);
        await StartOneAsync("clipboard", () => _clipboard.StartAsync(cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IslandActionResult Invoke(in IslandPlatformAction action)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return IslandActionResult.Unsupported("the platform action seam has been disposed");
        }

        return action.Kind switch
        {
            IslandPlatformActionKind.MediaTogglePlay => DispatchMedia(
                () => _media.TogglePlayPauseAsync(), "island.actions.media-toggle"),
            IslandPlatformActionKind.MediaPrevious => DispatchMedia(
                () => _media.PreviousAsync(), "island.actions.media-previous"),
            IslandPlatformActionKind.MediaNext => DispatchMedia(
                () => _media.NextAsync(), "island.actions.media-next"),
            IslandPlatformActionKind.MediaSeek => Seek(action.Value),
            IslandPlatformActionKind.VolumeSet => SetVolume(action.Value),
            IslandPlatformActionKind.BrightnessSet => SetBrightness(action.Value),
            IslandPlatformActionKind.QuickToggle => Toggle(action.Toggle),
            IslandPlatformActionKind.MediaOutput => Unsupported(
                "no audio-output picker service exists (needs an IAudioOutputService over IMMDeviceEnumerator)"),
            IslandPlatformActionKind.MediaQueue => Unsupported(
                "no media queue service exists (the GSMTC snapshot exposes no queue)"),
            IslandPlatformActionKind.NotificationActivate => Defer(
                action, "notification activation is App-owned; the Windows notification listener exposes no activate API"),
            IslandPlatformActionKind.NotificationDismiss => Defer(
                action, "notification dismissal is App-owned; Windows exposes no public toast-dismiss API"),
            IslandPlatformActionKind.ClipboardCopy => CopyClipboard(action.Text),
            IslandPlatformActionKind.ClipboardPin => Defer(
                action, "clipboard pinning is App-owned; ClipboardService has no pin state"),
            IslandPlatformActionKind.AgentActivate => Defer(
                action, "agent session activation is App-owned (no platform session-activate service)"),
            IslandPlatformActionKind.TaskToggle => Defer(
                action, "task state is App-owned (no platform task service)"),
            IslandPlatformActionKind.CalendarNavigate => Defer(
                action, "calendar month state is App-owned (no platform calendar service)"),
            _ => Unsupported($"unknown island action {action.Kind}"),
        };
    }

    private IslandActionResult DispatchMedia(Func<Task<bool>> command, string eventId)
    {
        if (_media.State != PlatformMonitorState.Running)
        {
            return Unsupported("GsmtcMediaSessionService is not running (no media session source)");
        }

        var snapshot = _media.Current;
        if (snapshot is null || snapshot.IsEmpty)
        {
            return Unsupported("no active GSMTC media session; media transport is ignored until a session exists");
        }

        _ = RunAsync(async () => { await command().ConfigureAwait(false); }, eventId);
        return IslandActionResult.Handled;
    }

    private IslandActionResult Seek(double fraction)
    {
        if (_media.State != PlatformMonitorState.Running)
        {
            return Unsupported("GsmtcMediaSessionService is not running (no media session source)");
        }

        var snapshot = _media.Current;
        if (snapshot is null || snapshot.IsEmpty)
        {
            return Unsupported(
                "no active GSMTC media session; seek fallback: the slider is ignored until a session exists");
        }

        if (!snapshot.CanSeek)
        {
            return Unsupported("the active media session reports CanSeek=false; seek fallback: the slider is ignored");
        }

        if (snapshot.Duration <= TimeSpan.Zero)
        {
            return Unsupported(
                "the active media session has no known duration; seek fallback: a fraction cannot be converted to a position");
        }

        var position = TimeSpan.FromTicks((long)Math.Round(snapshot.Duration.Ticks * Math.Clamp(fraction, 0, 1)));
        _ = RunAsync(() => _media.SeekAsync(position), "island.actions.media-seek");
        return IslandActionResult.Handled;
    }

    private IslandActionResult SetVolume(double fraction)
    {
        if (_volume.State != PlatformMonitorState.Running)
        {
            return Unsupported("VolumeMonitor is not running (no default audio endpoint)");
        }

        var percent = ToPercent(fraction);
        _ = RunAsync(() => _volume.SetVolumeAsync(percent), "island.actions.volume-set");
        return IslandActionResult.Handled;
    }

    private IslandActionResult SetBrightness(double fraction)
    {
        if (_brightness.State != PlatformMonitorState.Running)
        {
            return Unsupported("BrightnessMonitor is not running (no DDC/CI or WMI provider)");
        }

        var percent = ToPercent(fraction);
        _ = RunAsync(() => _brightness.SetPercentAsync(percent), "island.actions.brightness-set");
        return IslandActionResult.Handled;
    }

    private IslandActionResult Toggle(IslandQuickToggle toggle)
    {
        switch (toggle)
        {
            case IslandQuickToggle.Wifi:
                return Unsupported(
                    "no Wi-Fi radio toggle service exists: NetworkMonitor is read-only and the platform layer has no Windows.Devices.Radios/WlanSetInterface seam");
            case IslandQuickToggle.Bluetooth:
                return Unsupported(
                    "no Bluetooth radio toggle service exists: BluetoothDeviceMonitor is read-only and the platform layer has no Windows.Devices.Radios.Radio seam");
            case IslandQuickToggle.Focus:
                return Unsupported(
                    "no Focus Assist service exists: Windows has no supported public quiet-hours toggle API");
            case IslandQuickToggle.Theme:
                try
                {
                    var next = _settings.Current.Theme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
                    _settings.Update(settings => settings with { Theme = next });
                    return IslandActionResult.Handled;
                }
                catch (ObjectDisposedException)
                {
                    return Unsupported("SettingsController is disposed; the theme cannot be toggled");
                }

            default:
                return Unsupported($"unknown quick toggle {toggle}");
        }
    }

    private IslandActionResult CopyClipboard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Unsupported("the clipboard row has no text to copy (image/file rows are not supported)");
        }

        _ = RunAsync(() => _clipboard.SetTextAsync(text), "island.actions.clipboard-copy");
        return IslandActionResult.Handled;
    }

    private IslandActionResult Defer(in IslandPlatformAction action, string reason)
    {
        DeferredActionRequested?.Invoke(action);
        return IslandActionResult.Deferred(reason);
    }

    private static IslandActionResult Unsupported(string reason) => IslandActionResult.Unsupported(reason);

    private static int ToPercent(double fraction)
        => (int)Math.Round(Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1) * 100);

    private async Task StartSafelyAsync()
    {
        try
        {
            await StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "island.actions.start-failed",
                "The page-control platform services could not be started; controls will report unsupported.", ex);
        }
    }

    private async Task StartOneAsync(string name, Func<Task> start)
    {
        try
        {
            await start().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, $"island.actions.{name}-start-failed",
                $"Platform service '{name}' failed to start; its controls will report unsupported.", ex);
        }
    }

    private async Task RunAsync(Func<Task> work, string eventId)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, eventId + "-failed",
                "A page-control platform call failed; the island stays responsive.", ex);
        }
    }

    /// <summary>Disposes in the background (App exit is synchronous); failures are logged.</summary>
    public void DisposeInBackground() => _ = DisposeSafelyAsync();

    private async Task DisposeSafelyAsync()
    {
        try
        {
            await DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "island.actions.dispose-failed",
                "The page-control platform services did not stop cleanly.", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopOneAsync("clipboard", () => _clipboard.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("media", () => _media.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("brightness", () => _brightness.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("volume", () => _volume.DisposeAsync().AsTask()).ConfigureAwait(false);
        await StopOneAsync("message-host", () => _messageHost.DisposeAsync().AsTask()).ConfigureAwait(false);
        _artworkTempStore.Dispose();
    }

    private async Task StopOneAsync(string name, Func<Task> stop)
    {
        try
        {
            await stop().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, $"island.actions.{name}-stop-failed",
                $"Platform service '{name}' failed to stop cleanly.", ex);
        }
    }
}
