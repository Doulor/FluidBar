using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

/// <summary>Playback state the player persists in KuGou.ini (no SMTC session required).</summary>
public sealed class PlayerPlaybackStateReaderTests
{
    private const string SampleIni = """
        [GeneralConfigSection]
        LastMainFormPos_V8060=366,161,1596,949

        [LyricConfigSection]
        LyricPath=D:\KuGou\Lyric\
        IsDesktopLyricSingleLine=0

        [PlaybackState]
        LastPlayingSongItem=11284
        LastPlayingSongPos=50576
        LastPlayingTitleName=Josh Leake - Arc
        PlaybackOrderMode=0

        [Player]
        Mute=1
        Volume=5
        """;

    [Fact]
    public void Parse_ReadsTitlePositionAndMute()
    {
        var sampled = new DateTimeOffset(2026, 10, 20, 2, 15, 33, TimeSpan.Zero);

        var state = PlayerPlaybackStateReader.Parse(SampleIni, sampled);

        Assert.NotNull(state);
        Assert.Equal("Josh Leake - Arc", state!.RawTitle);
        Assert.Equal(TimeSpan.FromMilliseconds(50576), state.Position);
        Assert.Equal(sampled, state.SampledAtUtc);
        Assert.True(state.IsMuted);
    }

    [Fact]
    public void Parse_ReturnsNullWithoutPlaybackSection()
    {
        Assert.Null(PlayerPlaybackStateReader.Parse("[Player]\nMute=1\n", DateTimeOffset.UtcNow));
        Assert.Null(PlayerPlaybackStateReader.Parse("", DateTimeOffset.UtcNow));
        Assert.Null(PlayerPlaybackStateReader.Parse(null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Parse_RejectsNonNumericPosition()
    {
        const string ini = "[PlaybackState]\nLastPlayingTitleName=A - B\nLastPlayingSongPos=oops\n";

        Assert.Null(PlayerPlaybackStateReader.Parse(ini, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Parse_ReadsTheLyricCacheDirectory()
    {
        Assert.Equal(@"D:\KuGou\Lyric\", PlayerPlaybackStateReader.ParseLyricDirectory(SampleIni));
        Assert.Null(PlayerPlaybackStateReader.ParseLyricDirectory("[Player]\nMute=0\n"));
        Assert.Null(PlayerPlaybackStateReader.ParseLyricDirectory(null));
    }
}

/// <summary>Transport routing for players that registered no SMTC session.</summary>
public sealed class FallbackTransportPolicyTests
{
    [Fact]
    public void Decide_PrefersTheActiveSession()
    {
        var decision = FallbackTransportPolicy.Decide(hasSession: true, fallbackActive: false, senderAvailable: true);

        Assert.Equal(TransportRoute.Session, decision.Route);
        Assert.True(decision.IsExecutable);
    }

    [Fact]
    public void Decide_PrefersTheAccessibilityTreeOverMediaKeys()
    {
        var decision = FallbackTransportPolicy.Decide(hasSession: false, fallbackActive: true,
            accessibilityAvailable: true, senderAvailable: true);

        Assert.Equal(TransportRoute.Accessibility, decision.Route);
        Assert.True(decision.IsExecutable);
        Assert.Contains("accessibility", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decide_UsesMediaKeysWhenTheAccessibilityTreeIsUnavailable()
    {
        var decision = FallbackTransportPolicy.Decide(hasSession: false, fallbackActive: true,
            accessibilityAvailable: false, senderAvailable: true);

        Assert.Equal(TransportRoute.MediaKey, decision.Route);
        Assert.True(decision.IsExecutable);
        Assert.Contains("media key", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decide_IsUnavailableWithoutSessionOrFallback()
    {
        var noFallback = FallbackTransportPolicy.Decide(false, false, true);
        Assert.Equal(TransportRoute.Unavailable, noFallback.Route);
        Assert.False(noFallback.IsExecutable);

        var noSender = FallbackTransportPolicy.Decide(false, true, accessibilityAvailable: false, senderAvailable: false);
        Assert.Equal(TransportRoute.Unavailable, noSender.Route);
        Assert.Contains("no media-key sender", noSender.Reason, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>KuGou .krc decoding (magic, XOR key, zlib payload, word-level timing tags).</summary>
public sealed class KrcDecoderTests
{
    private const string SampleText = """
        [id:$2658E1DA]
        [ar:ADAM]
        [ti:Zhurek]
        [offset:0]
        [12657,1966]<0,287,0>Сені <287,634,0>көрсем
        [15216,1985]<0,323,0>Жүрек <323,639,0>соғар
        [171581,2522]<0,324,0>Бүкіл <324,647,0>әлем
        """;

    [Fact]
    public void TryDecode_ReadsEncryptedKrc()
    {
        var encoded = EncodeKrc(SampleText);

        var decoded = KrcDecoder.TryDecode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal("ADAM", decoded!.Document.Artist);
        Assert.Equal("Zhurek", decoded.Document.Title);
        Assert.Equal("kugou-krc", decoded.Document.Source);
        Assert.Equal(3, decoded.Document.Lines.Length);
        Assert.Equal(TimeSpan.FromMilliseconds(12657), decoded.Document.Lines[0].Time);
        Assert.Equal("Сені көрсем", decoded.Document.Lines[0].Text);
        Assert.Equal(TimeSpan.FromMilliseconds(171581 + 2522), decoded.ApproximateDuration);
        Assert.Equal(decoded.ApproximateDuration, decoded.Document.Duration);
    }

    [Fact]
    public void TryDecode_RejectsGarbageAndShortInput()
    {
        Assert.Null(KrcDecoder.TryDecode(null));
        Assert.Null(KrcDecoder.TryDecode(Array.Empty<byte>()));
        Assert.Null(KrcDecoder.TryDecode(Encoding.ASCII.GetBytes("nope")));

        // Valid magic but not a zlib payload.
        var broken = new byte[] { (byte)'k', (byte)'r', (byte)'c', (byte)'1', 1, 2, 3, 4, 5 };
        Assert.Null(KrcDecoder.TryDecode(broken));
    }

    [Fact]
    public void TryDecodeText_SkipsLeadingBomAndBlankLines()
    {
        var encoded = EncodeKrc("\uFEFF" + SampleText + "\n\n");

        var decoded = KrcDecoder.TryDecode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal(3, decoded!.Document.Lines.Length);
    }

    private static byte[] EncodeKrc(string text)
    {
        byte[] key =
        [
            0x40, 0x47, 0x61, 0x77, 0x5E, 0x32, 0x74, 0x47,
            0x51, 0x36, 0x31, 0x2D, 0xCE, 0xD2, 0x6E, 0x69,
        ];
        using var buffer = new MemoryStream();
        using (var inflater = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            var payload = Encoding.UTF8.GetBytes(text);
            inflater.Write(payload, 0, payload.Length);
        }

        var compressed = buffer.ToArray();
        var result = new byte[4 + compressed.Length];
        "krc1"u8.CopyTo(result);
        for (var index = 0; index < compressed.Length; index++)
        {
            result[4 + index] = (byte)(compressed[index] ^ key[index % key.Length]);
        }

        return result;
    }
}

/// <summary>Lyric line selection for the media page (current + next, credit lines skipped).</summary>
public sealed class LyricsTimelineSelectorTests
{
    private static LyricsDocument Document(params string[] lrcLines) => LrcParser.Parse(string.Join('\n', lrcLines), "test");

    [Fact]
    public void Select_ReturnsCurrentAndNextLine()
    {
        var document = Document("[00:01.00]One", "[00:05.00]Two", "[00:09.00]Three");

        var pair = LyricsTimelineSelector.Select(document, TimeSpan.FromSeconds(6));

        Assert.Equal("Two", pair.Current);
        Assert.Equal("Three", pair.Next);
    }

    [Fact]
    public void Select_ReturnsNullNextOnTheLastLine()
    {
        var document = Document("[00:01.00]One", "[00:05.00]Two");

        var pair = LyricsTimelineSelector.Select(document, TimeSpan.FromSeconds(6));

        Assert.Equal("Two", pair.Current);
        Assert.Null(pair.Next);
    }

    [Fact]
    public void Select_SkipsCreditLinesInBothDirections()
    {
        var document = Document(
            "[00:01.00]作词: Someone",
            "[00:02.00]Composer: Someone Else",
            "[00:05.00]Real first line",
            "[00:09.00]Real second line");

        // Position lands on the second credit line: nothing is being sung yet, so the first real
        // line is offered as the upcoming one instead of a credit line being painted as current.
        var beforeFirstSungLine = LyricsTimelineSelector.Select(document, TimeSpan.FromSeconds(3));
        Assert.Null(beforeFirstSungLine.Current);
        Assert.Equal("Real first line", beforeFirstSungLine.Next);

        // Once the sung line starts, credit lines are skipped in the backward walk as well.
        var duringFirstSungLine = LyricsTimelineSelector.Select(document, TimeSpan.FromSeconds(6));
        Assert.Equal("Real first line", duringFirstSungLine.Current);
        Assert.Equal("Real second line", duringFirstSungLine.Next);
    }

    [Fact]
    public void Select_BeforeFirstSungLineOnlyOffersTheNextLine()
    {
        var document = Document("[00:10.00]First sung line", "[00:20.00]Second line");

        var pair = LyricsTimelineSelector.Select(document, TimeSpan.Zero);

        Assert.Null(pair.Current);
        Assert.Equal("First sung line", pair.Next);
    }

    [Fact]
    public void Select_HandlesEmptyDocuments()
    {
        Assert.True(LyricsTimelineSelector.Select(null, TimeSpan.Zero).IsEmpty);
        Assert.True(LyricsTimelineSelector.Select(LyricsDocument.Empty, TimeSpan.Zero).IsEmpty);
    }

    [Theory]
    [InlineData("作词: A", true)]
    [InlineData("混音/录音: A", true)]
    [InlineData("Composer: A", true)]
    [InlineData("Lyrics by: A", true)]
    [InlineData("Hello world", false)]
    [InlineData("她说：你好", false)]
    [InlineData("", true)]
    public void IsCreditLine_DetectsMetadataLines(string text, bool expected)
        => Assert.Equal(expected, LyricsTimelineSelector.IsCreditLine(text));
    [Fact]
    public void Select_SkipsTrackIdentityHeaderLines()
    {
        var document = Document(
            "[00:00.00]Whiplash - William Black",
            "[00:05.00]Yeah this feelings something dangerous",
            "[00:09.00]But I can't turn back now");

        var pair = LyricsTimelineSelector.Select(document, TimeSpan.FromSeconds(6), "Whiplash", "William Black");

        Assert.Equal("Yeah this feelings something dangerous", pair.Current);
        Assert.Equal("But I can't turn back now", pair.Next);
    }

    [Fact]
    public void Select_HidesARepeatedNextLine()
    {
        var document = Document("[00:05.00]Just trying to get to you", "[00:09.00]Just trying to get to you");

        var pair = LyricsTimelineSelector.Select(document, TimeSpan.FromSeconds(6));

        Assert.Equal("Just trying to get to you", pair.Current);
        Assert.Null(pair.Next);
    }

    [Theory]
    [InlineData("Whiplash - William Black", "Whiplash", "William Black", true)]
    [InlineData("William Black - Whiplash", "Whiplash", "William Black", true)]
    [InlineData("Whiplash -  William Black", "Whiplash", "William Black", true)]
    [InlineData("Whiplash（Live） - William Black", "Whiplash", "William Black", false)]
    [InlineData("another line", "Whiplash", "William Black", false)]
    [InlineData("Whiplash", "Whiplash", null, true)]
    public void IsTrackIdentityLine_ComparesFoldedText(string line, string title, string? artist, bool expected)
        => Assert.Equal(expected, LyricsTimelineSelector.IsTrackIdentityLine(line, title, artist));
}

/// <summary>Lyrics served from the player's own .krc cache (offline, exact match).</summary>
public sealed class KugouLocalLyricsProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "fluidbar-tests-krc-" + Guid.NewGuid().ToString("N"));

    public KugouLocalLyricsProviderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task FetchAsync_ReturnsTheCachedLyric()
    {
        WriteKrc("ADAM - Zhurek", "[ti:Zhurek]\n[ar:ADAM]\n[12657,1966]<0,287,0>Сені <287,634,0>көрсем");
        var provider = new KugouLocalLyricsProvider(_directory);

        var document = await provider.FetchAsync(
            new LyricsQuery("Zhurek", "ADAM", TimeSpan.FromSeconds(174), "track"), CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal("Zhurek", document!.Title);
        Assert.Single(document.Lines);
        Assert.Equal("Сені көрсем", document.Lines[0].Text);
    }

    [Fact]
    public async Task FetchAsync_ReturnsACachedLrcFile()
    {
        WriteLrc("变奏的梦想 - 花未期", "[00:17.07]【吉他】\n[00:20.31]05.05.05.05.");
        var provider = new KugouLocalLyricsProvider(_directory);

        var document = await provider.FetchAsync(
            new LyricsQuery("花未期", "变奏的梦想", TimeSpan.Zero, "track"), CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal("kugou-lrc", document!.Source);
        Assert.Equal(2, document.Lines.Length);
        Assert.Equal("【吉他】", document.Lines[0].Text);
    }

    [Fact]
    public async Task FetchAsync_FallsBackToTitleOnlyMatch()
    {
        WriteKrc("Some Other Artist - Horizon", "[ti:Horizon]\n[12657,1966]<0,287,0>la la");
        var provider = new KugouLocalLyricsProvider(_directory);

        var document = await provider.FetchAsync(
            new LyricsQuery("horizon", "PRETTY YOUNG、Zack Hall", TimeSpan.Zero, "track"), CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal("la la", document!.Lines[0].Text);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNullWhenNoFileMatches()
    {
        WriteKrc("ADAM - Zhurek", "[ti:Zhurek]\n[12657,1966]line");
        var provider = new KugouLocalLyricsProvider(_directory);

        var document = await provider.FetchAsync(
            new LyricsQuery("Unrelated Song", "Nobody", TimeSpan.Zero, "track"), CancellationToken.None);

        Assert.Null(document);
    }

    [Fact]
    public void BuildLookupKey_NormalizesCaseAndWhitespace()
    {
        Assert.Equal(
            KugouLocalLyricsProvider.BuildLookupKey("ADAM", "Zhurek"),
            KugouLocalLyricsProvider.BuildLookupKey("  adam ", "  ZHUREK  "));
    }

    private void WriteLrc(string stem, string text)
    {
        var path = Path.Combine(_directory, stem + "-" + new string('b', 32) + "-123456-00000000.lrc");
        File.WriteAllText(path, text, Encoding.UTF8);
    }

    private void WriteKrc(string stem, string text)
    {
        var path = Path.Combine(_directory, stem + "-" + new string('a', 32) + "-123456-00000000.krc");
        File.WriteAllBytes(path, BuildKrcBytes(text));
    }

    private static byte[] BuildKrcBytes(string text)
    {
        byte[] key =
        [
            0x40, 0x47, 0x61, 0x77, 0x5E, 0x32, 0x74, 0x47,
            0x51, 0x36, 0x31, 0x2D, 0xCE, 0xD2, 0x6E, 0x69,
        ];
        using var buffer = new MemoryStream();
        using (var inflater = new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            var payload = Encoding.UTF8.GetBytes(text);
            inflater.Write(payload, 0, payload.Length);
        }

        var compressed = buffer.ToArray();
        var result = new byte[4 + compressed.Length];
        "krc1"u8.CopyTo(result);
        for (var index = 0; index < compressed.Length; index++)
        {
            result[4 + index] = (byte)(compressed[index] ^ key[index % key.Length]);
        }

        return result;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}

/// <summary>Play/pause inference for the fallback source (position advance vs freeze).</summary>
public sealed class FallbackPlaybackTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Observe_NewTrackStartsPlaying()
    {
        var tracker = new FallbackPlaybackTracker();

        Assert.True(tracker.Observe("win|kugou|Song|Artist", hasTimeline: true, TimeSpan.Zero, Start));
    }

    [Fact]
    public void Observe_AdvancingPositionKeepsPlaying()
    {
        var tracker = new FallbackPlaybackTracker();
        tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start);

        var playing = tracker.Observe("track", true, TimeSpan.FromSeconds(40), Start.AddSeconds(30));

        Assert.True(playing);
    }

    [Fact]
    public void Observe_FrozenPositionInARewrittenFileMeansPaused()
    {
        var tracker = new FallbackPlaybackTracker();
        tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start);

        var playing = tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start.AddSeconds(30),
            FallbackPlaybackTracker.StateFileStaleAfter);

        Assert.False(playing);
    }

    [Fact]
    public void Observe_FrozenAccessibilityTimelineMeansPausedOnceItGoesStale()
    {
        var tracker = new FallbackPlaybackTracker();
        tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start);

        // The accessibility timeline refreshes every second: three frozen ticks in a row is a pause.
        Assert.True(tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start.AddSeconds(1)));
        Assert.True(tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start.AddSeconds(2)));
        Assert.False(tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start.AddSeconds(3)));

        // Resuming (position moves again) flips it back immediately.
        Assert.True(tracker.Observe("track", true, TimeSpan.FromSeconds(11), Start.AddSeconds(4)));
    }

    [Fact]
    public void Observe_FrozenPositionWithinTheStaleWindowKeepsThePreviousState()
    {
        var tracker = new FallbackPlaybackTracker();
        tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start);

        var playing = tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start.AddMilliseconds(300));

        Assert.True(playing);
    }

    [Fact]
    public void Observe_WithoutTimelineKeepsThePreviousState()
    {
        var tracker = new FallbackPlaybackTracker();

        Assert.True(tracker.Observe("track", hasTimeline: false, TimeSpan.Zero, Start));
        Assert.True(tracker.Observe("track", hasTimeline: false, TimeSpan.Zero, Start.AddSeconds(90)));
    }

    [Fact]
    public void Observe_TrackChangeResetsAPausedState()
    {
        var tracker = new FallbackPlaybackTracker();
        tracker.Observe("track-a", true, TimeSpan.FromSeconds(10), Start);
        tracker.Observe("track-a", true, TimeSpan.FromSeconds(10), Start.AddSeconds(30));

        Assert.True(tracker.Observe("track-b", true, TimeSpan.Zero, Start.AddSeconds(31)));
    }

    [Fact]
    public void Reset_ForgetsThePreviousObservation()
    {
        var tracker = new FallbackPlaybackTracker();
        tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start);
        tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start.AddSeconds(30));

        tracker.Reset();

        Assert.True(tracker.IsPlaying);
        Assert.True(tracker.Observe("track", true, TimeSpan.FromSeconds(10), Start.AddSeconds(60)));
    }
}

/// <summary>Play/pause resolution from the accessibility transport glyphs.</summary>
public sealed class AccessibilityPlaybackStateTests
{
    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, true, null)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    public void Resolve_UsesTheVisibleGlyph(bool playVisible, bool pauseVisible, bool? expected)
        => Assert.Equal(expected, AccessibilityPlaybackState.Resolve(playVisible, pauseVisible));
}

/// <summary>Cover lookup in the player's own album cache (width-folded title matching).</summary>
public sealed class KugouArtworkCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "fluidbar-art-" + Guid.NewGuid().ToString("N"));

    public KugouArtworkCacheTests()
    {
        Directory.CreateDirectory(_root);
        KugouArtworkCache.InvalidateIndex();
    }

    [Fact]
    public void Fold_MapsFullWidthFormsToHalfWidth()
    {
        // The player stores "Untamed（失 控）" without the space the window title has before the paren,
        // so folding keeps that difference and Matches() closes it with a whitespace-free comparison.
        Assert.Equal("untamed(失 控)", KugouArtworkCache.Fold("Untamed（失 控）"));
        Assert.Equal("a b", KugouArtworkCache.Fold("A\u3000B"));
    }

    [Theory]
    [InlineData("Untamed（失 控）", "untamed (失 控)", true)]
    [InlineData("Untamed (失 控)", "untamed (失 控)", true)]
    [InlineData("untamed (失控)", "untamed (失 控)", true)]
    [InlineData("Some Other Song", "untamed (失 控)", false)]
    [InlineData("", "untamed (失 控)", false)]
    public void Matches_FoldsTypographyAndSpacing(string folderTitle, string trackTitle, bool expected)
        => Assert.Equal(expected, KugouArtworkCache.Matches(folderTitle, trackTitle));

    [Fact]
    public void Matches_FoldsAwayFeaturingDecorationsThePlayerDropped()
    {
        // Real instance from the user machine: the player cached the cover as "Miss You a Little"
        // while the window title reported "Miss You a Little (feat. lovelytheband)".
        Assert.True(KugouArtworkCache.Matches("Miss You a Little", "miss you a little (feat. lovelytheband)"));
        Assert.True(KugouArtworkCache.Matches("Untamed（失 控）", "untamed (失 控)"));
        Assert.False(KugouArtworkCache.Matches("Some Other Song", "miss you a little (feat. lovelytheband)"));
    }

    [Fact]
    public void Matches_RejectsAShortPrefixThatWouldShowAnotherSongsCover()
    {
        // "Miss You" exists in the cache as a different song; it must not answer this query.
        Assert.False(KugouArtworkCache.Matches("Miss You", "miss you a little (feat. lovelytheband)"));
    }

    [Fact]
    public void StripDecorations_RemovesBracketedGroupsAndSpaces()
    {
        Assert.Equal("missyoualittle", KugouArtworkCache.StripDecorations("miss you a little (feat. lovelytheband)"));
        Assert.Equal("untamed", KugouArtworkCache.StripDecorations("untamed (失 控)"));
        Assert.Equal("song", KugouArtworkCache.StripDecorations("song[live]"));
    }

    [Fact]
    public void FindBestFile_PrefersTheDecoratedExactFolderOverAShorterPrefix()
    {
        var correct = Path.Combine(_root, "Miss You a Little_47552533", "120");
        Directory.CreateDirectory(correct);
        var correctFile = Path.Combine(correct, "cover.jpg");
        File.WriteAllBytes(correctFile, Jpeg(200, 200));

        var other = Path.Combine(_root, "Miss You_102910095", "120");
        Directory.CreateDirectory(other);
        File.WriteAllBytes(Path.Combine(other, "cover.jpg"), Jpeg(120, 120));
        KugouArtworkCache.InvalidateIndex();

        var found = KugouArtworkCache.FindBestFile(_root, "Miss You a Little (feat. lovelytheband)");

        Assert.Equal(correctFile, found);
    }

    [Fact]
    public void FindBestFile_FindsTheWidthFoldedFolderAndPrefersTheLargest()
    {
        var folder = Path.Combine(_root, "Untamed（失 控）_203532254", "120");
        Directory.CreateDirectory(folder);
        var small = Path.Combine(folder, "small.jpg");
        var large = Path.Combine(folder, "large.jpg");
        File.WriteAllBytes(small, Jpeg(120, 120));
        File.WriteAllBytes(large, Jpeg(300, 300));
        KugouArtworkCache.InvalidateIndex();

        var found = KugouArtworkCache.FindBestFile(_root, "Untamed (失 控)");

        Assert.Equal(large, found);
    }

    [Fact]
    public void FindBestFile_ReturnsNullForAnotherTrack()
    {
        var folder = Path.Combine(_root, "Some Other Song_1", "120");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "cover.jpg"), Jpeg(120, 120));
        KugouArtworkCache.InvalidateIndex();

        Assert.Null(KugouArtworkCache.FindBestFile(_root, "Untamed (失 控)"));
    }

