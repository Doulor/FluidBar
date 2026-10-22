using System.Diagnostics;

namespace FluidBar.Platform.Windows.Media;

/// <summary>A clock reading published by <see cref="KugouClockReader"/>.</summary>
/// <param name="Title">Track the reading belongs to (a new track must never inherit the old position).</param>
/// <param name="Position">Position to publish (the player's second plus the time since it was shown).</param>
/// <param name="Shown">The whole second the player is displaying.</param>
/// <param name="Duration">Track length the player displays, or zero.</param>
/// <param name="Flipped">True when the text flipped to a new second on this reading.</param>
/// <param name="AgeMs">How long ago the reading was taken.</param>
internal readonly record struct KugouClockSample(
    string Title,
    TimeSpan Position,
    TimeSpan Shown,
    TimeSpan Duration,
    bool Flipped,
    long AgeMs);

/// <summary>
/// Reads KuGou's "mm:ss / mm:ss" clock on its own thread, at 250 ms, without ever blocking the media poll.
/// </summary>
/// <remarks>
/// <para>The media fallback poll used to call the accessibility tree inline. When the cached element went
/// stale that call walked ~900 nodes (measured ~2.5 s), which stretched the 250 ms poll into a multi-second
/// one and starved the clock — the island froze for seconds and then jumped. Measured 2026-10-22: a fresh
/// session reads 24 of 24 times at 250 ms, while the poll path managed roughly one read every 10 s.</para>
/// <para>This thread only uses the cached element or its child-index path (a handful of COM calls). A full
/// rebuild happens at most every two seconds, on this thread, so the poll never pays for it.</para>
/// </remarks>
internal sealed class KugouClockReader : IDisposable
{
    private const int TickMs = 250;
    private const int RebuildEveryTicks = 8;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly object _gate = new();

    private volatile int _processId;
    private volatile string _title = string.Empty;
    private int _disposed;

    private KugouClockSample _sample;
    private long _sampledAtMs;

    public KugouClockReader()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "fluidbar-kugou-clock",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    /// <summary>Points the reader at the player process showing <paramref name="title"/>.</summary>
    public void SetTarget(int processId, string? title)
    {
        _processId = processId;
        _title = title ?? string.Empty;
    }

    /// <summary>
    /// Latest reading for the track the reader is pointed at, or false when it is stale or belongs to the
    /// previous track.
    /// </summary>
    /// <remarks>
    /// Without the title check a reading taken from the out-going song (say 3:51 of 4:31) was applied to the
    /// new one, so the island showed the new title sitting at the old position and the old length until the
    /// next reading — "切歌后进度条没重置，还是上一首的时长".
    /// </remarks>
    public bool TryRead(out KugouClockSample sample)
    {
        var title = _title;
        lock (_gate)
        {
            sample = _sample with { AgeMs = Environment.TickCount64 - _sampledAtMs };
        }

        return _sampledAtMs > 0
               && sample.AgeMs < 3000
               && string.Equals(sample.Title, title, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Set();
        _thread.Join(1500);
        _stop.Dispose();
    }

    private void Loop()
    {
        PlayerAccessibilitySession? session = null;
        var sessionPid = 0;
        var tick = 0;
        try
        {
            while (!_stop.Wait(TickMs))
            {
                var pid = _processId;
                if (pid <= 0)
                {
                    if (session is not null)
                    {
                        session.Dispose();
                        session = null;
                        sessionPid = 0;
                    }

                    continue;
                }

                if (session is null || sessionPid != pid)
                {
                    session?.Dispose();
                    session = PlayerAccessibilitySession.TryCreate(pid);
                    sessionPid = session is null ? 0 : pid;
                }

                if (session is null)
                {
                    continue;
                }

                var ok = session.TryReadTimeline(out var timeline, null, allowFullWalk: false);
                if (!ok && tick % RebuildEveryTicks == 0)
                {
                    // The element cache is useless (track change / tree rebuild): pay for one walk here,
                    // never on the media poll thread.
                    ok = session.TryReadTimeline(out timeline, null, allowFullWalk: true);
                }

                tick++;
                if (!ok)
                {
                    continue;
                }

                lock (_gate)
                {
                    _sample = new KugouClockSample(_title, timeline.Position, timeline.ShownPosition,
                        timeline.Duration, timeline.Changed, 0);
                    _sampledAtMs = Environment.TickCount64;
                }
            }
        }
        finally
        {
            session?.Dispose();
        }
    }
}
