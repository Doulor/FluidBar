using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

public sealed class MediaSelectionTests
{
    private static MediaSessionDescriptor Session(string aumid, MediaPlaybackState state,
        string? title = "Track", int minute = 0)
        => new(aumid, state, title, new DateTimeOffset(2026, 10, 18, 12, minute, 0, TimeSpan.Zero));

    [Fact]
    public void SelectActive_PrefersPlayingSession()
    {
        var sessions = new[]
        {
            Session("Spotify", MediaPlaybackState.Paused),
            Session("chrome", MediaPlaybackState.Playing, minute: 1),
            Session("kugou", MediaPlaybackState.Stopped),
        };
        Assert.Equal("chrome", MediaSourceSelectionPolicy.SelectActive(sessions)!.SourceAppUserModelId);
    }

    [Fact]
    public void SelectActive_PrefersMusicPlayerOverBrowserWhenBothPaused()
    {
        var sessions = new[]
        {
            Session("chrome", MediaPlaybackState.Paused, minute: 5),
            Session("cloudmusic", MediaPlaybackState.Paused),
        };
        Assert.Equal("cloudmusic", MediaSourceSelectionPolicy.SelectActive(sessions)!.SourceAppUserModelId);
    }

    [Fact]
    public void SelectActive_IgnoresClosedSessions()
    {
        var sessions = new[]
        {
            Session("Spotify", MediaPlaybackState.Closed),
            Session("kugou", MediaPlaybackState.Paused),
        };
        Assert.Equal("kugou", MediaSourceSelectionPolicy.SelectActive(sessions)!.SourceAppUserModelId);
    }

    [Fact]
    public void SelectActive_ReturnsNullForNoUsableSessions()
    {
        Assert.Null(MediaSourceSelectionPolicy.SelectActive(Array.Empty<MediaSessionDescriptor>()));
        Assert.Null(MediaSourceSelectionPolicy.SelectActive(
            new[] { Session("", MediaPlaybackState.Playing) }));
    }

    [Fact]
    public void SelectActive_DemotesAppNameOnlyTitles()
    {
        var sessions = new[]
        {
            Session("Spotify", MediaPlaybackState.Paused, "Spotify"),
            Session("kugou", MediaPlaybackState.Paused, "Real Song"),
        };
        Assert.Equal("kugou", MediaSourceSelectionPolicy.SelectActive(sessions)!.SourceAppUserModelId);
    }

    [Fact]
    public void SourcePriority_RanksPlayersBrowsersAndOthers()
    {
        Assert.True(MediaSourceSelectionPolicy.GetSourcePriority("KugouMusic.exe") >
                    MediaSourceSelectionPolicy.GetSourcePriority("chrome"));
        Assert.True(MediaSourceSelectionPolicy.GetSourcePriority("chrome") >
                    MediaSourceSelectionPolicy.GetSourcePriority("notepad"));
        Assert.Equal(0, MediaSourceSelectionPolicy.GetSourcePriority(null));
    }

    [Theory]
    [InlineData("Spotify", true)]
    [InlineData("酷狗音乐", true)]
    [InlineData("", true)]
    [InlineData("Bohemian Rhapsody", false)]
    [InlineData("{12345678-1234-1234-1234-123456789012}", true)]
    public void IsAppNameOnlyTitle_DetectsPlaceholders(string title, bool expected)
    {
        Assert.Equal(expected, MediaSourceSelectionPolicy.IsAppNameOnlyTitle(title));
    }

    [Theory]
    [InlineData("app://./renderer/index.html?webcontentsid=8&processGroupNS=createBlankWindow1#/screen-record/result", true)]
    [InlineData("file:///C:/Music/track.mp3", true)]
    [InlineData("https://example.com/watch?v=1", true)]
    [InlineData("./renderer/index.html", true)]
    [InlineData("C:/Program Files/App/renderer/index.js", true)]
    [InlineData("Midnight City", false)]
    [InlineData("AC/DC - Thunderstruck", false)]
    [InlineData("正在播放：晴天 - 周杰伦", false)]
    public void IsResourceIdentifierTitle_DetectsPageUrlsButKeepsSongTitles(string title, bool expected)
    {
        Assert.Equal(expected, MediaSourceSelectionPolicy.IsResourceIdentifierTitle(title));
    }

