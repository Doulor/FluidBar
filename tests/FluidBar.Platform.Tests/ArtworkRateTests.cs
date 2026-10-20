using FluidBar.Platform.Windows.Media;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Live cover-rate measurement over the user's own library: the "before" configuration (iTunes only,
/// threshold 0.72) against the shipped one (iTunes + Deezer, threshold 0.65), with per-class attribution.
/// Gated behind <c>FLUIDBAR_LIVE_ARTWORK=1</c>.
/// </summary>
public sealed class ArtworkRateTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("FLUIDBAR_LIVE_ARTWORK") == "1";

    [Fact]
    public async Task Cover_hit_rate_before_and_after_the_second_source()
    {
        if (!Enabled)
        {
            return;
        }

        var songs = SampleSongs(40);
        var report = new List<string> { "songs=" + songs.Length };

        var before = await Measure(songs, threshold: 0.72, secondSource: false);
        report.Add("BEFORE (itunes only, threshold 0.72): " + before);
        var after = await Measure(songs, threshold: 0.65, secondSource: true);
        report.Add("AFTER  (itunes+deezer, threshold 0.65): " + after);

        foreach (var (artist, title) in songs)
        {
            report.Add("song " + artist + " - " + title);
        }

        File.WriteAllText("D:/build/GitLocal/FluidBar/artifacts/artwork-rate.txt",
            string.Join(Environment.NewLine, report) + Environment.NewLine);
    }

    private static async Task<string> Measure((string Artist, string Title)[] songs, double threshold,
        bool secondSource)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluidbar-artwork-rate", Guid.NewGuid().ToString("N"));
        var cache = new ArtworkDiskCache(new ArtworkDiskCacheOptions { DirectoryPath = directory });
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        http.DefaultRequestHeaders.Add("User-Agent", "FluidBar/2.0 (artwork)");
        var sources = new List<IArtworkSource> { new ItunesArtworkSource(http, threshold) };
        if (secondSource)
        {
            sources.Add(new DeezerArtworkSource(http, threshold));
        }

        using var fetcher = new ArtworkFetcher(cache, sources,
            new ArtworkFetcherOptions { Threshold = threshold });
        var chips = new Dictionary<ArtworkFetchFailure, int>();
        var hits = 0;
        foreach (var (artist, title) in songs)
        {
            var outcome = await fetcher.FetchAsync(artist, title, album: null);
            if (outcome.Succeeded)
            {
                hits++;
                continue;
            }

            chips[outcome.Failure] = chips.TryGetValue(outcome.Failure, out var count) ? count + 1 : 1;
        }

        var failureText = string.Join(", ", chips.Select(pair => pair.Key + "=" + pair.Value));
        return $"hits={hits}/{songs.Length} ({(double)hits / songs.Length:P0}) failures: {failureText}";
    }

    /// <summary>Songs the player never cached a cover for (the population the user complains about).</summary>
    private static (string Artist, string Title)[] SampleSongs(int take)
    {
        var lyricDir = @"D:\KuGou\Lyric";
        var artRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KuGou8", "ImagesCache", "AlbumImg");
        var songs = new List<(string, string)>();
        if (!Directory.Exists(lyricDir) || !Directory.Exists(artRoot))
        {
            return songs.ToArray();
        }

        foreach (var file in Directory.EnumerateFiles(lyricDir, "*.krc"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var match = System.Text.RegularExpressions.Regex.Match(stem,
                @"^(?<artist>.+?) - (?<title>.+)-[0-9a-f]{32}-\d+(-\d+)?$");
            if (!match.Success)
            {
                continue;
            }

            var artist = match.Groups["artist"].Value.Trim();
            var title = match.Groups["title"].Value.Trim();
            if (title.Length == 0 || KugouArtworkCache.FindBestFile(artRoot, title) is not null)
            {
                continue;
            }

            songs.Add((artist, title));
            if (songs.Count >= take)
            {
                break;
            }
        }

        return songs.ToArray();
    }
}
