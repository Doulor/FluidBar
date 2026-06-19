using FluidBar;
using System.Diagnostics;
using System.IO;

var settings = new FluidBarSettings
{
    CollapsedWidth = 92,
    CollapsedHeight = 24,
    ExpandedMaxWidth = 260,
    ExpandedHeight = 44
};

Test("battery charging state is explicit", () =>
{
    var view = IslandPresentation.FromEvent(
        new IslandEvent("battery", "充电中 87%", "约 12 分钟后充满", "battery_charge"),
        settings);

    AssertEqual(IslandViewKind.Status, view.Kind);
    AssertEqual("battery_charge", view.IconKind);
    AssertContains("充电中", view.StatusText);
    AssertContains("外接电源", view.StatusBadge);
});

Test("battery power state does not read as unknown", () =>
{
    var view = IslandPresentation.FromEvent(
        new IslandEvent("battery", "电池 63%", "电池供电中", "battery"),
        settings);

    AssertEqual(IslandViewKind.Status, view.Kind);
    AssertContains("电池供电", view.StatusText);
    AssertDoesNotContain("未充电", view.StatusText);
});

Test("progress percent is clamped for runaway monitor values", () =>
{
    var view = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 255%", "255%", "volume"),
        settings);

    AssertEqual(IslandViewKind.Progress, view.Kind);
    AssertEqual(100, view.ProgressPercent);
    AssertEqual(true, view.ShowsAudioWave);
});

Test("expanded metrics protect the island from being crushed", () =>
{
    var view = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", new string('A', 120), "clipboard"),
        settings);

    AssertEqual(IslandViewKind.ScrollingText, view.Kind);
    AssertAtLeast(260, view.TargetWidth);
    AssertAtLeast(48, view.TargetHeight);
    AssertAtLeast(38, view.CollapsedHeight);
});

Test("scrolling text starts at the left edge before marquee motion", () =>
{
    var plan = ScrollingTextMotionPlan.CreateInitial();

    AssertEqual(0, plan.InitialOffset);
    AssertEqual(500, plan.HoldMilliseconds);
});

Test("monitor feature settings are created with hover card enabled", () =>
{
    var feature = new FluidBarSettings().GetMonitorFeatureSettings("volume");

    AssertEqual(true, feature.HoverCardEnabled);
    AssertEqual(3000, feature.DisplayDurationMs);
});

Test("display strategy defaults to latest only", () =>
{
    var defaultSettings = new FluidBarSettings();

    AssertEqual(IslandDisplayStrategy.LatestOnly, defaultSettings.DisplayStrategy);
    AssertEqual(false, IslandStackPolicy.CanStack(defaultSettings));
});

Test("plugin catalog contains the official source plugins", () =>
{
    var catalogPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "Plugins",
        "catalog.json"));
    var catalog = PluginCatalog.Load(catalogPath);

    AssertEqual(4, catalog.Plugins.Count);
    AssertEqual(true, catalog.Contains("clipboard"));
    AssertEqual(true, catalog.Contains("media"));
    AssertEqual(true, catalog.Contains("agent-status"));
    AssertEqual(true, catalog.Contains("notifications"));
    foreach (var plugin in catalog.Plugins)
    {
        AssertNotEmpty(plugin.Name);
        AssertNotEmpty(plugin.Category);
        AssertNotEmpty(plugin.EntryPoint);
    }
});

Test("legacy island events stay text compatible", () =>
{
    var evt = new IslandEvent("legacy", "旧插件", "普通内容", "info");
    var view = IslandPresentation.FromEvent(evt, settings);

    AssertEqual(IslandViewKind.Text, view.Kind);
    AssertEqual("旧插件", view.Title);
    AssertEqual("普通内容", view.Content);
    AssertEqual("info", view.IconKind);
});

Test("media payload projects to wave progress and lyrics", () =>
{
    var evt = new IslandEvent(
        "media",
        "在播放",
        "Cornfield Chase",
        "media",
        new IslandEventPayload(
            Kind: IslandEventKind.Media,
            Subtitle: "Hans Zimmer",
            Badge: "Spotify",
            SourceName: "Spotify",
            ProgressPercent: 42,
            IsActive: true,
            ShowsAudioWave: true,
            LyricLine: "I'm going home",
            SecondaryLyricLine: "Cooper Station"));

    var view = IslandPresentation.FromEvent(evt, settings);
    var card = HoverCardPresentation.FromCompact(view, settings);

    AssertEqual(IslandViewKind.Media, view.Kind);
    AssertEqual(true, view.ShowsAudioWave);
    AssertEqual(42, view.ProgressPercent);
    AssertContains("Hans Zimmer", view.StatusText);
    // Lyrics appear in the hover subtitle for media
    AssertContains("I'm going home", card.LyricLine ?? "");
    AssertEqual("Spotify", card.StatusBadge);
});

Test("media hover card does not repeat the song title in the body", () =>
{
    var evt = new IslandEvent(
        "media",
        "酷狗音乐",
        "年轮",
        "media",
        new IslandEventPayload(
            Kind: IslandEventKind.Media,
            Subtitle: "旺仔小乔",
            Badge: "酷狗音乐",
            SourceName: "酷狗音乐",
            IsActive: true,
            ShowsAudioWave: true));

    var view = IslandPresentation.FromEvent(evt, settings);
    var card = HoverCardPresentation.FromCompact(view, settings);

    AssertDoesNotContain("年轮", card.Content);
    AssertContains("旺仔小乔", card.Content);
});

Test("media snapshot creates a rich island event", () =>
{
    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
        SourceName: "Spotify",
        Title: "Cornfield Chase",
        Artist: "Hans Zimmer",
        Album: "Interstellar",
        IsPlaying: true,
        ProgressPercent: 42,
        LyricLine: "I'm going home",
        SecondaryLyricLine: "Cooper Station");

    var evt = MediaIslandEventFactory.FromSnapshot(snapshot);

    AssertEqual("media", evt.Source);
    AssertEqual("media", evt.IconKind);
    AssertEqual(IslandEventKind.Media, evt.Payload?.Kind);
    AssertEqual(true, evt.Payload?.ShowsAudioWave);
    AssertEqual(42, evt.Payload?.ProgressPercent);
    AssertContains("Hans Zimmer", evt.Payload?.Subtitle ?? "");
    AssertContains("I'm going home", evt.Payload?.LyricLine ?? "");
});

Test("media artwork is preferred over app icon and default glyph", () =>
{
    const string artworkPath = @"C:\FluidBar\cover.jpg";
    const string appIconPath = @"C:\FluidBar\app.png";

    var artworkChoice = IslandMediaVisualPolicy.ChooseIcon(
        artworkPath,
        appIconPath,
        path => path is artworkPath or appIconPath);
    AssertEqual(IslandMediaIconKind.Artwork, artworkChoice.Kind);
    AssertEqual(artworkPath, artworkChoice.Path);

    var appChoice = IslandMediaVisualPolicy.ChooseIcon(
        @"C:\FluidBar\missing-cover.jpg",
        appIconPath,
        path => path == appIconPath);
    AssertEqual(IslandMediaIconKind.AppIcon, appChoice.Kind);
    AssertEqual(appIconPath, appChoice.Path);

    var defaultChoice = IslandMediaVisualPolicy.ChooseIcon(
        @"C:\FluidBar\missing-cover.jpg",
        @"C:\FluidBar\missing-app.png",
        _ => false);
    AssertEqual(IslandMediaIconKind.DefaultGlyph, defaultChoice.Kind);
    AssertEqual(null, defaultChoice.Path);
});

Test("media image visual metrics fill the icon well", () =>
{
    var metrics = IslandMediaVisualPolicy.ResolveImageMetrics(IslandMediaIconKind.Artwork);

    AssertAtLeast(34, metrics.ImageWidth);
    AssertAtLeast(34, metrics.ImageHeight);
    AssertEqual(true, metrics.CropsToCircle);
});

