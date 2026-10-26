using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

/// <summary>
/// The player keeps the exact lyric file it shows next to its install (<c>D:\KuGou\Lyric</c>: 2390 <c>.krc</c> on the
/// reporting machine), but KuGou 8.x no longer writes <c>LyricPath</c> into its ini, so the settings-based lookup and
/// the roaming fallback both missed it and the local provider never answered.
/// </summary>
public sealed class KugouLocalLyricsDiscoveryTests
{
    [Fact]
    public void The_cache_next_to_the_running_player_is_probed_first()
    {
        var directories = KugouLocalLyricsProvider
            .LyricDirectoriesFor(@"D:\KuGou\KuGou.exe", [@"C:\", @"D:\"])
            .ToList();

        Assert.Equal(@"D:\KuGou\Lyric", directories[0]);
        Assert.Contains(@"C:\KuGou\Lyric", directories);
        Assert.Contains(@"D:\KuGou\Lyric", directories);
        Assert.Contains(@"D:\KuGou8\Lyric", directories);
    }

    [Fact]
    public void With_no_running_player_only_the_drive_probes_remain()
    {
        var directories = KugouLocalLyricsProvider.LyricDirectoriesFor(null, [@"D:\"]).ToList();

        Assert.Equal(2, directories.Count);
        Assert.All(directories, directory => Assert.StartsWith(@"D:\", directory));
    }
}
