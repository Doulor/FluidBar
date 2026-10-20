using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Threading;

/// <summary>
/// Event-driven registry change watcher: <c>RegNotifyChangeKeyValue</c> on a manual-reset event plus a
/// registered wait. No polling thread and no timer. Used by the system-theme monitor.
/// </summary>
internal sealed class RegistryChangeWatcher : IDisposable
{
    private readonly string _subKeyPath;
    private readonly uint _notifyFilter;
    private readonly Action _onChanged;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    /// <summary>
    /// Rooted callback for <c>RegisterWaitForSingleObject</c>. The native wait stores only a function
    /// pointer, so passing a method group here lets GC collect the delegate and the next notification
    /// kills the process with a FailFast; the delegate must be held for the watcher's lifetime.
    /// </summary>
    private readonly NativeMethods.WaitOrTimerProc _waitProc;
    private SafeRegistryKey? _key;
    private SafeKernelHandle? _event;
    private IntPtr _waitHandle;
    private int _disposed;

    public RegistryChangeWatcher(string subKeyPath, bool watchSubTree, Action onChanged, IPlatformLogger logger,
        PlatformCounters counters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);
        _subKeyPath = subKeyPath;
        _notifyFilter = NativeMethods.REG_NOTIFY_CHANGE_LAST_SET | NativeMethods.REG_NOTIFY_CHANGE_NAME;
        WatchSubTree = watchSubTree;
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _waitProc = OnWaitCompleted;
    }

    public bool WatchSubTree { get; }

    /// <summary>Opens the key and arms the first notification. Throws on Win32 failure (caller logs/degrades).</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _key = SafeRegistryKey.OpenCurrentUser(_subKeyPath, NativeMethods.KEY_NOTIFY);
        _event = SafeKernelHandle.CreateEvent(manualReset: true, initialState: false);
        Arm();

        var registered = NativeMethods.Kernel32.RegisterWaitForSingleObject(out _waitHandle,
            _event.DangerousGetHandle(), _waitProc, IntPtr.Zero, uint.MaxValue, 0);
        if (!registered)
        {
            var error = Win32Error.LastError;
            Dispose();
            throw Win32Error.CreateException($"RegisterWaitForSingleObject('{_subKeyPath}')", error);
        }

        _logger.LogDebug(PlatformLogCategory.Platform, "registry-watcher.started",
            "Registry change watcher armed.", new Dictionary<string, object?> { ["key"] = _subKeyPath });
    }

    private void Arm()
    {
        var result = NativeMethods.Advapi32.RegNotifyChangeKeyValue(_key!.DangerousGetHandle(), WatchSubTree,
            _notifyFilter, _event!.DangerousGetHandle(), true);
        if (result != 0)
        {
            throw Win32Error.CreateException($"RegNotifyChangeKeyValue('{_subKeyPath}')", result);
        }
    }

    private void OnWaitCompleted(IntPtr context, bool timedOut)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            var eventHandle = _event;
            var key = _key;
            if (eventHandle is null || key is null || eventHandle.IsInvalid || key.IsInvalid)
            {
                return;
            }

            // Reset + re-arm before invoking the handler so changes that happen during the handler
            // are reported by the freshly armed notification (no lost edge).
            NativeMethods.Kernel32.ResetEvent(eventHandle.DangerousGetHandle());
            Arm();
            _onChanged();
        }
        catch (Exception ex)
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "registry-watcher.failed",
                $"Registry watcher for '{_subKeyPath}' failed to re-arm.", ex);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_waitHandle != IntPtr.Zero)
        {
            // IntPtr.Zero waits for in-flight callbacks, avoiding use-after-close of the key/event.
            NativeMethods.Kernel32.UnregisterWaitEx(_waitHandle, IntPtr.Zero);
            _waitHandle = IntPtr.Zero;
        }

        _key?.Dispose();
        _key = null;
        _event?.Dispose();
        _event = null;
        _logger.LogDebug(PlatformLogCategory.Platform, "registry-watcher.stopped",
            "Registry change watcher stopped.");
    }
}