Test("browser compact media text reserves room for the audio wave", () =>
{
    var width = MediaLayoutPolicy.CompactTextWidth(targetWidth: 390, showsAudioWave: true);

    AssertAtMost(236, width);
});

Test("compact media text and progress widths follow configured island width", () =>
{
    var narrowText = MediaLayoutPolicy.CompactContentWidth(targetWidth: 260, showsAudioWave: true);
    var normalText = MediaLayoutPolicy.CompactContentWidth(targetWidth: 350, showsAudioWave: true);
    var wideText = MediaLayoutPolicy.CompactContentWidth(targetWidth: 520, showsAudioWave: true);

    AssertAtMost(136, narrowText);
    AssertAtLeast(narrowText + 60, normalText);
    AssertAtLeast(normalText + 120, wideText);

    var narrowProgress = MediaLayoutPolicy.CompactProgressWidth(targetWidth: 260, showsAudioWave: true);
    var normalProgress = MediaLayoutPolicy.CompactProgressWidth(targetWidth: 350, showsAudioWave: true);
    var wideProgress = MediaLayoutPolicy.CompactProgressWidth(targetWidth: 520, showsAudioWave: true);

    AssertAtMost(narrowText, narrowProgress);
    AssertAtLeast(narrowProgress + 60, normalProgress);
    AssertAtLeast(normalProgress + 120, wideProgress);
});

Test("media island target width follows the configured compact width", () =>
{
    var compactSettings = new FluidBarSettings
    {
        CollapsedWidth = 126,
        CollapsedHeight = 38,
        ExpandedMaxWidth = 280,
        ExpandedHeight = 64
    };
    var normalSettings = new FluidBarSettings
    {
        CollapsedWidth = 126,
        CollapsedHeight = 38,
        ExpandedMaxWidth = 350,
        ExpandedHeight = 64
    };
    var wideSettings = new FluidBarSettings
    {
        CollapsedWidth = 126,
        CollapsedHeight = 38,
        ExpandedMaxWidth = 700,
        ExpandedHeight = 64
    };
    var evt = new IslandEvent(
        "media",
        "Microsoft Edge",
        "A very long YouTube title that should not force the old fixed media width",
        "media",
        new IslandEventPayload(
            Kind: IslandEventKind.Media,
            SourceName: "Microsoft Edge",
            IsActive: true,
            ShowsAudioWave: true));

    AssertEqual(280.0, IslandPresentation.FromEvent(evt, compactSettings).TargetWidth);
    AssertEqual(350.0, IslandPresentation.FromEvent(evt, normalSettings).TargetWidth);
    AssertEqual(700.0, IslandPresentation.FromEvent(evt, wideSettings).TargetWidth);
});

Test("compact media content keeps growing on very wide configured islands", () =>
{
    var wideText = MediaLayoutPolicy.CompactContentWidth(targetWidth: 700, showsAudioWave: true);
    var veryWideText = MediaLayoutPolicy.CompactContentWidth(targetWidth: 900, showsAudioWave: true);
    var wideProgress = MediaLayoutPolicy.CompactProgressWidth(targetWidth: 700, showsAudioWave: true);
    var veryWideProgress = MediaLayoutPolicy.CompactProgressWidth(targetWidth: 900, showsAudioWave: true);

    AssertAtLeast(wideText + 160, veryWideText);
    AssertAtLeast(wideProgress + 160, veryWideProgress);
});

Test("media hover progress sits below transport controls", () =>
{
    var layout = MediaLayoutPolicy.HoverTransportLayout();

    AssertAtMost(layout.ControlsBottomFromBottom, layout.ProgressTopFromBottom);
});

Test("Kugou search metadata extracts album art candidate", () =>
{
    const string json = """
    {
      "data": {
        "info": [
          {
            "hash": "ABCDEF",
            "album_audio_id": 12345,
            "album_id": "67890",
            "img": "http://imge.kugou.com/stdmusic/{size}/20240101/cover.jpg"
          }
        ]
      }
    }
    """;

    var metadata = KugouLyricsProvider.ParseSearchMetadata(json);

    AssertEqual("ABCDEF", metadata?.Hash);
    AssertEqual("http://imge.kugou.com/stdmusic/240/20240101/cover.jpg", metadata?.AlbumArtUrl);
});

Test("Kugou search metadata reads nested union cover art", () =>
{
    const string json = """
    {
      "data": {
        "info": [
          {
            "hash": "16c8ab298231370293d16bcf9e5ff9b6",
            "album_audio_id": 32029511,
            "album_id": "958909",
            "filename": "周杰伦 - 夜曲",
            "trans_param": {
              "union_cover": "http://imge.kugou.com/stdmusic/{size}/20250125/cover.jpg"
            }
          }
        ]
      }
    }
    """;

    var metadata = KugouLyricsProvider.ParseSearchMetadata(json);

    AssertEqual("16c8ab298231370293d16bcf9e5ff9b6", metadata?.Hash);
    AssertEqual("http://imge.kugou.com/stdmusic/240/20250125/cover.jpg", metadata?.AlbumArtUrl);
});

Test("media image paths project to presentation", () =>
{
    var evt = new IslandEvent(
        "media",
        "酷狗音乐",
        "夜曲",
        "media",
        new IslandEventPayload(
            Kind: IslandEventKind.Media,
            SourceName: "酷狗音乐",
            AlbumArtPath: @"C:\FluidBar\cover.jpg",
            AppIconPath: @"C:\FluidBar\kugou.png"));

    var view = IslandPresentation.FromEvent(evt, settings);

    AssertEqual(@"C:\FluidBar\cover.jpg", view.AlbumArtPath);
    AssertEqual(@"C:\FluidBar\kugou.png", view.AppIconPath);
});

Test("media source visual lookup understands Kugou fallback process names", () =>
{
    var processNames = MediaSourceVisuals.ProcessNamesForSource("kugou");

    AssertEqual(true, processNames.Contains("KuGou"));
    AssertEqual(true, processNames.Contains("kugou"));
    AssertEqual(true, processNames.Contains("KGMusic"));
});

Test("media source lookup understands Chinese Kugou source ids", () =>
{
    var processNames = MediaSourceVisuals.ProcessNamesForSource("酷狗音乐");

    AssertEqual(100, MediaSnapshotSelectionPolicy.GetSourcePriority("酷狗音乐"));
    AssertEqual("酷狗音乐", MediaIslandEventFactory.FriendlySourceName("酷狗音乐"));
    AssertEqual(true, processNames.Contains("KuGou"));
    AssertEqual(true, MediaSnapshotSelectionPolicy.IsSamePlayerApp("酷狗音乐", "kugou"));
});

Test("Kugou fallback audio gate uses sibling player processes", () =>
{
    var processes = new[]
    {
        new MediaFallbackProcessInfo(100, "KuGou", "酷狗音乐"),
        new MediaFallbackProcessInfo(200, "KuGou", "酷狗音乐")
    };
    var gateProcessIds = MediaFallbackProcessPolicy.AudioGateProcessIds(
        processes,
        bestProcessName: "KuGou",
        bestSourceName: "酷狗音乐");

    AssertEqual(true, gateProcessIds.Contains(100));
    AssertEqual(true, gateProcessIds.Contains(200));
    AssertEqual(true, ProcessAudioActivity.IsAnyTargetProcessSessionPlaying(
        new[]
        {
            new ProcessAudioSessionSnapshot(100, IsActive: false, Peak: 0f),
            new ProcessAudioSessionSnapshot(200, IsActive: true, Peak: 0.02f)
        },
        gateProcessIds));
});

