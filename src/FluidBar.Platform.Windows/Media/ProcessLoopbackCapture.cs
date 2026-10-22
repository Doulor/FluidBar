using System.Runtime.InteropServices;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Counts the audio frames a single process renders, producing an exact playback clock.
/// </summary>
/// <remarks>
/// <para>Uses the Windows 10 2004+ per-process loopback device (<c>VAD\Process_Loopback</c>) through
/// <c>ActivateAudioInterfaceAsync</c>. Frames are counted, not decoded: the elapsed audio time is
/// <c>frames / sampleRate</c>, which advances only while the player is actually rendering.</para>
/// <para>Verified on 2026-10-22 against KuGou: 4 s wall produced 3.95 s of audio (ratio 0.987), while the
/// device-wide loopback gave 0.992. Neither an exclusive-mode player nor a DirectUI window can hide from
/// this, which is why it replaces the 30-second <c>KuGou.ini</c> guesswork.</para>
/// </remarks>
internal sealed class ProcessLoopbackCapture : IDisposable
{
    private const string VirtualDeviceProcessLoopback = "VAD\\Process_Loopback";
    private const int AudclntShareModeShared = 0;
    private const int AudclntStreamFlagsLoopback = 0x00020000;
    private const int AudclntStreamFlagsEventCallback = 0x00040000;
    private const int AudclntStreamFlagsAutoConvertPcm = unchecked((int)0x80000000);
    private const int AudclntStreamFlagsSrcDefaultQuality = 0x08000000;

    private readonly object _gate = new();
    private IAudioClient? _client;
    private IAudioCaptureClient? _capture;
    private CompletionHandler? _handler;
    private long _frames;
    private int _sampleRate = 48000;
    private int _processId;

    /// <summary>Elapsed audio time the process has rendered since the capture started.</summary>
    public TimeSpan Consumed
    {
        get
        {
            lock (_gate)
            {
                return TimeSpan.FromSeconds(_frames / (double)_sampleRate);
            }
        }
    }

    /// <summary>True when frames could be read recently (the player is rendering audio).</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Starts capturing a process; safe to call repeatedly with the same id.</summary>
    public bool TryStart(int processId)
    {
        lock (_gate)
        {
            if (_client is not null && _processId == processId)
            {
                DrainLocked();
                return true;
            }

            DisposeLocked();
            _processId = processId;
            try
            {
                var parameters = BuildActivationParams(processId);
                var handler = new CompletionHandler();
                _handler = handler;
                Activate(VirtualDeviceProcessLoopback, typeof(IAudioClient).GUID, parameters, handler, out _);
                if (!handler.Wait(3000) || handler.Client is null)
                {
                    IsAvailable = false;
                    return false;
                }

                _client = handler.Client;
                var format = CreatePcmFormat(48000, 2, 16);
                var flags = AudclntStreamFlagsLoopback | AudclntStreamFlagsEventCallback
                    | AudclntStreamFlagsAutoConvertPcm | AudclntStreamFlagsSrcDefaultQuality;
                if (_client.Initialize(AudclntShareModeShared, flags, 2_000_000, 0, format, IntPtr.Zero) != 0)
                {
                    DisposeLocked();
                    return false;
                }

                var captureIid = typeof(IAudioCaptureClient).GUID;
                if (_client.GetService(ref captureIid, out var capturePtr) != 0 || capturePtr == IntPtr.Zero)
                {
                    DisposeLocked();
                    return false;
                }

                _capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(capturePtr);
                var eventHandle = CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
                _client.SetEventHandle(eventHandle);
                if (_client.Start() != 0)
                {
                    DisposeLocked();
                    return false;
                }

                _frames = 0;
                IsAvailable = true;
                return true;
            }
            catch (Exception)
            {
                DisposeLocked();
                IsAvailable = false;
                return false;
            }
        }
    }

    /// <summary>Reads everything the process rendered since the previous call.</summary>
    public long DrainFrames()
    {
        lock (_gate)
        {
            return DrainLocked();
        }
    }

    private long DrainLocked()
    {
        if (_capture is null)
        {
            return 0;
        }

        long total = 0;
        try
        {
            while (true)
            {
                if (_capture.GetNextPacketSize(out var pending) != 0 || pending == 0)
                {
                    break;
                }

                if (_capture.GetBuffer(out _, out var frames, out _, out _, out _) != 0 || frames == 0)
                {
                    break;
                }

                total += frames;
                _capture.ReleaseBuffer(frames);
            }
        }
        catch (Exception)
        {
            IsAvailable = false;
        }

        _frames += total;
        return total;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            DisposeLocked();
        }
    }

    private void DisposeLocked()
    {
        try { _client?.Stop(); } catch { }
        if (_capture is not null) Marshal.ReleaseComObject(_capture);
        if (_client is not null) Marshal.ReleaseComObject(_client);
        _capture = null;
        _client = null;
        _handler = null;
        IsAvailable = false;
    }

    private static IntPtr BuildActivationParams(int processId)
    {
        var payload = Marshal.AllocHGlobal(12);
        Marshal.WriteInt32(payload, 0, 1);
        Marshal.WriteInt32(payload, 4, processId);
        Marshal.WriteInt32(payload, 8, 0);

        var propVariant = Marshal.AllocHGlobal(24);
        for (var i = 0; i < 24; i++)
        {
            Marshal.WriteByte(propVariant, i, 0);
        }

        Marshal.WriteInt16(propVariant, 0, 65);
        Marshal.WriteInt32(propVariant, 8, 12);
        Marshal.WriteIntPtr(propVariant, 16, payload);
        return propVariant;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    private static IntPtr CreatePcmFormat(int sampleRate, int channels, int bits)
    {
        var format = new WaveFormatEx
        {
            FormatTag = 1,
            Channels = (ushort)channels,
            SamplesPerSecond = (uint)sampleRate,
            AverageBytesPerSecond = (uint)(sampleRate * channels * bits / 8),
            BlockAlign = (ushort)(channels * bits / 8),
            BitsPerSample = (ushort)bits,
        };
        var block = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        Marshal.StructureToPtr(format, block, false);
        return block;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams,
        IAudioInterfaceCompletionHandler completionHandler,
        out IAudioInterfaceAsyncOperation activationOperation);

    private static void Activate(string path, Guid riid, IntPtr parameters,
        IAudioInterfaceCompletionHandler handler, out IAudioInterfaceAsyncOperation operation)
        => ActivateAudioInterfaceAsync(path, riid, parameters, handler, out operation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEventW(IntPtr attributes, bool manualReset, bool initialState, IntPtr name);

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
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
    private interface IAudioCaptureClient
    {
        int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        int ReleaseBuffer(uint frames);
        int GetNextPacketSize(out uint frames);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioInterfaceAsyncOperation
    {
        int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    private sealed class CompletionHandler : IAudioInterfaceCompletionHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public IAudioClient? Client { get; private set; }

        public void ActivateCompleted(IAudioInterfaceAsyncOperation operation)
        {
            var hr = operation.GetActivateResult(out var activateResult, out var instance);
            if (hr == 0 && activateResult == 0 && instance is IAudioClient client)
            {
                Client = client;
            }

            _done.Set();
        }

        public bool Wait(int milliseconds) => _done.Wait(milliseconds);
    }
}
