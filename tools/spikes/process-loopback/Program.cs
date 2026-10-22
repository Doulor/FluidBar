using System.Diagnostics;
using System.Runtime.InteropServices;

// Spike: measure whether per-process audio loopback can give us a frame-accurate play clock.
// Usage: ProcessLoopback <processName> [seconds]

var name = args.Length > 0 ? args[0] : "KuGou";
var seconds = args.Length > 1 ? int.Parse(args[1]) : 5;
int targetPid;
string label;
if (int.TryParse(name, out var explicitPid))
{
    targetPid = explicitPid;
    label = "(explicit pid)";
}
else
{
    var target = Process.GetProcessesByName(name).FirstOrDefault();
    if (target is null)
    {
        Console.WriteLine($"process '{name}' not running");
        return 1;
    }

    targetPid = target.Id;
    label = name;
}

Console.WriteLine($"target pid={targetPid} ({label})");
var capture = new ProcessLoopbackCapture();
if (!capture.TryStart(targetPid, out var error))
{
    Console.WriteLine($"start failed: {error}");
    return 2;
}

var wall = Stopwatch.StartNew();
long frames = 0;
var format = capture.FormatDescription;
while (wall.Elapsed.TotalSeconds < seconds)
{
    frames += capture.DrainFrames();
    Thread.Sleep(20);
}

wall.Stop();
var audioSeconds = frames / (double)capture.SampleRate;
Console.WriteLine($"format={format}");
Console.WriteLine($"wall={wall.Elapsed.TotalSeconds:0.000}s audio={audioSeconds:0.000}s ratio={audioSeconds / wall.Elapsed.TotalSeconds:0.0000} frames={frames}");
capture.Dispose();
return 0;

internal sealed class ProcessLoopbackCapture : IDisposable
{
    private AudioInterop.IAudioClient? _client;
    private AudioInterop.IAudioCaptureClient? _capture;
    private IntPtr _eventHandle;
    private AudioInterop.IAudioInterfaceCompletionHandler? _handler;

    public int SampleRate { get; private set; } = 48000;
    public IntPtr EventHandle => _eventHandle;
    public string FormatDescription { get; private set; } = "?";