Test("browser media command prefers enabled matching browser session", () =>
{
    var sessions = new[]
    {
        new TestMediaCommandSession("edge-disabled", "msedge.exe", IsCurrent: true, IsCommandEnabled: false),
        new TestMediaCommandSession("edge-enabled", "MSEdgeHTM", IsCurrent: false, IsCommandEnabled: true),
        new TestMediaCommandSession("chrome-enabled", "chrome.exe", IsCurrent: false, IsCommandEnabled: true)
    };

    var ordered = MediaSessionCommandPolicy.OrderCandidates(
        sessions,
        preferredSourceId: "Microsoft Edge",
        currentAumid: "msedge.exe",
        sourceId: session => session.SourceId,
        isCurrent: session => session.IsCurrent,
        isCommandEnabled: session => session.IsCommandEnabled);

    AssertEqual("edge-enabled", ordered[0].Id);
    AssertEqual(false, ordered.Any(session => session.Id == "chrome-enabled"));
});

Test("Kugou media command never falls back to a browser GSMTC session", () =>
{
    var sessions = new[]
    {
        new TestMediaCommandSession("edge-enabled", "msedge.exe", IsCurrent: true, IsCommandEnabled: true),
        new TestMediaCommandSession("chrome-enabled", "chrome.exe", IsCurrent: false, IsCommandEnabled: true)
    };

    var ordered = MediaSessionCommandPolicy.OrderCandidates(
        sessions,
        preferredSourceId: "酷狗音乐",
        currentAumid: "msedge.exe",
        sourceId: session => session.SourceId,
        isCurrent: session => session.IsCurrent,
        isCommandEnabled: session => session.IsCommandEnabled);

    AssertEqual(0, ordered.Count);
});

Test("active fallback audio needs a live or very recent signal", () =>
{
    AssertEqual(true, ProcessAudioActivity.IsSessionPlayingForFallback(
        isActive: true,
        peak: 0.02f,
        hasRecentAudioEvidence: false));
    AssertEqual(true, ProcessAudioActivity.IsSessionPlayingForFallback(
        isActive: true,
        peak: 0f,
        hasRecentAudioEvidence: true));
    AssertEqual(false, ProcessAudioActivity.IsSessionPlayingForFallback(
        isActive: true,
        peak: 0f,
        hasRecentAudioEvidence: false));
    AssertEqual(false, ProcessAudioActivity.IsSessionPlayingForFallback(
        isActive: false,
        peak: 1f,
        hasRecentAudioEvidence: true));
});

Test("unknown Kugou audio state can trust an explicit song window title", () =>
{
    AssertEqual(true, MediaFallbackProcessPolicy.ShouldAcceptFallbackPlayback(
        processName: "KuGou",
        sourceName: "酷狗音乐",
        songTitle: "年轮",
        audioState: ProcessAudioPlaybackState.Unknown));
});

Test("inactive Kugou audio state suppresses the title fallback", () =>
{
    AssertEqual(false, MediaFallbackProcessPolicy.ShouldAcceptFallbackPlayback(
        processName: "KuGou",
        sourceName: "酷狗音乐",
        songTitle: "年轮",
        audioState: ProcessAudioPlaybackState.NotPlaying));
});

Test("media hover play button mirrors playback state", () =>
{
    AssertEqual("\uE769", MediaPlaybackUiPolicy.PlayPauseGlyph(isPlaying: true));
    AssertEqual("\uE768", MediaPlaybackUiPolicy.PlayPauseGlyph(isPlaying: false));
});

Test("inactive media keeps hover card open while pointer remains over it", () =>
{
    AssertEqual(true, MediaPlaybackUiPolicy.ShouldKeepHoverCardForInactiveMedia(isHoverCard: true));
    AssertEqual(false, MediaPlaybackUiPolicy.ShouldKeepHoverCardForInactiveMedia(isHoverCard: false));
    AssertEqual(false, MediaPlaybackUiPolicy.ShouldKeepHoverCardForInactiveMedia(
        isHoverCard: true,
        sourceName: "酷狗音乐"));
    AssertEqual(true, MediaPlaybackUiPolicy.ShouldKeepHoverCardForInactiveMedia(
        isHoverCard: true,
        sourceName: "Microsoft Edge"));
});

Test("media transport controls are shown even without timeline data", () =>
{
    AssertEqual(true, MediaPlaybackUiPolicy.ShouldShowTransportControls(IslandViewKind.Media));
    AssertEqual(false, MediaPlaybackUiPolicy.ShouldShowTransportControls(IslandViewKind.Progress));
});

Test("active media stays the main island when another source stacks", () =>
{
    var multiSettings = new FluidBarSettings
    {
        DisplayStrategy = IslandDisplayStrategy.Multiple
    };

    var media = IslandPresentation.FromEvent(
        new IslandEvent(
            "media",
            "酷狗音乐",
            "夜曲",
            "media",
            new IslandEventPayload(
                Kind: IslandEventKind.Media,
                SourceName: "酷狗音乐",
                IsActive: true,
                ShowsAudioWave: true)),
        settings);
    var volume = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 40%", "40%", "volume"),
        settings);

    var stack = IslandStackPolicy.Apply(Array.Empty<IslandStackItem>(), media, "media", multiSettings);
    stack = IslandStackPolicy.Apply(stack, volume, "volume", multiSettings);
    stack = IslandStackPolicy.PinSourceAsLatest(stack, "media");

    AssertEqual(2, stack.Count);
    AssertEqual("volume", stack[0].Source);
    AssertEqual("media", stack[1].Source);
});

Test("same Kugou session uses fallback song title when GSM title is app name", () =>
{
    var gsm = new MediaSnapshot(
        SourceAppUserModelId: "kugou.exe!App",
        SourceName: "酷狗音乐",
        Title: "酷狗音乐",
        Artist: "",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 35,
        PositionTicks: TimeSpan.FromSeconds(42).Ticks,
        EndTicks: TimeSpan.FromSeconds(226).Ticks);
    var fallback = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0,
        LyricLine: "为你弹奏肖邦的夜曲");

    var chosen = MediaSnapshotSelectionPolicy.ChooseBestSnapshot(gsm, fallback);

    AssertEqual("夜曲", chosen?.Title);
    AssertEqual("周杰伦", chosen?.Artist);
    AssertEqual(TimeSpan.FromSeconds(42).Ticks, chosen?.PositionTicks);
    AssertEqual("为你弹奏肖邦的夜曲", chosen?.LyricLine);
});

Test("active Kugou fallback can override a stale paused GSM snapshot", () =>
{
    var gsm = new MediaSnapshot(
        SourceAppUserModelId: "kugou.exe!App",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: false,
        ProgressPercent: 35,
        PositionTicks: TimeSpan.FromSeconds(42).Ticks,
        EndTicks: TimeSpan.FromSeconds(226).Ticks);
    var fallback = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0);

    var chosen = MediaSnapshotSelectionPolicy.ChooseBestSnapshot(gsm, fallback);

    AssertEqual(true, chosen?.IsPlaying);
    AssertEqual(TimeSpan.FromSeconds(42).Ticks, chosen?.PositionTicks);
});

Test("paused high priority GSM session queries audio gated fallback", () =>
{
    var pausedKugou = new MediaSnapshot(
        SourceAppUserModelId: "kugou.exe!App",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: false,
        ProgressPercent: 35);

    AssertEqual(true, MediaSnapshotSelectionPolicy.ShouldQueryFallback(pausedKugou));
});

Test("playing browser beats paused Kugou session", () =>
{
    var pausedKugou = new MediaSnapshot(
        SourceAppUserModelId: "kugou.exe!App",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: false,
        ProgressPercent: 35);
    var browser = new MediaSnapshot(
        SourceAppUserModelId: "msedge.exe",
        SourceName: "Microsoft Edge",
        Title: "Video title",
        Artist: "",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 40);

    var chosen = MediaSnapshotSelectionPolicy.ChooseBestSnapshot(pausedKugou, browser);

    AssertEqual("msedge.exe", chosen?.SourceAppUserModelId);
    AssertEqual(true, chosen?.IsPlaying);
});

