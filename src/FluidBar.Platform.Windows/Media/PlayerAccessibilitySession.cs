using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace FluidBar.Platform.Windows.Media;

/// <summary>Timeline a player exposes through its accessibility tree.</summary>
/// <param name="Position">Current playback position.</param>
/// <param name="Duration">Track length reported by the player.</param>
internal readonly record struct PlayerTimeline(TimeSpan Position, TimeSpan Duration)
{
    /// <summary>
    /// How old the reading already is. The player publishes its clock with one-second resolution, so a
    /// freshly seen "0:13" may have flipped anywhere inside the last poll window; anchoring it at the
    /// midpoint of that window removes the systematic "the lyric is always a bit late" bias.
    /// </summary>
    public TimeSpan Age { get; init; }
}

/// <summary>
/// Drives a player that registers no SMTC session through its MSAA (IAccessible) tree.
/// </summary>
/// <remarks>
/// <para>KuGou publishes neither an SMTC session nor a UIA provider (the UIA raw view has exactly one
/// node), yet it does implement MSAA: the DirectUI window exposes named push buttons
/// ("上一首" / "播放" / "暂停" / "下一首") with a "click" default action, plus a static text that reads
/// <c>mm:ss/mm:ss</c> with the live position and the track length.</para>
/// <para>Access path: <c>oleacc!AccessibleObjectFromWindow(hwnd, OBJID_CLIENT, IID_IAccessible)</c>.
/// The interface is declared here with its full vtable (IDispatch's four slots first) because the
/// DirectUI object only implements the raw vtable, not <c>IDispatch</c> — a dynamic/IDispatch-only
/// consumer cannot call it, which is why UIA and PowerShell's COM binding both come up empty.</para>
/// <para>Calling <c>accDoDefaultAction</c> needs no input desktop: it is a direct method call into the
/// player's UI thread, so it also works while the workstation is locked or the player window is
/// parked off-screen (KuGou's mini bar lives at -32000,-32000 in mini mode).</para>
/// </remarks>
internal sealed class PlayerAccessibilitySession : IDisposable
{
    private const uint ObjidClient = 0xFFFFFFFC;
    private const int RolePushButton = 43;
    private const int RoleStaticText = 41;
    private const int RoleGraphic = 40;
    private const int MaxDepth = 12;
    private const int MaxNodes = 900;
    private const int ReadRetryBackoffMs = 2500;

    // A player that does not expose a timeline (KuGou in mini mode) has no cached time element, so
    // every poll used to walk its whole MSAA tree (~900 nodes of cross-process calls) at the 250 ms
    // fallback cadence — measured at ~12% of one core. The transport buttons stay cached, so the walk
    // is only needed to (re)find the time text; once a second is plenty for an mm:ss label.
    private const int TimelineWalkIntervalMs = 1000;

    private long _lastTimelineWalkMs;

    /// <summary>Monotonic time of the previous successful time-text read (0 until the first one).</summary>
    private long _lastTimelineReadMs;

    /// <summary>Position parsed on that read, used to detect the one-second flip.</summary>
    private TimeSpan _lastTimelinePosition;
    private const string PreviousButtonName = "上一首";
    private const string NextButtonName = "下一首";
    private const string PlayButtonName = "播放";
    private const string PauseButtonName = "暂停";

