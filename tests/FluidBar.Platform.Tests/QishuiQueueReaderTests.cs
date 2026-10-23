using System.IO.Compression;
using System.Text;
using FluidBar.Platform.Windows.Media;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Unit tests for the Soda Music queue-cache reader (synthetic payloads shaped like the real
/// QueueCache file, measured on the desktop player 3.2.1).
/// </summary>
public sealed class QishuiQueueReaderTests
{
    private static byte[] Payload(string json)
    {
        using var gzip = new MemoryStream();
        using (var stream = new GZipStream(gzip, CompressionLevel.Optimal))
        {
            stream.Write(Encoding.UTF8.GetBytes(json));
        }

        var magic = "LUNA"u8.ToArray();
        var body = gzip.ToArray();
        var bytes = new byte[magic.Length + body.Length];
        magic.CopyTo(bytes, 0);
        body.CopyTo(bytes, magic.Length);
        return bytes;
    }

    private const string QueueJson =
        """
        {"u_1917966210050712:feed":{"version":2,"savedAt":1790527506941,"cursor":null,"hasMore":true,
        "playables":[
          {"key":"track-6704957080027006978","type":"track","track":{"id":"999","album":{},
           "artists":[{"id":"1","name":"Aya Nakamura"}],"duration":171720,"name":"Copines"}},
          {"key":"track-6715466579594708993","type":"track","track":{"id":"998","album":{},
           "artists":[{"id":"2","name":"Tez Cadey"}],"duration":210853,"name":"Seve (Radio Edit)"}}
        ],
        "lastPlayedKey":"track-6715466579594708993"}}
        """;

    [Fact]
    public void Parse_returns_the_last_played_track()
    {
        var track = QishuiQueueReader.Parse(Payload(QueueJson));

        Assert.NotNull(track);
        Assert.Equal("track-6715466579594708993", track!.Key);
        Assert.Equal("Seve (Radio Edit)", track.Title);
        Assert.Equal("Tez Cadey", track.Artist);
        Assert.Equal(TimeSpan.FromSeconds(210.853), track.Duration);
    }

    [Fact]
    public void Parse_rejects_a_payload_without_the_luna_magic()
    {
        Assert.Null(QishuiQueueReader.Parse(Encoding.UTF8.GetBytes("{}")));
        Assert.Null(QishuiQueueReader.Parse([]));
        Assert.Null(QishuiQueueReader.Parse(null));
    }

    [Fact]
    public void Parse_rejects_corrupt_gzip()
    {
        var bytes = new byte[] { 0x4C, 0x55, 0x4E, 0x41, 0x1F, 0x8B, 0x00, 0x01, 0x02 };
        Assert.Null(QishuiQueueReader.Parse(bytes));
    }

    [Fact]
    public void Parse_returns_null_when_the_last_played_key_is_not_in_the_queue()
    {
        var json = QueueJson.Replace("lastPlayedKey\":\"track-6715466579594708993",
            "lastPlayedKey\":\"track-missing", StringComparison.Ordinal);
        Assert.Null(QishuiQueueReader.Parse(Payload(json)));
    }

    [Fact]
    public void Parse_returns_null_when_the_track_has_no_name()
    {
        var json = QueueJson.Replace("\"name\":\"Seve (Radio Edit)\"", "\"name\":\"\"", StringComparison.Ordinal);
        Assert.Null(QishuiQueueReader.Parse(Payload(json)));
    }
}
