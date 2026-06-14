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
            MediaSnapshot? snapshot = null;

            // 1. Try WinRT GSMTC first (works for browsers, Spotify, etc.)
            if (_sessionProvider is not null)
            {
                snapshot = await _sessionProvider.ReadAsync(_lyricsProvider, _settings.ShowLyrics);
            }

            // 2. Fallback: window title monitoring (for Kugou, Netease, etc. that don't use GSMTC)
            if (snapshot is null)
            {
                snapshot = FindPlayingMediaFallback();
            }

            if (snapshot is null)
            {
                // Media stopped
                if (!string.IsNullOrEmpty(_lastSignature))
                {
                    _lastSignature = string.Empty;
                    EventTriggered?.Invoke(MediaIslandEventFactory.CreateStopped());
                }
                return;
            }

            if (!snapshot.IsPlaying && !_settings.ShowWhenPaused)
                return;

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

                // Also search child windows for song info
                if (!title.Contains(" - ", StringComparison.Ordinal))
                {
                    EnumChildWindows(hWnd, (childHwnd, __) =>
                    {
                        if (!IsWindowVisible(childHwnd))
                            return true;
                        var childTitle = GetWindowTitle(childHwnd);
                        if (!string.IsNullOrWhiteSpace(childTitle) &&
                            childTitle.Contains(" - ", StringComparison.Ordinal))
                        {
                            _fallbackWindows!.Add((childHwnd, childTitle, "", sourceName));
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);
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

        return new MediaSnapshot(
            SourceAppUserModelId: best.SourceName,
            SourceName: best.SourceName,
            Title: song,
            Artist: artist,
            Album: "",
            IsPlaying: true,
            ProgressPercent: 0);
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