    private static readonly Regex TimeTextPattern = new(
        @"^\s*(?<position>\d{1,3}:\d{2})\s*/\s*(?<duration>\d{1,3}:\d{2})\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly int _processId;
    private IAccessible? _root;
    private IAccessible? _timeText;
    private readonly List<ButtonCandidate> _previousButtons = [];
    private readonly List<ButtonCandidate> _nextButtons = [];
    private readonly List<ButtonCandidate> _playButtons = [];
    private readonly List<ButtonCandidate> _pauseButtons = [];
    private int _disposed;
    private int _lastWalkNodes;
    private long _readRetryAfterMs;

    /// <summary>Whether the last failed MSAA read is still in its short retry backoff.</summary>
    internal bool IsReadBackoffActive => Volatile.Read(ref _readRetryAfterMs) > Environment.TickCount64;

    /// <summary>Clears the backoff so an audible player is probed on the next fallback tick.</summary>
    public void ResetReadBackoff() => Volatile.Write(ref _readRetryAfterMs, 0);

    private void MarkReadFailure()
        => Volatile.Write(ref _readRetryAfterMs, Environment.TickCount64 + ReadRetryBackoffMs);

    private void MarkReadSuccess() => Volatile.Write(ref _readRetryAfterMs, 0);

    /// <summary>
    /// One transport control found in the tree. KuGou renders every button twice — a GRAPHIC wrapper
    /// (role 40) and a PUSHBUTTON (role 43) — and only the visible one reacts, so all candidates are
    /// kept and tried in visibility order.
    /// </summary>
    private readonly record struct ButtonCandidate(IAccessible Element, int Role);

    private PlayerAccessibilitySession(int processId, IAccessible root)
    {
        _processId = processId;
        _root = root;
    }

    /// <summary>
    /// Opens the accessibility tree of a player process, or null when it exposes none.
    /// </summary>
    /// <remarks>
    /// A player can own several DirectUI windows (mini bar, desktop lyrics, hidden main frame) and only
    /// the window that carries the transport row answers with a timeline. Candidates are probed in
    /// preference order (the window Windows reports as main first) and the first one that yields a
    /// position/duration pair wins, so a lyrics-only window is never mistaken for the player.
    /// </remarks>
    public static PlayerAccessibilitySession? TryCreate(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        foreach (var window in FindPlayerWindows(processId))
        {
            var root = TryGetAccessible(window);
            if (root is null)
            {
                continue;
            }

            var session = new PlayerAccessibilitySession(processId, root);
            if (session.TryReadTimeline(out _) || session.HasTransportControls)
            {
                return session;
            }

            session.Dispose();
        }

        return null;
    }

    /// <summary>Player process this session is bound to.</summary>
    public int ProcessId => _processId;

    /// <summary>Nodes visited by the last tree walk (diagnostics).</summary>
    internal int LastWalkNodes => Volatile.Read(ref _lastWalkNodes);

    /// <summary>
    /// Whether the tree exposes the transport row. Some player states (preview / VIP-locked tracks) hide
    /// the time text but keep the buttons, and transport must keep working there.
    /// </summary>
    internal bool HasTransportControls
    {
        get
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            lock (_gate)
            {
                if (_previousButtons.Count == 0 && _nextButtons.Count == 0)
                {
                    RebuildLocked();
                }

                return _previousButtons.Count > 0 && _nextButtons.Count > 0;
            }
        }
    }

    /// <summary>
    /// Reads the play/pause state from the visible transport glyph, or false when the tree cannot tell.
    /// </summary>
    public bool TryReadPlaybackState(out bool isPlaying)
    {
        isPlaying = false;
        if (Volatile.Read(ref _disposed) != 0 || IsReadBackoffActive)
        {
            return false;
        }

        lock (_gate)
        {
            if (_playButtons.Count == 0 && _pauseButtons.Count == 0)
            {
                RebuildLocked();
            }

            var resolved = AccessibilityPlaybackState.Resolve(HasVisible(_playButtons), HasVisible(_pauseButtons));
            if (resolved is null)
            {
                // A track change rebuilds the transport row: retry once with a fresh walk.
                RebuildLocked();
                resolved = AccessibilityPlaybackState.Resolve(HasVisible(_playButtons), HasVisible(_pauseButtons));
                if (resolved is null)
                {
                    MarkReadFailure();
                    return false;
                }
            }

            MarkReadSuccess();
            isPlaying = resolved.Value;
            return true;
        }
    }

