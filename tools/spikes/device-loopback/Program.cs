using System.Diagnostics;
using System.Runtime.InteropServices;

// Control experiment: classic DEVICE loopback (whole system) with the same capture code.
// If this yields frames while the process-loopback spike does not, the capture code is fine and
// per-process loopback is what this machine/player blocks.

var seconds = args.Length > 0 ? int.Parse(args[0]) : 4;
var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out var device);
var iid = typeof(IAudioClient).GUID;
device.Activate(ref iid, 0x17 /*CLSCTX_ALL*/, IntPtr.Zero, out var clientObj);
var client = (IAudioClient)clientObj;
client.GetMixFormat(out var format);
var wfx = Marshal.PtrToStructure<WaveFormatEx>(format);
Console.WriteLine($"device mix: tag={wfx.FormatTag} ch={wfx.Channels} rate={wfx.SamplesPerSecond} bits={wfx.BitsPerSample}");
var hr = client.Initialize(0 /*shared*/, 0x00020000 /*LOOPBACK*/, 2_000_000, 0, format, IntPtr.Zero);
Console.WriteLine($"Initialize hr=0x{hr:X8}");
var capIid = typeof(IAudioCaptureClient).GUID;
client.GetService(ref capIid, out var capPtr);
client.Start();

var cap = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(capPtr);
long frames = 0;
var sw = Stopwatch.StartNew();
while (sw.Elapsed.TotalSeconds < seconds)
{
    while (true)
    {
        var r = cap.GetBuffer(out _, out var n, out _, out _, out _);
        if (r != 0 || n == 0) break;
        frames += n;
        cap.ReleaseBuffer(n);
    }

    Thread.Sleep(20);
}

sw.Stop();
var audio = frames / (double)wfx.SamplesPerSecond;
Console.WriteLine($"wall={sw.Elapsed.TotalSeconds:0.00}s audio={audio:0.00}s ratio={audio / sw.Elapsed.TotalSeconds:0.000} frames={frames}");
client.Stop();
return 0;

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int GetBufferDelegate(IntPtr self, out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int ReleaseDelegate(IntPtr self, uint frames);

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WaveFormatEx
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSecond;
    public uint AverageBytesPerSecond;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort Size;
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorComObject { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(int flow, int stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    int OpenPropertyStore(int access, out IntPtr store);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out int state);
}

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient
{
    int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    int GetBufferSize(out uint frames);
    int GetStreamLatency(out long latency);
    int GetCurrentPadding(out uint padding);
    int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
    int GetMixFormat(out IntPtr format);
    int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    int Start();
    int Stop();
    int Reset();
    int SetEventHandle(IntPtr handle);
    int GetService(ref Guid riid, out IntPtr service);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCaptureClient
{
    int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    int ReleaseBuffer(uint frames);
    int GetNextPacketSize(out uint frames);
}