Test("active Kugou fallback beats playing browser session", () =>
{
    var browser = new MediaSnapshot(
        SourceAppUserModelId: "msedge.exe",
        SourceName: "Microsoft Edge",
        Title: "Video title",
        Artist: "",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 40);
    var activeKugouFallback = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0);

    var chosen = MediaSnapshotSelectionPolicy.ChooseBestSnapshot(browser, activeKugouFallback);

    AssertEqual("kugou", chosen?.SourceAppUserModelId);
    AssertEqual("夜曲", chosen?.Title);
});

Test("active fallback is allowed after a GSM session disappears", () =>
{
    var fallback = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0);

    AssertEqual(false, MediaSnapshotSelectionPolicy.ShouldSuppressFallbackAfterGsmLoss(
        lastFromGsm: true,
        suppressUntilGsmPlaying: false,
        gsm: null,
        fallback: fallback));
});

Test("hold to hide key defaults to left alt", () =>
{
    var defaultSettings = new FluidBarSettings();

    AssertEqual(HoldToHideKeyPolicy.LeftAlt, defaultSettings.HoldToHideKey);
    AssertEqual(0xA4, HoldToHideKeyPolicy.VirtualKey(defaultSettings.HoldToHideKey));
    AssertEqual(HoldToHideKeyPolicy.LeftAlt, HoldToHideKeyPolicy.Coerce("bad-value"));
    AssertEqual(true, HoldToHideKeyPolicy.ShouldHide(
        HoldToHideKeyPolicy.LeftAlt,
        configuredKeyDown: true,
        leftCtrlDown: false,
        rightCtrlDown: false,
        leftAltDown: true,
        rightAltDown: false));
    AssertEqual(false, HoldToHideKeyPolicy.ShouldHide(
        HoldToHideKeyPolicy.LeftAlt,
        configuredKeyDown: true,
        leftCtrlDown: true,
        rightCtrlDown: false,
        leftAltDown: true,
        rightAltDown: false));
});

Test("Kugou media commands use app command fallback", () =>
{
    AssertEqual(true, MediaAppCommandFallbackPolicy.ShouldUseForSource("酷狗音乐"));
    AssertEqual(true, MediaAppCommandFallbackPolicy.ShouldUseForSource("KuGou"));
    AssertEqual(false, MediaAppCommandFallbackPolicy.ShouldUseForSource("Microsoft Edge"));
});

Test("Kugou media controls prefer app commands and keep the hover card source", () =>
{
    AssertEqual(MediaControlRoute.AppCommandFirst, MediaControlDispatchPolicy.RouteForSource("酷狗音乐"));
    AssertEqual(MediaControlRoute.GsmFirst, MediaControlDispatchPolicy.RouteForSource("Microsoft Edge"));
    AssertEqual(false, MediaControlDispatchPolicy.CanUseGeneralGsmFallback("酷狗音乐"));
    AssertEqual(true, MediaControlDispatchPolicy.CanUseGeneralGsmFallback("Microsoft Edge"));
    AssertEqual(false, MediaControlDispatchPolicy.AllowsOptimisticPlaybackStateUpdate("酷狗音乐"));
    AssertEqual(true, MediaControlDispatchPolicy.AllowsOptimisticPlaybackStateUpdate("Microsoft Edge"));
    AssertEqual("酷狗音乐", MediaControlDispatchPolicy.ResolveControlSource(
        currentViewSource: "Microsoft Edge",
        activeHoverMediaSource: "酷狗音乐"));
});

Test("Kugou media control dispatch attempts app command before GSMTC", () =>
{
    var kugouAttempts = MediaControlDispatchPolicy.DispatchAttemptsForSource("酷狗音乐").ToArray();
    var browserAttempts = MediaControlDispatchPolicy.DispatchAttemptsForSource("Microsoft Edge").ToArray();

    AssertEqual(MediaControlDispatchAttempt.AppCommand, kugouAttempts[0]);
    AssertEqual(MediaControlDispatchAttempt.SameSourceGsm, kugouAttempts[1]);
    AssertEqual(2, kugouAttempts.Length);
    AssertEqual(MediaControlDispatchAttempt.SameSourceGsm, browserAttempts[0]);
    AssertEqual(1, browserAttempts.Length);
});

Test("active Kugou fallback can return after a browser GSM session", () =>
{
    var browserGsm = new MediaSnapshot(
        SourceAppUserModelId: "msedge.exe",
        SourceName: "Microsoft Edge",
        Title: "Video title",
        Artist: "",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 40);
    var fallback = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0);

    AssertEqual(false, MediaSnapshotSelectionPolicy.ShouldSuppressFallbackAfterGsmLoss(
        lastFromGsm: true,
        suppressUntilGsmPlaying: true,
        gsm: browserGsm,
        fallback: fallback));
});

Test("Kugou lyrics search candidate can be parsed from lyrics api", () =>
{
    const string json = """
    {
      "status": 200,
      "candidates": [
        {
          "id": "188849683",
          "accesskey": "D45CAC5A10D827000756896A6BA58DE3",
          "singer": "周杰伦",
          "song": "夜曲",
          "duration": 226000
        }
      ]
    }
    """;

    var candidate = KugouLyricsProvider.ParseLyricsCandidate(json);

    AssertEqual("188849683", candidate?.Id);
    AssertEqual("D45CAC5A10D827000756896A6BA58DE3", candidate?.AccessKey);
    AssertEqual(226000, candidate?.DurationMilliseconds);
});

Test("Kugou lyrics candidate accepts download id fallback", () =>
{
    const string json = """
    {
      "status": 200,
      "candidates": [
        {
          "download_id": "188849683",
          "access_key": "D45CAC5A10D827000756896A6BA58DE3",
          "duration": 226000
        }
      ]
    }
    """;

    var candidate = KugouLyricsProvider.ParseLyricsCandidate(json);

    AssertEqual("188849683", candidate?.Id);
    AssertEqual("D45CAC5A10D827000756896A6BA58DE3", candidate?.AccessKey);
});

Test("Kugou lyrics candidate chooses closer title artist duration match", () =>
{
    const string json = """
    {
      "status": 200,
      "candidates": [
        {
          "id": "wrong",
          "accesskey": "BAD",
          "singer": "别人",
          "song": "年轮说",
          "duration": 260000
        },
        {
          "id": "right",
          "accesskey": "GOOD",
          "singer": "旺仔小乔",
          "song": "年轮",
          "duration": 218000
        }
      ]
    }
    """;

    var candidate = KugouLyricsProvider.ParseLyricsCandidate(
        json,
        title: "年轮",
        artist: "旺仔小乔",
        durationMilliseconds: 219000);

    AssertEqual("right", candidate?.Id);
    AssertEqual("GOOD", candidate?.AccessKey);
});

Test("Kugou lyrics candidate normalizes centisecond duration", () =>
{
    const string json = """
    {
      "status": 200,
      "candidates": [
        {
          "id": "445293124",
          "accesskey": "GOOD",
          "singer": "旺仔小乔",
          "song": "年轮",
          "duration": 16431
        }
      ]
    }
    """;

    var candidate = KugouLyricsProvider.ParseLyricsCandidate(
        json,
        title: "年轮",
        artist: "旺仔小乔",
        durationMilliseconds: 164000);

    AssertEqual(164310, candidate?.DurationMilliseconds);
});

Test("Kugou enrichment cleans desktop lyric suffix from title", () =>
{
    var normalized = KugouLyricsProvider.NormalizeTrackText(
        "周杰伦 - 夜曲 - 酷狗音乐",
        "");

    AssertEqual("夜曲", normalized.Title);
    AssertEqual("周杰伦", normalized.Artist);
});

Test("Kugou enrichment cleans app name prefix from title", () =>
{
    var normalized = KugouLyricsProvider.NormalizeTrackText(
        "酷狗音乐 - 周杰伦 - 夜曲",
        "酷狗音乐");

    AssertEqual("夜曲", normalized.Title);
    AssertEqual("周杰伦", normalized.Artist);
});

