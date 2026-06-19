using System.Runtime.InteropServices;

namespace FluidBar;

public static class ProcessAudioActivity
{
    private const float AudiblePeakThreshold = 0.0001f;
    private const int EDataFlowRender = 0;
    private const int ERoleMultimedia = 1;
    private const int DeviceStateActive = 0x00000001;
    private const int ClsctxAll = 23;
    private const int AudioSessionStateActive = 1;
    private const long RecentAudioGraceMilliseconds = 900;
    private static readonly int[] RenderRoles = [0, ERoleMultimedia, 2];
    private static readonly object RecentAudioLock = new();
    private static readonly Dictionary<int, long> RecentAudioTicksByProcessId = new();

    private static readonly Guid CLSID_MMDeviceEnumerator =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator =
        new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IID_IAudioSessionManager2 =
        new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    public static bool IsProcessAudiblyActive(int processId)
    {
        return IsAnyProcessAudiblyActive([processId]);
    }

    public static bool IsAnyProcessAudiblyActive(IEnumerable<int> processIds)
    {
        return GetAnyProcessPlaybackState(processIds) == ProcessAudioPlaybackState.Playing;
    }

    public static ProcessAudioPlaybackState GetAnyProcessPlaybackState(IEnumerable<int> processIds)
    {
        try
        {
            var targetProcessIds = processIds
                .Where(id => id > 0)
                .ToHashSet();
            if (targetProcessIds.Count == 0)
                return ProcessAudioPlaybackState.Unknown;

            var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
            if (type is null)
                return ProcessAudioPlaybackState.Unknown;

            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
            var foundTargetSession = false;

            var devices = GetActiveRenderDevices(enumerator);
            foreach (var device in devices)
            {
                var state = GetDevicePlaybackState(device, targetProcessIds);
                if (state == ProcessAudioPlaybackState.Playing)
                    return state;
                if (state == ProcessAudioPlaybackState.NotPlaying)
                    foundTargetSession = true;
            }

            if (devices.Count == 0)
            {
                foreach (var role in RenderRoles)
                {
                    var state = GetDefaultEndpointPlaybackState(enumerator, role, targetProcessIds);
                    if (state == ProcessAudioPlaybackState.Playing)
                        return state;
                    if (state == ProcessAudioPlaybackState.NotPlaying)
                        foundTargetSession = true;
                }
            }

            return foundTargetSession
                ? ProcessAudioPlaybackState.NotPlaying
                : ProcessAudioPlaybackState.Unknown;
        }
        catch
        {
        }

        return ProcessAudioPlaybackState.Unknown;
    }

    private static IReadOnlyList<IMMDevice> GetActiveRenderDevices(IMMDeviceEnumerator enumerator)
    {
        var devices = new List<IMMDevice>();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(
                EDataFlowRender,
                DeviceStateActive,
                out var collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            for (var i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(collection.Item(i, out var device));
                devices.Add(device);
            }
        }
        catch
        {
        }

        return devices;
    }