    public bool TryStart(int processId, out string error)
    {
        error = string.Empty;
        try
        {
            var parameters = AudioInterop.BuildActivationParams(processId);
            var handler = new AudioInterop.Handler();
            _handler = handler;
            AudioInterop.Activate(
                AudioInterop.VirtualDeviceProcessLoopback,
                typeof(AudioInterop.IAudioClient).GUID,
                parameters,
                handler,
                out var operation);

            if (operation is null || !handler.Wait(5000))
            {
                error = "activation timed out";
                return false;
            }

            _client = handler.Client;
            if (_client is null)
            {
                error = $"activation failed hr=0x{handler.Result:X8}";
                return false;
            }

            Console.WriteLine("client activated, initializing...");
            // GetMixFormat is not implemented on the process-loopback virtual device: use our own PCM format
            // (AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM converts it).
            var format = AudioInterop.CreatePcmFormat(48000, 2, 16);
            Console.WriteLine("format 48000/2ch/16bit (autoconvert)");
            var flags = AudioInterop.AudclntStreamFlagsLoopback
                | AudioInterop.AudclntStreamFlagsAutoConvertPcm
                | AudioInterop.AudclntStreamFlagsSrcDefaultQuality
                | AudioInterop.AudclntStreamFlagsEventCallback;
            _eventHandle = AudioInterop.CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
            Console.WriteLine($"event handle={_eventHandle}");
            var initHr = _client.Initialize(
                AudioInterop.AudclntShareModeShared,
                flags,
                2_000_000, 0, format, IntPtr.Zero);
            Console.WriteLine($"Initialize(flags=0x{flags:X}) hr=0x{initHr:X8}");
            var captureIid = typeof(AudioInterop.IAudioCaptureClient).GUID;
            var serviceHr = _client.GetService(ref captureIid, out var capturePtr);
            Console.WriteLine($"GetService hr=0x{serviceHr:X8} ptr={capturePtr}");
            if (serviceHr != 0 || capturePtr == IntPtr.Zero)
            {
                error = $"GetService failed hr=0x{serviceHr:X8}";
                return false;
            }

            _capture = (AudioInterop.IAudioCaptureClient)Marshal.GetObjectForIUnknown(capturePtr);
            Console.WriteLine($"capture RCW ok: {_capture.GetType().Name}");
            var setHandleHr = _client.SetEventHandle(_eventHandle);
            Console.WriteLine($"SetEventHandle hr=0x{setHandleHr:X8}");
            var startHr = _client.Start();
            Console.WriteLine($"Start hr=0x{startHr:X8}");
            SampleRate = 48000;
            FormatDescription = "48000/2ch/16bit";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public long DrainFrames()
    {
        if (_capture is null) return 0;
        long total = 0;
        try
        {
            while (true)
            {
                var next = _capture.GetNextPacketSize(out var pending);
                if (next != 0 || pending == 0)
                {
                    break;
                }

                var hr = _capture.GetBuffer(out _, out var frames, out _, out _, out _);
                if (hr != 0 || frames == 0)
                {
                    break;
                }

                total += frames;
                _capture.ReleaseBuffer(frames);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"drain error: {ex.GetType().Name}: {ex.Message}");
        }

        return total;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBufferDelegate(IntPtr self, out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReleaseBufferDelegate(IntPtr self, uint frames);

    public void Dispose()
    {
        try { _client?.Stop(); } catch { }
        if (_capture is not null) Marshal.ReleaseComObject(_capture);
        if (_client is not null) Marshal.ReleaseComObject(_client);
        // _handler is a managed object implementing the interface; releasing it is not valid.
    }
}

internal static class AudioInterop
{
    public const string VirtualDeviceProcessLoopback = "VAD\\Process_Loopback";
    public const int AudclntShareModeShared = 0;
    public const int AudclntStreamFlagsLoopback = 0x00020000;
    public const int AudclntStreamFlagsAutoConvertPcm = unchecked((int)0x80000000);
    public const int AudclntStreamFlagsSrcDefaultQuality = 0x08000000;
    public const int AudclntStreamFlagsEventCallback = 0x00040000;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEventW(IntPtr attributes, bool manualReset, bool initialState, IntPtr name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams,
        IAudioInterfaceCompletionHandler completionHandler,
        out IAudioInterfaceAsyncOperation activationOperation);

    public static void Activate(string path, Guid riid, IntPtr parameters,
        IAudioInterfaceCompletionHandler handler, out IAudioInterfaceAsyncOperation operation)
        => ActivateAudioInterfaceAsync(path, riid, parameters, handler, out operation);

    public static IntPtr BuildActivationParams(int processId)
    {
        // ActivateAudioInterfaceAsync expects a PROPVARIANT whose vt is VT_BLOB (65) carrying an
        // AUDIOCLIENT_ACTIVATION_PARAMS { ActivationType = 1 (ProcessLoopback),
        //   ProcessLoopbackParams { TargetProcessId, ProcessLoopbackMode = 0 (include process tree) } }.
        var payload = Marshal.AllocHGlobal(12);
        Marshal.WriteInt32(payload, 0, 1);
        Marshal.WriteInt32(payload, 4, processId);
        Marshal.WriteInt32(payload, 8, 0);

        var propVariant = Marshal.AllocHGlobal(24);
        for (var i = 0; i < 24; i++) Marshal.WriteByte(propVariant, i, 0);
        Marshal.WriteInt16(propVariant, 0, 65);          // vt = VT_BLOB
        Marshal.WriteInt32(propVariant, 8, 12);          // BLOB.cbSize
        Marshal.WriteIntPtr(propVariant, 16, payload);   // BLOB.pBlobData
        return propVariant;
    }

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

    public static IntPtr CreatePcmFormat(int sampleRate, int channels, int bits)
    {
        var format = new WaveFormatEx
        {
            FormatTag = 1,
            Channels = (ushort)channels,
            SamplesPerSecond = (uint)sampleRate,
            AverageBytesPerSecond = (uint)(sampleRate * channels * bits / 8),
            BlockAlign = (ushort)(channels * bits / 8),
            BitsPerSample = (ushort)bits,
            Size = 0,
        };
        var block = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        Marshal.StructureToPtr(format, block, false);
        return block;
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient
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
    public interface IAudioCaptureClient
    {
        int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        int ReleaseBuffer(uint frames);
        int GetNextPacketSize(out uint frames);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioInterfaceAsyncOperation
    {
        int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    internal sealed class Handler : IAudioInterfaceCompletionHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        public IAudioClient? Client { get; private set; }
        public int Result { get; private set; }

        public void ActivateCompleted(IAudioInterfaceAsyncOperation operation)
        {
            var hr = operation.GetActivateResult(out var activateResult, out var instance);
            Result = activateResult;
            if (hr == 0 && activateResult == 0 && instance is IAudioClient client)
            {
                Client = client;
            }

            _done.Set();
        }

        public bool Wait(int milliseconds) => _done.Wait(milliseconds);
    }
}
