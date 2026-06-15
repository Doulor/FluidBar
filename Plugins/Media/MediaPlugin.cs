using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace FluidBar;

public sealed class MediaPlugin : IIslandPlugin
{
    public string Id => "media";
    public string Name => "媒体播放";
    public string Description => "显示当前媒体来源、曲目、播放状态、进度、波形";
    public string Icon => "\uE768";
    public bool Enabled { get; set; } = true;
    public IPluginConfig? Config => _config;
    public event Action<IslandEvent>? EventTriggered;

    private readonly DispatcherTimer _timer;
    private MediaPluginSettings _settings;
    private MediaPluginConfig? _config;
    private ILyricsProvider _lyricsProvider = new NullLyricsProvider();
    private IMediaSessionProvider? _sessionProvider;
    private string _lastSignature = string.Empty;
    private bool _isPolling;
    private bool _disposed;
    private bool _lastFromGsm;
    private readonly KugouLyricsProvider _kugouLyrics = new();

    public IMediaSessionProvider? SessionProvider => _sessionProvider;

    // Known media player process names → friendly source name
    private static readonly Dictionary<string, string> FallbackPlayers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kugou"]      = "酷狗音乐",
        ["cloudmusic"] = "网易云音乐",
        ["qqmusic"]    = "QQ 音乐",
        ["kwmusic"]    = "酷我音乐",
        ["spotify"]    = "Spotify",
        ["wmplayer"]   = "Media Player",
    };

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public MediaPlugin()
    {
        _settings = MediaPluginSettings.Load();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.PollIntervalMs, 800, 5000))
        };
        _timer.Tick += (_, _) => _ = SafePollAsync();
    }

    public void Initialize()
    {
        _config = new MediaPluginConfig(_settings);
        _lyricsProvider = _settings.ShowLyrics
            ? new LocalLrcLyricsProvider(_settings.LyricsDirectory)
            : new NullLyricsProvider();

        try
        {
            _sessionProvider = MediaSessionProviderFactory.Create();
        }
        catch
        {
            _sessionProvider = null;
        }
    }

    public void Start()
    {
        if (_disposed || _timer.IsEnabled)
            return;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.PollIntervalMs, 800, 5000));
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _config?.Save();
    }

    private async Task SafePollAsync()
    {
        if (_disposed)
            return;

        try
        {
            await PollAsync();
        }
        catch
        {
        }
    }

    private async Task PollAsync()
    {
        if (_isPolling)
            return;

        _isPolling = true;
        try
        {
            // Run BOTH sources and prefer the higher-priority one.
            // This ensures music apps (Kugou etc.) take precedence over browser sessions.
            MediaSnapshot? gsmSnapshot = null;
            MediaSnapshot? fallbackSnapshot = null;

            if (_sessionProvider is not null)
                gsmSnapshot = await _sessionProvider.ReadAsync(_lyricsProvider, _settings.ShowLyrics);

            fallbackSnapshot = FindPlayingMediaFallback();

            // When GSMTC was the last active provider and now returns null,
            // suppress the fallback to avoid flashing paused Kugou window.
            MediaSnapshot? snapshot;
            if (_lastFromGsm && gsmSnapshot is null && fallbackSnapshot is not null)
            {
                // GSMTC was playing but stopped — don't fall back to window title
                snapshot = null;
            }
            else
            {
                snapshot = ChooseBestSnapshot(gsmSnapshot, fallbackSnapshot);
            }

            if (snapshot is null)
            {
                _lastFromGsm = false;
                // Media stopped
                if (!string.IsNullOrEmpty(_lastSignature))
                {
                    _lastSignature = string.Empty;
                    EventTriggered?.Invoke(MediaIslandEventFactory.CreateStopped());
                }
                return;
            }

            // Track active provider
            _lastFromGsm = snapshot.PositionTicks > 0;

            if (!snapshot.IsPlaying && !_settings.ShowWhenPaused)
                return;

            // For Kugou and other fallback sources, try to fetch lyrics from API
            if (string.IsNullOrWhiteSpace(snapshot.LyricLine) && !_lastFromGsm)
            {
                var lyric = _kugouLyrics.TryGetCurrentLine(snapshot, TimeSpan.Zero);
                if (!string.IsNullOrWhiteSpace(lyric))
                    snapshot = snapshot with { LyricLine = lyric };
            }

            var signature = BuildSignature(snapshot);
            if (signature == _lastSignature)
                return;

            _lastSignature = signature;
            EventTriggered?.Invoke(MediaIslandEventFactory.FromSnapshot(snapshot));
        }
        catch
        {
        }
        finally
        {
            _isPolling = false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [ThreadStatic]
    private static List<(IntPtr Hwnd, string Title, string ProcessName, string SourceName)>? _fallbackWindows;

    private static MediaSnapshot? FindPlayingMediaFallback()
    {
        // Build a map of known process IDs → source names
        var knownProcessIds = new Dictionary<uint, string>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (FallbackPlayers.TryGetValue(process.ProcessName, out var sourceName))
                    knownProcessIds[(uint)process.Id] = sourceName;
            }
            catch { }
        }

        if (knownProcessIds.Count == 0)
            return null;

        // Enumerate all top-level windows and match by process ID
        _fallbackWindows = new List<(IntPtr, string, string, string)>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (!knownProcessIds.TryGetValue(pid, out var sourceName))
                return true;

            var title = GetWindowTitle(hWnd);
            if (!string.IsNullOrWhiteSpace(title))
            {
                GetWindowThreadProcessId(hWnd, out uint _);
                _fallbackWindows!.Add((hWnd, title, "", sourceName));

                // Also search child windows recursively for song info
                if (!title.Contains(" - ", StringComparison.Ordinal))
                {
                    SearchChildWindowsRecursive(hWnd, sourceName);
                }
            }

            return true;
        }, IntPtr.Zero);

        var candidates = _fallbackWindows;
        _fallbackWindows = null;

        // Prefer windows with " - " pattern (Artist - Song)
        var best = candidates
            .OrderByDescending(t => t.Title.Contains(" - ", StringComparison.Ordinal) ? 100 : t.Title.Length)
            .FirstOrDefault();

        if (best.Title is null)
            return null;

        var (artist, song) = ParseMediaTitle(best.Title, best.ProcessName);
        if (string.IsNullOrWhiteSpace(song))
            return null;

        // Try to read Kugou desktop lyrics for this song
        var lyricLine = TryGetKugouLyrics();

        return new MediaSnapshot(
            SourceAppUserModelId: best.SourceName,
            SourceName: best.SourceName,
            Title: song,
            Artist: artist,
            Album: "",
            IsPlaying: true,
            ProgressPercent: 0,
            LyricLine: lyricLine);
    }

    [ThreadStatic]
    private static string? _kugouWindowLyrics;

    private static string? TryGetKugouLyrics()
    {
        try
        {
            _kugouWindowLyrics = null;
            var kugouWindow = IntPtr.Zero;

            // Find Kugou's main window first
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    if (proc.ProcessName.Equals("kugou", StringComparison.OrdinalIgnoreCase)
                        || proc.ProcessName.Equals("KuGou", StringComparison.OrdinalIgnoreCase))
                    {
                        kugouWindow = hWnd;
                        return false; // Stop
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            if (kugouWindow == IntPtr.Zero)
                return null;

            // Search for lyrics window among Kugou's children
            // Kugou lyrics window typically has class containing "Lyric" or title containing "歌词"
            _kugouWindowLyrics = null;
            SearchLyricsWindow(kugouWindow);

            return _kugouWindowLyrics;
        }
        catch
        {
            return null;
        }
    }

    private static void SearchLyricsWindow(IntPtr parent)
    {
        var sb = new StringBuilder(128);
        GetWindowText(parent, sb, sb.Capacity);
        var title = sb.ToString();
        sb.Clear();
        GetClassName(parent, sb, sb.Capacity);
        var cls = sb.ToString().ToLowerInvariant();

        // Check if this is a lyrics window
        if (title.Contains("歌词", StringComparison.Ordinal) ||
            title.Contains("Lyric", StringComparison.OrdinalIgnoreCase) ||
            cls.Contains("lyric") || cls.Contains("kugou_desktoplyric"))
        {
            // Try to read lyrics text from this window's child controls
            EnumChildWindows(parent, (child, _) =>
            {
                var t = GetWindowTitle(child);
                if (!string.IsNullOrWhiteSpace(t) && t.Length > 4 && !t.Contains("酷狗"))
                {
                    _kugouWindowLyrics = t;
                    return false;
                }
                // Also try WM_GETTEXT for rich edit controls
                var textSb = new StringBuilder(512);
                SendMessage(child, WM_GETTEXT, (IntPtr)511, textSb);
                var rt = textSb.ToString();
                if (!string.IsNullOrWhiteSpace(rt) && rt.Length > 4 && !rt.Contains("酷狗"))
                {
                    _kugouWindowLyrics = rt;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (!string.IsNullOrWhiteSpace(_kugouWindowLyrics))
                return;
        }

        // Recurse into children
        EnumChildWindows(parent, (child, _) =>
        {
            SearchLyricsWindow(child);
            return _kugouWindowLyrics is null; // Continue if not found
        }, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    private const uint WM_GETTEXT = 0x000D;

    private static void SearchChildWindowsRecursive(IntPtr parent, string sourceName)
    {
        EnumChildWindows(parent, (childHwnd, _) =>
        {
            var childTitle = GetWindowTitle(childHwnd);
            if (!string.IsNullOrWhiteSpace(childTitle) &&
                childTitle.Contains(" - ", StringComparison.Ordinal))
            {
                _fallbackWindows!.Add((childHwnd, childTitle, "", sourceName));
                return false; // Found, stop this branch
            }
            // Recurse into grandchildren
            SearchChildWindowsRecursive(childHwnd, sourceName);
            return true;
        }, IntPtr.Zero);
    }

    private static (string Artist, string Song) ParseMediaTitle(string title, string processName)
    {
        var dashIndex = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex > 0 && dashIndex < title.Length - 3)
        {
            var artist = title[..dashIndex].Trim();
            var song = title[(dashIndex + 3)..].Trim();

            var extraDash = song.LastIndexOf(" - ", StringComparison.Ordinal);
            if (extraDash > 0)
            {
                var suffix = song[(extraDash + 3)..].Trim();
                if (suffix.Contains("云音乐", StringComparison.Ordinal) ||
                    suffix.Contains("Music", StringComparison.Ordinal) ||
                    suffix.Contains("音乐", StringComparison.Ordinal) ||
                    suffix.Contains("酷狗", StringComparison.Ordinal))
                {
                    song = song[..extraDash].Trim();
                }
            }

            return (artist, song);
        }

        return ("", title.Trim());
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
            return string.Empty;

        var builder = new StringBuilder(length + 1);
        GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static MediaSnapshot? ChooseBestSnapshot(MediaSnapshot? gsm, MediaSnapshot? fallback)
    {
        if (gsm is null) return fallback;
        if (fallback is null) return gsm;

        // Music apps from fallback always beat browser sessions from GSMTC
        var gsmPriority = GetSourcePriority(gsm.SourceAppUserModelId);
        var fbPriority = GetSourcePriority(fallback.SourceAppUserModelId);
        return fbPriority >= gsmPriority ? fallback : gsm;
    }

    private static int GetSourcePriority(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return 0;
        var lower = sourceId.ToLowerInvariant();
        if (lower.Contains("kugou") || lower.Contains("cloudmusic") || lower.Contains("netease") ||
            lower.Contains("qqmusic") || lower.Contains("spotify") || lower.Contains("kwmusic"))
            return 100;
        if (lower.Contains("chrome") || lower.Contains("edge") || lower.Contains("firefox"))
            return 50;
        return 10;
    }

    private static string BuildSignature(MediaSnapshot snapshot)
    {
        return string.Join("|",
            snapshot.SourceAppUserModelId,
            snapshot.Title,
            snapshot.Artist,
            snapshot.Album,
            snapshot.IsPlaying.ToString(),
            snapshot.ProgressPercent.ToString(),
            snapshot.LyricLine ?? "");
    }
}