    [Fact]
    public void SelectActive_IgnoresASessionWhoseTitleIsAPageUrl()
    {
        // The screen recorder publishes a "playing" session with its page URL as the title; a paused real track must
        // still win, and with no real track at all nothing may be selected.
        var recorder = Session("App.ScreenRecorder", MediaPlaybackState.Playing,
            "app://./renderer/index.html#/screen-record/result");
        var music = Session("SpotifyAB.SpotifyMusic", MediaPlaybackState.Paused, "Midnight City");

        Assert.Equal("SpotifyAB.SpotifyMusic", MediaSourceSelectionPolicy.SelectActive([recorder, music])?.SourceAppUserModelId);
        Assert.Null(MediaSourceSelectionPolicy.SelectActive([recorder]));
    }

    [Fact]
    public void IsSamePlayer_MatchesPackagedAndUnpackagedVariants()
    {
        Assert.True(MediaSourceSelectionPolicy.IsSamePlayer("SpotifyAB.SpotifyMusic", "Spotify.exe"));
        Assert.True(MediaSourceSelectionPolicy.IsSamePlayer("chrome", "chrome"));
        Assert.False(MediaSourceSelectionPolicy.IsSamePlayer("chrome", "kugou"));
        Assert.False(MediaSourceSelectionPolicy.IsSamePlayer(null, "chrome"));
    }

    [Fact]
    public void BuildTrackKey_IsStableAndSourceAware()
    {
        var first = MediaSourceSelectionPolicy.BuildTrackKey("spotify", "Song", "Artist", "Album");
        var second = MediaSourceSelectionPolicy.BuildTrackKey("spotify", "Song", "Artist", "Album");
        Assert.Equal(first, second);
        Assert.NotEqual(first, MediaSourceSelectionPolicy.BuildTrackKey("kugou", "Song", "Artist", "Album"));
    }
}

public sealed class ImageHeaderTests
{
    [Fact]
    public void TryReadDimensions_ReadsPng()
    {
        var png = BuildPng(64, 48);
        Assert.Equal(new ImageDimensions(64, 48), ImageHeaderReader.TryReadDimensions(png));
        Assert.Equal("image/png", ImageHeaderReader.DetectContentType(png));
    }

    [Fact]
    public void TryReadDimensions_ReadsJpeg()
    {
        // SOI + APP0 + SOF0 (8x6)
        var jpeg = new byte[]
        {
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x06, 0x00, 0x08,
            0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        };
        Assert.Equal(new ImageDimensions(8, 6), ImageHeaderReader.TryReadDimensions(jpeg));
        Assert.Equal("image/jpeg", ImageHeaderReader.DetectContentType(jpeg));
    }

    [Fact]
    public void TryReadDimensions_ReadsGifAndBmp()
    {
        var gif = new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0x20, 0x00, 0x10, 0x00 };
        Assert.Equal(new ImageDimensions(32, 16), ImageHeaderReader.TryReadDimensions(gif));