    /// <summary>Reads the position/duration the player shows, or false when the tree moved on.</summary>
    public bool TryReadTimeline(out PlayerTimeline timeline)
    {
        timeline = default;
        if (Volatile.Read(ref _disposed) != 0 || IsReadBackoffActive)
        {
            return false;
        }

        lock (_gate)
        {
            // No cached time element: rate-limit the full tree walk instead of repeating it on every
            // poll tick (the buttons are already cached and keep working).
            var nowMs = Environment.TickCount64;
            if (_timeText is null && nowMs - _lastTimelineWalkMs < TimelineWalkIntervalMs)
            {
                return false;
            }

            _lastTimelineWalkMs = nowMs;

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var element = _timeText ?? (attempt == 0 ? null : RebuildLocked());
                if (element is null)
                {
                    if (attempt == 0)
                    {
                        RebuildLocked();
                        element = _timeText;
                    }

                    if (element is null)
                    {
                        MarkReadFailure();
                        return false;
                    }
                }

                if (TryReadName(element, out var text) && TimeTextPattern.Match(text) is { Success: true } match)
                {
                    if (TryParseClock(match.Groups["position"].Value, out var position)
                        && TryParseClock(match.Groups["duration"].Value, out var duration)
                        && duration > TimeSpan.Zero)
                    {
                        MarkReadSuccess();
                        var age = TimeSpan.Zero;
                        var previousRead = _lastTimelineReadMs;
                        if (previousRead > 0 && _lastTimelinePosition != position)
                        {
                            // The flip happened somewhere inside this window: its midpoint is the best
                            // estimate of the real boundary, so the sample is that much old.
                            age = TimeSpan.FromMilliseconds(
                                Math.Clamp((nowMs - previousRead) / 2.0, 0, 400));
                        }

                        _lastTimelineReadMs = nowMs;
                        _lastTimelinePosition = position;
                        timeline = new PlayerTimeline(position, duration) { Age = age };
                        return true;
                    }
                }

                // Stale element (track change rebuilds the transport row): rebuild once and retry.
                _timeText = null;
                RebuildLocked();
            }

            MarkReadFailure();
            return false;
        }
    }

    /// <summary>
    /// Presses a transport control through <c>accDoDefaultAction</c>.
    /// </summary>
    /// <remarks>
    /// Every transport control exists twice in the tree (graphic + push button) and the play/pause pair
    /// is always both present — only the shown icon is pressable (the hidden one answers with
    /// <c>S_FALSE</c>). The play/pause target is therefore chosen from the element state
    /// (<c>STATE_SYSTEM_INVISIBLE</c>), with the caller's playing state as the tie-break.
    /// </remarks>
    public bool TryInvoke(MediaKeyCommand command, bool isCurrentlyPlaying)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        lock (_gate)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var candidates = CommandCandidatesLocked(command, isCurrentlyPlaying);
                if (candidates.Count == 0)
                {
                    RebuildLocked();
                    candidates = CommandCandidatesLocked(command, isCurrentlyPlaying);
                }

                if (PressAny(candidates))
                {
                    return true;
                }

                if (attempt == 1)
                {
                    return false;
                }

                RebuildLocked();
            }

            return false;
        }
    }

    /// <summary>Candidate buttons for one command, most-likely-pressable first.</summary>
    private List<ButtonCandidate> CommandCandidatesLocked(MediaKeyCommand command, bool isCurrentlyPlaying)
    {
        if (command == MediaKeyCommand.NextTrack)
        {
            return Order(_nextButtons);
        }

        if (command == MediaKeyCommand.PreviousTrack)
        {
            return Order(_previousButtons);
        }

        // Play/pause: the shown icon is the one that reacts. When neither state is readable, fall back
        // to the caller's observation ("playing" => press the pause icon).
        var playVisible = HasVisible(_playButtons);
        var pauseVisible = HasVisible(_pauseButtons);
        bool pressPause;
        if (playVisible != pauseVisible)
        {
            pressPause = !playVisible;
        }
        else
        {
            pressPause = isCurrentlyPlaying;
        }

        return Order(pressPause ? _pauseButtons : _playButtons);
    }

    private static List<ButtonCandidate> Order(List<ButtonCandidate> candidates)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        return candidates
            .OrderByDescending(candidate => IsVisible(candidate.Element) ? 1 : 0)
            .ThenByDescending(candidate => candidate.Role == RolePushButton ? 1 : 0)
            .ToList();
    }

    private static bool PressAny(List<ButtonCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            try
            {
                if (candidate.Element.accDoDefaultAction(0) == 0)
                {
                    return true;
                }
            }
            catch (COMException)
            {
                // Element disconnected: caller rebuilds and retries.
            }
        }

        return false;
    }

    private static bool HasVisible(List<ButtonCandidate> candidates)
        => candidates.Any(candidate => IsVisible(candidate.Element));

    /// <summary>True when the element is not marked invisible (0x8000) or unavailable (0x1).</summary>
    private static bool IsVisible(IAccessible element)
    {
        try
        {
            if (element.get_accState(0, out var value) != 0 || value is null)
            {
                return false;
            }

            var state = Convert.ToInt32(value);
            return (state & 0x8000) == 0 && (state & 0x1) == 0;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }

    private IAccessible? RebuildLocked()
    {
        ResetElementsLocked();
        Interlocked.Exchange(ref _lastWalkNodes, 0);
        var root = _root;
        if (root is null)
        {
            return null;
        }

        try
        {
            Walk(root, 0);
        }
        catch (COMException)
        {
            // The player window went away; the next poll recreates the session.
            return null;
        }

        return _timeText;
    }

    private void ResetElementsLocked()
    {
        _timeText = null;
        _previousButtons.Clear();
        _nextButtons.Clear();
        _playButtons.Clear();
        _pauseButtons.Clear();
    }

    private static void AddCandidate(List<ButtonCandidate> target, IAccessible element, int role)
    {
        // The same element can be visited twice (graphic + pushbutton share a name); keep one entry per
        // element reference and prefer the higher role.
        foreach (var existing in target)
        {
            if (ReferenceEquals(existing.Element, element))
            {
                return;
            }
        }

        target.Add(new ButtonCandidate(element, role));
    }

    private void Walk(IAccessible parent, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }


        int count;
        if (parent.get_accChildCount(out count) != 0)
        {
            return;
        }

        for (var index = 1; index <= count && index <= MaxNodes; index++)
        {
            IAccessible? child;
            try
            {
                if (parent.get_accChild(index, out var childObject) != 0 || childObject is null)
                {
                    continue;
                }

                child = childObject as IAccessible;
                if (child is null)
                {
                    continue;
                }
            }
            catch (COMException)
            {
                continue;
            }

            Interlocked.Increment(ref _lastWalkNodes);
            if (!TryReadName(child, out var name) || name.Length == 0)
            {
                Walk(child, depth + 1);
                continue;
            }

            if (_timeText is null
                && TryReadRole(child, out var role)
                && (role == RoleStaticText || role == RoleGraphic || role == 0)
                && TimeTextPattern.IsMatch(name))
            {
                _timeText = child;
            }
            else if (TryReadRole(child, out var buttonRole) && buttonRole is RolePushButton or RoleGraphic)
            {
                switch (name)
                {
                    case PreviousButtonName:
                        AddCandidate(_previousButtons, child, buttonRole);
                        break;
                    case NextButtonName:
                        AddCandidate(_nextButtons, child, buttonRole);
                        break;
                    case PlayButtonName:
                        AddCandidate(_playButtons, child, buttonRole);
                        break;
                    case PauseButtonName:
                        AddCandidate(_pauseButtons, child, buttonRole);
                        break;
                }
            }

            Walk(child, depth + 1);
        }
    }

    private static bool TryReadName(IAccessible element, out string name)
    {
        try
        {
            return element.get_accName(0, out name) == 0 && name is not null;
        }
        catch (COMException)
        {
            name = string.Empty;
            return false;
        }
    }

    private static bool TryReadRole(IAccessible element, out int role)
    {
        role = 0;
        try
        {
            if (element.get_accRole(0, out var value) != 0 || value is null)
            {
                return false;
            }

            role = Convert.ToInt32(value);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }

    internal static bool TryParseClock(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        var seconds = 0;
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var part) || part < 0)
            {
                return false;
            }

            seconds = (seconds * 60) + part;
        }

        value = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static List<IntPtr> FindPlayerWindows(int processId)
    {
        var candidates = new List<(IntPtr Window, long Area)>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)processId)
            {
                return true;
            }

            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            if (!className.ToString().StartsWith("kugou_ui", StringComparison.OrdinalIgnoreCase)
                && !className.ToString().StartsWith("Kugou", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IsWindow(window) || !GetWindowRect(window, out var rect))
            {
                return true;
            }

            var area = (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
            candidates.Add((window, area));
            return true;
        }, IntPtr.Zero);

        var ordered = new List<IntPtr>();
        // The window the shell reports as main is the one that normally carries the transport row.
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            var main = process.MainWindowHandle;
            if (main != IntPtr.Zero && candidates.Any(candidate => candidate.Window == main))
            {
                ordered.Add(main);
            }
        }
        catch (ArgumentException)
        {
            // Process vanished.
        }
        catch (InvalidOperationException)
        {
            // Process vanished.
        }

        // Only regular player windows are meaningful for transport; the desktop-lyrics overlay and the
        // MV windows expose no transport row and are filtered out by the timeline probe anyway.
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Area))
        {
            if (!ordered.Contains(candidate.Window))
            {
                ordered.Add(candidate.Window);
            }
        }

        return ordered;
    }

    private static IAccessible? TryGetAccessible(IntPtr window)
    {
        var iid = typeof(IAccessible).GUID;
        var hr = AccessibleObjectFromWindow(window, ObjidClient, ref iid, out var accessible);
        return hr == 0 ? accessible : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var candidate in _playButtons)
            {
                ReleaseCom(candidate.Element);
            }

            foreach (var candidate in _pauseButtons)
            {
                ReleaseCom(candidate.Element);
            }

            foreach (var candidate in _previousButtons)
            {
                ReleaseCom(candidate.Element);
            }

            foreach (var candidate in _nextButtons)
            {
                ReleaseCom(candidate.Element);
            }

            _playButtons.Clear();
            _pauseButtons.Clear();
            _previousButtons.Clear();
            _nextButtons.Clear();
            ReleaseCom(_timeText);
            ReleaseCom(_root);
            _timeText = null;
            _root = null;
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
        {
            return;
        }

        try
        {
            Marshal.ReleaseComObject(value);
        }
        catch (ArgumentException)
        {
            // Already released.
        }
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxLength);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder title, int maxLength);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);

    /// <summary>
    /// Raw <c>IAccessible</c> vtable. The first four slots belong to <c>IDispatch</c> and are declared
    /// only to keep the layout correct; DirectUI objects do not implement <c>IDispatch</c>.
    /// </summary>
    [ComImport]
    [Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccessible
    {
        [PreserveSig]
        int GetTypeInfoCount(out uint count);

        [PreserveSig]
        int GetTypeInfo(uint typeInfoIndex, uint lcid, out IntPtr typeInfo);

        [PreserveSig]
        int GetIDsOfNames(ref Guid riid,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)]
            string[] names, uint nameCount, uint lcid,
            [MarshalAs(UnmanagedType.LPArray)] int[] dispIds);

        [PreserveSig]
        int Invoke(int dispId, ref Guid riid, uint lcid, ushort flags, IntPtr parameters, IntPtr result,
            IntPtr exceptionInfo, IntPtr argumentError);

        [PreserveSig]
        int get_accParent(out IAccessible parent);

        [PreserveSig]
        int get_accChildCount(out int count);

        [PreserveSig]
        int get_accChild([In] [MarshalAs(UnmanagedType.Struct)] object index,
            [MarshalAs(UnmanagedType.IUnknown)] out object child);

        [PreserveSig]
        int get_accName([In] [MarshalAs(UnmanagedType.Struct)] object index,
            [MarshalAs(UnmanagedType.BStr)] out string name);

        [PreserveSig]
        int get_accValue([In] [MarshalAs(UnmanagedType.Struct)] object index,
            [MarshalAs(UnmanagedType.BStr)] out string value);

        [PreserveSig]
        int get_accDescription([In] [MarshalAs(UnmanagedType.Struct)] object index,
            [MarshalAs(UnmanagedType.BStr)] out string description);

        [PreserveSig]
        int get_accRole([In] [MarshalAs(UnmanagedType.Struct)] object index,
            [MarshalAs(UnmanagedType.Struct)] out object role);

        [PreserveSig]
        int get_accState([In] [MarshalAs(UnmanagedType.Struct)] object index,
            [MarshalAs(UnmanagedType.Struct)] out object state);

        [PreserveSig]
        int get_accHelp([In] [MarshalAs(UnmanagedType.Struct)] object index,
            [MarshalAs(UnmanagedType.BStr)] out string help);

        [PreserveSig]
        int get_accHelpTopic([MarshalAs(UnmanagedType.BStr)] out string helpFile,
            [In] [MarshalAs(UnmanagedType.Struct)] object index, out int topicId);

        [PreserveSig]
        int get_accKeyboardShortcut([In] [MarshalAs(UnmanagedType.Struct)] object index,
            [MarshalAs(UnmanagedType.BStr)] out string shortcut);

        [PreserveSig]
        int get_accFocus([MarshalAs(UnmanagedType.Struct)] out object focus);

        [PreserveSig]
        int get_accSelection([MarshalAs(UnmanagedType.Struct)] out object selection);

        [PreserveSig]
        int get_accDefaultAction([In] [MarshalAs(UnmanagedType.Struct)] object index,
            [MarshalAs(UnmanagedType.BStr)] out string action);

        [PreserveSig]
        int accSelect(int flags, [In] [MarshalAs(UnmanagedType.Struct)] object index);

        [PreserveSig]
        int accLocation(out int left, out int top, out int width, out int height,
            [In] [MarshalAs(UnmanagedType.Struct)] object index);

        [PreserveSig]
        int accNavigate(int direction, [In] [MarshalAs(UnmanagedType.Struct)] object start,
            [MarshalAs(UnmanagedType.Struct)] out object end);

        [PreserveSig]
        int accHitTest(int x, int y, [MarshalAs(UnmanagedType.Struct)] out object child);

        [PreserveSig]
        int accDoDefaultAction([In] [MarshalAs(UnmanagedType.Struct)] object index);

        [PreserveSig]
        int put_accName([In] [MarshalAs(UnmanagedType.Struct)] object index,
            [MarshalAs(UnmanagedType.BStr)] string name);

        [PreserveSig]
        int put_accValue([In] [MarshalAs(UnmanagedType.Struct)] object index,
            [MarshalAs(UnmanagedType.BStr)] string value);
    }
}