using System.Windows.Threading;
using Windows.Media.Control;

namespace FluidBar;

public sealed class MediaPlugin : IIslandPlugin
{
    public string Id => "media";
    public string Name => "媒体播放";
    public string Description => "显示当前媒体来源、曲目、播放状态、进度、波形和可用歌词";
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

    public MediaPlugin()
    {
        _settings = MediaPluginSettings.Load();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.PollIntervalMs, 400, 5000))
        };
        _timer.Tick += async (_, _) => await PollAsync();
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
        if (_timer.IsEnabled)
            return;

        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.PollIntervalMs, 400, 5000));
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        Stop();
        _config?.Save();
    }

    private async Task PollAsync()
    {
        if (_isPolling)
            return;

        _isPolling = true;
        try
        {
            var snapshot = await TryReadSnapshotAsync();
            if (snapshot is null)
                return;

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

    private async Task<MediaSnapshot?> TryReadSnapshotAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var session = manager.GetCurrentSession();
        if (session is null)
            return null;

        var properties = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var isPlaying = playback.PlaybackStatus ==
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var progress = CalculateProgressPercent(timeline.Position, timeline.StartTime, timeline.EndTime);
        var sourceId = session.SourceAppUserModelId ?? "";
        var sourceName = MediaIslandEventFactory.FriendlySourceName(sourceId);

        var baseSnapshot = new MediaSnapshot(
            SourceAppUserModelId: sourceId,
            SourceName: sourceName,
            Title: properties.Title,
            Artist: properties.Artist,
            Album: properties.AlbumTitle,
            IsPlaying: isPlaying,
            ProgressPercent: progress);
        var lyric = _settings.ShowLyrics
            ? _lyricsProvider.TryGetCurrentLine(baseSnapshot, timeline.Position)
            : null;

        return baseSnapshot with { LyricLine = lyric };
    }

    private static int CalculateProgressPercent(TimeSpan position, TimeSpan start, TimeSpan end)
    {
        var duration = end - start;
        if (duration.TotalMilliseconds <= 0)
            return 0;

        return Math.Clamp(
            (int)Math.Round((position - start).TotalMilliseconds / duration.TotalMilliseconds * 100),
            0,
            100);
    }

    private static string BuildSignature(MediaSnapshot snapshot)
    {
        return string.Join("|", new[]
        {
            snapshot.SourceAppUserModelId,
            snapshot.Title,
            snapshot.Artist,
            snapshot.Album,
            snapshot.IsPlaying.ToString(),
            snapshot.ProgressPercent.ToString(),
            snapshot.LyricLine ?? ""
        });
    }
}

