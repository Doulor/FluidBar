using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Monitors;

/// <summary>Immutable system volume snapshot.</summary>
/// <param name="Percent">Master volume 0–100.</param>
/// <param name="IsMuted">System mute flag.</param>
/// <param name="HasDevice">False when no render endpoint could be activated.</param>
/// <param name="ChannelCount">Endpoint channel count (0 when unknown).</param>
public sealed record VolumeSnapshot(int Percent, bool IsMuted, bool HasDevice, uint ChannelCount);

/// <summary>Pure volume conversion / clamping policy (tested without touching COM).</summary>
public static class VolumeLevelPolicy
{
    /// <summary>Converts a WASAPI scalar (0.0–1.0) to an integer percentage, clamped to 0–100.</summary>
    public static int ToPercent(float scalar)
    {
        if (float.IsNaN(scalar))
        {
            return 0;
        }

        var percent = (int)Math.Round(scalar * 100.0, MidpointRounding.AwayFromZero);
        return Math.Clamp(percent, 0, 100);
    }

    /// <summary>Converts an integer percentage to a WASAPI scalar.</summary>
    public static float ToScalar(int percent) => Math.Clamp(percent, 0, 100) / 100f;

    /// <summary>Applies a relative step (e.g. hotkey ±2) with clamping.</summary>
    public static int Step(int currentPercent, int delta) => Math.Clamp(currentPercent + delta, 0, 100);

    /// <summary>Builds a snapshot from a notification payload (internal shape; used by the monitor).</summary>
    internal static VolumeSnapshot FromNotification(in AudioInterop.AudioVolumeNotification notification,
        bool hasDevice = true)
        => new(ToPercent(notification.MasterVolume), notification.IsMuted, hasDevice, notification.ChannelCount);
}

/// <summary>
/// Event-driven volume monitor: one <c>IAudioEndpointVolume</c> activation, one
/// <c>IAudioEndpointVolumeCallback</c> registration and one <c>IMMNotificationClient</c> for default
/// device changes. Zero polling (v1 activated COM every 150 ms — audit finding F-01).
/// </summary>
/// <remarks>
/// All COM objects live on a dedicated MTA thread (<see cref="ComWorker"/>); callbacks only parse the
/// payload and publish an immutable snapshot. Endpoints are re-activated on default-device changes.
/// </remarks>
public sealed class VolumeMonitor : PlatformMonitor<VolumeSnapshot>
{
    private readonly ComWorker _worker;
    private readonly SemaphoreSlim _reinitializeGate = new(1, 1);
    private AudioInterop.IMMDeviceEnumerator? _enumerator;
    private AudioInterop.IMMDevice? _device;
    private AudioInterop.IAudioEndpointVolume? _volume;
    private AudioInterop.IAudioMeterInformation? _meter;
    private AudioInterop.AudioVolumeCallback? _volumeCallback;
    private AudioInterop.AudioDeviceNotificationClient? _deviceCallback;
    private int _stopped;

