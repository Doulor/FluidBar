using System.Text;
using FluidBar.Platform.Windows.Media;

namespace FluidBar.Platform.Tests;

/// <summary>
/// A player's "no cover" image cannot be recognised by its statistics once it stops being a flat tile — the only
/// reliable signal is that the same bytes arrive for a different track (measured 2026-10-28: KuGou served an 18 KB
/// logo tile for "妈妈的话" while its real 27 KB cover appeared a minute later and was never picked up again).
/// </summary>
public sealed class ArtworkThumbprintRegistryTests
{
    [Fact]
    public void The_same_bytes_for_a_different_track_are_generic()
    {
        var registry = new ArtworkThumbprintRegistry();
        var bytes = Encoding.UTF8.GetBytes("generic-cover-image");
        var print = ArtworkThumbprintRegistry.Thumbprint(bytes);

        Assert.False(registry.IsGeneric(print, "kugou|妈妈的话|沈梦溪|", out var firstOwner));
        Assert.Null(firstOwner);

        Assert.True(registry.IsGeneric(print, "kugou|另一首|歌手|", out var owner));
        Assert.Equal("kugou|妈妈的话|沈梦溪|", owner);
    }

    [Fact]
    public void The_same_track_may_repeat_its_own_thumbprint()
    {
        var registry = new ArtworkThumbprintRegistry();
        var bytes = Encoding.UTF8.GetBytes("real-cover");
        var print = ArtworkThumbprintRegistry.Thumbprint(bytes);

        Assert.False(registry.IsGeneric(print, "kugou|A|x|", out _));
        Assert.False(registry.IsGeneric(print, "kugou|A|x|", out _));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Different_bytes_are_never_generic()
    {
        var registry = new ArtworkThumbprintRegistry();
        Assert.False(registry.IsGeneric(ArtworkThumbprintRegistry.Thumbprint(Encoding.UTF8.GetBytes("one")), "k|1", out _));
        Assert.False(registry.IsGeneric(ArtworkThumbprintRegistry.Thumbprint(Encoding.UTF8.GetBytes("two")), "k|2", out _));
        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void The_thumbprint_covers_length_and_content()
    {
        var a = ArtworkThumbprintRegistry.Thumbprint(Encoding.UTF8.GetBytes("same"));
        var b = ArtworkThumbprintRegistry.Thumbprint(Encoding.UTF8.GetBytes("same"));
        var c = ArtworkThumbprintRegistry.Thumbprint(Encoding.UTF8.GetBytes("samf"));

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith("4:", a);
    }

    [Fact]
    public void The_registry_stays_bounded()
    {
        var registry = new ArtworkThumbprintRegistry(capacity: 4);
        for (var i = 0; i < 20; i++)
        {
            registry.IsGeneric(ArtworkThumbprintRegistry.Thumbprint(Encoding.UTF8.GetBytes("image-" + i)), "track-" + i, out _);
        }

        Assert.Equal(4, registry.Count);
    }
}