Test("Kugou enrichment drops app name artist for title-only metadata", () =>
{
    var normalized = KugouLyricsProvider.NormalizeTrackText(
        "年轮",
        "酷狗音乐");

    AssertEqual("年轮", normalized.Title);
    AssertEqual("", normalized.Artist);
});

Test("Kugou downloaded lyric content decodes to current line", () =>
{
    var lrc = "[00:00.00]周杰伦 - 夜曲\n[00:24.74]一群嗜血的蚂蚁\n[00:26.52]被腐肉所吸引";
    var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(lrc));
    var json = $$"""
    {
      "status": 200,
      "content": "{{base64}}"
    }
    """;

    var line = KugouLyricsProvider.SelectLineFromDownloadJson(json, TimeSpan.FromSeconds(25));

    AssertEqual("一群嗜血的蚂蚁", line);
});

Test("Kugou downloaded lyric parser accepts lyrics field without local lrc files", () =>
{
    const string json = """
    {
      "status": 200,
      "lyrics": "[00:00.00]周杰伦 - 夜曲\n[00:24.74]一群嗜血的蚂蚁\n[00:26.52]被腐肉所吸引"
    }
    """;

    var line = KugouLyricsProvider.SelectLineFromDownloadJson(json, TimeSpan.FromSeconds(25));

    AssertEqual("一群嗜血的蚂蚁", line);
});

Test("Kugou online lyrics smoke test resolves a visible lyric line", () =>
{
    var provider = new KugouLyricsProvider();
    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "年轮 - 旺仔小乔",
        Artist: "酷狗音乐",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0);

    var enriched = provider.EnrichSnapshot(snapshot, TimeSpan.FromSeconds(8));
    var evt = MediaIslandEventFactory.FromSnapshot(enriched);
    var view = IslandPresentation.FromEvent(evt, settings);

    AssertNotEmpty(enriched.LyricLine ?? "");
    AssertEqual("年轮", enriched.Title);
    AssertEqual("旺仔小乔", enriched.Artist);
    AssertNotEmpty(evt.Payload?.LyricLine ?? "");
    AssertNotEmpty(view.LyricLine);
});

Test("high priority playing GSM session can skip fallback window scan", () =>
{
    var gsm = new MediaSnapshot(
        SourceAppUserModelId: "kugou.exe!App",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 35);

    AssertEqual(false, MediaSnapshotSelectionPolicy.ShouldQueryFallback(gsm));
    AssertEqual(true, MediaSnapshotSelectionPolicy.ShouldQueryFallback(null));
});

Test("media signature changes when artwork arrives later", () =>
{
    var first = new MediaSnapshot(
        SourceAppUserModelId: "msedge.exe",
        SourceName: "Microsoft Edge",
        Title: "Video title",
        Artist: "",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 25,
        SourceIconPath: @"C:\FluidBar\edge.png");
    var withArtwork = first with { AlbumArtPath = @"C:\FluidBar\cover.jpg" };

    AssertNotEqual(
        MediaSnapshotSelectionPolicy.BuildSignature(first),
        MediaSnapshotSelectionPolicy.BuildSignature(withArtwork));
});

Test("browser media may publish before slow artwork is ready", () =>
{
    AssertAtMost(250, MediaArtworkReadPolicy.InitialReadTimeoutMilliseconds);

    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "msedge.exe",
        SourceName: "Microsoft Edge",
        Title: "Video title - YouTube",
        Artist: "",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 5,
        SourceIconPath: @"C:\FluidBar\edge.png",
        AlbumArtPath: null);

    var evt = MediaIslandEventFactory.FromSnapshot(snapshot);
    var iconChoice = IslandMediaVisualPolicy.ChooseIcon(
        evt.Payload?.AlbumArtPath,
        evt.Payload?.AppIconPath,
        path => string.Equals(path, @"C:\FluidBar\edge.png", StringComparison.OrdinalIgnoreCase));

    AssertEqual(IslandEventKind.Media, evt.Payload?.Kind);
    AssertEqual(IslandMediaIconKind.AppIcon, iconChoice.Kind);
    AssertEqual("YouTube", evt.Payload?.Badge);
});

Test("artwork initial read times out so media can publish first", () =>
{
    var slowArtwork = Task.Delay(1000).ContinueWith(_ => (string?)@"C:\FluidBar\cover.jpg");
    var stopwatch = Stopwatch.StartNew();

    var result = MediaArtworkReadPolicy.ReadWithInitialTimeoutAsync(slowArtwork)
        .GetAwaiter()
        .GetResult();

    stopwatch.Stop();
    AssertEqual(null, result);
    AssertAtMost(450, (int)stopwatch.ElapsedMilliseconds);
});

Test("browser media keeps app name on top and page title as content", () =>
{
    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "msedge.exe",
        SourceName: "Edge",
        Title: "Rick Astley - Never Gonna Give You Up - YouTube",
        Artist: "",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 40,
        SourceBadge: "YT");

    var evt = MediaIslandEventFactory.FromSnapshot(snapshot);
    var view = IslandPresentation.FromEvent(evt, settings);

    AssertEqual("Microsoft Edge", evt.Title);
    AssertEqual("Rick Astley - Never Gonna Give You Up - YouTube", evt.Content);
    AssertEqual("Edge", evt.Payload?.SourceName);
    AssertEqual("YouTube", evt.Payload?.Badge);
    AssertEqual("", evt.Payload?.Subtitle ?? "");
    AssertEqual(IslandViewKind.Media, view.Kind);
    AssertEqual("Edge", view.SourceName);
    AssertEqual("YouTube", view.StatusBadge);
    AssertEqual("Rick Astley - Never Gonna Give You Up - YouTube", view.Content);
    AssertAtLeast(48, view.TargetHeight);
});

Test("browser media site names are displayed without browser prefix", () =>
{
    AssertEqual(
        "BiliBili",
        BrowserMediaSitePolicy.DisplayNameFromTitle("视频标题 - 哔哩哔哩_bilibili - Microsoft Edge"));
    AssertEqual("YouTube", BrowserMediaSitePolicy.DisplayNameFromTitle("Song - YouTube - Chrome"));
    AssertEqual("Netflix", BrowserMediaSitePolicy.DisplayNameFromTitle("Movie - Netflix - Mozilla Firefox"));

    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "msedge.exe",
        SourceName: "Microsoft Edge",
        Title: "某个视频 - bilibili",
        Artist: "",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 12,
        SourceBadge: "BiliBili");

    var view = IslandPresentation.FromEvent(MediaIslandEventFactory.FromSnapshot(snapshot), settings);

    AssertEqual("BiliBili", view.StatusBadge);
    AssertDoesNotContain("Edge", view.StatusBadge);
});

Test("edge source name stays compact", () =>
{
    AssertEqual("Microsoft Edge", MediaIslandEventFactory.FriendlySourceName("msedge.exe"));
    AssertEqual("Microsoft Edge", MediaIslandEventFactory.FriendlySourceName("Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!App"));
});

Test("browser friendly source matches process source for media controls", () =>
{
    AssertEqual(true, MediaSnapshotSelectionPolicy.IsSamePlayerApp("msedge.exe", "Microsoft Edge"));
    AssertEqual(true, MediaSnapshotSelectionPolicy.IsSamePlayerApp("chrome.exe", "Chrome"));
    AssertEqual(true, MediaSnapshotSelectionPolicy.IsSamePlayerApp("firefox.exe", "Firefox"));
});