        var bmp = new byte[26];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(100).CopyTo(bmp, 18);
        BitConverter.GetBytes(-50).CopyTo(bmp, 22);
        Assert.Equal(new ImageDimensions(100, 50), ImageHeaderReader.TryReadDimensions(bmp));
    }

    [Fact]
    public void TryReadDimensions_ReadsWebP()
    {
        // VP8X: canvas size is stored as width-1 / height-1 (24-bit little-endian).
        var vp8x = new byte[30];
        "RIFF"u8.CopyTo(vp8x);
        "WEBP"u8.CopyTo(vp8x.AsSpan(8));
        "VP8X"u8.CopyTo(vp8x.AsSpan(12));
        vp8x[24] = 119;      // 120 - 1
        vp8x[27] = 89;       // 90 - 1
        Assert.Equal(new ImageDimensions(120, 90), ImageHeaderReader.TryReadDimensions(vp8x));
        Assert.Equal("image/webp", ImageHeaderReader.DetectContentType(vp8x));

        // VP8L: 14-bit (width-1) then 14-bit (height-1), little-endian bit packing.
        var vp8l = new byte[30];
        "RIFF"u8.CopyTo(vp8l);
        "WEBP"u8.CopyTo(vp8l.AsSpan(8));
        "VP8L"u8.CopyTo(vp8l.AsSpan(12));
        vp8l[20] = 0x2F;
        var bits = (uint)((64 - 1) | ((48 - 1) << 14));
        vp8l[21] = (byte)bits;
        vp8l[22] = (byte)(bits >> 8);
        vp8l[23] = (byte)(bits >> 16);
        vp8l[24] = (byte)(bits >> 24);
        Assert.Equal(new ImageDimensions(64, 48), ImageHeaderReader.TryReadDimensions(vp8l));
    }

    [Fact]
    public void TryReadDimensions_RejectsGarbage()
    {
        Assert.Null(ImageHeaderReader.TryReadDimensions(ReadOnlySpan<byte>.Empty));
        Assert.Null(ImageHeaderReader.TryReadDimensions(new byte[] { 1, 2, 3, 4 }));
        Assert.Equal("application/octet-stream", ImageHeaderReader.DetectContentType(new byte[] { 1, 2, 3 }));
    }

    private static byte[] BuildPng(int width, int height)
    {
        var png = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        png[8] = 0; png[9] = 0; png[10] = 0; png[11] = 13; // IHDR length
        new byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(png, 12);
        png[16] = (byte)(width >> 24); png[17] = (byte)(width >> 16);
        png[18] = (byte)(width >> 8); png[19] = (byte)width;
        png[20] = (byte)(height >> 24); png[21] = (byte)(height >> 16);
        png[22] = (byte)(height >> 8); png[23] = (byte)height;
        return png;
    }
}

public sealed class ArtworkCacheTests
{
    private static byte[] Png(int width, int height)
    {
        var png = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        png[16] = (byte)(width >> 24); png[17] = (byte)(width >> 16);
        png[18] = (byte)(width >> 8); png[19] = (byte)width;
        png[20] = (byte)(height >> 24); png[21] = (byte)(height >> 16);
        png[22] = (byte)(height >> 8); png[23] = (byte)height;
        return png;
    }

    [Fact]
    public void Add_ThenTryGet_ReturnsEntry()
    {
        using var cache = new ArtworkCache();
        var entry = cache.Add("track-1", Png(64, 64));
        Assert.NotNull(entry);
        Assert.Equal("image/png", entry!.ContentType);
        Assert.True(cache.TryGet("track-1", out var fetched));
        Assert.Equal(entry.Dimensions, fetched!.Dimensions);
        Assert.Equal(1, cache.Hits);
    }

