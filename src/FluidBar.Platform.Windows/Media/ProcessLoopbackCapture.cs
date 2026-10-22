using System.Runtime.InteropServices;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Counts the audio frames a single process renders, producing an exact playback clock.
/// </summary>
/// <remarks>
/// <para>Uses the Windows 10 2004+ per-process loopback device (<c>VAD\\Process_Loopback</c>) through
/// <c>ActivateAudioInterfaceAsync</c>. Frames are counted, never decoded: elapsed audio time is
/// <c>frames / sampleRate</c> and it advances only while the player is actually rendering.</para>
/// <para>Measured on 2026-10-22 against KuGou: 4 s of wall time produced 3.95 s of audio (ratio 0.987);
/// the device-wide loopback produced 0.992 for comparison. This removes the 30-second granularity of
/// <c>KuGou.ini</c>, which was the accuracy ceiling of the previous design.</para>
/// <para>The capture must run on an MTA thread: the host application's UI thread is STA and the capture
/// client silently yields no frames there (measured 2026-10-22: estimate froze, island drifted 27 s).</para>
/// </remarks>
internal sealed class ProcessLoopbackCapture : IDisposable
{
    private readonly Thread _thread;
    private long _frames;
    private long _lastFrameTick;
    private int _sampleRate = 48000;
    private int _targetPid;
    private volatile bool _disposed;

    public ProcessLoopbackCapture()
    {
        _thread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "fluidbar-process-loopback",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    /// <summary>Elapsed audio time the process has rendered since it became the capture target.</summary>
    public TimeSpan Consumed => TimeSpan.FromSeconds(Interlocked.Read(ref _frames) / (double)_sampleRate);

    /// <summary>True when frames arrived within the last 1.5 s (the player is rendering audio).</summary>
    public bool IsAvailable => Environment.TickCount64 - Interlocked.Read(ref _lastFrameTick) < 1500;

    /// <summary>Points the capture at a process. Safe to call at any rate; only changes restart counting.</summary>
    /// <remarks>
    /// False means the capture is currently producing frames for a DIFFERENT process: two players
    /// polling in turns used to steal the target from each other, resetting the frame count on every
    /// switch. The loser keeps its previous timeline instead.
    /// </remarks>
    public bool TryStart(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        // KuGou exposes several processes and the reported id changes between polls; switching targets
        // constantly restarts counting, so keep whichever target is currently producing frames.
        if (IsAvailable)
        {
            return Volatile.Read(ref _targetPid) == processId;
        }

        if (Interlocked.CompareExchange(ref _targetPid, processId, processId) != processId)
        {
            Volatile.Write(ref _targetPid, processId);
            Interlocked.Exchange(ref _frames, 0);
        }

        return IsAvailable;
    }

    public void Dispose()
    {
        _disposed = true;
        _thread.Join(1000);
    }

    private void CaptureLoop()
    {
        CaptureSession? session = null;
        var activePid = 0;
        try
        {
            while (!_disposed)
            {
                var pid = Volatile.Read(ref _targetPid);
                if (pid != activePid)
                {
                    session?.Dispose();
                    session = null;
                    activePid = pid;
                    if (pid > 0)
                    {
                        session = CaptureSession.TryCreate(pid);
                    }
                }

                if (session is null)
                {
                    Thread.Sleep(300); // bg-thread-ok: dedicated MTA capture thread, never the UI thread
                    continue;
                }

                var frames = session.Drain();
                if (frames > 0)
                {
                    Interlocked.Add(ref _frames, frames);
                    Interlocked.Exchange(ref _lastFrameTick, Environment.TickCount64);
                }

                Thread.Sleep(10); // bg-thread-ok: dedicated MTA capture thread, never the UI thread
            }
        }
        finally
        {
            session?.Dispose();
        }
    }

    private sealed class CaptureSession : IDisposable
    {
        private const string VirtualDeviceProcessLoopback = "VAD\\Process_Loopback";
        private const int AudclntShareModeShared = 0;
        private const int AudclntStreamFlagsLoopback = 0x00020000;
        private const int AudclntStreamFlagsAutoConvertPcm = unchecked((int)0x80000000);
        private const int AudclntStreamFlagsSrcDefaultQuality = 0x08000000;

        private IAudioClient? _client;
        private IAudioCaptureClient? _capture;

        public static CaptureSession? TryCreate(int processId)
        {
            try
            {
                var parameters = BuildActivationParams(processId);
                var handler = new CompletionHandler();
                Activate(VirtualDeviceProcessLoopback, typeof(IAudioClient).GUID, parameters, handler, out _);
                if (!handler.Wait(2000) || handler.Client is null)
                {
                    return null;
                }

                var format = CreatePcmFormat(48000, 2, 16);
                var flags = AudclntStreamFlagsLoopback | AudclntStreamFlagsAutoConvertPcm
                    | AudclntStreamFlagsSrcDefaultQuality;
                if (handler.Client.Initialize(AudclntShareModeShared, flags, 2_000_000, 0, format, IntPtr.Zero) != 0)
                {
                    return null;
                }

                var captureIid = typeof(IAudioCaptureClient).GUID;
                if (handler.Client.GetService(ref captureIid, out var capturePtr) != 0 || capturePtr == IntPtr.Zero)
                {
                    return null;
                }

                var session = new CaptureSession
                {
                    _client = handler.Client,
                    _capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(capturePtr),
                };
                if (session._client.Start() != 0)
                {
                    session.Dispose();
                    return null;
                }

                return session;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public long Drain()
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
                return total;
            }

            return total;
        }

        public void Dispose()
        {
            try { _client?.Stop(); } catch { }
            if (_capture is not null) Marshal.ReleaseComObject(_capture);
            if (_client is not null) Marshal.ReleaseComObject(_client);
            _capture = null;
            _client = null;
        }
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