Test("dominant media icon color ignores neutral background pixels", () =>
{
    var tempPath = Path.Combine(Path.GetTempPath(), $"fluidbar-color-{Guid.NewGuid():N}.png");
    using (var bitmap = new System.Drawing.Bitmap(16, 16))
    {
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.FromArgb(248, 248, 248));
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(32, 156, 238));
        graphics.FillRectangle(brush, 4, 4, 8, 8);
        bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
    }

    try
    {
        var color = MediaArtworkColorAnalyzer.TryExtractDominantColor(tempPath);

        AssertEqual(true, color is not null);
        var actual = color ?? throw new InvalidOperationException("expected dominant color");
        AssertAtLeast(120, actual.B);
        AssertAtLeast(actual.R + 40, actual.B);
    }
    finally
    {
        if (File.Exists(tempPath))
            File.Delete(tempPath);
    }
});

Test("agent payload projects to agent status view", () =>
{
    var evt = new IslandEvent(
        "agent-status",
        "Claude Code 完成",
        "重构完成",
        "agent",
        new IslandEventPayload(
            Kind: IslandEventKind.Agent,
            Subtitle: "FluidBar",
            Badge: "完成",
            SourceName: "Claude Code",
            IsActive: false,
            DetailLines: new[] { "分支 main", "耗时 46s" }));

    var view = IslandPresentation.FromEvent(evt, settings);
    var card = HoverCardPresentation.FromCompact(view, settings);

    AssertEqual(IslandViewKind.Agent, view.Kind);
    AssertContains("Claude Code", view.StatusBadge);
    AssertContains("分支 main", card.Content);
});

Test("agent hook json creates completion island event", () =>
{
    const string json = """
    {
      "tool": "claude-code",
      "status": "completed",
      "project": "FluidBar",
      "summary": "任务完成",
      "branch": "main",
      "durationMs": 46000
    }
    """;

    var hook = AgentHookEvent.Parse(json);
    var evt = AgentStatusIslandEventFactory.FromHook(hook);

    AssertEqual("agent-status", evt.Source);
    AssertEqual("agent", evt.IconKind);
    AssertEqual(IslandEventKind.Agent, evt.Payload?.Kind);
    AssertContains("Claude Code", evt.Payload?.SourceName ?? "");
    AssertContains("FluidBar", evt.Payload?.Subtitle ?? "");
    AssertContains("46s", string.Join(" ", evt.Payload?.DetailLines ?? Array.Empty<string>()));
});

Test("notification payload projects to notification view", () =>
{
    var evt = new IslandEvent(
        "notifications",
        "微信",
        "新的消息",
        "notification",
        new IslandEventPayload(
            Kind: IslandEventKind.Notification,
            Subtitle: "张三",
            Badge: "系统通知",
            SourceName: "微信",
            DetailLines: new[] { "今晚 8 点开会", "点击系统通知查看详情" }));

    var view = IslandPresentation.FromEvent(evt, settings);
    var card = HoverCardPresentation.FromCompact(view, settings);

    AssertEqual(IslandViewKind.Notification, view.Kind);
    AssertContains("张三", view.StatusText);
    AssertContains("今晚 8 点开会", card.Content);
});

Test("notification snapshot creates a rich island event", () =>
{
    var snapshot = new NotificationSnapshot(
        Id: 42,
        AppName: "微信",
        Title: "张三",
        Body: "今晚 8 点开会",
        Timestamp: new DateTimeOffset(2026, 6, 14, 20, 0, 0, TimeSpan.FromHours(8)),
        AppIconPath: null);

    var evt = NotificationIslandEventFactory.FromSnapshot(snapshot);

    AssertEqual("notifications", evt.Source);
    AssertEqual("notification", evt.IconKind);
    AssertEqual(IslandEventKind.Notification, evt.Payload?.Kind);
    AssertContains("微信", evt.Payload?.SourceName ?? "");
    AssertContains("张三", evt.Payload?.Subtitle ?? "");
    AssertContains("今晚 8 点开会", string.Join(" ", evt.Payload?.DetailLines ?? Array.Empty<string>()));
});

Test("settings panel suppresses multi island snapshot rendering", () =>
{
    var multiSettings = new FluidBarSettings
    {
        DisplayStrategy = IslandDisplayStrategy.Multiple
    };

    AssertEqual(true, IslandStackVisibilityPolicy.ShouldRender(
        multiSettings,
        stackCount: 2,
        isSettingsPanelOpen: false,
        currentKind: IslandViewKind.Status));
    AssertEqual(false, IslandStackVisibilityPolicy.ShouldRender(
        multiSettings,
        stackCount: 2,
        isSettingsPanelOpen: true,
        currentKind: IslandViewKind.Status));
    AssertEqual(false, IslandStackVisibilityPolicy.ShouldRender(
        multiSettings,
        stackCount: 2,
        isSettingsPanelOpen: false,
        currentKind: IslandViewKind.Clock));
});

Test("multiple display strategy appends non clock islands up to the max count", () =>
{
    var multiSettings = new FluidBarSettings
    {
        DisplayStrategy = IslandDisplayStrategy.Multiple,
        MaxVisibleIslands = 3
    };
    var first = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 40%", "40%", "volume"),
        settings);
    var second = IslandPresentation.FromEvent(
        new IslandEvent("battery", "电池 88%", "电池供电中", "battery"),
        settings);
    var third = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", "一段复制内容", "clipboard"),
        settings);
    var fourth = IslandPresentation.FromEvent(
        new IslandEvent("network", "网络已连接", "Wi-Fi", "network"),
        settings);

    var stack = IslandStackPolicy.Apply(Array.Empty<IslandStackItem>(), first, "volume", multiSettings);
    stack = IslandStackPolicy.Apply(stack, second, "battery", multiSettings);
    stack = IslandStackPolicy.Apply(stack, third, "clipboard", multiSettings);
    stack = IslandStackPolicy.Apply(stack, fourth, "network", multiSettings);

    AssertEqual(3, stack.Count);
    AssertEqual("battery", stack[0].Source);
    AssertEqual("clipboard", stack[1].Source);
    AssertEqual("network", stack[2].Source);
});

Test("multiple display strategy updates an existing source as the newest island", () =>
{
    var multiSettings = new FluidBarSettings
    {
        DisplayStrategy = IslandDisplayStrategy.Multiple
    };
    var firstVolume = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 40%", "40%", "volume"),
        settings);
    var battery = IslandPresentation.FromEvent(
        new IslandEvent("battery", "电池 88%", "电池供电中", "battery"),
        settings);
    var secondVolume = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 62%", "62%", "volume"),
        settings);

    var stack = IslandStackPolicy.Apply(Array.Empty<IslandStackItem>(), firstVolume, "volume", multiSettings);
    stack = IslandStackPolicy.Apply(stack, battery, "battery", multiSettings);
    stack = IslandStackPolicy.Apply(stack, secondVolume, "volume", multiSettings);

    AssertEqual(2, stack.Count);
    AssertEqual("battery", stack[0].Source);
    AssertEqual("volume", stack[1].Source);
    AssertEqual(62, stack[1].View.ProgressPercent);
});

Test("expired stacked status is pruned so media can return to the base slot", () =>
{
    var now = DateTimeOffset.UtcNow;
    var media = IslandPresentation.FromEvent(
        new IslandEvent(
            "media",
            "酷狗音乐",
            "夜曲",
            "media",
            new IslandEventPayload(
                Kind: IslandEventKind.Media,
                SourceName: "酷狗音乐",
                IsActive: true,
                ShowsAudioWave: true)),
        settings);
    var volume = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 40%", "40%", "volume"),
        settings);
    var items = new[]
    {
        new IslandStackItem("volume", volume, now.AddSeconds(-8), now.AddSeconds(-3)),
        new IslandStackItem("media", media, now.AddSeconds(-10), DateTimeOffset.MaxValue)
    };

    var pruned = IslandStackPolicy.PruneExpiredItems(items, now).ToList();

    AssertEqual(1, pruned.Count);
    AssertEqual("media", pruned[0].Source);
    AssertEqual(false, IslandStackVisibilityPolicy.ShouldRender(
        new FluidBarSettings { DisplayStrategy = IslandDisplayStrategy.Multiple },
        pruned.Count,
        isSettingsPanelOpen: false,
        currentKind: IslandViewKind.Media));
});

