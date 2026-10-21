using System.Diagnostics;
using System.Runtime.InteropServices;

// Read-only probe: finds KuGou's playback position in its own memory and prints it live.
// No injection, no writes — OpenProcess(PROCESS_VM_READ) + ReadProcessMemory only.
var seed = KuGou.ReadIniPositionMs();
var (pid, addresses) = KuGou.ResolveAny(seed);
var address = addresses.Count > 0 ? addresses[0] : 0;
if (pid == 0 || addresses.Count == 0) { Console.WriteLine("no address resolved"); return; }
Console.WriteLine($"pid={pid} ini={seed}ms address=0x{address:X}");
for (var i = 0; i < 120; i++)
{
    var value = KuGou.Read(pid, address);
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} pos={(value < 0 ? "?" : $"{value / 1000.0:0.000}s")}");
    Thread.Sleep(250);
}

internal static class KuGou
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll")] private static extern int VirtualQueryEx(IntPtr h, IntPtr addr, out Mbi mbi, int len);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    private struct Mbi { public IntPtr Base; public IntPtr AllocationBase; public uint AllocationProtect; public IntPtr RegionSize; public uint State; public uint Protect; public uint Type; }

    private const uint VmRead = 0x0010, QueryInfo = 0x0400, MemCommit = 0x1000;
    private const uint PageReadWrite = 0x04, PageWriteCopy = 0x08, PageExecuteReadWrite = 0x40, PageExecuteWriteCopy = 0x80, PageReadOnly = 0x02;

    public static int FindMainProcess()
    {
        var best = 0; long bestWs = 0;
        foreach (var process in Process.GetProcessesByName("KuGou"))
        {
            if (process.WorkingSet64 > bestWs) { bestWs = process.WorkingSet64; best = process.Id; }
        }

        return best;
    }

    public static long ReadIniPositionMs()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KuGou8", "KuGou.ini");
        if (!File.Exists(path)) return 0;
        foreach (var line in File.ReadAllLines(path, System.Text.Encoding.Unicode))
        {
            if (line.StartsWith("LastPlayingSongPos=", StringComparison.Ordinal)
                && long.TryParse(line["LastPlayingSongPos=".Length..], out var ms)) return ms;
        }

        return 0;
    }

    /// <summary>Scans every KuGou process and keeps the best validated candidate.</summary>
    /// <remarks>
    /// A single "advanced by the sleep interval" check is not enough: timers and frame counters satisfy it
    /// and then freeze (measured 2026-10-21). The rule is three consecutive advancing samples, with the
    /// adjacency of an identical twin and the largest value as tie-breaks.
    /// </remarks>
    public static (int Pid, List<long> Addresses) ResolveAny(long seedMs)
    {
        var bestPid = 0; var best = new List<long>();
        foreach (var process in Process.GetProcessesByName("KuGou"))
        {
            if (process.WorkingSet64 < 20L * 1024 * 1024) continue;
            var survivors = Resolve(process.Id, seedMs);
            if (survivors.Count == 0) { Console.WriteLine($"  pid={process.Id}: no candidate"); continue; }
            Console.WriteLine($"  pid={process.Id}: {survivors.Count} survivor(s) -> " +
                string.Join(", ", survivors.ConvertAll(a => $"0x{a:X}")));
            if (survivors.Count > best.Count) { best = survivors; bestPid = process.Id; }
        }

        return (bestPid, best);
    }

    public static List<long> Resolve(int pid, long seedMs)
    {
        var lo = Math.Max(3000, seedMs - 60000);
        var hi = seedMs + 60000;
        var first = Snapshot(pid, lo, hi);
        if (first.Count == 0) return new List<long>();
        var values = new Dictionary<long, int>(first.Count);
        foreach (var (address, value) in first) values[address] = value;

        for (var round = 0; round < 3; round++)
        {
            var clock = Stopwatch.StartNew();
            Thread.Sleep(1000);
            var elapsed = clock.ElapsedMilliseconds;
            var next = new Dictionary<long, int>(values.Count);
            foreach (var (address, previous) in values)
            {
                var now = Read(pid, address);
                if (now < 0) continue;
                // The counter may update in 250 / 500 / 1000 ms steps, so compare against the REAL
                // elapsed time and allow one step of slop (measured: a fixed 1200 ms assumption failed
                // on every process as soon as the scheduler jittered).
                if (Math.Abs(now - previous - elapsed) <= elapsed / 2) next[address] = now;
            }

            values = next;
            Console.WriteLine($"    round {round + 1} (elapsed={elapsed}ms): {values.Count} still advancing");
            if (values.Count == 0) return new List<long>();
        }

        // Survivors ranked: twins first, then larger values (the position is the biggest monotone counter).
        var ranked = new List<long>();
        foreach (var (address, value) in values)
        {
            var twin = values.TryGetValue(address + 4, out var neighbour) && neighbour == value;
            ranked.Add(twin ? address | (1L << 62) : address);
        }

        ranked.Sort((a, b) => (b >> 62).CompareTo(a >> 62));
        return ranked.ConvertAll(a => a & ~(1L << 62));
    }

    public static List<(long Address, int Value)> Snapshot(int pid, long lo, long hi)
    {
        var hits = new List<(long, int)>();
        var handle = OpenProcess(VmRead | QueryInfo, false, pid);
        if (handle == IntPtr.Zero) return hits;
        try
        {
            var buffer = new byte[1 << 20];
            var address = IntPtr.Zero;
            while (VirtualQueryEx(handle, address, out var mbi, Marshal.SizeOf<Mbi>()) != 0)
            {
                var baseAddress = mbi.Base.ToInt64();
                var size = mbi.RegionSize.ToInt64();
                var readable = mbi.State == MemCommit && (mbi.Protect is PageReadWrite or PageWriteCopy or PageExecuteReadWrite or PageExecuteWriteCopy or PageReadOnly);
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
                            if (value >= lo && value <= hi) hits.Add((baseAddress + offset + i, value));
                        }

                        if (hits.Count > 4_000_000) break;
                    }
                }

                var next = baseAddress + size;
                if (next <= baseAddress) break;
                address = new IntPtr(next);
            }
        }
        finally { CloseHandle(handle); }

        return hits;
    }

    public static int Read(int pid, long address)
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
}
