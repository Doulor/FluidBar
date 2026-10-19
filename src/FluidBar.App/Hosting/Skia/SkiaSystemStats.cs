using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FluidBar.App.State;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Cheap system samplers for the widgets page: CPU busy time from <c>GetSystemTimes</c> and physical
/// memory use from <c>GlobalMemoryStatusEx</c>. Both are ~µs P/Invoke calls with no threads, timers or
/// WMI, so the UI thread can sample them while building an `IslandContent` snapshot.
/// <para>
/// CPU needs two samples: the first call only records the baseline, later calls return the busy share
/// of the elapsed kernel+user time. The last value is cached and reused until the next baseline is at
/// least <see cref="MinCpuInterval"/> old, so a burst of snapshots cannot report 0 %.
/// </para>
/// </summary>
internal sealed class SkiaSystemStats
{
    /// <summary>Minimum spacing between two CPU baselines (500 ms).</summary>
    private static readonly TimeSpan MinCpuInterval = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _time;
    private ulong _idle;
    private ulong _kernel;
    private ulong _user;
    private DateTimeOffset _sampledAt;
    private int? _cpuPercent;
    private bool _primed;

    /// <summary>Creates the sampler over an injected clock (tests).</summary>
    /// <param name="timeProvider">Clock used to space CPU baselines.</param>
    public SkiaSystemStats(TimeProvider timeProvider)
        => _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>Records the CPU baseline so the first snapshot after start has a delta to compare against.</summary>
    public void Prime()
    {
        if (TryReadCpu(out var idle, out var kernel, out var user))
        {
            _idle = idle;
            _kernel = kernel;
            _user = user;
            _sampledAt = _time.GetUtcNow();
            _primed = true;
        }
    }

    /// <summary>CPU busy percentage 0..100, or null until enough time has elapsed for a delta.</summary>
    public int? CpuPercent()
    {
        var now = _time.GetUtcNow();
        if (!_primed)
        {
            Prime();
            return null;
        }

        if (now - _sampledAt >= MinCpuInterval && TryReadCpu(out var idle, out var kernel, out var user))
        {
            var idleDelta = idle - _idle;
            var totalDelta = (kernel - _kernel) + (user - _user);
            _idle = idle;
            _kernel = kernel;
            _user = user;
            _sampledAt = now;
            if (totalDelta > 0)
            {
                var busy = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
                _cpuPercent = (int)Math.Clamp(Math.Round(busy * 100.0 / totalDelta), 0, 100);
            }
        }

        return _cpuPercent;
    }

    /// <summary>Physical memory in use 0..100, or null when the API fails.</summary>
    public int? MemoryPercent()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            return null;
        }

        var used = status.TotalPhys - status.AvailPhys;
        return (int)Math.Clamp(Math.Round(used * 100.0 / status.TotalPhys), 0, 100);
    }

    /// <summary>Builds the widgets stat rows from the two samplers plus the battery snapshot.</summary>
    /// <param name="batteryPercent">Battery level, or null when the device has no battery.</param>
    /// <param name="batteryLabel">Localized label for the battery row.</param>
    public ImmutableArray<WidgetStatData> Build(int? batteryPercent, string batteryLabel)
    {
        var builder = ImmutableArray.CreateBuilder<WidgetStatData>(3);
        if (CpuPercent() is { } cpu)
        {
            builder.Add(new WidgetStatData("CPU", cpu, WidgetStatKind.Accent));
        }

        if (MemoryPercent() is { } memory)
        {
            builder.Add(new WidgetStatData("RAM", memory, WidgetStatKind.Info));
        }

        if (batteryPercent is { } battery)
        {
            builder.Add(new WidgetStatData(batteryLabel, battery, WidgetStatKind.Success));
        }

        return builder.ToImmutable();
    }

    private static bool TryReadCpu(out ulong idle, out ulong kernel, out ulong user)
    {
        idle = 0;
        kernel = 0;
        user = 0;
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return false;
        }

        idle = ((ulong)idleTime.High << 32) | idleTime.Low;
        kernel = ((ulong)kernelTime.High << 32) | kernelTime.Low;
        user = ((ulong)userTime.High << 32) | userTime.Low;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}
