using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Optional, experimental read-only playback position for KuGou.
/// </summary>
/// <remarks>
/// <para>KuGou exposes no SMTC session and its accessibility clock only has one-second resolution, which is
/// the floor of the lyric accuracy. Its own UI keeps a millisecond position counter, so this reads that
/// counter with <c>ReadProcessMemory</c>: no injection, no writes, read-only.</para>
/// <para>Off by default (<c>FLUIDBAR_KUGOU_MEM_POSITION=1</c> enables it). It always degrades to the
/// accessibility timeline: a stale address, a restarted player or a protected process simply returns the
/// candidate unchanged.</para>
/// </remarks>
internal static class KugouMemoryPosition
{
    private static readonly object Gate = new();
    private static int _pid;
    private static long _address;
    private static int _lastValue = -1;
    private static int _frozenReads;
    private static bool _resolving;

    /// <summary>Whether the experimental reader is enabled for this process.</summary>
    public static bool Enabled { get; } =
        Environment.GetEnvironmentVariable("FLUIDBAR_KUGOU_MEM_POSITION") == "1";

    /// <summary>Applies the memory position when it is available and trustworthy.</summary>
    /// <param name="candidate">Fallback candidate (carries the accessibility/ini position).</param>
    /// <param name="now">Current UTC time.</param>
    public static WindowMediaCandidate Apply(WindowMediaCandidate candidate, DateTimeOffset now)
    {
        if (!Enabled || candidate.ProcessId <= 0 || !MediaWindowFallback.IsKugouProcessName(candidate.ProcessName))
        {
            return candidate;
        }

        var expected = candidate.Position.TotalMilliseconds;
        lock (Gate)
        {
            if (_pid == candidate.ProcessId && _address != 0)
            {
                var value = Read(candidate.ProcessId, _address);
                if (value >= 0 && IsPlausible(value, expected))
                {
                    if (value == _lastValue)
                    {
                        if (++_frozenReads > 5)
                        {
                            _address = 0; // stale address: forget it and resolve again
                        }
                    }
                    else
                    {
                        _frozenReads = 0;
                        _lastValue = value;
                        MediaDiagnostics.Log($"position-source=mem pid={candidate.ProcessId} addr=0x{_address:X} value={value}ms expected={expected:0}ms");
                        return candidate with
                        {
                            Position = TimeSpan.FromMilliseconds(value),
                            PositionSampledAtUtc = now,
                            HasTimeline = true,
                        };
                    }
                }
                else
                {
                    _address = 0;
                }
            }

            if (_address == 0 && !_resolving && expected > 1000)
            {
                _resolving = true;
                var pid = candidate.ProcessId;
                _ = Task.Run(() => Track(pid));
            }
        }

        return candidate;
    }

    private static bool IsPlausible(int valueMs, double expectedMs)
        => valueMs >= 0
           && (expectedMs <= 0 || Math.Abs(valueMs - expectedMs) <= 300_000);

    /// <summary>
    /// Finds the position counter without a seed: it is the address whose value advances by exactly the
    /// real elapsed time, four rounds in a row. The seeded scan failed because the accessibility position
    /// can be tens of seconds off and the window then contains only unrelated numbers
    /// (measured 2026-10-21: 5 of 6 attempts ended with zero survivors).
    /// </summary>
    private static void Track(int pid)
    {
        try
        {
            var values = Snapshot(pid, 10_000, 900_000);
            for (var round = 0; round < 5 && values.Count > 0; round++)
            {
                var clock = Stopwatch.StartNew();
                Thread.Sleep(1000);
                var elapsed = clock.ElapsedMilliseconds;
                var next = new Dictionary<long, int>(values.Count);
                foreach (var (address, previous) in values)
                {
                    var now = Read(pid, address);
                    if (now < 0) continue;
                    // Strictly monotone and inside a plausible track length: random buffers jump around
                    // and passed the loose delta test (measured 22:58: the picked address returned 359s,
                    // 294s, 9s, 343s in a row).
                    if (now > previous && now - previous <= elapsed + 500 && now <= 1_800_000) next[address] = now;
                }

                values = next;
                MediaDiagnostics.Log($"position-source=mem-track pid={pid} round={round + 1} survivors={values.Count}");
            }

            long best = 0;
            var bestValue = int.MinValue;
            foreach (var (address, value) in values)
            {
                var twin = values.TryGetValue(address + 4, out var neighbour) && neighbour == value;
                if ((twin && best == 0) || value > bestValue)
                {
                    best = address;
                    bestValue = value;
                }
            }

            MediaDiagnostics.Log($"position-source=mem-resolve pid={pid} addr=0x{best:X} survivors={values.Count} value={bestValue}ms");
            lock (Gate)
            {
                _pid = pid;
                _address = best;
                _lastValue = bestValue;
                _frozenReads = 0;
            }
        }
        catch (Exception)
        {
            lock (Gate) { _address = 0; }
        }
        finally
        {
            lock (Gate) { _resolving = false; }
        }
    }

    private static Dictionary<long, int> Snapshot(int pid, long lo, long hi)
    {
        var values = new Dictionary<long, int>();
        var handle = OpenProcess(VmRead | QueryInfo, false, pid);
        if (handle == IntPtr.Zero) return values;
        try
        {
            var buffer = new byte[1 << 20];
            var address = IntPtr.Zero;
            while (VirtualQueryEx(handle, address, out var mbi, Marshal.SizeOf<Mbi>()) != 0)
            {
                var baseAddress = mbi.Base.ToInt64();
                var size = mbi.RegionSize.ToInt64();
                var readable = mbi.State == MemCommit
                    && (mbi.Protect is PageReadWrite or PageWriteCopy or PageExecuteReadWrite or PageExecuteWriteCopy or PageReadOnly);
                if (readable && size > 0 && size < (1L << 32))
                {
                    for (long offset = 0; offset < size; offset += buffer.Length)
                    {
                        var toRead = (int)Math.Min(buffer.Length, size - offset);
                        if (toRead < 4) break;
                        if (!ReadProcessMemory(handle, new IntPtr(baseAddress + offset), buffer, toRead, out var read)) continue;
                        var count = (int)read;
                        for (var i = 0; i + 4 <= count; i += 4)
                        {
                            var value = BitConverter.ToInt32(buffer, i);
                            if (value >= lo && value <= hi) values[baseAddress + offset + i] = value;
                        }

                        if (values.Count > 2_000_000) break;
                    }
                }

                var next = baseAddress + size;
                if (next <= baseAddress) break;
                address = new IntPtr(next);
            }
        }
        finally { CloseHandle(handle); }

        return values;
    }

    private static int Read(int pid, long address)
    {
        var handle = OpenProcess(VmRead | QueryInfo, false, pid);
        if (handle == IntPtr.Zero) return -1;
        try
        {
            var buffer = new byte[4];
            return ReadProcessMemory(handle, new IntPtr(address), buffer, 4, out _) ? BitConverter.ToInt32(buffer, 0) : -1;
        }
        finally { CloseHandle(handle); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mbi
    {
        public IntPtr Base;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    private const uint VmRead = 0x0010, QueryInfo = 0x0400, MemCommit = 0x1000;
    private const uint PageReadWrite = 0x04, PageWriteCopy = 0x08, PageExecuteReadWrite = 0x40, PageExecuteWriteCopy = 0x80, PageReadOnly = 0x02;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, int size, out IntPtr read);

    [DllImport("kernel32.dll")]
    private static extern int VirtualQueryEx(IntPtr handle, IntPtr address, out Mbi mbi, int length);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
