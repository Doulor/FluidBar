using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace FluidBar.Monitors;

/// <summary>
/// 音量变化监控 - 持续轮询系统音量
/// </summary>
public sealed class VolumeMonitor : ISystemMonitor
{
    public string Id => "volume";
    public string Name => "音量指示";
    public string Description => "调节音量时在灵动岛显示音量条";
    public string Icon => "\uE767"; // Segoe MDL2 Volume
    public bool Enabled { get; set; } = true;
    public event Action<IslandEvent>? EventTriggered;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int NotImpl1(); int NotImpl2(); int NotImpl3(); int NotImpl4();
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int GetMute(out bool pbMute);
    }

    private static readonly Guid CLSID_MMDeviceEnumerator =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IAudioEndpointVolume =
        new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private DispatcherTimer? _pollTimer;
    private int _lastVolume = -1;
    private bool _lastMute;
    private bool _isRunning;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _pollTimer.Tick += (_, _) => PollVolume();
        _pollTimer.Start();
        PollVolume();
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private void PollVolume()
    {
        try
        {
            GetVolume(out var volume, out var isMute);

            if (volume != _lastVolume || isMute != _lastMute)
            {
                _lastVolume = volume;
                _lastMute = isMute;

                var iconKind = isMute ? "volume_mute" : "volume";
                var title = isMute ? "已静音" : $"音量 {volume}%";
                EventTriggered?.Invoke(new IslandEvent(Id, title, $"{volume}%", iconKind));
            }
        }
        catch
        {
            // COM 调用偶尔失败，不停止轮询
        }
    }

    private static void GetVolume(out int volume, out bool isMute)
    {
        volume = 0;
        isMute = false;
        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
            if (type == null) return;
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
            enumerator.GetDefaultAudioEndpoint(0, 1, out var device);

            var iid = IID_IAudioEndpointVolume;
            device.Activate(ref iid, 1, IntPtr.Zero, out var obj);

            if (obj is IAudioEndpointVolume ep)
            {
                ep.GetMasterVolumeLevelScalar(out var level);
                ep.GetMute(out isMute);
                volume = (int)(level * 100);
            }
        }
        catch { }
    }

    public void Dispose() => Stop();
}