    [Fact]
    public void TryGet_MissCountsAndReturnsFalse()
    {
        using var cache = new ArtworkCache();
        Assert.False(cache.TryGet("missing", out _));
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void Add_RejectsOversizedDecodedImage()
    {
        using var cache = new ArtworkCache(maxDecodedPixels: 1000);
        Assert.Null(cache.Add("huge", Png(100, 100)));
        Assert.Equal(1, cache.Rejected);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Add_RejectsUnknownFormat()
    {
        using var cache = new ArtworkCache();
        Assert.Null(cache.Add("garbage", new byte[] { 1, 2, 3, 4, 5 }));
        Assert.Equal(1, cache.Rejected);
    }

    [Fact]
    public void Add_EvictsLeastRecentlyUsedWithinEntryBudget()
    {
        using var cache = new ArtworkCache(maxEntries: 2);
        cache.Add("a", Png(10, 10));
        cache.Add("b", Png(10, 10));
        cache.Add("c", Png(10, 10));
        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(1, cache.Evictions);
    }

    [Fact]
    public void Add_EvictsWithinByteBudget()
    {
        using var cache = new ArtworkCache(maxBytes: 600, maxEntries: 10);
        var blob = new byte[200];
        new byte[] { 0xFF, 0xD8 }.CopyTo(blob, 0);
        blob[2] = 0xFF; blob[3] = 0xC0; blob[4] = 0x00; blob[5] = 0x11; blob[6] = 0x08;
        blob[7] = 0; blob[8] = 6; blob[9] = 0; blob[10] = 8;
        Assert.NotNull(cache.Add("a", blob));
        Assert.Equal(1, cache.Count);
        Assert.NotNull(cache.Add("b", blob));
        Assert.Equal(1, cache.Count); // 2 × ~328 bytes > 600 → one evicted
        Assert.True(cache.TotalBytes <= cache.MaxBytes);
    }

    [Fact]
    public void Add_ReplacesExistingKeyWithoutGrowingCount()
    {
        using var cache = new ArtworkCache();
        Assert.NotNull(cache.Add("k", Png(10, 10)));
        Assert.NotNull(cache.Add("k", Png(20, 20)));
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("k", out var entry));
        Assert.Equal(new ImageDimensions(20, 20), entry!.Dimensions);
    }

    [Fact]
    public void RemoveAndClear_ReleaseBudget()
    {
        using var cache = new ArtworkCache();
        cache.Add("a", Png(10, 10));
        cache.Add("b", Png(10, 10));
        Assert.True(cache.Remove("a"));
        Assert.Equal(1, cache.Count);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.TotalBytes);
    }
}

public sealed class LrcParserTests
{
    [Fact]
    public void Parse_ReadsMetadataAndLines()
    {
        const string lrc = "[ti:Song]\n[ar:Artist]\n[00:01.50]First line\n[00:05.00]Second line";
        var document = LrcParser.Parse(lrc);
        Assert.Equal("Song", document.Title);
        Assert.Equal("Artist", document.Artist);
        Assert.Equal(2, document.Lines.Length);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), document.Lines[0].Time);
        Assert.Equal("First line", document.Lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(5), document.Lines[1].Time);
    }

    [Fact]
    public void Parse_SupportsMultipleTimestampsPerLine()
    {
        var document = LrcParser.Parse("[00:01.00][00:31.00]Chorus\n[00:10.00]Verse");
        Assert.Equal(3, document.Lines.Length);
        Assert.Equal("Chorus", document.Lines[0].Text);
        Assert.Equal("Verse", document.Lines[1].Text);
        Assert.Equal("Chorus", document.Lines[2].Text);
        Assert.Equal(TimeSpan.FromSeconds(31), document.Lines[2].Time);
    }

    [Fact]
    public void Parse_AppliesPositiveOffset()
    {
        var document = LrcParser.Parse("[offset:+500]\n[00:01.00]Line");
        Assert.Equal(TimeSpan.FromMilliseconds(500), document.Lines[0].Time);
    }

    [Fact]
    public void Parse_ClampsNegativeTimestampToZero()
    {
        // A positive LRC offset shifts lines earlier; an offset larger than the timestamp clamps to 0.
        var document = LrcParser.Parse("[offset:+5000]\n[00:01.00]Line");
        Assert.Equal(TimeSpan.Zero, document.Lines[0].Time);

        // A negative offset shifts lines later.
        var later = LrcParser.Parse("[offset:-1000]\n[00:01.00]Line");
        Assert.Equal(TimeSpan.FromSeconds(2), later.Lines[0].Time);
    }

    [Fact]
    public void Parse_SkipsMalformedLinesAndReturnsEmptyForEmptyInput()
    {
        var document = LrcParser.Parse("not lyrics at all\n[00:bad]oops\n[00:02.00]ok");
        Assert.Single(document.Lines);
        Assert.True(LrcParser.Parse("").IsEmpty);
        Assert.True(LrcParser.Parse(null).IsEmpty);
    }

    [Fact]
    public void LineAt_AndNextLine_NavigateByPosition()
    {
        var document = LrcParser.Parse("[00:01.00]One\n[00:05.00]Two\n[00:09.00]Three");
        Assert.Null(document.LineAt(TimeSpan.Zero));
        Assert.Equal("One", document.LineAt(TimeSpan.FromSeconds(2))!.Text);
        Assert.Equal("Two", document.LineAt(TimeSpan.FromSeconds(7))!.Text);
        Assert.Equal("Three", document.NextLine(TimeSpan.FromSeconds(7))!.Text);
        Assert.Null(document.NextLine(TimeSpan.FromSeconds(30)));
        Assert.Equal(-1, document.FindLineIndex(TimeSpan.Zero));
    }

    [Fact]
    public void MergeTranslations_JoinsByTimestamp()
    {
        var main = LrcParser.Parse("[00:01.00]Hello\n[00:05.00]World");
        var translation = LrcParser.Parse("[00:01.00]你好\n[00:03.00]ignored");
        var merged = LrcParser.MergeTranslations(main, translation);
        Assert.Equal("你好", merged.Lines[0].Translation);
        Assert.Null(merged.Lines[1].Translation);
    }

    [Fact]
    public void TryDecodeBase64_DecodesAndRejectsInvalid()
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[00:01.00]Hi"));
        Assert.Equal("[00:01.00]Hi", LrcParser.TryDecodeBase64(encoded));
        Assert.Null(LrcParser.TryDecodeBase64("not base64 %%%"));
        Assert.Null(LrcParser.TryDecodeBase64(null));
    }
}

