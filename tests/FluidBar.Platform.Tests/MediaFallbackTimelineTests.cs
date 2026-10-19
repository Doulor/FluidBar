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