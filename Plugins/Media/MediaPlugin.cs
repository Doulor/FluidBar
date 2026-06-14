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
        if (_isPolling || _sessionProvider is null)
            return;

        _isPolling = true;
        try
        {
            var snapshot = await _sessionProvider.ReadAsync(_lyricsProvider, _settings.ShowLyrics);
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