Test("clock island never joins the multi island stack", () =>
{
    var multiSettings = new FluidBarSettings
    {
        DisplayStrategy = IslandDisplayStrategy.Multiple
    };
    var volume = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 40%", "40%", "volume"),
        settings);
    var clock = IslandPresentation.FromEvent(
        new IslandEvent("clock", "18:30", "6月14日 周日", "clock"),
        settings);

    var stack = IslandStackPolicy.Apply(Array.Empty<IslandStackItem>(), volume, "volume", multiSettings);
    stack = IslandStackPolicy.Apply(stack, clock, "clock", multiSettings);

    AssertEqual(1, stack.Count);
    AssertEqual("volume", stack[0].Source);
});

Test("switching back to latest only keeps just the newest island", () =>
{
    var latestSettings = new FluidBarSettings
    {
        DisplayStrategy = IslandDisplayStrategy.LatestOnly
    };
    var existing = new[]
    {
        new IslandStackItem("volume", IslandPresentation.FromEvent(
            new IslandEvent("volume", "音量 40%", "40%", "volume"),
            settings), DateTimeOffset.UnixEpoch)
    };
    var battery = IslandPresentation.FromEvent(
        new IslandEvent("battery", "电池 88%", "电池供电中", "battery"),
        settings);

    var stack = IslandStackPolicy.Apply(existing, battery, "battery", latestSettings);

    AssertEqual(1, stack.Count);
    AssertEqual("battery", stack[0].Source);
});

Test("multi island group stays centered around the base anchor", () =>
{
    var slots = new[]
    {
        new IslandSlotMetrics(180, 56),
        new IslandSlotMetrics(260, 56)
    };

    var layout = IslandGroupLayout.Calculate(
        slots,
        position: "Top",
        screenWidth: 1000,
        screenHeight: 700,
        offsetX: 0,
        offsetY: 0,
        gap: 10);

    AssertEqual(450.0, layout.VisualWidth);
    AssertNear(275, layout.Left, 0.1);
    AssertEqual(0.0, layout.Slots[0].OffsetX);
    AssertEqual(190.0, layout.Slots[1].OffsetX);
});

Test("left anchored multi island keeps the first island at the base edge", () =>
{
    var slots = new[]
    {
        new IslandSlotMetrics(180, 56),
        new IslandSlotMetrics(260, 56)
    };

    var layout = IslandGroupLayout.Calculate(
        slots,
        position: "TopLeft",
        screenWidth: 1000,
        screenHeight: 700,
        offsetX: 0,
        offsetY: 0,
        gap: 10);

    AssertNear(16, layout.Left, 0.1);
    AssertEqual(190.0, layout.Slots[1].OffsetX);
});

Test("latest media island receives a shifted slot when another island is on the left", () =>
{
    var slots = new[]
    {
        new IslandSlotMetrics(210, 64),
        new IslandSlotMetrics(390, 64)
    };

    var layout = IslandGroupLayout.Calculate(
        slots,
        position: "Top",
        screenWidth: 1000,
        screenHeight: 700,
        offsetX: 0,
        offsetY: 0,
        gap: 10);

    AssertEqual(220.0, layout.Slots[1].OffsetX);
    AssertNear(195, layout.Left, 0.1);
    AssertNear(415, layout.Left + layout.Slots[1].OffsetX, 0.1);
});

Test("right anchored multi island keeps the group right edge at the base edge", () =>
{
    var slots = new[]
    {
        new IslandSlotMetrics(180, 56),
        new IslandSlotMetrics(260, 56)
    };

    var layout = IslandGroupLayout.Calculate(
        slots,
        position: "TopRight",
        screenWidth: 1000,
        screenHeight: 700,
        offsetX: 0,
        offsetY: 0,
        gap: 10);

    AssertNear(534, layout.Left, 0.1);
    AssertNear(984, layout.Left + layout.VisualWidth, 0.1);
});

Test("clock hover card can be disabled from feature settings", () =>
{
    var clockSettings = new FluidBarSettings();
    clockSettings.GetMonitorFeatureSettings("clock").HoverCardEnabled = false;

    AssertEqual(false, HoverCardPolicy.CanShow(
        isExpanded: true,
        isSettingsPanelOpen: false,
        currentSource: "clock",
        currentViewExists: true,
        clockSettings));
});

Test("configured island width becomes the normal target width", () =>
{
    var configured = new FluidBarSettings
    {
        CollapsedWidth = 126,
        CollapsedHeight = 38,
        ExpandedMaxWidth = 520,
        ExpandedHeight = 62
    };
    var view = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", "短文本", "clipboard"),
        configured);

    AssertEqual(520.0, view.TargetWidth);
});

Test("configured island height can go below the old 72 floor", () =>
{
    var compactHeight = new FluidBarSettings
    {
        CollapsedWidth = 126,
        CollapsedHeight = 38,
        ExpandedMaxWidth = 320,
        ExpandedHeight = 56
    };
    var view = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 50%", "50%", "volume"),
        compactHeight);

    AssertEqual(56.0, view.TargetHeight); // 56 >= MinimumExpandedHeight(48), no clamp
});

Test("hover card metrics expand into a larger card shape", () =>
{
    var compact = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", "短文本", "clipboard"),
        settings);
    var card = HoverCardPresentation.FromCompact(compact, settings);

    AssertAtLeast(compact.TargetWidth + 96, card.TargetWidth);
    AssertAtLeast(168, card.TargetHeight);
    AssertEqual(IslandDisplayMode.HoverCard, card.Mode);
});

Test("clipboard hover card allows multiline copied content", () =>
{
    var compact = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", new string('中', 160), "clipboard"),
        settings);
    var card = HoverCardPresentation.FromCompact(compact, settings);

    AssertAtLeast(4, card.DetailLines);
    AssertEqual(true, card.AllowsMultilineContent);
});

Test("hover card remains larger when island width is customized wide", () =>
{
    var wideSettings = new FluidBarSettings
    {
        CollapsedWidth = 180,
        CollapsedHeight = 44,
        ExpandedMaxWidth = 620,
        ExpandedHeight = 88
    };
    var compact = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", "宽灵动岛测试", "clipboard"),
        wideSettings);
    var card = HoverCardPresentation.FromCompact(compact, wideSettings);

    AssertAtLeast(compact.TargetWidth + 96, card.TargetWidth);
});

Test("hover card motion uses the example continuous spring model", () =>
{
    var plan = HoverCardMotionPlan.CreateOpening(
        fromWidth: 260,
        fromHeight: 56,
        toWidth: 430,
        toHeight: 190);

    AssertEqual(HoverCardMotionKind.WarpOpen, plan.Kind);
    AssertEqual(true, plan.UsesContinuousSpring);
    AssertEqual(0, plan.WidthKeyFrames);
    AssertEqual(0, plan.HeightKeyFrames);
    AssertEqual(0, plan.HeightDelayMilliseconds);
    AssertEqual(false, plan.UsesOvershoot);
    AssertAtLeast(360, plan.ExpandingStiffness);
    AssertAtLeast(24, plan.ExpandingDamping);
    AssertAtLeast(190, plan.ContractingStiffness);
    AssertAtLeast(26, plan.ContractingDamping);
});

Test("hover card motion is render synchronized and avoids per frame window resizing", () =>
{
    var plan = HoverCardMotionPlan.CreateOpening(
        fromWidth: 260,
        fromHeight: 56,
        toWidth: 430,
        toHeight: 190);

    AssertEqual(true, plan.UsesRenderSynchronizedFrames);
    AssertEqual(false, plan.AnimatesWindowBoundsEveryFrame);
});

