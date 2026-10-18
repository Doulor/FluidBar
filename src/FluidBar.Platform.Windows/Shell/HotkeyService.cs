using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Shell;

/// <summary>Why a hotkey registration did not succeed.</summary>
public enum HotkeyRegistrationFailure
{
    None = 0,
    InvalidGesture,
    AlreadyRegisteredByName,
    AlreadyRegisteredByAnotherApp,
    Win32Failure,
}

/// <summary>Result of <see cref="HotkeyService.RegisterAsync"/>.</summary>
public readonly record struct HotkeyRegistrationResult(
    bool Success,
    string? RegistrationName,
    HotkeyGesture Gesture,
    HotkeyRegistrationFailure Failure,
    int Win32Error);

/// <summary>Raised when a registered hotkey is pressed. Raised on a thread-pool thread.</summary>
public sealed class HotkeyPressedEventArgs : EventArgs
{
    public HotkeyPressedEventArgs(string registrationName, HotkeyGesture gesture)
    {
        RegistrationName = registrationName;
        Gesture = gesture;
    }

    public string RegistrationName { get; }

    public HotkeyGesture Gesture { get; }
}

/// <summary>
/// Global hotkey service built on <c>RegisterHotKey</c> + the platform message host.
/// </summary>
/// <remarks>
/// <para>No polling: <c>WM_HOTKEY</c> arrives on the host thread. Press events are dispatched to the
/// thread pool so a slow subscriber cannot stall other platform windows.</para>
/// <para>Registration failures are explicit (<see cref="HotkeyRegistrationResult"/>) and logged —
/// the design requires a visible conflict indicator in settings, never a silent failure.</para>
/// </remarks>
public sealed class HotkeyService : IAsyncDisposable
{
    private const int FirstHotkeyId = 0x1000;

    private readonly PlatformMessageHost _host;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly Dictionary<string, RegistrationEntry> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, RegistrationEntry> _byId = new();
    private PlatformWindow? _window;
    private int _nextId = FirstHotkeyId;
    private int _disposed;

