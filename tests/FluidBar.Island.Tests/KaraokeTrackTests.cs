using System.Collections.Immutable;
using FluidBar.Island;

namespace FluidBar.Island.Tests;

/// <summary>The per-frame karaoke sweep math: monotonic, smooth inside a span, clamped at the line end.</summary>
public sealed class KaraokeTrackTests
{
    private static KaraokeTrack Track(params (int Offset, int Length)[] spans) =>
        KaraokeTrack.Create(2000, 0, spans.Select(s => new KaraokeSpan(s.Offset, s.Length)).ToImmutableArray())!;

    [Fact]
    public void Create_returns_null_without_word_timing()
    {
        Assert.Null(KaraokeTrack.Create(0, 500, ImmutableArray<KaraokeSpan>.Empty));
        Assert.Null(KaraokeTrack.Create(0, 500, [new KaraokeSpan(0, 0), new KaraokeSpan(100, 0)]));
    }

    [Fact]
    public void Progress_is_zero_before_the_line_starts()
    {
        var track = Track((0, 2), (300, 2));

        Assert.Equal(0, track.Progress(1999));
        Assert.Equal(0, track.Progress(2000));
    }

    [Fact]
    public void Progress_interpolates_inside_a_span()
    {
        // Two spans of 2 characters each; the second starts at +300 ms and runs to the line extent.
        var track = Track((0, 2), (300, 2));

        // Half way through the first span: one of the four characters.
        Assert.Equal(0.25, track.Progress(2150), 2);
        // Just before the second span starts: the first span is fully consumed.
        Assert.Equal(0.5, track.Progress(2299), 2);
        // 50 ms into the second span: its window runs from its offset to the line extent (offset + 400 ms).
        Assert.Equal(0.5625, track.Progress(2350), 3);
        // The second span is done at its window end: the whole line has been sung.
        Assert.Equal(1, track.Progress(2700), 3);
    }

    [Fact]
    public void Progress_is_monotonic_and_clamped_at_the_line_end()
    {
        var track = Track((0, 3), (400, 3), (900, 2));
        var previous = -1.0;

        for (var position = 1900.0; position <= 3600.0; position += 16)
        {
            var sweep = track.Progress(position);
            Assert.InRange(sweep, 0, 1);
            Assert.True(sweep >= previous - 1e-9, $"sweep went backwards at {position} ms ({sweep} < {previous})");
            previous = sweep;
        }

        Assert.Equal(1, track.Progress(track.LineEndMs), 3);
        Assert.True(track.IsFinished(track.LineEndMs));
        Assert.False(track.IsFinished(track.LineEndMs - 1));
    }

    [Fact]
    public void Progress_tolerates_out_of_order_spans()
    {
        // Real KRC files occasionally carry a span before the previous one; the next offset is the
        // effective end, so the sweep must still stay inside 0..1 and never go backwards.
        var track = Track((500, 2), (0, 2), (800, 2));
        var previous = -1.0;
        for (var position = 2000.0; position <= 3000.0; position += 20)
        {
            var sweep = track.Progress(position);
            Assert.InRange(sweep, 0, 1);
            Assert.True(sweep >= previous - 1e-9);
            previous = sweep;
        }
    }
}