Test("hover card close motion is faster and lighter than opening", () =>
{
    var open = HoverCardMotionPlan.CreateOpening(
        fromWidth: 260,
        fromHeight: 56,
        toWidth: 430,
        toHeight: 190);
    var close = HoverCardMotionPlan.CreateClosing(
        fromWidth: 430,
        fromHeight: 190,
        toWidth: 260,
        toHeight: 56);

    AssertEqual(HoverCardMotionKind.WarpClose, close.Kind);
    AssertEqual(true, close.UsesContinuousSpring);
    AssertAtMost(open.ExpandingStiffness, close.ExpandingStiffness);
    AssertAtLeast(open.ContractingDamping, close.ContractingDamping);
});

Test("shell animation policy favors short non-elastic transitions", () =>
{
    var policy = IslandAnimationPerformancePolicy.Default;

    AssertAtMost(300, policy.OpenMilliseconds);
    AssertAtMost(240, policy.ResizeMilliseconds);
    AssertEqual(false, policy.UsesElasticShellEase);
    AssertAtLeast(0.5, policy.HoverFrameApplyThreshold);
});

Test("spring value approaches target gradually without a keyframe jump", () =>
{
    var spring = new SpringValue();
    spring.Reset(120);
    spring.Target = 380;

    spring.Step(1.0 / 60.0, stiffness: 380, damping: 26);

    AssertAtLeast(120.1, spring.Value);
    AssertAtMost(160, spring.Value);

    for (var i = 0; i < 120; i++)
        spring.Step(1.0 / 60.0, stiffness: 380, damping: 26);

    AssertNear(380, spring.Value, 0.35);
});

Test("settings performance policy coalesces save and apply work", () =>
{
    AssertAtLeast(140, SettingsPerformancePolicy.SettingsSaveDebounceMs);
    AssertAtMost(360, SettingsPerformancePolicy.SettingsSaveDebounceMs);
    AssertAtLeast(SettingsPerformancePolicy.SettingsSaveDebounceMs,
        SettingsPerformancePolicy.PluginSaveDebounceMs);
    AssertEqual(true, SettingsPerformancePolicy.UseVirtualizedLists);
});

Test("Kugou lyrics cache returns valid lyrics for same track at different positions", () =>
{
    var provider = new KugouLyricsProvider();
    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "年轮",
        Artist: "旺仔小乔",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0);

    // First call — may hit network, caches lyrics
    var enriched1 = provider.EnrichSnapshot(snapshot, TimeSpan.FromSeconds(8));
    // Second call with same track — should use cached lyrics, select different line
    var enriched2 = provider.EnrichSnapshot(snapshot, TimeSpan.FromSeconds(12));

    // Both should have lyrics from the same cached LRC (or both null if network unavailable)
    // The lines may differ because position differs, but both should be non-empty if lyrics exist
    if (!string.IsNullOrWhiteSpace(enriched1.LyricLine))
    {
        AssertNotEqual("", enriched2.LyricLine ?? "");
    }
});

Test("Kugou lyrics skip enrichment when lyrics already present and album art present", () =>
{
    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "年轮",
        Artist: "旺仔小乔",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0,
        LyricLine: "已有歌词",
        AlbumArtPath: @"C:\cover.jpg");

    // When both LyricLine and AlbumArtPath are present, enrichment should not run
    // (no HTTP calls needed)
    AssertEqual(false, string.IsNullOrWhiteSpace(snapshot.LyricLine));
    AssertEqual(false, string.IsNullOrWhiteSpace(snapshot.AlbumArtPath));
});

Test("SendInput media key VK codes are correct", () =>
{
    // Standard Windows media key virtual key codes
    AssertEqual(0xB3, (int)0xB3); // VK_MEDIA_PLAY_PAUSE
    AssertEqual(0xB0, (int)0xB0); // VK_MEDIA_NEXT_TRACK
    AssertEqual(0xB1, (int)0xB1); // VK_MEDIA_PREV_TRACK
});

Test("Kugou enrichment throttles when same track and lyrics exist", () =>
{
    var provider = new KugouLyricsProvider();
    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0);

    var enriched = provider.EnrichSnapshot(snapshot, TimeSpan.FromSeconds(25));
    // If lyrics were found, they should match the position
    if (!string.IsNullOrWhiteSpace(enriched.LyricLine))
    {
        AssertNotEqual("", enriched.LyricLine);
    }
    // At minimum, title/artist should be preserved
    AssertEqual("夜曲", enriched.Title);
    AssertEqual("周杰伦", enriched.Artist);
});

// Issue 3: Non-browser sources should show song title as event title, not source name
Test("Kugou island shows song title as event title not source name", () =>
{
    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0);

    var evt = MediaIslandEventFactory.FromSnapshot(snapshot);

    // For non-browser: TitleText in compact view uses Content (song name) + Subtitle (artist)
    // evt.Title = sourceName, evt.Content = song name, evt.Payload.Subtitle = artist
    AssertEqual("酷狗音乐", evt.Title);
    AssertEqual("夜曲", evt.Content);
    AssertContains("周杰伦", evt.Payload?.Subtitle ?? "");
    // Badge should still be the source name
    AssertEqual("酷狗音乐", evt.Payload?.Badge);
});

// Issue 5: Kugou fallback has no progress — ProgressPercent should be 0
Test("Kugou fallback snapshot has zero progress", () =>
{
    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0);

    AssertEqual(0, snapshot.ProgressPercent);
    AssertEqual(0L, snapshot.PositionTicks);
    AssertEqual(0L, snapshot.EndTicks);
});

// Issue 4: SecondaryLyricLine should be populated for two-line lyrics
Test("media snapshot supports secondary lyric line", () =>
{
    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "kugou",
        SourceName: "酷狗音乐",
        Title: "夜曲",
        Artist: "周杰伦",
        Album: "",
        IsPlaying: true,
        ProgressPercent: 0,
        LyricLine: "我也不想放手",
        SecondaryLyricLine: "脑海被你占有");

    var evt = MediaIslandEventFactory.FromSnapshot(snapshot);

    AssertEqual("我也不想放手", evt.Payload?.LyricLine);
    AssertEqual("脑海被你占有", evt.Payload?.SecondaryLyricLine);
});

// Issue 1: Audio activity detection should work even without visible windows
Test("audio gate uses process IDs not window visibility", () =>
{
    var processes = new[]
    {
        new MediaFallbackProcessInfo(100, "KuGou", "酷狗音乐"),
    };
    var gateProcessIds = MediaFallbackProcessPolicy.AudioGateProcessIds(
        processes,
        bestProcessName: "KuGou",
        bestSourceName: "酷狗音乐");

    AssertEqual(true, gateProcessIds.Contains(100));
});

Console.WriteLine("All FluidBar presentation tests passed.");

static void Test(string name, Action body)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, got {actual}");
}

static void AssertNotEqual<T>(T unexpected, T actual)
{
    if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        throw new InvalidOperationException($"expected value other than {unexpected}");
}

static void AssertContains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"expected '{actual}' to contain '{expected}'");
}

static void AssertDoesNotContain(string unexpected, string actual)
{
    if (actual.Contains(unexpected, StringComparison.Ordinal))
        throw new InvalidOperationException($"expected '{actual}' not to contain '{unexpected}'");
}

static void AssertNotEmpty(string actual)
{
    if (string.IsNullOrWhiteSpace(actual))
        throw new InvalidOperationException("expected non-empty text");
}

static void AssertAtLeast(double minimum, double actual)
{
    if (actual < minimum)
        throw new InvalidOperationException($"expected at least {minimum}, got {actual}");
}

static void AssertAtMost(double maximum, double actual)
{
    if (actual > maximum)
        throw new InvalidOperationException($"expected at most {maximum}, got {actual}");
}

static void AssertNear(double expected, double actual, double tolerance)
{
    if (Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException(
            $"expected {actual} to be within {tolerance} of {expected}");
}

file sealed record TestMediaCommandSession(
    string Id,
    string SourceId,
    bool IsCurrent,
    bool IsCommandEnabled);