public sealed class LyricsCacheTests
{
    private static LyricsDocument Document(string text, int lines = 2)
    {
        var builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<LyricsLine>(lines);
        for (var index = 0; index < lines; index++)
        {
            builder.Add(new LyricsLine(TimeSpan.FromSeconds(index * 5), $"{text} {index}"));
        }

        return new LyricsDocument(text, "artist", "test", builder.MoveToImmutable());
    }

    [Fact]
    public void AddThenTryGet_ReturnsDocument()
    {
        using var cache = new LyricsCache();
        cache.Add("track", Document("hello"));
        Assert.True(cache.TryGet("track", out var document));
        Assert.Equal("hello", document!.Title);
        Assert.Equal(1, cache.Hits);
    }

    [Fact]
    public void TryGet_MissIncreasesCounter()
    {
        using var cache = new LyricsCache();
        Assert.False(cache.TryGet("nope", out _));
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void Add_EvictsByEntryBudget()
    {
        using var cache = new LyricsCache(maxBytes: 100_000, maxEntries: 2);
        cache.Add("a", Document("a"));
        cache.Add("b", Document("b"));
        cache.Add("c", Document("c"));
        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.Equal(1, cache.Evictions);
    }

    [Fact]
    public void Add_EvictsByByteBudget()
    {
        using var cache = new LyricsCache(maxBytes: 3_000, maxEntries: 100);
        cache.Add("a", Document("a", 20));
        cache.Add("b", Document("b", 20));
        cache.Add("c", Document("c", 20));
        Assert.True(cache.TotalBytes <= cache.MaxBytes);
        Assert.True(cache.Count <= 2);
        Assert.True(cache.Evictions >= 1);
    }

    [Fact]
    public void Add_RejectsSingleOversizedDocument()
    {
        using var cache = new LyricsCache(maxBytes: 100, maxEntries: 100);
        cache.Add("huge", Document("huge", 100));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void TryGet_ExpiresAfterTtl()
    {
        var time = new ManualTimeProvider();
        using var cache = new LyricsCache(ttl: TimeSpan.FromMinutes(5), timeProvider: time);
        cache.Add("track", Document("hello"));
        time.Advance(TimeSpan.FromMinutes(6));
        Assert.False(cache.TryGet("track", out _));
        Assert.Equal(1, cache.Evictions);
    }

    [Fact]
    public void EstimateBytes_GrowsWithLines()
    {
        Assert.True(LyricsCache.EstimateBytes(Document("x", 10)) > LyricsCache.EstimateBytes(Document("x", 2)));
    }
}

public sealed class LyricsServiceTests
{
    private sealed class FakeProvider : ILyricsProvider
    {
        private readonly Func<LyricsQuery, CancellationToken, Task<LyricsDocument?>> _fetch;

        public FakeProvider(string name, Func<LyricsQuery, CancellationToken, Task<LyricsDocument?>> fetch)
        {
            Name = name;
            _fetch = fetch;
        }

        public string Name { get; }

        public int Calls { get; private set; }

        public Task<LyricsDocument?> FetchAsync(LyricsQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            return _fetch(query, cancellationToken);
        }
    }

    private static LyricsQuery Query(string trackKey = "track")
        => new("Title", "Artist", TimeSpan.FromSeconds(200), trackKey);

    private static LyricsDocument Document(string title = "Title")
        => new(title, "Artist", "fake",
            System.Collections.Immutable.ImmutableArray.Create(new LyricsLine(TimeSpan.Zero, "line")));

    [Fact]
    public async Task GetLyricsAsync_UsesCacheOnSecondCall()
    {
        var provider = new FakeProvider("p", (_, _) => Task.FromResult<LyricsDocument?>(Document()));
        await using var service = new LyricsService(new[] { provider }, new LyricsCache(), new CapturingLogger(),
            new FluidBar.Platform.Windows.Diagnostics.PlatformCounters());

        var first = await service.GetLyricsAsync(Query());
        var second = await service.GetLyricsAsync(Query());

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task GetLyricsAsync_IsSingleFlightForConcurrentRequests()
    {
        var gate = new TaskCompletionSource<LyricsDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider("p", (_, _) => gate.Task);
        await using var service = new LyricsService(new[] { provider }, new LyricsCache(), new CapturingLogger(),
            new FluidBar.Platform.Windows.Diagnostics.PlatformCounters());

        var first = service.GetLyricsAsync(Query());
        var second = service.GetLyricsAsync(Query());
        gate.SetResult(Document());

        var results = await Task.WhenAll(first, second);
        Assert.All(results, item => Assert.NotNull(item));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task GetLyricsAsync_FallsBackToNextProviderOnFailure()
    {
        var failing = new FakeProvider("bad", (_, _) =>
            Task.FromException<LyricsDocument?>(new HttpRequestException("boom")));
        var working = new FakeProvider("good", (_, _) => Task.FromResult<LyricsDocument?>(Document("good")));
        await using var service = new LyricsService(new[] { failing, working }, new LyricsCache(),
            new CapturingLogger(), new FluidBar.Platform.Windows.Diagnostics.PlatformCounters());

        var document = await service.GetLyricsAsync(Query());
        Assert.Equal("good", document!.Title);
        Assert.Equal(1, failing.Calls);
        Assert.Equal(1, working.Calls);
    }

    [Fact]
    public async Task GetLyricsAsync_SkipsUnsearchableQueries()
    {
        var provider = new FakeProvider("p", (_, _) => Task.FromResult<LyricsDocument?>(Document()));
        await using var service = new LyricsService(new[] { provider }, new LyricsCache(), new CapturingLogger(),
            new FluidBar.Platform.Windows.Diagnostics.PlatformCounters());
        var result = await service.GetLyricsAsync(new LyricsQuery("", "", TimeSpan.Zero, "key"));
        Assert.Null(result);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task CancelPending_CancelsInFlightFetch()
    {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider("p", async (_, token) =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return Document();
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult(true);
                throw;
            }
        });
        await using var service = new LyricsService(new[] { provider }, new LyricsCache(), new CapturingLogger(),
            new FluidBar.Platform.Windows.Diagnostics.PlatformCounters());

        var pending = service.GetLyricsAsync(Query());
        await TestWait.UntilAsync(() => provider.Calls == 1);
        service.CancelPending();
        Assert.Null(await pending);
        Assert.True(await cancelled.Task);
    }

    [Fact]
    public async Task Provider_failure_after_the_timeout_is_observed_not_escalated()
    {
        // Regression (2026-10-24): the per-provider WaitAsync timeout abandons the provider task, so a
        // late SSL/HTTP failure had no observer and escalated to TaskScheduler.UnobservedTaskException at
        // finalization — the steady "The SSL connection could not be established" entries in crash.log.
        var markers = new List<string>();
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
        {
            foreach (var inner in e.Exception.Flatten().InnerExceptions)
            {
                if (inner.Message.StartsWith("late-lyrics-fault", StringComparison.Ordinal))
                {
                    markers.Add(inner.Message);
                }
            }
        };

        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            await StartTimedOutRequestAsync();
            for (var round = 0; round < 3; round++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(50);
            }

            // Escalation only happens once the abandoned task is collected, so this asserts the absence
            // of the symptom rather than a specific collection: a reintroduced leak shows up here.
            Assert.Empty(markers);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    [Fact]
    public async Task A_provider_that_fails_after_the_timeout_does_not_poison_the_next_request()
    {
        var gate = new TaskCompletionSource<LyricsDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new FakeProvider("slow", (_, _) =>
        {
            calls++;
            return calls == 1 ? gate.Task : Task.FromResult<LyricsDocument?>(Document("second"));
        });
        await using var service = new LyricsService(new[] { provider }, new LyricsCache(), new CapturingLogger(),
            new FluidBar.Platform.Windows.Diagnostics.PlatformCounters(),
            providerTimeout: TimeSpan.FromMilliseconds(50));

        Assert.Null(await service.GetLyricsAsync(Query()));   // provider times out

        gate.SetException(new HttpRequestException("late-lyrics-fault"));
        await Task.Delay(50);

        // Single-flight bookkeeping must be clean: the abandoned failure cannot wedge the track.
        var document = await service.GetLyricsAsync(Query());
        Assert.Equal("second", document!.Title);
        Assert.Equal(2, provider.Calls);
    }

    /// <summary>
    /// Runs a lookup that times out, then faults the abandoned provider task far enough outside this
    /// frame that the task can be collected (that is what triggers unobserved-task escalation).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task StartTimedOutRequestAsync()
    {
        var gate = new TaskCompletionSource<LyricsDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider("slow", (_, _) => gate.Task);
        await using (var service = new LyricsService(new[] { provider }, new LyricsCache(), new CapturingLogger(),
            new FluidBar.Platform.Windows.Diagnostics.PlatformCounters(),
            providerTimeout: TimeSpan.FromMilliseconds(50)))
        {
            Assert.Null(await service.GetLyricsAsync(Query()));
        }

        // Faulted after the caller stopped waiting. Nothing keeps a strong reference once this frame
        // returns, so the finalizer is free to escalate if the fault was never observed.
        gate.SetException(new HttpRequestException("late-lyrics-fault-" + Guid.NewGuid().ToString("N")));
        await Task.Delay(50);
    }
}

public sealed class OnlineLyricsParserTests
{
    [Fact]
    public void Netease_TryParseSongId_TakesTheHitThatMatchesTheTrack()
    {
        // Real shape from 2026-10-26: the engine can rank a different song of the same name first. Taking
        // songs[0] blind served "作词 : 小柯" for a Jeff Satur track.
        const string json = """
        {"result":{"songs":[
          {"id":111,"name":"Fade (English Version)","artists":[{"name":"Unrelated"}],"duration":262000},
          {"id":1983149480,"name":"Fade (English Version)","artists":[{"name":"Jeff Satur"}],"duration":262000}
        ]}}
        """;
        var query = new LyricsQuery("Fade (English Version)", "Jeff Satur", TimeSpan.FromSeconds(262), "k");
        Assert.True(NeteaseLyricsProvider.TryParseNeteaseSongId(json, query, out var id));
        Assert.Equal(1983149480, id);

        Assert.False(NeteaseLyricsProvider.TryParseNeteaseSongId("""{"result":{}}""", query, out _));
        Assert.False(NeteaseLyricsProvider.TryParseNeteaseSongId("not json", query, out _));
    }

    [Fact]
    public void Netease_TryParseSongId_RejectsEveryHitWhenNoneIsTheTrack()
    {
        const string json = """
        {"result":{"songs":[
          {"id":5,"name":"Fade","artists":[{"name":"Someone Else"}],"duration":200000}
        ]}}
        """;
        var query = new LyricsQuery("Fade (English Version)", "Jeff Satur", TimeSpan.Zero, "k");
        Assert.False(NeteaseLyricsProvider.TryParseNeteaseSongId(json, query, out var id));
        Assert.Equal(0, id);
    }

    [Fact]
    public void Netease_ParseLyrics_MergesTranslation()
    {
        const string json = """
        {"lrc":{"lyric":"[00:01.00]Hello\n[00:05.00]World"},"tlyric":{"lyric":"[00:01.00]你好"}}
        """;
        var document = NeteaseLyricsProvider.ParseNeteaseLyrics(json);
        Assert.NotNull(document);
        Assert.Equal(2, document!.Lines.Length);
        Assert.Equal("你好", document.Lines[0].Translation);
        Assert.Equal("netease", document.Source);
    }

    [Fact]
    public void Netease_ParseLyrics_ReturnsNullWithoutLyrics()
    {
        Assert.Null(NeteaseLyricsProvider.ParseNeteaseLyrics("""{"code":404}"""));
        Assert.Null(NeteaseLyricsProvider.ParseNeteaseLyrics("broken"));
    }

    [Fact]
    public void Kugou_Parsers_ReadSearchCandidateAndDownload()
    {
        var query = new LyricsQuery("Song", "Artist", TimeSpan.FromSeconds(215), "k");
        const string search = """
        {"data":{"info":[
          {"hash":"WRONG","songname":"Song","singername":"Someone Else","duration":215},
          {"hash":"ABC123","songname":"Song","singername":"Artist","duration":215}
        ]}}
        """;
        Assert.True(KugouLyricsProvider.TryParseKugouSong(search, query, out var hash, out var duration));
        Assert.Equal("ABC123", hash);
        Assert.Equal(215, duration);

        const string candidates = """{"candidates":[{"id":"777","accesskey":"KEY"}]}""";
        Assert.True(KugouLyricsProvider.TryParseKugouCandidate(candidates, out var id, out var accessKey));
        Assert.Equal("777", id);
        Assert.Equal("KEY", accessKey);

        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[00:02.00]Line"));
        var document = KugouLyricsProvider.ParseKugouDownload($$"""{"content":"{{encoded}}"}""");
        Assert.NotNull(document);
        Assert.Equal("Line", document!.Lines[0].Text);

        Assert.False(KugouLyricsProvider.TryParseKugouSong("""{"data":{}}""", query, out _, out _));
        Assert.False(KugouLyricsProvider.TryParseKugouSong(
            """{"data":{"info":[{"hash":"X","songname":"Other","singername":"Nobody","duration":10}]}}""",
            query, out _, out _));
        Assert.False(KugouLyricsProvider.TryParseKugouCandidate("""{"candidates":[]}""", out _, out _));
        Assert.Null(KugouLyricsProvider.ParseKugouDownload("""{"status":100}"""));
    }
}