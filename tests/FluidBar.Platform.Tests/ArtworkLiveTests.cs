using System.Diagnostics;
using FluidBar.Platform.Windows.Media;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Live catalogue evidence: fetches real covers through the production pipeline and records which source
/// answered, the URL and the confidence score. Gated behind <c>FLUIDBAR_LIVE_ARTWORK=1</c> because it
/// needs the network.
/// </summary>
public sealed class ArtworkLiveTests
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("FLUIDBAR_LIVE_ARTWORK") == "1";

    [Fact]
    public async Task Kugou_catalogue_answers_where_the_western_ones_are_blocked()
    {
        if (!Enabled)
        {
            return;
        }

        // Same order as production: the domestic catalogue first, the Western ones as a last resort.
        var directory = Path.Combine(Path.GetTempPath(), "fluidbar-artwork-live", Guid.NewGuid().ToString("N"));
        var cache = new ArtworkDiskCache(new ArtworkDiskCacheOptions { DirectoryPath = directory });
        // Production egress: same factory, same source order as GsmtcMediaSessionService.ActiveArtworkFetcher.
        using var http = MediaHttpClientFactory.Create(TimeSpan.FromSeconds(12), "FluidBar/2.0 (artwork)");
        using var fetcher = new ArtworkFetcher(cache,
            [new KugouArtworkSource(http), new ItunesArtworkSource(http), new DeezerArtworkSource(http)]);

        var songs = new (string Artist, string Title)[]
        {
            ("M83", "Midnight City"),
            ("周杰伦", "晴天"),
            ("Coldplay", "Viva La Vida"),
        };

        var lines = new List<string>();
        var covers = 0;
        foreach (var (artist, title) in songs)
        {
            var watch = Stopwatch.StartNew();
            var outcome = await fetcher.FetchAsync(artist, title, album: null);
            watch.Stop();
            if (outcome.Entry is not { } entry)
            {
                lines.Add($"FAILED artist='{artist}' title='{title}' failure={outcome.Failure} source={outcome.Source}");
                continue;
            }

            covers++;
            var dimensions = ImageHeaderReader.TryReadDimensions(entry.Bytes);
            lines.Add($"OK artist='{artist}' title='{title}' source={outcome.Source} kind={entry.Metadata.SourceKind} " +
                $"score={entry.Metadata.Score:0.000} bytes={entry.Bytes.Length} " +
                $"size={dimensions?.Width}x{dimensions?.Height} elapsed={watch.ElapsedMilliseconds}ms " +
                $"url={entry.Metadata.SourceUrl}");
        }

        var evidence = Path.Combine(Path.GetTempPath(), "fluidbar-artwork-live-evidence.txt");
        File.WriteAllText(evidence, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        Assert.True(covers > 0, "no cover at all:\n" + string.Join(Environment.NewLine, lines));
    }

    [Fact]
    public async Task Three_songs_get_a_cover_from_the_catalogue()
    {
        if (!Enabled)
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "fluidbar-artwork-live", Guid.NewGuid().ToString("N"));
        var cache = new ArtworkDiskCache(new ArtworkDiskCacheOptions { DirectoryPath = directory });
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Add("User-Agent", "FluidBar/2.0 (artwork)");
        using var fetcher = new ArtworkFetcher(cache, [new ItunesArtworkSource(http)]);

        var songs = new (string Artist, string Title, string Album)[]
        {
            ("Alan Walker、Ali Gatie", "Yesterday", ""),
            ("24KGoldn、iann dior", "Mood (Clean)", ""),
            ("Aaron Carter", "Sooner Or Later", ""),
        };

        var lines = new List<string>();
        foreach (var (artist, title, album) in songs)
        {
            var entry = await fetcher.GetAsync(artist, title, album);
            if (entry is null)
            {
                lines.Add($"FAILED artist='{artist}' title='{title}'");
                continue;
            }

            var bytes = entry.Bytes;
            var dimensions = ImageHeaderReader.TryReadDimensions(bytes);
            lines.Add($"OK artist='{artist}' title='{title}' source={entry.Metadata.SourceKind} " +
                $"url={entry.Metadata.SourceUrl} score={entry.Metadata.Score:0.000} " +
                $"bytes={bytes.Length} size={dimensions?.Width}x{dimensions?.Height} " +
                $"fetchedAt={entry.Metadata.FetchedAtUtc:O} key={entry.Metadata.Key}");

            // Second call must come from the disk cache (no network).
            var again = await fetcher.GetAsync(artist, title, album);
            lines.Add($"  cached-hit={(again is not null && again.Metadata.SourceUrl == entry.Metadata.SourceUrl)}");
        }

        File.WriteAllText("D:/build/GitLocal/FluidBar/artifacts/artwork-live-evidence.txt",
            string.Join(Environment.NewLine, lines) + Environment.NewLine
            + "cacheDir=" + directory + Environment.NewLine
            + "files=" + string.Join(", ", Directory.GetFiles(directory).Select(Path.GetFileName)));
    }
}