    public VolumeMonitor(IPlatformLogger logger, PlatformCounters counters, TimeProvider? timeProvider = null)
        : base("volume", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _worker = new ComWorker("audio", logger, counters);
    }

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _stopped, 0);
        await _worker.StartAsync(cancellationToken).ConfigureAwait(false);

        var initial = await _worker.InvokeAsync(InitializeCore, cancellationToken).ConfigureAwait(false);
        Publish(initial);
    }

    private VolumeSnapshot InitializeCore()
    {
        try
        {
            _enumerator ??= (AudioInterop.IMMDeviceEnumerator)(object)new AudioInterop.MMDeviceEnumeratorComObject();

            if (_deviceCallback is null)
            {
                _deviceCallback = new AudioInterop.AudioDeviceNotificationClient(OnDefaultDeviceChanged,
                    ex => Logger.LogWarning(PlatformLogCategory.Monitor, "volume.device-callback-failed",
                        "Default audio device callback failed.", ex));
                var registerResult = _enumerator.RegisterEndpointNotificationCallback(_deviceCallback);
                if (registerResult != 0)
                {
                    Counters.IncrementComFailures();
                    Logger.LogWarning(PlatformLogCategory.Monitor, "volume.device-notification-failed",
                        $"RegisterEndpointNotificationCallback failed with 0x{registerResult:X8}.");
                }
            }

            var hr = _enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlowRender,
                AudioInterop.ERoleMultimedia, out var device);
            if (hr != 0 || device is null)
            {
                Counters.IncrementComFailures();
                Logger.LogInfo(PlatformLogCategory.Monitor, "volume.no-endpoint",
                    $"No default render endpoint (0x{hr:X8}).");
                return new VolumeSnapshot(0, false, false, 0);
            }

            _device = device;
            var iid = AudioInterop.IIdAudioEndpointVolume;
            var activate = device.Activate(ref iid, NativeMethods.CLSCTX_ALL, IntPtr.Zero, out var instance);
            if (activate != 0 || instance is not AudioInterop.IAudioEndpointVolume volume)
            {
                Counters.IncrementComFailures();
                Logger.LogWarning(PlatformLogCategory.Monitor, "volume.activate-failed",
                    $"IAudioEndpointVolume activation failed with 0x{activate:X8}.");
                ComWorker.ReleaseComObject(instance, Logger);
                ReleaseDeviceCore();
                return new VolumeSnapshot(0, false, false, 0);
            }

            _volume = volume;
            _volumeCallback = new AudioInterop.AudioVolumeCallback(OnVolumeChanged,
                ex => Logger.LogWarning(PlatformLogCategory.Monitor, "volume.callback-failed",
                    "Audio volume callback failed.", ex));
            var register = _volume.RegisterControlChangeNotify(_volumeCallback);
            if (register != 0)
            {
                Counters.IncrementComFailures();
                Logger.LogWarning(PlatformLogCategory.Monitor, "volume.notification-failed",
                    $"RegisterControlChangeNotify failed with 0x{register:X8}.");
            }

            var meterIid = AudioInterop.IIdAudioMeterInformation;
            if (device.Activate(ref meterIid, NativeMethods.CLSCTX_ALL, IntPtr.Zero, out var meterInstance) == 0
                && meterInstance is AudioInterop.IAudioMeterInformation meter)
            {
                _meter = meter;
            }
            else
            {
                ComWorker.ReleaseComObject(meterInstance, Logger);
            }

            return ReadSnapshotCore();
        }
        catch (Exception ex)
        {
            Counters.IncrementComFailures();
            Logger.LogError(PlatformLogCategory.Monitor, "volume.init-failed",
                "Failed to initialise the audio endpoint.", ex);
            ReleaseDeviceCore();
            return new VolumeSnapshot(0, false, false, 0);
        }
    }

    private VolumeSnapshot ReadSnapshotCore()
    {
        if (_volume is null)
        {
            return new VolumeSnapshot(0, false, false, 0);
        }

        var volumeResult = _volume.GetMasterVolumeLevelScalar(out var scalar);
        var muteResult = _volume.GetMute(out var muted);
        var channelResult = _volume.GetChannelCount(out var channels);
        if (volumeResult != 0 || muteResult != 0)
        {
            Counters.IncrementComFailures();
            Logger.LogWarning(PlatformLogCategory.Monitor, "volume.read-failed",
                $"Reading endpoint volume failed (volume=0x{volumeResult:X8}, mute=0x{muteResult:X8}).");
            return Current ?? new VolumeSnapshot(0, false, false, 0);
        }

        return new VolumeSnapshot(VolumeLevelPolicy.ToPercent(scalar), muted, true,
            channelResult == 0 ? channels : 0);
    }

    private void OnVolumeChanged(AudioInterop.AudioVolumeNotification notification)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        Publish(VolumeLevelPolicy.FromNotification(notification));
    }

    private void OnDefaultDeviceChanged()
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        _ = ReinitializeSafeAsync();
    }

    private async Task ReinitializeSafeAsync()
    {
        if (!await _reinitializeGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var snapshot = await _worker.InvokeAsync(() =>
            {
                ReleaseDeviceCore();
                return InitializeCore();
            }).ConfigureAwait(false);
            Publish(snapshot);
        }
        catch (Exception ex)
        {
            Counters.IncrementComFailures();
            Logger.LogError(PlatformLogCategory.Monitor, "volume.reinit-failed",
                "Failed to re-initialise the audio endpoint after a device change.", ex);
        }
        finally
        {
            _reinitializeGate.Release();
        }
    }

    private void ReleaseDeviceCore()
    {
        if (_volume is not null && _volumeCallback is not null)
        {
            var result = _volume.UnregisterControlChangeNotify(_volumeCallback);
            if (result != 0)
            {
                Logger.LogDebug(PlatformLogCategory.Monitor, "volume.unregister-failed",
                    $"UnregisterControlChangeNotify failed with 0x{result:X8}.");
            }
        }

        ComWorker.ReleaseComObject(_meter, Logger);
        _meter = null;
        ComWorker.ReleaseComObject(_volume, Logger);
        _volume = null;
        _volumeCallback = null;
        ComWorker.ReleaseComObject(_device, Logger);
        _device = null;
    }

    /// <summary>Sets the master volume (marshalled to the COM worker; never on the UI thread).</summary>
    public Task SetVolumeAsync(int percent, CancellationToken cancellationToken = default)
    {
        var scalar = VolumeLevelPolicy.ToScalar(percent);
        return MutateAsync(() =>
        {
            if (_volume is null)
            {
                return Current ?? new VolumeSnapshot(0, false, false, 0);
            }

            var hr = _volume.SetMasterVolumeLevelScalar(scalar, IntPtr.Zero);
            if (hr != 0)
            {
                Counters.IncrementComFailures();
                Logger.LogWarning(PlatformLogCategory.Monitor, "volume.set-failed",
                    $"SetMasterVolumeLevelScalar failed with 0x{hr:X8}.");
            }

            return ReadSnapshotCore();
        }, cancellationToken);
    }

    /// <summary>Applies a relative volume step (e.g. ±2 for keyboard shortcuts).</summary>
    public Task StepVolumeAsync(int delta, CancellationToken cancellationToken = default)
    {
        var target = VolumeLevelPolicy.Step(Current?.Percent ?? 0, delta);
        return SetVolumeAsync(target, cancellationToken);
    }

    /// <summary>Sets or clears the system mute flag.</summary>
    public Task SetMuteAsync(bool muted, CancellationToken cancellationToken = default)
        => MutateAsync(() =>
        {
            if (_volume is null)
            {
                return Current ?? new VolumeSnapshot(0, false, false, 0);
            }

            var hr = _volume.SetMute(muted, IntPtr.Zero);
            if (hr != 0)
            {
                Counters.IncrementComFailures();
                Logger.LogWarning(PlatformLogCategory.Monitor, "volume.mute-failed",
                    $"SetMute failed with 0x{hr:X8}.");
            }

            return ReadSnapshotCore();
        }, cancellationToken);

    private async Task MutateAsync(Func<VolumeSnapshot> mutation, CancellationToken cancellationToken)
    {
        var snapshot = await _worker.InvokeAsync(mutation, cancellationToken).ConfigureAwait(false);
        Publish(snapshot);
    }

    /// <summary>
    /// Reads the current peak level (0.0–1.0) for the waveform visual. The App calls this only while the
    /// island is visible; the platform layer never runs a peak-meter timer itself.
    /// </summary>
    public async Task<float?> TryReadPeakLevelAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _worker.InvokeAsync(() =>
            {
                if (_meter is null)
                {
                    return (float?)null;
                }

                return _meter.GetPeakValue(out var peak) == 0 ? peak : null;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Monitor, "volume.peak-failed",
                "Reading the audio peak level failed.", ex);
            return null;
        }
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _stopped, 1);
        try
        {
            await _worker.InvokeAsync(() =>
            {
                ReleaseDeviceCore();
                if (_enumerator is not null && _deviceCallback is not null)
                {
                    _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback);
                }

                ComWorker.ReleaseComObject(_enumerator, Logger);
                _enumerator = null;
                _deviceCallback = null;
                return true;
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Monitor, "volume.release-failed",
                "Failed to release audio COM objects cleanly.", ex);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _worker.DisposeAsync().ConfigureAwait(false);
    }
}
