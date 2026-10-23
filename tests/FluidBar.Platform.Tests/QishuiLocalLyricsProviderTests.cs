using System.Text;
using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Unit tests for the Soda Music local lyric cache reader (synthetic cache files built from the real
/// payload shapes measured on the desktop player).
/// </summary>
public sealed class QishuiLocalLyricsProviderTests : IDisposable
{
    private readonly string _file;

    public QishuiLocalLyricsProviderTests()
    {
        _file = Path.Combine(Path.GetTempPath(), "fluidbar-qishui-" + Guid.NewGuid().ToString("N") + ".db");
    }

    public void Dispose()
    {
        try { File.Delete(_file); }
        catch (IOException) { }
    }

    private void Write(params byte[][] chunks)
    {
        using var stream = File.Create(_file);
        foreach (var chunk in chunks)
        {
            stream.Write(chunk);
        }
    }

    private static byte[] Bin(params string[] parts)
    {
        // Fake LMDB-ish framing: binary noise between the text payloads, like the real cache.
        var noise = new byte[] { 0x00, 0xDE, 0xC0, 0xAD, 0x0A, 0x1F };
        using var buffer = new MemoryStream();
        foreach (var part in parts)
        {
            buffer.Write(noise);
            buffer.Write(Encoding.UTF8.GetBytes(part));
            buffer.Write(noise);
        }
        return buffer.ToArray();
    }

    private const string WordTimedLyric =
        "[8520,2240]<0,180,0>I <180,200,0>ain't <380,200,0>tryna <580,210,0>play <920,230,0>no <1160,270,0>games\n" +
        "[10760,2360]<0,360,0>Member <360,140,0>we <500,160,0>were <660,280,0>kids <940,210,0>way back in the day\n" +
        "[13120,1960]<0,220,0>No <220,850,0>responsibilities <1120,230,0>but now <1640,320,0>me\n" +
        "[15080,2100]<0,300,0>Smooth <300,300,0>talkin' <600,300,0>all <900,300,0>night\n";

    private const string LrcLyric =
        "[00:12.34]第一次遇见你的时候\n" +
        "[00:18.20]风吹过街道的角落\n" +
        "[00:24.90]你的笑像春天的河\n" +
        "[00:31.10]流进我心中的漩涡\n";

    [Fact]
    public async Task Word_timed_block_matches_title_and_artist()
    {
        Write(Bin("mediaDetail", "6704957080027006978", "name", "No Games", "artists", "Samuel Laveaga",
            "content", WordTimedLyric));

        var provider = new QishuiLocalLyricsProvider(_file);
        var document = await provider.FetchAsync(new LyricsQuery("No Games", "Samuel Laveaga", TimeSpan.Zero, "k"), default);

        Assert.NotNull(document);
        Assert.Equal("qishui-krc", document!.Source);
        Assert.Equal(4, document.Lines.Length);
        Assert.Equal(TimeSpan.FromMilliseconds(8520), document.Lines[0].Time);
        Assert.Equal("I ain't tryna play no games", document.Lines[0].Text);
    }

    [Fact]
    public async Task Lrc_block_is_parsed_through_the_lrc_reader()
    {
        Write(Bin("mediaDetail", "6900000000000000001", "name", "春天的河", "artists", "测试歌手",
            "content", LrcLyric));

        var provider = new QishuiLocalLyricsProvider(_file);
        var document = await provider.FetchAsync(new LyricsQuery("春天的河", "测试歌手", TimeSpan.Zero, "k"), default);

        Assert.NotNull(document);
        Assert.Equal("qishui-lrc", document!.Source);
        Assert.Equal(4, document.Lines.Length);
        Assert.Equal(TimeSpan.FromMilliseconds(12340), document.Lines[0].Time);
    }

    [Fact]
    public async Task Missing_title_in_window_returns_null()
    {
        Write(Bin("mediaDetail", "6700000000000000002", "name", "Another Song", "content", WordTimedLyric));

        var provider = new QishuiLocalLyricsProvider(_file);
        var document = await provider.FetchAsync(new LyricsQuery("No Games", "Samuel Laveaga", TimeSpan.Zero, "k"), default);

        Assert.Null(document);
    }

    [Fact]
    public async Task Later_block_wins_among_equal_candidates()
    {
        // Two copies of the same track: the cache appends, so the later one is the fresher copy.
        Write(Bin(
            "mediaDetail", "6700000000000000003", "name", "No Games", "artists", "Samuel Laveaga",
            "content", "[1000,2000]<0,500,0>stale <500,500,0>copy\n[3000,2000]<0,500,0>second <500,500,0>line\n[5000,2000]<0,500,0>third <500,500,0>line\n",
            "mediaDetail", "6700000000000000004", "name", "No Games", "artists", "Samuel Laveaga",
            "content", WordTimedLyric));

        var provider = new QishuiLocalLyricsProvider(_file);
        var document = await provider.FetchAsync(new LyricsQuery("No Games", "Samuel Laveaga", TimeSpan.Zero, "k"), default);

        Assert.NotNull(document);
        Assert.Equal(4, document!.Lines.Length);
        Assert.Equal("I ain't tryna play no games", document.Lines[0].Text);
    }

    [Fact]
    public async Task Title_only_match_succeeds_when_artist_is_unknown()
    {
        Write(Bin("mediaDetail", "6700000000000000005", "name", "No Games", "artists", "Samuel Laveaga",
            "content", WordTimedLyric));

        var provider = new QishuiLocalLyricsProvider(_file);
        var document = await provider.FetchAsync(new LyricsQuery("No Games", "Various Artists", TimeSpan.Zero, "k"), default);

        Assert.NotNull(document);
    }

    [Fact]
    public async Task Missing_cache_file_returns_null()
    {
        var provider = new QishuiLocalLyricsProvider(Path.Combine(Path.GetTempPath(), "does-not-exist.db"));
        var document = await provider.FetchAsync(new LyricsQuery("No Games", "Samuel Laveaga", TimeSpan.Zero, "k"), default);
        Assert.Null(document);
    }

    [Fact]
    public async Task Short_runs_are_not_lyrics()
    {
        // Two consecutive timed lines only: below the 3-line threshold, so no block is indexed.
        Write(Bin("mediaDetail", "6700000000000000006", "name", "No Games", "artists", "Samuel Laveaga",
            "content",
            "[1000,2000]<0,500,0>one <500,500,0>two\n[3000,2000]<0,500,0>three <500,500,0>four\n"));

        var provider = new QishuiLocalLyricsProvider(_file);
        var document = await provider.FetchAsync(new LyricsQuery("No Games", "Samuel Laveaga", TimeSpan.Zero, "k"), default);
        Assert.Null(document);
    }

    [Fact]
    public void Score_prefers_title_and_artist_over_title_only()
    {
        var strings = new[] { "No Games", "Samuel Laveaga", "mediaDetail" };
        Assert.Equal(2, QishuiLocalLyricsProvider.Score(strings, "no games", "samuel laveaga"));
        Assert.Equal(1, QishuiLocalLyricsProvider.Score(strings, "no games", "someone else"));
        Assert.Equal(0, QishuiLocalLyricsProvider.Score(strings, "other title", "samuel laveaga"));
    }

    [Fact]
    public async Task Binary_entry_trailer_glued_to_the_last_line_is_cut()
    {
        // A real cache entry ends with its MessagePack trailer and NO newline before it, so the last
        // lyric line runs straight into the bytes (measured 2026-10-22, user screenshot:
        // "…" + "£krcij" + "◆7566176614707430459◆Gentlem…"). The trailer is a type tag, the
        // "not_luna_user" field, the track ids and the NEXT entry's title; without a cut KrcDecoder
        // folded all of it into the line text the island renders.
        var lyric = "[8520,2240]<0,180,0>还能不能<180,240,0>相见\n"
            + "[10760,2360]<0,360,0>风吹过\n"
            + "[13120,1960]<0,220,0>街道的\n"
            + "[15080,2100]<0,300,0>角落";
        var trailer = new List<byte>
        {
            0xC2, 0xA3, // lossy-decodes to "£"
            0x6B, 0x72, 0x63, 0x69, // "krci"
            0xAD, // fixstr(13): "not_luna_user"
        };
        trailer.AddRange(Encoding.UTF8.GetBytes("not_luna_user"));
        trailer.AddRange(new byte[] { 0x6A, 0x4B, 0xB3 });
        trailer.AddRange(Encoding.UTF8.GetBytes("7566176614707430459"));
        trailer.AddRange(new byte[] { 0x4C, 0xB3 });
        trailer.AddRange(Encoding.UTF8.GetBytes("7566176614707430459"));
        trailer.Add(0xA6); // fixstr(6): the neighbouring entry's title
        trailer.AddRange(Encoding.UTF8.GetBytes("Gentlemen"));

        var mediaDetail = Encoding.UTF8.GetBytes("mediaDetail")
            .Concat(new byte[] { 0 })
            .Concat(Encoding.UTF8.GetBytes("6704957080027006978"))
            .Concat(new byte[] { 0 })
            .Concat(Encoding.UTF8.GetBytes("name"))
            .Concat(new byte[] { 0 })
            .Concat(Encoding.UTF8.GetBytes("还能不能相见"))
            .Concat(new byte[] { 0 })
            .Concat(Encoding.UTF8.GetBytes("artists"))
            .Concat(new byte[] { 0 })
            .Concat(Encoding.UTF8.GetBytes("测试歌手"))
            .Concat(new byte[] { 0 })
            .Concat(Encoding.UTF8.GetBytes("content"))
            .Concat(new byte[] { 0 })
            .ToArray();
        // The real cache separates the entry metadata from the lyric with a newline; without it
        // the first timed line starts mid-line and the metadata window in front of the block is empty.
        Write(mediaDetail, new byte[] { 0x0A }, Encoding.UTF8.GetBytes(lyric), trailer.ToArray());

        var provider = new QishuiLocalLyricsProvider(_file);
        var document = await provider.FetchAsync(
            new LyricsQuery("还能不能相见", "测试歌手", TimeSpan.Zero, "k"), default);

        Assert.NotNull(document);
        Assert.Equal(4, document!.Lines.Length);
        Assert.Equal("还能不能相见", document.Lines[0].Text);
        Assert.Equal("角落", document.Lines[^1].Text);
    }
}
