using System.Collections.Immutable;

namespace FluidBar.Island;

/// <summary>One sung span inside a lyric line (KRC <c>&lt;offset,length,0&gt;</c> tag).</summary>
/// <param name="OffsetMs">Start of the span, relative to the line start.</param>
/// <param name="Length">Characters the span covers.</param>
public readonly record struct KaraokeSpan(int OffsetMs, int Length);

/// <summary>
/// Per-word timeline of the lyric line that is being sung, in a form the render layer can evaluate at any
/// playback position.
/// </summary>
/// <remarks>
/// The island redraws the sweep every frame from an extrapolated position (see
/// <see cref="MediaPositionClock"/>), so the sweep has to be a pure function of the position: no state, no
/// allocation per frame. The math mirrors <c>LyricKaraoke.Progress</c> in the platform layer (which maps the
/// same KRC line onto the published scalar) so both agree to the character.
/// </remarks>
public sealed record KaraokeTrack(
    int LineStartMs,
    int LineDurationMs,
    int TotalChars,
    ImmutableArray<KaraokeSpan> Spans)
{
    /// <summary>Returned when there is nothing to sweep.</summary>
    public const double NoWordTiming = -1;

    /// <summary>Builds a track from a line's word spans; null when the line carries no timing.</summary>
    /// <param name="lineStartMs">Line start (absolute, from track start).</param>
    /// <param name="lineDurationMs">Line duration; values &lt;= 0 fall back to the span extent.</param>
    /// <param name="spans">Word spans in file order.</param>
    public static KaraokeTrack? Create(int lineStartMs, int lineDurationMs, ImmutableArray<KaraokeSpan> spans)
    {
        if (spans.IsDefaultOrEmpty)
        {
            return null;
        }

        var total = 0;
        var extent = 0;
        foreach (var span in spans)
        {
            total += Math.Max(0, span.Length);
            extent = Math.Max(extent, Math.Max(0, span.OffsetMs));
        }

        if (total <= 0)
        {
            return null;
        }

        var duration = lineDurationMs > 0 ? lineDurationMs : extent + 400;
        return new KaraokeTrack(lineStartMs, duration, total, spans);
    }

    /// <summary>Absolute end of the line (last span start + a syllable, at most the line duration).</summary>
    public int LineEndMs => LineStartMs + Math.Max(1, LineDurationMs);

    /// <summary>True once the position has passed the whole line.</summary>
    /// <param name="positionMs">Playback position in milliseconds.</param>
    public bool IsFinished(double positionMs) => positionMs >= LineEndMs;

    /// <summary>Fraction of the line already sung at <paramref name="positionMs"/> (0..1).</summary>
    /// <param name="positionMs">Playback position in milliseconds (absolute, from track start).</param>
    public double Progress(double positionMs)
    {
        var elapsed = positionMs - LineStartMs;
        if (elapsed <= 0)
        {
            return 0;
        }

        var consumed = 0;
        for (var index = 0; index < Spans.Length; index++)
        {
            var span = Spans[index];
            var length = Math.Max(0, span.Length);
            var start = Math.Max(0, span.OffsetMs);
            var end = index + 1 < Spans.Length
                ? Math.Max(start, Spans[index + 1].OffsetMs)
                : Math.Max(start, LineDurationMs);

            if (elapsed >= end)
            {
                consumed += length;
                continue;
            }

            if (elapsed <= start)
            {
                break;
            }

            var width = Math.Max(1.0, end - start);
            var within = Math.Clamp((elapsed - start) / width, 0, 1);
            return Math.Clamp((consumed + (within * length)) / TotalChars, 0, 1);
        }

        return Math.Clamp((double)consumed / TotalChars, 0, 1);
    }
}
