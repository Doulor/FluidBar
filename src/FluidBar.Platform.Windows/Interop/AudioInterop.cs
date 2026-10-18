using System.Runtime.InteropServices;

namespace FluidBar.Platform.Windows.Interop;

/// <summary>
/// Minimal Core Audio (WASAPI) COM interop: endpoint enumeration, endpoint volume with change callbacks,
/// metering and default-device notifications. All objects are activated on a dedicated MTA worker.
/// </summary>
internal static class AudioInterop
{
    internal static readonly Guid IIdAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    internal static readonly Guid IIdAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    internal const int EDataFlowRender = 0;
    internal const int EDataFlowCapture = 1;
    internal const int ERoleConsole = 0;
    internal const int ERoleMultimedia = 1;
    internal const int ERoleCommunications = 2;
    internal const int DeviceStateActive = 1;

    /// <summary>CLSID_MMDeviceEnumerator.</summary>
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal sealed class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr deviceCollection);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, [MarshalAs(UnmanagedType.Interface)] out IMMDevice? device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id,
            [MarshalAs(UnmanagedType.Interface)] out IMMDevice? device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback([MarshalAs(UnmanagedType.Interface)] IMMNotificationClient client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback([MarshalAs(UnmanagedType.Interface)] IMMNotificationClient client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

        [PreserveSig]
        int OpenPropertyStore(uint access, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);

        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PROPERTYKEY key);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [ComImport]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolumeCallback
    {
        void OnNotify(IntPtr notificationData);
    }

    /// <summary>AUDIO_VOLUME_NOTIFICATION_DATA (first channel only; the array tail is read separately).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AUDIO_VOLUME_NOTIFICATION_DATA
    {
        public Guid EventContext;
        public int Muted;
        public float MasterVolume;
        public uint ChannelCount;
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify([MarshalAs(UnmanagedType.Interface)] IAudioEndpointVolumeCallback callback);

        [PreserveSig]
        int UnregisterControlChangeNotify([MarshalAs(UnmanagedType.Interface)] IAudioEndpointVolumeCallback callback);

        [PreserveSig]
        int GetChannelCount(out uint channelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float level, IntPtr eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float level);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float level, IntPtr eventContext);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float level);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, IntPtr eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);

        [PreserveSig]
        int GetVolumeStepInfo(out uint step, out uint stepCount);

        [PreserveSig]
        int VolumeStepUp(IntPtr eventContext);

        [PreserveSig]
        int VolumeStepDown(IntPtr eventContext);

        [PreserveSig]
        int QueryHardwareSupport(out uint hardwareSupportMask);

        [PreserveSig]
        int GetVolumeRange(out float min, out float max, out float increment);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float peak);

        [PreserveSig]
        int GetMeteringChannelCount(out uint channelCount);

        [PreserveSig]
        int GetChannelsPeakValues(uint channelCount, [Out] float[] peakValues);

        [PreserveSig]
        int QueryHardwareSupport(out uint hardwareSupportMask);
    }

    /// <summary>Parses <c>AUDIO_VOLUME_NOTIFICATION_DATA</c> without copying the channel array.</summary>
    internal static AudioVolumeNotification ParseNotification(IntPtr notificationData)
    {
        var data = Marshal.PtrToStructure<AUDIO_VOLUME_NOTIFICATION_DATA>(notificationData);
        // The per-channel array follows the fixed header: sizeof(GUID) + BOOL + float + UINT.
        var firstChannel = data.ChannelCount > 0
            ? Marshal.PtrToStructure<float>(IntPtr.Add(notificationData, 28))
            : 0f;
        return new AudioVolumeNotification(data.EventContext, data.Muted != 0, data.MasterVolume, firstChannel,
            data.ChannelCount);
    }

    /// <summary>Managed view of the COM callback payload.</summary>
    internal readonly record struct AudioVolumeNotification(Guid EventContext, bool IsMuted, float MasterVolume,
        float FirstChannelVolume, uint ChannelCount);

    /// <summary>Managed implementation of <see cref="IAudioEndpointVolumeCallback"/>.</summary>
    internal sealed class AudioVolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly Action<AudioVolumeNotification> _onChanged;
        private readonly Action<Exception> _onError;

        public AudioVolumeCallback(Action<AudioVolumeNotification> onChanged, Action<Exception> onError)
        {
            _onChanged = onChanged;
            _onError = onError;
        }

        public void OnNotify(IntPtr notificationData)
        {
            try
            {
                _onChanged(ParseNotification(notificationData));
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
        }
    }

    /// <summary>Managed implementation of <see cref="IMMNotificationClient"/> raising default-device changes.</summary>
    internal sealed class AudioDeviceNotificationClient : IMMNotificationClient
    {
        private readonly Action _onDefaultRenderDeviceChanged;
        private readonly Action<Exception> _onError;

        public AudioDeviceNotificationClient(Action onDefaultRenderDeviceChanged, Action<Exception> onError)
        {
            _onDefaultRenderDeviceChanged = onDefaultRenderDeviceChanged;
            _onError = onError;
        }

        public void OnDeviceStateChanged(string deviceId, uint newState)
        {
        }

        public void OnDeviceAdded(string deviceId)
        {
        }

        public void OnDeviceRemoved(string deviceId)
        {
        }

        public void OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId)
        {
            if (flow != EDataFlowRender || role is not (ERoleMultimedia or ERoleConsole))
            {
                return;
            }

            try
            {
                _onDefaultRenderDeviceChanged();
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
        }

        public void OnPropertyValueChanged(string deviceId, PROPERTYKEY key)
        {
        }
    }
}
