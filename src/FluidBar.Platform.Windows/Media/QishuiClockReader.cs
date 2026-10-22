using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Reads Soda Music's playback clock on its own thread, at 250 ms, without ever blocking the media poll.
/// </summary>
/// <remarks>
/// <para>Soda's window title never changes, so the caption trick behind <see cref="KugouWindowTitle"/>
/// does not apply. What the player does expose is a UIA text node with its own clock
/// (<c>mm:ss / mm:ss</c>) inside the transport row — the same shape KuGou shows, just under UIA instead
/// of MSAA (the Electron renderer publishes no MSAA names, measured on 3.2.1).</para>
/// <para>The full UIA tree of the Chromium renderer is huge and built lazily across the process boundary
/// (first cold walk measured at 33 s warm-up, minutes when materialising every descendant), so this
/// reader walks once to locate the clock element, then only re-reads that single element's name per
/// tick. A failed or non-matching read invalidates the element; re-locating is rate-limited (10 s) so a
/// closed player cannot turn into a walk loop.</para>
/// </remarks>
internal sealed class QishuiClockReader : IDisposable
{
    private const int TickMs = 250;

    /// <summary>Minimum delay between full tree walks (each one can cost seconds on a cold renderer).</summary>
    private const int RelocateIntervalMs = 10_000;

    /// <summary>Node budget for one walk (the clock sits shallow; the budget only bounds a pathological tree).</summary>
    private const int MaxWalkNodes = 4000;

    private static readonly Regex ClockText = new(
        @"^\s*(?<position>\d{1,3}:\d{2})\s*/\s*(?<duration>\d{1,3}:\d{2})\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly object _gate = new();

    private volatile int _processId;
    private volatile string _title = string.Empty;
    private int _disposed;

    private KugouClockSample _sample;
    private long _sampledAtMs;

    public QishuiClockReader()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "fluidbar-qishui-clock",
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
    /// Same guard as <see cref="KugouClockReader.TryRead"/>: a reading taken from the out-going song must
    /// never be applied to the new one.
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
        AutomationElement? clock = null;
        var clockName = string.Empty;
        var relocateAtMs = 0L;
        try
        {
            while (!_stop.Wait(TickMs))
            {
                var pid = _processId;
                if (pid <= 0)
                {
                    clock = null;
                    continue;
                }

                var nowMs = Environment.TickCount64;
                if (clock is not null && TrySample(clock, ref clockName))
                {
                    continue;
                }

                clock = null;
                if (nowMs - relocateAtMs < RelocateIntervalMs)
                {
                    continue;
                }

                relocateAtMs = nowMs;
                clock = Locate(pid);
            }
        }
        catch (Exception)
        {
            // A background reader must never take the process down; the poll simply keeps publishing
            // without a timeline until the next locate succeeds.
        }
    }

    /// <summary>Reads the cached clock element. Returns false when it went stale or stopped matching.</summary>
    private bool TrySample(AutomationElement clock, ref string clockName)
    {
        string? name;
        try
        {
            name = clock.Current.Name;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }

        if (name is null || string.Equals(name, clockName, StringComparison.Ordinal))
        {
            // Unchanged text: still valid, but there is nothing new to publish.
            return true;
        }

        var match = ClockText.Match(name);
        if (!match.Success)
        {
            // The transport row was rebuilt (the element now answers with different content).
            return false;
        }

        clockName = name;
        if (!PlayerAccessibilitySession.TryParseClock(match.Groups["position"].Value, out var position)
            || !PlayerAccessibilitySession.TryParseClock(match.Groups["duration"].Value, out var duration)
            || duration <= TimeSpan.Zero)
        {
            return true;
        }

        var title = _title;
        var flipped = position != _sample.Shown;
        lock (_gate)
        {
            // Whole-second text: the real position is the shown value plus the time since it appeared,
            // same reasoning as the KuGou clock (no lead, no extrapolation past the player).
            _sample = new KugouClockSample(title, position, position, duration, flipped, 0);
            _sampledAtMs = Environment.TickCount64;
        }

        return true;
    }

    /// <summary>Finds the clock element of the player's main window, or null.</summary>
    private AutomationElement? Locate(int processId)
    {
        var hwnd = FindMainWindow(processId);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        AutomationElement root;
        try
        {
            root = AutomationElement.FromHandle(hwnd);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }

        var walker = TreeWalker.RawViewWalker;
        var stack = new Stack<AutomationElement>();
        stack.Push(root);
        var nodes = 0;
        while (stack.Count > 0 && nodes < MaxWalkNodes)
        {
            if (_stop.IsSet || _processId != processId)
            {
                return null;
            }

            var element = stack.Pop();
            nodes++;
            string? name;
            try
            {
                name = element.Current.Name;
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                continue;
            }

            if (name is not null && ClockText.IsMatch(name))
            {
                return element;
            }

            var child = SafeChild(element);
            while (child is not null)
            {
                stack.Push(child);
                child = SafeSibling(child);
            }
        }

        return null;
    }

    private static AutomationElement? SafeChild(AutomationElement element)
    {
        try
        {
            return TreeWalker.RawViewWalker.GetFirstChild(element);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static AutomationElement? SafeSibling(AutomationElement element)
    {
        try
        {
            return TreeWalker.RawViewWalker.GetNextSibling(element);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>The visible top-level window titled "汽水音乐" of the process, or zero.</summary>
    private static IntPtr FindMainWindow(int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != processId || !IsWindowVisible(hwnd))
            {
                return true;
            }

            var length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return true;
            }

            var buffer = new System.Text.StringBuilder(length + 2);
            if (GetWindowText(hwnd, buffer, buffer.Capacity) > 0 && buffer.ToString() == "汽水音乐")
            {
                found = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);
}