    private static ProcessAudioPlaybackState GetDefaultEndpointPlaybackState(
        IMMDeviceEnumerator enumerator,
        int role,
        ISet<int> targetProcessIds)
    {
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(
                EDataFlowRender,
                role,
                out var device));
            return GetDevicePlaybackState(device, targetProcessIds);
        }
        catch
        {
        }

        return ProcessAudioPlaybackState.Unknown;
    }

    private static ProcessAudioPlaybackState GetDevicePlaybackState(
        IMMDevice device,
        ISet<int> targetProcessIds)
    {
        try
        {
            var iid = IID_IAudioSessionManager2;
            Marshal.ThrowExceptionForHR(device.Activate(
                ref iid,
                ClsctxAll,
                IntPtr.Zero,
                out var managerObj));

            var manager = (IAudioSessionManager2)managerObj;
            Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out var sessionEnumerator));
            Marshal.ThrowExceptionForHR(sessionEnumerator.GetCount(out var count));

            var foundTargetSession = false;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    Marshal.ThrowExceptionForHR(sessionEnumerator.GetSession(i, out var control));
                    var control2 = (IAudioSessionControl2)control;
                    Marshal.ThrowExceptionForHR(control2.GetProcessId(out var sessionProcessId));
                    if (!targetProcessIds.Contains(sessionProcessId))
                        continue;

                    foundTargetSession = true;
                    Marshal.ThrowExceptionForHR(control.GetState(out var state));
                    var peak = 0f;
                    if (control is IAudioMeterInformation meter)
                        _ = meter.GetPeakValue(out peak);

                    var isActive = state == AudioSessionStateActive;
                    var hasLiveAudio = isActive && HasAudiblePeak(peak);
                    if (hasLiveAudio)
                        MarkRecentAudio(sessionProcessId);

                    if (IsSessionPlayingForFallback(
                            isActive,
                            peak,
                            hasLiveAudio || HasRecentAudio(sessionProcessId)))
                    {
                        return ProcessAudioPlaybackState.Playing;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return foundTargetSession
                ? ProcessAudioPlaybackState.NotPlaying
                : ProcessAudioPlaybackState.Unknown;
        }
        catch
        {
        }

        return ProcessAudioPlaybackState.Unknown;
    }

    public static bool IsAnyTargetProcessSessionPlaying(
        IEnumerable<ProcessAudioSessionSnapshot> sessions,
        IEnumerable<int> processIds)
    {
        var targetProcessIds = processIds
            .Where(id => id > 0)
            .ToHashSet();
        if (targetProcessIds.Count == 0)
            return false;

        foreach (var session in sessions)
        {
            if (!targetProcessIds.Contains(session.ProcessId))
                continue;
            if (IsSessionPlayingForFallback(session.IsActive, session.Peak))
                return true;
        }

        return false;
    }

    public static bool IsSessionPlayingForFallback(
        bool isActive,
        float peak,
        bool hasRecentAudioEvidence = false)
    {
        return isActive && (HasAudiblePeak(peak) || hasRecentAudioEvidence);
    }

    private static bool HasAudiblePeak(float peak) => peak > AudiblePeakThreshold;

    private static void MarkRecentAudio(int processId)
    {
        if (processId <= 0)
            return;

        lock (RecentAudioLock)
        {
            RecentAudioTicksByProcessId[processId] = Environment.TickCount64;
        }
    }

    private static bool HasRecentAudio(int processId)
    {
        if (processId <= 0)
            return false;

        lock (RecentAudioLock)
        {
            if (!RecentAudioTicksByProcessId.TryGetValue(processId, out var lastTicks))
                return false;

            var age = unchecked(Environment.TickCount64 - lastTicks);
            if (age <= RecentAudioGraceMilliseconds)
                return true;

            RecentAudioTicksByProcessId.Remove(processId);
            return false;
        }
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out int deviceCount);
        int Item(int deviceIndex, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(
            ref Guid iid,
            int clsCtx,
            IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int NotImpl1();
        int NotImpl2();
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int sessionCount);
        int GetSession(int sessionIndex, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        int GetState(out int state);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        int GetState(out int state);
        int GetDisplayName(IntPtr displayName);
        int SetDisplayName(string value, Guid eventContext);
        int GetIconPath(IntPtr iconPath);
        int SetIconPath(string value, Guid eventContext);
        int GetGroupingParam(out Guid groupingId);
        int SetGroupingParam(Guid groupingId, Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
        int GetSessionIdentifier(IntPtr retVal);
        int GetSessionInstanceIdentifier(IntPtr retVal);
        int GetProcessId(out int processId);
        int IsSystemSoundsSession();
        int SetDuckingPreference(bool optOut);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);
        int GetMeteringChannelCount(out int channelCount);
        int GetChannelsPeakValues(int channelCount, [Out] float[] peakValues);
        int QueryHardwareSupport(out int hardwareSupportMask);
    }
}

public sealed record ProcessAudioSessionSnapshot(int ProcessId, bool IsActive, float Peak);

public enum ProcessAudioPlaybackState
{
    Unknown,
    NotPlaying,
    Playing
}