    public HotkeyService(PlatformMessageHost host, IPlatformLogger logger, PlatformCounters counters)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
    }

    /// <summary>Raised when a registered hotkey fires.</summary>
    public event EventHandler<HotkeyPressedEventArgs>? Pressed;

    /// <summary>Whether the service window is created and registrations are accepted.</summary>
    public bool IsRunning => _window is not null;

    /// <summary>Starts the service (creates a message-only window). Idempotent.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_window is not null)
        {
            return;
        }

        _window = await _host.CreateWindowAsync("hotkeys", PlatformWindowKind.MessageOnly,
            OnWindowMessage, cancellationToken).ConfigureAwait(false);
        _logger.LogInfo(PlatformLogCategory.Shell, "hotkey.started", "Hotkey service started.");
    }

    /// <summary>Registers a global hotkey. Returns an explicit result instead of throwing on conflicts.</summary>
    public async Task<HotkeyRegistrationResult> RegisterAsync(string registrationName, string gesture,
        CancellationToken cancellationToken = default)
    {
        if (!HotkeyGesture.TryParse(gesture, out var parsed, out var error))
        {
            _counters.IncrementHotkeyRegistrationFailures();
            _logger.LogWarning(PlatformLogCategory.Shell, "hotkey.invalid-gesture",
                $"Hotkey '{registrationName}' is invalid: {error}");
            return new HotkeyRegistrationResult(false, registrationName, default,
                HotkeyRegistrationFailure.InvalidGesture, 0);
        }

        return await RegisterAsync(registrationName, parsed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Registers a global hotkey. Returns an explicit result instead of throwing on conflicts.</summary>
    public async Task<HotkeyRegistrationResult> RegisterAsync(string registrationName, HotkeyGesture gesture,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationName);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_window is null)
        {
            throw new InvalidOperationException("Hotkey service is not running; call StartAsync first.");
        }

        var window = _window;
        var result = await _host.InvokeAsync(() =>
        {
            if (_byName.ContainsKey(registrationName))
            {
                return new HotkeyRegistrationResult(false, registrationName, gesture,
                    HotkeyRegistrationFailure.AlreadyRegisteredByName, 0);
            }

            var id = _nextId++;
            var nativeModifiers = gesture.ToNativeModifiers();
            if (!NativeMethods.User32.RegisterHotKey(window.Handle, id, nativeModifiers, gesture.VirtualKey))
            {
                var error = Win32Error.LastError;
                var failure = error == Win32Error.ErrorHotkeyAlreadyRegistered
                    ? HotkeyRegistrationFailure.AlreadyRegisteredByAnotherApp
                    : HotkeyRegistrationFailure.Win32Failure;
                return new HotkeyRegistrationResult(false, registrationName, gesture, failure, error);
            }

            var entry = new RegistrationEntry(registrationName, gesture);
            lock (_byName)
            {
                _byName[registrationName] = entry;
                _byId[id] = entry;
            }

            return new HotkeyRegistrationResult(true, registrationName, gesture, HotkeyRegistrationFailure.None, 0);
        }, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            _counters.IncrementHotkeysRegistered();
            _logger.LogInfo(PlatformLogCategory.Shell, "hotkey.registered", "Global hotkey registered.",
                new Dictionary<string, object?>
                {
                    ["name"] = registrationName,
                    ["gesture"] = gesture.ToString(),
                });
        }
        else
        {
            _counters.IncrementHotkeyRegistrationFailures();
            _logger.LogWarning(PlatformLogCategory.Shell, "hotkey.registration-failed",
                $"Global hotkey '{registrationName}' could not be registered ({result.Failure}, win32={result.Win32Error}).",
                data: new Dictionary<string, object?>
                {
                    ["name"] = registrationName,
                    ["gesture"] = gesture.ToString(),
                    ["failure"] = result.Failure.ToString(),
                    ["win32"] = result.Win32Error,
                });
        }

        return result;
    }

    /// <summary>Unregisters one hotkey by its registration name.</summary>
    public async Task UnregisterAsync(string registrationName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_window is null || !_byName.TryGetValue(registrationName, out var entry))
        {
            return;
        }

        await UnregisterEntryAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    private async Task UnregisterEntryAsync(RegistrationEntry entry, CancellationToken cancellationToken)
    {
        var window = _window;
        if (window is null)
        {
            return;
        }

        int? id = null;
        lock (_byName)
        {
            _byName.Remove(entry.Name);
            foreach (var pair in _byId.ToArray())
            {
                if (ReferenceEquals(pair.Value, entry))
                {
                    id = pair.Key;
                    _byId.Remove(pair.Key);
                    break;
                }
            }
        }

        if (id is null)
        {
            return;
        }

        var registrationId = id.Value;
        await _host.InvokeAsync(() => NativeMethods.User32.UnregisterHotKey(window.Handle, registrationId),
            cancellationToken).ConfigureAwait(false);
        _logger.LogInfo(PlatformLogCategory.Shell, "hotkey.unregistered", "Global hotkey unregistered.",
            new Dictionary<string, object?> { ["name"] = entry.Name });
    }

    /// <summary>Unregisters every hotkey (used on shutdown before disposal).</summary>
    public async Task UnregisterAllAsync(CancellationToken cancellationToken = default)
    {
        var window = _window;
        if (window is null)
        {
            return;
        }

        KeyValuePair<int, RegistrationEntry>[] entries;
        lock (_byName)
        {
            entries = _byId.ToArray();
            _byId.Clear();
            _byName.Clear();
        }

        await _host.InvokeAsync(() =>
        {
            foreach (var pair in entries)
            {
                NativeMethods.User32.UnregisterHotKey(window.Handle, pair.Key);
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private IntPtr? OnWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message != NativeMethods.WM_HOTKEY)
        {
            return null;
        }

        var id = wParam.ToInt32();
        RegistrationEntry? entry;
        lock (_byName)
        {
            _byId.TryGetValue(id, out entry);
        }

        if (entry is null)
        {
            return IntPtr.Zero;
        }

        _counters.IncrementHotkeyPressed();
        ThreadPool.QueueUserWorkItem(static state =>
        {
            var (self, args) = ((HotkeyService, HotkeyPressedEventArgs))state!;
            self.RaisePressed(args);
        }, (this, new HotkeyPressedEventArgs(entry.Name, entry.Gesture)));
        return IntPtr.Zero;
    }

    private void RaisePressed(HotkeyPressedEventArgs args)
    {
        try
        {
            Pressed?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _counters.IncrementMonitorCallbackFailures();
            _logger.LogError(PlatformLogCategory.Shell, "hotkey.handler-failed",
                $"A hotkey subscriber threw for '{args.RegistrationName}'.", ex);
        }
    }

    /// <summary>Stops the service: unregisters every hotkey and destroys the window. Idempotent.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            await UnregisterAllAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            var window = _window;
            _window = null;
            if (window is not null)
            {
                await window.DisposeAsync().ConfigureAwait(false);
            }

            _logger.LogInfo(PlatformLogCategory.Shell, "hotkey.stopped", "Hotkey service stopped.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
    }

    private sealed class RegistrationEntry
    {
        public RegistrationEntry(string name, HotkeyGesture gesture)
        {
            Name = name;
            Gesture = gesture;
        }

        public string Name { get; }

        public HotkeyGesture Gesture { get; }
    }
}