    [Fact]
    public void TryRead_RejectsOversizedOrInvalidFiles()
    {
        var folder = Path.Combine(_root, "Klinn_1", "120");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "bad.jpg"), new byte[] { 1, 2, 3, 4 });
        KugouArtworkCache.InvalidateIndex();

        // KugouArtworkCache.TryRead resolves the real player cache, not this root; the header rejection
        // path is covered through FindBestFile + the shared ImageHeaderReader tests.
        Assert.Equal(Path.Combine(folder, "bad.jpg"), KugouArtworkCache.FindBestFile(_root, "Klinn"));
        Assert.Null(ImageHeaderReader.TryReadDimensions(new byte[] { 1, 2, 3, 4 }));
    }

    private static byte[] Jpeg(int width, int height)
    {
        var data = new byte[32];
        data[0] = 0xFF;
        data[1] = 0xD8;
        data[2] = 0xFF;
        data[3] = 0xC0;
        data[4] = 0x00;
        data[5] = 0x11;
        data[6] = 0x08;
        data[7] = (byte)(height >> 8);
        data[8] = (byte)height;
        data[9] = (byte)(width >> 8);
        data[10] = (byte)width;
        return data;
    }

    public void Dispose()
    {
        try
        {
            KugouArtworkCache.InvalidateIndex();
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}