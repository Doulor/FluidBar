using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace FluidBar;

public sealed class MediaPlugin : IIslandPlugin
{
    public string Id => "media";
    public string Name => "媒体播放";
    public string Description => "显示当前媒体来源、曲目、播放状态";
    public string Icon => "\uE768";
    public bool Enabled { get; set; } = true;
    public IPluginConfig? Config => _config;
    public event Action<IslandEvent>? EventTriggered;

    private readonly DispatcherTimer _timer;
    private MediaPluginSettings _settings;
    private MediaPluginConfig? _config;
    private ILyricsProvider _lyricsProvider = new NullLyricsProvider();
    private string _lastSignature = string.Empty;
    private bool _isPolling;
    private bool _disposed;

    // Known media player process names → friendly source name
    private static readonly Dictionary<string, string> KnownPlayers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spotify"]     = "Spotify",
        ["cloudmusic"]  = "网易云音乐",
        ["qqmusic"]     = "QQ 音乐",
        ["kugou"]       = "酷狗音乐",
        ["kwmusic"]     = "酷我音乐",
        ["wmplayer"]    = "Media Player",
        ["msedge"]      = "Microsoft Edge",
        ["chrome"]      = "Chrome",
        ["firefox"]     = "Firefox",
    };

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    public MediaPlugin()
    {
        _settings = MediaPluginSettings.Load();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.PollIntervalMs, 1200, 5000))
        };
        _timer.Tick += (_, _) => SafePoll();
    }

    public void Initialize()
    {
        _config = new MediaPluginConfig(_settings);
        _lyricsProvider = _settings.ShowLyrics
            ? new LocalLrcLyricsProvider(_settings.LyricsDirectory)
            : new NullLyricsProvider();
    }

    public void Start()
    {
        if (_disposed || _timer.IsEnabled)
            return;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.PollIntervalMs, 1200, 5000));
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

    private void SafePoll()
    {
        // Synchronous polling — no WinRT async needed
        if (_disposed)
            return;

        try
        {
            Poll();
        }
        catch
        {
        }
    }

    private void Poll()
    {
        if (_isPolling)
            return;

        _isPolling = true;
        try
        {
            var snapshot = FindPlayingMedia();
            if (snapshot is null)
                return;

            if (_settings.ShowLyrics)
            {
                var lyric = _lyricsProvider.TryGetCurrentLine(snapshot, TimeSpan.Zero);
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

    private static MediaSnapshot? FindPlayingMedia()
    {
        foreach (var process in Process.GetProcesses())
        {
            string? processName;
            try { processName = process.ProcessName; }
            catch { continue; }

            if (!KnownPlayers.TryGetValue(processName, out var sourceName))
                continue;

            IntPtr hWnd;
            try { hWnd = process.MainWindowHandle; }
            catch { continue; }

            if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd))
                continue;

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                continue;

            // Try to parse "Artist - Song" or similar patterns
            var (artist, song) = ParseMediaTitle(title, processName);
            if (string.IsNullOrWhiteSpace(song))
                continue;

            return new MediaSnapshot(
                SourceAppUserModelId: processName,
                SourceName: sourceName,
                Title: song,
                Artist: artist,
                Album: "",
                IsPlaying: true,
                ProgressPercent: 0);
        }

        return null;
    }

    private static (string Artist, string Song) ParseMediaTitle(string title, string processName)
    {
        // Try "Artist - Song" pattern first
        var dashIndex = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex > 0 && dashIndex < title.Length - 3)
        {
            var artist = title[..dashIndex].Trim();
            var song = title[(dashIndex + 3)..].Trim();

            // Remove trailing app names from song
            var extraDash = song.LastIndexOf(" - ", StringComparison.Ordinal);
            if (extraDash > 0)
            {
                var suffix = song[(extraDash + 3)..].Trim();
                if (suffix.Contains("云音乐", StringComparison.Ordinal) ||
                    suffix.Contains("Music", StringComparison.Ordinal) ||
                    suffix.Contains("音乐", StringComparison.Ordinal))
                {
                    song = song[..extraDash].Trim();
                }
            }

            return (artist, song);
        }

        // Single segment: treat as song, no artist
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
            snapshot.IsPlaying.ToString(),
            snapshot.LyricLine ?? "");
    }
}
